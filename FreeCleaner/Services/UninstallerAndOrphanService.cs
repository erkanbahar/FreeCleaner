using System.Diagnostics;
using System.IO;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class UninstallerAndOrphanService
{
    private static readonly string[] UninstallRegistryPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static async Task<List<InstalledAppModel>> GetInstalledAppsAsync()
    {
        return await Task.Run(() =>
        {
            var dict = new Dictionary<string, InstalledAppModel>(StringComparer.OrdinalIgnoreCase);

            foreach (string regPath in UninstallRegistryPaths)
            {
                ReadUninstallHive(Registry.LocalMachine, regPath, dict);
            }
            ReadUninstallHive(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", dict);

            return dict.Values
                .OrderByDescending(x => x.EstimatedSizeBytes)
                .ThenBy(x => x.DisplayName)
                .ToList();
        });
    }

    private static void ReadUninstallHive(RegistryKey root, string subKeyPath, Dictionary<string, InstalledAppModel> target)
    {
        try
        {
            using var baseKey = root.OpenSubKey(subKeyPath, writable: false);
            if (baseKey is null) return;

            foreach (string subName in baseKey.GetSubKeyNames())
            {
                try
                {
                    using var appKey = baseKey.OpenSubKey(subName, writable: false);
                    if (appKey is null) continue;

                    string? displayName = appKey.GetValue("DisplayName")?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(displayName))
                        continue;

                    // Skip system components & minor hotfixes
                    if (appKey.GetValue("SystemComponent")?.ToString() == "1")
                        continue;

                    string? uninstallString = appKey.GetValue("UninstallString")?.ToString()
                                              ?? appKey.GetValue("QuietUninstallString")?.ToString();
                    if (string.IsNullOrWhiteSpace(uninstallString))
                        continue;

                    string publisher = appKey.GetValue("Publisher")?.ToString()?.Trim() ?? "Bilinmiyor";
                    string version = appKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "-";
                    string installDateRaw = appKey.GetValue("InstallDate")?.ToString()?.Trim() ?? string.Empty;
                    string installLoc = appKey.GetValue("InstallLocation")?.ToString()?.Trim() ?? string.Empty;

                    long sizeBytes = 0;
                    if (appKey.GetValue("EstimatedSize") is int sizeKb && sizeKb > 0)
                    {
                        sizeBytes = sizeKb * 1024L;
                    }

                    string formattedDate = FormatRegistryDate(installDateRaw);

                    if (!target.ContainsKey(displayName))
                    {
                        target[displayName] = new InstalledAppModel
                        {
                            DisplayName = displayName,
                            Publisher = publisher,
                            DisplayVersion = version,
                            InstallDate = formattedDate,
                            EstimatedSizeBytes = sizeBytes,
                            UninstallString = uninstallString,
                            InstallLocation = installLoc
                        };
                    }
                }
                catch
                {
                    // Skip broken subkey
                }
            }
        }
        catch
        {
            // Ignore access errors
        }
    }

    public static bool LaunchUninstaller(InstalledAppModel app, out string message)
    {
        if (string.IsNullOrWhiteSpace(app.UninstallString))
        {
            message = "Kaldırma komutu bulunamadı.";
            return false;
        }

        try
        {
            string cmd = app.UninstallString.Trim();
            string fileName;
            string arguments = string.Empty;

            if (cmd.StartsWith('"'))
            {
                int endQuote = cmd.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    fileName = cmd[1..endQuote];
                    arguments = cmd[(endQuote + 1)..].Trim();
                }
                else
                {
                    fileName = cmd;
                }
            }
            else if (cmd.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "MsiExec.exe";
                arguments = cmd["MsiExec.exe".Length..].Trim();
            }
            else
            {
                int exeIdx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx > 0)
                {
                    fileName = cmd[..(exeIdx + 4)];
                    arguments = cmd[(exeIdx + 4)..].Trim();
                }
                else
                {
                    fileName = cmd;
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            });

            message = $"'{app.DisplayName}' kaldırıcısı başlatıldı.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Kaldırıcı başlatılamadı: {ex.Message}";
            return false;
        }
    }

    public static async Task<List<OrphanAppFolderItem>> ScanOrphanFoldersAsync(
        IEnumerable<InstalledAppModel> installedApps,
        CancellationToken cancellationToken = default)
    {
        var appSnapshot = installedApps.ToList();
        return await Task.Run(() =>
        {
            var orphans = new List<OrphanAppFolderItem>();

            // Build token set from installed app names & publishers + known safe system folders
            var knownKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft", "Windows", "Google", "Mozilla", "BraveSoftware", "Apple", "Intel",
                "NVIDIA", "AMD", "Realtek", "Package Cache", "Packages", "Temp", "ConnectedDevicesPlatform",
                "Comms", "D3DSCache", "Diagnostics", "FontCache", "IdentityNexusIntegration", "NuGet",
                "npm-cache", "pip", "Programs", "Publishers", "SquirrelTemp", "VirtualStore",
                "FreeCleaner", "Cursor", "Code", "GitHub", "Git", "JetBrains", "Docker", "Steam",
                "Discord", "Slack", "Spotify", "Zoom", "Adobe", "OneDrive", "PlaceholderTileLogoFolder"
            };

            foreach (var app in appSnapshot)
            {
                foreach (var word in SplitTokens(app.DisplayName))
                {
                    if (word.Length >= 3)
                        knownKeywords.Add(word);
                }
                foreach (var word in SplitTokens(app.Publisher))
                {
                    if (word.Length >= 3)
                        knownKeywords.Add(word);
                }
            }

            // Also add running process names so we never flag active portable apps
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    if (!string.IsNullOrWhiteSpace(proc.ProcessName) && proc.ProcessName.Length >= 3)
                        knownKeywords.Add(proc.ProcessName);
                }
            }
            catch
            {
                // Ignore
            }

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            ScanRootForOrphans(roaming, "AppData\\Roaming", knownKeywords, orphans, cancellationToken);
            ScanRootForOrphans(local, "AppData\\Local", knownKeywords, orphans, cancellationToken);

            return orphans.OrderByDescending(x => x.SizeBytes).ToList();
        }, cancellationToken);
    }

    private static void ScanRootForOrphans(
        string rootPath,
        string locationType,
        HashSet<string> knownKeywords,
        List<OrphanAppFolderItem> output,
        CancellationToken ct)
    {
        if (!Directory.Exists(rootPath)) return;

        try
        {
            foreach (var dir in new DirectoryInfo(rootPath).GetDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if (dir.Name.StartsWith('.')) continue;

                // Only consider folders not modified in the last 30 days to be extra safe
                if (dir.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-30))
                    continue;

                bool matchesInstalled = knownKeywords.Any(k =>
                    dir.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    k.Contains(dir.Name, StringComparison.OrdinalIgnoreCase));

                if (matchesInstalled)
                    continue;

                // Check if directory contains any .exe (if it has an .exe it might be a standalone app)
                try
                {
                    if (dir.GetFiles("*.exe", SearchOption.AllDirectories).Length > 0)
                        continue;

                    long folderSize = CalculateFolderSizeSafe(dir, maxDepth: 3);
                    if (folderSize > 64 * 1024) // > 64 KB
                    {
                        output.Add(new OrphanAppFolderItem
                        {
                            FolderName = dir.Name,
                            FullPath = dir.FullName,
                            LocationType = locationType,
                            SizeBytes = folderSize,
                            LastModified = dir.LastWriteTime,
                            IsSelected = false // Default unchecked so user reviews before deleting
                        });
                    }
                }
                catch
                {
                    // Skip inaccessible
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    private static long CalculateFolderSizeSafe(DirectoryInfo dir, int maxDepth)
    {
        if (maxDepth < 0) return 0;
        long total = 0;
        try
        {
            foreach (var f in dir.GetFiles())
                total += f.Length;

            if (maxDepth > 0)
            {
                foreach (var sub in dir.GetDirectories())
                    total += CalculateFolderSizeSafe(sub, maxDepth - 1);
            }
        }
        catch
        {
            // Ignore access errors
        }
        return total;
    }

    private static IEnumerable<string> SplitTokens(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];
        return input.Split([' ', '-', '_', '.', ',', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FormatRegistryDate(string raw)
    {
        if (raw.Length == 8 &&
            int.TryParse(raw[..4], out int y) &&
            int.TryParse(raw[4..6], out int m) &&
            int.TryParse(raw[6..8], out int d))
        {
            return $"{d:D2}.{m:D2}.{y}";
        }
        return string.IsNullOrWhiteSpace(raw) ? "-" : raw;
    }
}
