using System.Diagnostics;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public sealed record LockingProcessSpec(
    string FriendlyName,
    string[] ProcessNames,
    bool RequiresWslShutdown = false);

public sealed record LockingProcessGroup(
    string FriendlyName,
    string[] ProcessNames,
    int RunningInstanceCount,
    int[] ProcessIds,
    bool RequiresWslShutdown = false);

public static class ProcessLockGuardService
{
    public static List<LockingProcessSpec> ResolveTargetProcessSpecsForCleanerRules(IEnumerable<CleanerRule> rules)
    {
        var specs = new Dictionary<string, LockingProcessSpec>(StringComparer.OrdinalIgnoreCase);

        void AddSpec(string key, string friendly, bool wsl, params string[] names)
            => specs.TryAdd(key, new LockingProcessSpec(friendly, names, wsl));

        foreach (CleanerRule rule in rules)
        {
            string hay = $"{rule.Id} {rule.Name}".ToLowerInvariant();
            if (hay.Contains("chrome", StringComparison.Ordinal))
                AddSpec("chrome", "Google Chrome", false, "chrome");
            if (hay.Contains("edge", StringComparison.Ordinal))
                AddSpec("edge", "Microsoft Edge", false, "msedge");
            if (hay.Contains("brave", StringComparison.Ordinal))
                AddSpec("brave", "Brave Browser", false, "brave");
            if (hay.Contains("firefox", StringComparison.Ordinal))
                AddSpec("firefox", "Mozilla Firefox", false, "firefox");
            if (hay.Contains("opera", StringComparison.Ordinal))
                AddSpec("opera", "Opera Browser", false, "opera");
            if (hay.Contains("vivaldi", StringComparison.Ordinal))
                AddSpec("vivaldi", "Vivaldi Browser", false, "vivaldi");
            if (hay.Contains("discord", StringComparison.Ordinal))
                AddSpec("discord", "Discord", false, "Discord");
            if (hay.Contains("steam", StringComparison.Ordinal))
                AddSpec("steam", "Steam", false, "steam", "steamwebhelper");
            if (hay.Contains("vscode", StringComparison.Ordinal) || hay.Contains("vs code", StringComparison.Ordinal))
                AddSpec("vscode", "Visual Studio Code", false, "Code");
        }

        return specs.Values.ToList();
    }

    public static List<LockingProcessSpec> ResolveTargetProcessSpecsForVhdxPath(string? vhdxPath)
    {
        var specs = new List<LockingProcessSpec>();
        if (string.IsNullOrWhiteSpace(vhdxPath))
            return specs;

        string lower = vhdxPath.ToLowerInvariant();
        if (lower.Contains("claude", StringComparison.Ordinal))
        {
            specs.Add(new LockingProcessSpec(
                "Claude Desktop (VM)",
                ["claude"],
                RequiresWslShutdown: false));
        }

        if (lower.Contains("docker", StringComparison.Ordinal))
        {
            specs.Add(new LockingProcessSpec(
                "Docker Desktop & WSL2",
                ["Docker Desktop", "com.docker.backend", "vmmem", "vmmemWSL"],
                RequiresWslShutdown: true));
        }

        if (lower.Contains("wsl", StringComparison.Ordinal) ||
            lower.Contains("packages", StringComparison.Ordinal) ||
            lower.Contains("ubuntu", StringComparison.Ordinal) ||
            lower.Contains("debian", StringComparison.Ordinal))
        {
            if (specs.All(s => !s.RequiresWslShutdown))
            {
                specs.Add(new LockingProcessSpec(
                    "WSL2 (Windows Subsystem for Linux)",
                    ["wsl", "wslhost", "vmmem", "vmmemWSL"],
                    RequiresWslShutdown: true));
            }
        }

        return specs;
    }

    public static List<LockingProcessGroup> DetectRunningConflicts(
        IEnumerable<LockingProcessSpec> specs,
        Func<string, int[]>? pidLookup = null)
    {
        Func<string, int[]> resolver = pidLookup ?? LookupProcessIdsByName;
        var conflicts = new List<LockingProcessGroup>();

        foreach (LockingProcessSpec spec in specs)
        {
            var pids = new HashSet<int>();
            foreach (string procName in spec.ProcessNames)
            {
                foreach (int pid in resolver(procName))
                {
                    if (pid > 0)
                        pids.Add(pid);
                }
            }

            if (pids.Count > 0)
            {
                conflicts.Add(new LockingProcessGroup(
                    spec.FriendlyName,
                    spec.ProcessNames,
                    pids.Count,
                    pids.ToArray(),
                    spec.RequiresWslShutdown));
            }
        }

        return conflicts;
    }

    private static int[] LookupProcessIdsByName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return [];

        try
        {
            Process[] matches = Process.GetProcessesByName(processName);
            var ids = new List<int>(matches.Length);
            foreach (Process p in matches)
            {
                try
                {
                    ids.Add(p.Id);
                }
                catch
                {
                    // Process exited during enumeration.
                }
                finally
                {
                    p.Dispose();
                }
            }
            return [.. ids];
        }
        catch
        {
            return [];
        }
    }

    public static async Task<(int ClosedCount, List<string> Notes)> TryCloseProcessGroupsAsync(
        IEnumerable<LockingProcessGroup> groups,
        CancellationToken ct = default)
    {
        var groupList = groups.ToList();
        int closedCount = 0;
        var notes = new List<string>();
        bool needsWslShutdown = groupList.Any(g => g.RequiresWslShutdown);

        if (needsWslShutdown)
        {
            try
            {
                ProcessStartInfo wslPsi = SafeProcessRunner.BuildDirectProcessStartInfo("wsl.exe", ["--shutdown"]);
                ProcessRunResult wslRun = await SafeProcessRunner.RunAsync(wslPsi, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                notes.Add(wslRun.Started && !wslRun.TimedOut && wslRun.ExitCode == 0
                    ? "WSL2 alt sistemi 'wsl --shutdown' ile kapatıldı."
                    : wslRun.TimedOut
                        ? "WSL2 kapatma komutu zaman aşımına uğradı."
                        : $"WSL2 kapatılamadı (Çıkış kodu: {wslRun.ExitCode}).");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                notes.Add($"WSL2 kapatılamadı: {ex.Message}");
            }
        }

        foreach (LockingProcessGroup group in groupList)
        {
            foreach (string procName in group.ProcessNames)
            {
                ct.ThrowIfCancellationRequested();

                // vmmem/vmmemWSL belong to Hyper-V/WSL2 kernel and stop via wsl --shutdown rather than Kill().
                if (procName.Equals("vmmem", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("vmmemWSL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Process[] procs;
                try
                {
                    procs = Process.GetProcessesByName(procName);
                }
                catch
                {
                    continue;
                }

                foreach (Process proc in procs)
                {
                    try
                    {
                        if (proc.MainWindowHandle != IntPtr.Zero)
                            proc.CloseMainWindow();
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
        }

        if (groupList.Count > 0)
            await Task.Delay(450, ct).ConfigureAwait(false);

        foreach (LockingProcessGroup group in groupList)
        {
            foreach (string procName in group.ProcessNames)
            {
                ct.ThrowIfCancellationRequested();
                if (procName.Equals("vmmem", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("vmmemWSL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Process[] remaining;
                try
                {
                    remaining = Process.GetProcessesByName(procName);
                }
                catch
                {
                    continue;
                }

                foreach (Process proc in remaining)
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill(entireProcessTree: true);
                            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                        }
                        closedCount++;
                    }
                    catch
                    {
                        // Process exited or requires higher privilege.
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
        }

        return (closedCount, notes);
    }
}
