using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class EnterprisePowerToolsService
{
    // ==================== 1. WIN32 RESTART MANAGER (rstrtmgr.dll) FILE UNLOCKER ====================

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[]? rgsFilenames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    public static async Task<List<LockingProcessItem>> FindLockingProcessesAsync(string fileOrFolderPath)
    {
        return await Task.Run(() =>
        {
            var results = new List<LockingProcessItem>();
            if (string.IsNullOrWhiteSpace(fileOrFolderPath))
                return results;

            var candidateFiles = new List<string>();
            if (File.Exists(fileOrFolderPath))
            {
                candidateFiles.Add(fileOrFolderPath);
            }
            else if (Directory.Exists(fileOrFolderPath))
            {
                try
                {
                    candidateFiles.AddRange(Directory.GetFiles(fileOrFolderPath, "*", SearchOption.AllDirectories).Take(250));
                }
                catch { }
            }

            if (candidateFiles.Count == 0)
                return results;

            int hr = RmStartSession(out uint sessionHandle, 0, Guid.NewGuid().ToString("N"));
            if (hr != 0)
                return results;

            try
            {
                string[] resources = candidateFiles.ToArray();
                hr = RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
                if (hr != 0)
                    return results;

                uint pnProcInfoNeeded = 0;
                uint pnProcInfo = 0;
                uint lpdwRebootReasons = 0;

                hr = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
                if (hr == 234 && pnProcInfoNeeded > 0) // ERROR_MORE_DATA
                {
                    var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;
                    hr = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                    if (hr == 0)
                    {
                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            var info = processInfo[i];
                            results.Add(new LockingProcessItem
                            {
                                ProcessId = info.Process.dwProcessId,
                                ProcessName = string.IsNullOrWhiteSpace(info.strAppName) ? $"PID {info.Process.dwProcessId}" : info.strAppName,
                                ApplicationType = info.ApplicationType.ToString(),
                                LockedTargetPath = fileOrFolderPath
                            });
                        }
                    }
                }
            }
            finally
            {
                _ = RmEndSession(sessionHandle);
            }

            return results;
        });
    }

    public static bool KillLockingProcess(int processId, out string message)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            string name = proc.ProcessName;
            proc.Kill(entireProcessTree: false);
            proc.WaitForExit(3000);
            message = $"'{name}' (PID: {processId}) sonlandırıldı ve dosya kilidi açıldı.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"İşlem sonlandırılamadı: {ex.Message}";
            return false;
        }
    }

    // ==================== 2. BROWSER EXTENSIONS AUDITOR ====================

    public static async Task<List<BrowserExtensionItem>> ScanBrowserExtensionsAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<BrowserExtensionItem>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var browsers = new (string Name, string UserDataPath)[]
            {
                ("Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data")),
                ("Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data")),
                ("Brave Browser", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")),
                ("Vivaldi", Path.Combine(localAppData, "Vivaldi", "User Data"))
            };

            foreach (var (browserName, userDataPath) in browsers)
            {
                if (!Directory.Exists(userDataPath)) continue;

                try
                {
                    foreach (string profileDir in Directory.GetDirectories(userDataPath))
                    {
                        string profileName = Path.GetFileName(profileDir);
                        if (!profileName.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                            !profileName.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string extRoot = Path.Combine(profileDir, "Extensions");
                        if (!Directory.Exists(extRoot)) continue;

                        foreach (string extIdDir in Directory.GetDirectories(extRoot))
                        {
                            string extId = Path.GetFileName(extIdDir);
                            if (extId.Equals("Temp", StringComparison.OrdinalIgnoreCase))
                                continue;

                            try
                            {
                                var versionDirs = Directory.GetDirectories(extIdDir);
                                if (versionDirs.Length == 0) continue;

                                string latestVerDir = versionDirs.OrderByDescending(x => x).First();
                                string manifestPath = Path.Combine(latestVerDir, "manifest.json");
                                if (!File.Exists(manifestPath)) continue;

                                string json = File.ReadAllText(manifestPath);
                                using var doc = JsonDocument.Parse(json);
                                var root = doc.RootElement;

                                string rawName = root.TryGetProperty("name", out var pName) ? pName.GetString() ?? extId : extId;
                                string version = root.TryGetProperty("version", out var pVer) ? pVer.GetString() ?? "-" : "-";
                                string desc = root.TryGetProperty("description", out var pDesc) ? pDesc.GetString() ?? string.Empty : string.Empty;

                                // Resolve localized __MSG_...__ names if possible
                                if (rawName.StartsWith("__MSG_", StringComparison.OrdinalIgnoreCase))
                                {
                                    rawName = ResolveLocalizedExtensionString(latestVerDir, rawName) ?? extId;
                                }
                                if (desc.StartsWith("__MSG_", StringComparison.OrdinalIgnoreCase))
                                {
                                    desc = ResolveLocalizedExtensionString(latestVerDir, desc) ?? string.Empty;
                                }

                                long folderSize = Directory.GetFiles(extIdDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

                                results.Add(new BrowserExtensionItem
                                {
                                    BrowserName = browserName,
                                    ProfileName = profileName,
                                    ExtensionId = extId,
                                    Name = rawName,
                                    Version = version,
                                    Description = desc,
                                    FullPath = extIdDir,
                                    SizeBytes = folderSize
                                });
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            return results.OrderBy(x => x.BrowserName).ThenBy(x => x.Name).ToList();
        });
    }

    private static string? ResolveLocalizedExtensionString(string versionDir, string msgPlaceholder)
    {
        string key = msgPlaceholder.Trim('_');
        if (key.StartsWith("MSG_", StringComparison.OrdinalIgnoreCase))
            key = key[4..];

        string[] candidateLocales = ["tr", "en", "en_US", "en_GB"];
        foreach (string loc in candidateLocales)
        {
            string msgFile = Path.Combine(versionDir, "_locales", loc, "messages.json");
            if (!File.Exists(msgFile)) continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(msgFile));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.TryGetProperty("message", out var msgVal))
                    {
                        return msgVal.GetString();
                    }
                }
            }
            catch { }
        }
        return null;
    }

    // ==================== 3. WINDOWS UPDATES (HOTFIX) & DRIVER STORE AUDITOR ====================

    public static async Task<List<DriverOrHotfixItem>> GetHotfixesAndDriversAsync()
    {
        return await Task.Run(() =>
        {
            var items = new List<DriverOrHotfixItem>();

            // 1. Query installed Windows Hotfixes (KB updates) via wmic / powershell
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Get-HotFix | Select-Object HotFixID,Description,InstalledBy,InstalledOn | ConvertTo-Json -Compress\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string json = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(5000);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in doc.RootElement.EnumerateArray())
                                AddHotfixElement(el, items);
                        }
                        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            AddHotfixElement(doc.RootElement, items);
                        }
                    }
                }
            }
            catch { }

            // 2. Enumerate third-party OEM DriverStore packages via pnputil /enum-drivers
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"chcp 65001 >nul && pnputil.exe /enum-drivers\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);

                    string currentOem = string.Empty;
                    string currentProvider = string.Empty;
                    string currentClass = string.Empty;
                    string currentVer = string.Empty;

                    foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = rawLine.Trim();
                        int colon = line.IndexOf(':');
                        if (colon <= 0) continue;

                        string val = line[(colon + 1)..].Trim();
                        if (val.StartsWith("oem", StringComparison.OrdinalIgnoreCase) && val.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(currentOem))
                            {
                                items.Add(new DriverOrHotfixItem
                                {
                                    Category = "3. Parti Sürücü (DriverStore)",
                                    Identifier = currentOem,
                                    ProviderOrType = currentProvider,
                                    DateOrVersion = currentVer,
                                    Details = currentClass
                                });
                            }
                            currentOem = val;
                            currentProvider = string.Empty;
                            currentClass = string.Empty;
                            currentVer = string.Empty;
                        }
                        else if (!string.IsNullOrEmpty(currentOem))
                        {
                            if ( string.IsNullOrEmpty(currentProvider))
                                currentProvider = val;
                            else if (string.IsNullOrEmpty(currentClass))
                                currentClass = val;
                            else if (string.IsNullOrEmpty(currentVer) && val.Any(char.IsDigit))
                                currentVer = val;
                        }
                    }

                    if (!string.IsNullOrEmpty(currentOem))
                    {
                        items.Add(new DriverOrHotfixItem
                        {
                            Category = "3. Parti Sürücü (DriverStore)",
                            Identifier = currentOem,
                            ProviderOrType = currentProvider,
                            DateOrVersion = currentVer,
                            Details = currentClass
                        });
                    }
                }
            }
            catch { }

            return items;
        });

        static void AddHotfixElement(JsonElement el, List<DriverOrHotfixItem> list)
        {
            string id = el.TryGetProperty("HotFixID", out var pId) ? pId.ToString() : "KB";
            string desc = el.TryGetProperty("Description", out var pDesc) ? pDesc.ToString() : "Update";
            string by = el.TryGetProperty("InstalledBy", out var pBy) ? pBy.ToString() : "System";

            list.Add(new DriverOrHotfixItem
            {
                Category = "Windows Güncellemesi (HotFix)",
                Identifier = id,
                ProviderOrType = desc,
                DateOrVersion = "Yüklü",
                Details = string.IsNullOrWhiteSpace(by) ? "Windows Update" : by
            });
        }
    }

    // ==================== 4. ENVIRONMENT PATH DOCTOR & BROKEN SHORTCUT (.LNK) SCANNER ====================

    public static async Task<List<PathOrShortcutIssueItem>> ScanPathAndBrokenShortcutsAsync()
    {
        return await Task.Run(() =>
        {
            var issues = new List<PathOrShortcutIssueItem>();

            // 1. Check User PATH and System PATH
            InspectEnvironmentPathVariable(EnvironmentVariableTarget.User, "Kullanıcı PATH", issues);
            InspectEnvironmentPathVariable(EnvironmentVariableTarget.Machine, "Sistem PATH", issues);

            // 2. Scan Desktop & Start Menu for Broken .LNK Shortcuts
            string[] shortcutFolders =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs)
            ];

            foreach (string folder in shortcutFolders.Distinct())
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    continue;

                try
                {
                    foreach (string lnkFile in Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories))
                    {
                        string? targetPath = TryExtractLnkTarget(lnkFile);
                        if (!string.IsNullOrEmpty(targetPath) &&
                            targetPath.StartsWith("C:\\", StringComparison.OrdinalIgnoreCase) &&
                            !targetPath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) &&
                            !File.Exists(targetPath) &&
                            !Directory.Exists(targetPath))
                        {
                            issues.Add(new PathOrShortcutIssueItem
                            {
                                IssueType = "Kırık Kısayol (.lnk)",
                                Scope = Path.GetFileName(folder),
                                PathOrShortcutFile = lnkFile,
                                MissingTarget = targetPath,
                                IsShortcutFile = true,
                                IsUserEnvironment = true
                            });
                        }
                    }
                }
                catch { }
            }

            return issues;
        });
    }

    private static void InspectEnvironmentPathVariable(
        EnvironmentVariableTarget target,
        string scopeLabel,
        List<PathOrShortcutIssueItem> issues)
    {
        try
        {
            string? rawPath = Environment.GetEnvironmentVariable("PATH", target);
            if (string.IsNullOrWhiteSpace(rawPath)) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in rawPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = entry.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                string expanded = Environment.ExpandEnvironmentVariables(trimmed);
                if (!seen.Add(expanded.TrimEnd('\\')))
                {
                    issues.Add(new PathOrShortcutIssueItem
                    {
                        IssueType = "Tekrarlanan (Duplicate) PATH Girdisi",
                        Scope = scopeLabel,
                        PathOrShortcutFile = trimmed,
                        MissingTarget = "Aynı klasör PATH içinde birden fazla kez tanımlı",
                        IsShortcutFile = false,
                        IsUserEnvironment = target == EnvironmentVariableTarget.User
                    });
                }
                else if (Path.IsPathRooted(expanded) && !Directory.Exists(expanded))
                {
                    issues.Add(new PathOrShortcutIssueItem
                    {
                        IssueType = "Var Olmayan (Silinmiş) PATH Klasörü",
                        Scope = scopeLabel,
                        PathOrShortcutFile = trimmed,
                        MissingTarget = expanded,
                        IsShortcutFile = false,
                        IsUserEnvironment = target == EnvironmentVariableTarget.User
                    });
                }
            }
        }
        catch { }
    }

    public static (int FixedPathEntries, int DeletedShortcuts) FixPathAndShortcuts(IEnumerable<PathOrShortcutIssueItem> selectedItems)
    {
        var list = selectedItems.Where(x => x.IsSelected).ToList();
        int fixedPath = 0;
        int deletedShortcuts = 0;

        // 1. Delete selected broken .lnk files
        foreach (var lnk in list.Where(x => x.IsShortcutFile))
        {
            if (RecycleBinHelper.MoveFileToRecycleBin(lnk.PathOrShortcutFile))
                deletedShortcuts++;
        }

        // 2. Clean User PATH invalid/duplicate entries safely
        var userPathIssues = list.Where(x => !x.IsShortcutFile && x.IsUserEnvironment).ToList();
        if (userPathIssues.Count > 0)
        {
            try
            {
                string? currentUserPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(currentUserPath))
                {
                    var toRemove = userPathIssues.Select(x => x.PathOrShortcutFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var uniqueValid = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (string part in currentUserPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string norm = part.Trim().TrimEnd('\\');
                        if (!seen.Add(norm))
                        {
                            fixedPath++;
                            continue;
                        }
                        if (toRemove.Contains(part.Trim()) && !Directory.Exists(Environment.ExpandEnvironmentVariables(part.Trim())))
                        {
                            fixedPath++;
                            continue;
                        }
                        uniqueValid.Add(part.Trim());
                    }

                    Environment.SetEnvironmentVariable("PATH", string.Join(";", uniqueValid), EnvironmentVariableTarget.User);
                }
            }
            catch { }
        }

        return (fixedPath, deletedShortcuts);
    }

    private static string? TryExtractLnkTarget(string lnkFilePath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(lnkFilePath);
            string ascii = Encoding.Default.GetString(bytes);

            int idx = ascii.IndexOf(":\\", StringComparison.Ordinal);
            while (idx > 0)
            {
                char driveLetter = ascii[idx - 1];
                if (char.IsLetter(driveLetter))
                {
                    int start = idx - 1;
                    int end = ascii.IndexOf('\0', start);
                    if (end > start && end - start < 260)
                    {
                        string candidate = ascii[start..end];
                        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            return candidate;
                    }
                }
                idx = ascii.IndexOf(":\\", idx + 2, StringComparison.Ordinal);
            }
        }
        catch { }
        return null;
    }

    // ==================== 5. ONE-CLICK GAME BOOSTER & LAPTOP BATTERY REPORT ====================

    public static async Task<string> ActivateGameAndPerformanceBoostAsync()
    {
        var (freedRam, trimmedProcs) = await SystemBoosterAndReportService.OptimizeRamWorkingSetAsync();
        await SystemBoosterAndReportService.FlushDnsCacheAsync();

        // Switch to High/Ultimate performance power plan if available
        var plans = await UltimateSystemSuiteService.GetPowerPlansAsync();
        var highPerf = plans.FirstOrDefault(p =>
            p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Nihai", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("High", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Yüksek", StringComparison.OrdinalIgnoreCase));

        if (highPerf is not null)
        {
            await UltimateSystemSuiteService.SetActivePowerPlanAsync(highPerf.Guid);
        }

        return $"Oyun & Performans Modu Aktif! {trimmedProcs} arka plan süreci optimize edildi ({CleanerRule.FormatBytes(freedRam)} RAM boşaltıldı), DNS önbelleği sıfırlandı" +
               (highPerf is not null ? $" ve '{highPerf.Name}' güç planına geçildi." : ".");
    }

    public static async Task<(bool Success, string ReportPath, string Message)> GenerateBatteryHealthReportAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                string reportDir = AppStoragePathsService.GetReportsDirectory();
                Directory.CreateDirectory(reportDir);

                string reportFile = Path.Combine(reportDir, $"Pil_Saglik_Raporu_{DateTime.Now:yyyyMMdd_HHmmss}.html");

                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = $"/batteryreport /output \"{reportFile}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                    return (false, string.Empty, "powercfg başlatılamadı.");

                string output = proc.StandardOutput.ReadToEnd() + " " + proc.StandardError.ReadToEnd();
                proc.WaitForExit(6000);

                if (File.Exists(reportFile))
                {
                    return (true, reportFile, $"Pil sağlık raporu oluşturuldu: {reportFile}");
                }

                return (false, string.Empty, "Bu sistemde pil algılanmadı (Masaüstü bilgisayar) veya rapor desteklenmiyor.");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, ex.Message);
            }
        });
    }
}
