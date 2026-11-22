using System.Diagnostics;
using System.Text;

namespace FreeCleaner.Services;

/// <summary>
/// Builds shell-injection-safe ProcessStartInfo configurations using ArgumentList
/// or UTF-16LE Base64 -EncodedCommand payloads instead of interpolated cmd.exe / shell strings.
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
                psi.StandardOutputEncoding = Encoding.UTF8;
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-WindowStyle");
            psi.ArgumentList.Add("Hidden");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);
        }

        return psi;
    }

    public static ProcessStartInfo BuildDirectProcessStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        bool redirectOutput = false)
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
            psi.StandardOutputEncoding = Encoding.UTF8;

        foreach (string arg in arguments)
            psi.ArgumentList.Add(SanitizeSingleLine(arg));

        return psi;
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
}
