using System.Diagnostics;
using System.Text;

namespace FreeCleaner.Services;

public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Started);

/// <summary>
/// Builds shell-injection-safe ProcessStartInfo configurations using ArgumentList
/// or UTF-16LE Base64 -EncodedCommand payloads instead of interpolated cmd.exe / shell strings,
/// and executes processes with concurrent stream draining to prevent pipe deadlocks.
/// </summary>
public static class SafeProcessRunner
{
    public static string SanitizeSingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    public static string QuotePowerShellSingle(string? value)
    {
        string normalized = value ?? string.Empty;
        return $"'{normalized.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    public static string EncodePowerShellCommand(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    public static ProcessStartInfo BuildEncodedPowerShellStartInfo(
        string script,
        bool runAsAdmin = false,
        bool redirectOutput = false)
    {
        string encoded = EncodePowerShellCommand(script);
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe"
        };

        if (runAsAdmin)
        {
            // ShellExecute with runas does not allow Output redirection.
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}";
        }
        else
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = redirectOutput;
            psi.RedirectStandardError = redirectOutput;
            if (redirectOutput)
            {
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
            }
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);
        }

        return psi;
    }

    public static ProcessStartInfo BuildDirectProcessStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        bool redirectOutput = false,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput
        };

        if (redirectOutput)
        {
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        if (environmentVariables is not null)
        {
            foreach (KeyValuePair<string, string> kv in environmentVariables)
                psi.Environment[kv.Key] = kv.Value;
        }

        foreach (string arg in arguments)
            psi.ArgumentList.Add(SanitizeSingleLine(arg));

        return psi;
    }

    public static async Task<ProcessRunResult> RunAsync(
        ProcessStartInfo psi,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(psi);

        try
        {
            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                ErrorLogService.Log("SafeProcessRunner", $"Process.Start returned null for {psi.FileName}.");
                return new ProcessRunResult(-1, string.Empty, $"Failed to start {psi.FileName}.", false, false);
            }

            Task<string> outTask = psi.RedirectStandardOutput
                ? proc.StandardOutput.ReadToEndAsync(CancellationToken.None)
                : Task.FromResult(string.Empty);
            Task<string> errTask = psi.RedirectStandardError
                ? proc.StandardError.ReadToEndAsync(CancellationToken.None)
                : Task.FromResult(string.Empty);

            bool timedOut = false;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                timeoutCts.CancelAfter(timeout);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    ErrorLogService.Log("SafeProcessRunner:KillAsync", killEx);
                }

                if (ct.IsCancellationRequested)
                    throw;

                timedOut = true;
            }

            try
            {
                await Task.WhenAll(outTask, errTask)
                    .WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception drainEx)
            {
                ErrorLogService.Log("SafeProcessRunner:DrainAsync", drainEx);
            }

            string stdOut = outTask.IsCompletedSuccessfully ? outTask.Result : string.Empty;
            string stdErr = errTask.IsCompletedSuccessfully ? errTask.Result : string.Empty;
            int exitCode = !timedOut && proc.HasExited ? proc.ExitCode : -1;
            return new ProcessRunResult(exitCode, stdOut, stdErr, timedOut, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorLogService.Log($"SafeProcessRunner:RunAsync:{psi.FileName}", ex);
            return new ProcessRunResult(-1, string.Empty, ex.Message, false, false);
        }
    }

    public static ProcessRunResult Run(ProcessStartInfo psi, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(psi);

        try
        {
            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                ErrorLogService.Log("SafeProcessRunner", $"Process.Start returned null for {psi.FileName}.");
                return new ProcessRunResult(-1, string.Empty, $"Failed to start {psi.FileName}.", false, false);
            }

            var outSb = new StringBuilder();
            var errSb = new StringBuilder();
            object outLock = new();
            object errLock = new();

            if (psi.RedirectStandardOutput)
            {
                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is null) return;
                    lock (outLock) outSb.AppendLine(e.Data);
                };
                proc.BeginOutputReadLine();
            }

            if (psi.RedirectStandardError)
            {
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is null) return;
                    lock (errLock) errSb.AppendLine(e.Data);
                };
                proc.BeginErrorReadLine();
            }

            int waitMs = timeout == Timeout.InfiniteTimeSpan
                ? Timeout.Infinite
                : (int)Math.Clamp(timeout.TotalMilliseconds, 100, int.MaxValue);

            bool exited = proc.WaitForExit(waitMs);
            if (!exited)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    ErrorLogService.Log("SafeProcessRunner:KillSync", killEx);
                }

                proc.WaitForExit(1000);
            }
            else
            {
                // Zero-argument WaitForExit guarantees async output handlers have flushed.
                proc.WaitForExit();
            }

            string stdOut;
            string stdErr;
            lock (outLock) stdOut = outSb.ToString();
            lock (errLock) stdErr = errSb.ToString();

            return new ProcessRunResult(exited ? proc.ExitCode : -1, stdOut, stdErr, !exited, true);
        }
        catch (Exception ex)
        {
            ErrorLogService.Log($"SafeProcessRunner:Run:{psi.FileName}", ex);
            return new ProcessRunResult(-1, string.Empty, ex.Message, false, false);
        }
    }

    public static string BuildFirewallBlockScript(string ruleName, string exeFullPath)
    {
        string cleanRule = QuotePowerShellSingle(SanitizeSingleLine(ruleName));
        string cleanExe = QuotePowerShellSingle(SanitizeSingleLine(exeFullPath));
        return string.Join(Environment.NewLine,
            $"$ruleName = {cleanRule}",
            $"$exePath = {cleanExe}",
            "& netsh.exe advfirewall firewall add rule \"name=$ruleName\" dir=out action=block \"program=$exePath\" enable=yes | Out-Null",
            "& netsh.exe advfirewall firewall add rule \"name=${ruleName}_In\" dir=in action=block \"program=$exePath\" enable=yes | Out-Null");
    }

    public static string BuildFirewallDeleteScript(IEnumerable<string> ruleNames)
    {
        var lines = new List<string>();
        foreach (string rawName in ruleNames)
        {
            string clean = SanitizeSingleLine(rawName);
            if (string.IsNullOrWhiteSpace(clean))
                continue;
            string quoted = QuotePowerShellSingle(clean);
            lines.Add($"$rn = {quoted}; & netsh.exe advfirewall firewall delete rule \"name=$rn\" | Out-Null");
        }
        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "return";
    }

    public static string BuildMountDiskImageScript(string imagePath)
        => $"Mount-DiskImage -ImagePath {QuotePowerShellSingle(SanitizeSingleLine(imagePath))} -ErrorAction Stop";

    public static string BuildDismountDiskImageScript(string? backingImagePath, char driveLetter)
    {
        string cleanBacking = SanitizeSingleLine(backingImagePath);
        if (!string.IsNullOrWhiteSpace(cleanBacking) &&
            (cleanBacking.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase) ||
             cleanBacking.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase) ||
             cleanBacking.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)))
        {
            return $"Dismount-DiskImage -ImagePath {QuotePowerShellSingle(cleanBacking)} -ErrorAction SilentlyContinue";
        }

        char safeLetter = char.IsLetter(driveLetter) ? char.ToUpperInvariant(driveLetter) : 'C';
        return $"Get-Volume -DriveLetter '{safeLetter}' | Get-DiskImage -ErrorAction SilentlyContinue | Dismount-DiskImage -ErrorAction SilentlyContinue; (New-Object -ComObject Shell.Application).Namespace(17).ParseName('{safeLetter}:').InvokeVerb('Eject')";
    }

    public static string BuildRemoveAppxScript(string packageFullName)
        => $"Remove-AppxPackage -Package {QuotePowerShellSingle(SanitizeSingleLine(packageFullName))} -ErrorAction Stop";

    public static string BuildNetworkRepairScript(string actionKey)
        => actionKey switch
        {
            "ARP_FLUSH" => string.Join(Environment.NewLine,
                "& ipconfig.exe /flushdns | Out-Null",
                "& arp.exe -d * | Out-Null"),
            "TCP_WINSOCK_RESET" => string.Join(Environment.NewLine,
                "& netsh.exe winsock reset | Out-Null",
                "& netsh.exe int ip reset | Out-Null"),
            "DHCP_RENEW" => string.Join(Environment.NewLine,
                "& ipconfig.exe /release | Out-Null",
                "& ipconfig.exe /renew | Out-Null"),
            _ => "& ipconfig.exe /flushdns | Out-Null"
        };

    public static string BuildDismCheckHealthElevatedScript(string outputFilePath)
    {
        string quotedOut = QuotePowerShellSingle(SanitizeSingleLine(outputFilePath));
        return $"& dism.exe /Online /Cleanup-Image /CheckHealth 2>&1 | Out-File -FilePath {quotedOut} -Encoding utf8";
    }

    public static string BuildServiceControlElevatedScript(string action, string serviceName)
    {
        string safeAction = string.Equals(action, "start", StringComparison.OrdinalIgnoreCase) ? "start" : "stop";
        string safeService = QuotePowerShellSingle(SanitizeSingleLine(serviceName));
        return $"$svc = {safeService}; & sc.exe {safeAction} $svc | Out-Null";
    }

    public static string BuildRegImportScript(string backupPath)
    {
        string quotedPath = QuotePowerShellSingle(SanitizeSingleLine(backupPath));
        return $"& reg.exe import {quotedPath}";
    }
}
