using System.IO;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class RegistryCleanerService
{
    public static async Task<List<RegistryIssueItem>> ScanRegistryIssuesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var issues = new List<RegistryIssueItem>();

            // 1. Scan Application Paths (HKCU & HKLM App Paths)
            ScanAppPaths(Registry.CurrentUser, isCurrentUser: true, issues, cancellationToken);
            ScanAppPaths(Registry.LocalMachine, isCurrentUser: false, issues, cancellationToken);

            // 2. Scan MUI Cache (Missing executable references)
            ScanMuiCache(issues, cancellationToken);

            // 3. Scan Windows Explorer Compatibility / AppCompatFlags Store for deleted executables
            ScanAppCompatStore(issues, cancellationToken);

            // 4. Scan CurrentUser Run for missing executables
            ScanBrokenRunEntries(issues, cancellationToken);

            return issues;
        }, cancellationToken);
    }

    private static void ScanAppPaths(RegistryKey root, bool isCurrentUser, List<RegistryIssueItem> issues, CancellationToken ct)
    {
        const string subPath = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
        try
        {
            using var appPathsKey = root.OpenSubKey(subPath, writable: false);
            if (appPathsKey is null) return;

            foreach (string exeKeyName in appPathsKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var subKey = appPathsKey.OpenSubKey(exeKeyName, writable: false);
                    string? defaultVal = subKey?.GetValue(null)?.ToString();
                    if (string.IsNullOrWhiteSpace(defaultVal)) continue;

                    string cleanPath = ExtractExecutablePath(defaultVal);
                    if (!string.IsNullOrEmpty(cleanPath) && Path.IsPathRooted(cleanPath) && !File.Exists(cleanPath))
                    {
                        issues.Add(new RegistryIssueItem
                        {
                            Category = "Geçersiz Uygulama Yolu (App Paths)",
                            KeyPath = $"{(isCurrentUser ? "HKCU" : "HKLM")}\\{subPath}\\{exeKeyName}",
                            SubKeyPath = $"{subPath}\\{exeKeyName}",
                            ValueName = "(Default)",
                            ProblemDescription = $"Uygulama dosyası bulunamadı: {Path.GetFileName(cleanPath)}",
                            MissingTarget = cleanPath,
                            IsCurrentUser = isCurrentUser
                        });
                    }
                }
                catch
                {
                    // Skip inaccessible subkey
                }
            }
        }
        catch
        {
            // Ignore access errors
        }
    }

    private static void ScanMuiCache(List<RegistryIssueItem> issues, CancellationToken ct)
    {
        const string muiPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(muiPath, writable: false);
            if (key is null) return;

            foreach (string valName in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(valName) || valName.StartsWith('@')) continue;

                // MuiCache value names look like "C:\Path\app.exe.FriendlyAppName"
                string candidatePath = valName;
                if (candidatePath.EndsWith(".FriendlyAppName", StringComparison.OrdinalIgnoreCase))
                    candidatePath = candidatePath[..^".FriendlyAppName".Length];
                else if (candidatePath.EndsWith(".ApplicationCompany", StringComparison.OrdinalIgnoreCase))
                    candidatePath = candidatePath[..^".ApplicationCompany".Length];

                if (Path.IsPathRooted(candidatePath) &&
                    candidatePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !File.Exists(candidatePath))
                {
                    issues.Add(new RegistryIssueItem
                    {
                        Category = "Eski MUI Önbellek Kaydı (MuiCache)",
                        KeyPath = $"HKCU\\{muiPath}",
                        SubKeyPath = muiPath,
                        ValueName = valName,
                        ProblemDescription = "Silinmiş programa ait kabuk önbellek referansı",
                        MissingTarget = candidatePath,
                        IsCurrentUser = true
                    });
                }
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    private static void ScanAppCompatStore(List<RegistryIssueItem> issues, CancellationToken ct)
    {
        const string compatPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(compatPath, writable: false);
            if (key is null) return;

            foreach (string valName in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(valName) || !Path.IsPathRooted(valName))
                    continue;

                // Ignore removable drives or network paths
                if (!valName.StartsWith("C:\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!File.Exists(valName))
                {
                    issues.Add(new RegistryIssueItem
                    {
                        Category = "Uyumluluk Yardımcısı Kalıntısı (AppCompat)",
                        KeyPath = $"HKCU\\{compatPath}",
                        SubKeyPath = compatPath,
                        ValueName = valName,
                        ProblemDescription = "Kaldırılmış çalıştırılabilir dosya (EXE) geçmişi",
                        MissingTarget = valName,
                        IsCurrentUser = true
                    });
                }
            }
        }
        catch
        {
            // Ignore access errors
        }
    }

    private static void ScanBrokenRunEntries(List<RegistryIssueItem> issues, CancellationToken ct)
    {
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(runPath, writable: false);
            if (key is null) return;

            foreach (string valName in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                string? rawCmd = key.GetValue(valName)?.ToString();
                if (string.IsNullOrWhiteSpace(rawCmd)) continue;

                string exePath = ExtractExecutablePath(rawCmd);
                if (!string.IsNullOrEmpty(exePath) && Path.IsPathRooted(exePath) && !File.Exists(exePath))
                {
                    issues.Add(new RegistryIssueItem
                    {
                        Category = "Kırık Başlangıç Kaydı (Broken Startup)",
                        KeyPath = $"HKCU\\{runPath}",
                        SubKeyPath = runPath,
                        ValueName = valName,
                        ProblemDescription = "Başlangıçta çalıştırılmak istenen dosya artık mevcut değil",
                        MissingTarget = exePath,
                        IsCurrentUser = true
                    });
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    public static string ExportBackupFile(IEnumerable<RegistryIssueItem> itemsToBackup)
    {
        var items = itemsToBackup.ToList();
        if (items.Count == 0)
            throw new InvalidOperationException("Yedeklenecek registry kaydı bulunamadı.");

        string backupDir = AppStoragePathsService.GetRegistryBackupsDirectory();

        Directory.CreateDirectory(backupDir);
        string filePath = Path.Combine(backupDir, $"FreeCleaner_RegBackup_{DateTime.Now:yyyyMMdd_HHmmss}.reg");

        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine($"; FreeCleaner Registry Backup - Created {DateTime.Now:O}");
        sb.AppendLine();

        foreach (var group in items.GroupBy(x => x.KeyPath, StringComparer.OrdinalIgnoreCase))
        {
            bool isCurrentUser = group.First().IsCurrentUser;
            string subKeyPath = group.First().SubKeyPath;
            RegistryKey root = isCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
            using RegistryKey key = root.OpenSubKey(subKeyPath, writable: false)
                ?? throw new InvalidOperationException($"Registry anahtarı okunamadı: {group.Key}");

            string fullHiveKey = $"{(isCurrentUser ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE")}\\{subKeyPath}";
            sb.AppendLine($"[{fullHiveKey}]");
            foreach (string valueName in key.GetValueNames())
            {
                object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value is null)
                    continue;

                RegistryValueKind kind = key.GetValueKind(valueName);
                sb.AppendLine($"{FormatValueName(valueName)}={FormatValue(value, kind)}");
            }
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
        return filePath;
    }

    private static string FormatValueName(string valueName)
        => string.IsNullOrEmpty(valueName)
            ? "@"
            : $"\"{valueName.Replace("\"", "\\\"")}\"";

    private static string FormatValue(object value, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                $"\"{value.ToString()?.Replace("\"", "\\\"") ?? string.Empty}\"",
            RegistryValueKind.DWord =>
                $"dword:{Convert.ToUInt32(value):x8}",
            RegistryValueKind.QWord =>
                $"hex(b):{FormatBytes(BitConverter.GetBytes(Convert.ToUInt64(value)))}",
            RegistryValueKind.Binary =>
                $"hex:{FormatBytes((byte[])value)}",
            RegistryValueKind.MultiString =>
                $"hex(7):{FormatBytes(Encoding.Unicode.GetBytes(string.Join('\0', (string[])value) + "\0\0"))}",
            _ => $"\"{value}\""
        };
    }

    private static string FormatBytes(byte[] bytes)
        => string.Join(",", bytes.Select(b => b.ToString("x2")));

    public static string? GetLatestBackupFile()
    {
        string backupDir = AppStoragePathsService.GetRegistryBackupsDirectory();
        return Directory.Exists(backupDir)
            ? Directory.EnumerateFiles(backupDir, "*.reg")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
    }

    public static (bool Success, string Message) RestoreBackupFile(string backupPath)
    {
        if (!File.Exists(backupPath))
            return (false, "Registry yedek dosyası bulunamadı.");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"import \"{backupPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit();
            return process?.ExitCode == 0
                ? (true, $"Registry yedeği geri yüklendi: {backupPath}")
                : (false, "Registry yedeği geri yüklenemedi.");
        }
        catch (Win32Exception)
        {
            return (false, "Yönetici izni verilmedi veya registry geri yükleme iptal edildi.");
        }
        catch (Exception ex)
        {
            return (false, $"Registry geri yükleme hatası: {ex.Message}");
        }
    }

    public static (int FixedCount, int SkippedCount, string BackupPath) FixRegistryIssues(IEnumerable<RegistryIssueItem> selectedIssues)
    {
        var list = selectedIssues.Where(x => x.IsSelected).ToList();
        if (list.Count == 0)
            return (0, 0, string.Empty);

        string backupPath = ExportBackupFile(list);
        int fixedCount = 0;
        int skippedCount = 0;

        foreach (var issue in list)
        {
            try
            {
                RegistryKey root = issue.IsCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
                if (issue.ValueName == "(Default)" && issue.Category.Contains("App Paths"))
                {
                    root.DeleteSubKeyTree(issue.SubKeyPath, throwOnMissingSubKey: false);
                    fixedCount++;
                }
                else
                {
                    using var key = root.OpenSubKey(issue.SubKeyPath, writable: true);
                    if (key is not null)
                    {
                        key.DeleteValue(issue.ValueName, throwOnMissingValue: false);
                        fixedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
            }
            catch
            {
                skippedCount++;
            }
        }

        return (fixedCount, skippedCount, backupPath);
    }

    private static string ExtractExecutablePath(string commandLine)
    {
        string trimmed = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        if (trimmed.StartsWith('"'))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
                return trimmed[1..endQuote];
        }

        int exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
        {
            return trimmed[..(exeIdx + 4)];
        }

        return trimmed;
    }
}
