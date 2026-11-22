using System.IO;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class BackupManagerService
{
    public static List<BackupFileItem> ListBackups(
        string? dataDirectoryOverride = null,
        string? legacyHostsDirectoryOverride = null)
    {
        var backups = new List<BackupFileItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string dataRoot = string.IsNullOrWhiteSpace(dataDirectoryOverride)
            ? AppStoragePathsService.GetDataDirectory()
            : dataDirectoryOverride;

        string registryDirectory = Path.Combine(dataRoot, "RegistryBackups");
        string hostsDirectory = Path.Combine(dataRoot, "HostsBackups");
        string sqliteDirectory = Path.Combine(dataRoot, "SQLiteBackups");
        string legacyHostsDirectory = string.IsNullOrWhiteSpace(legacyHostsDirectoryOverride)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FreeCleaner_Backups")
            : legacyHostsDirectoryOverride;

        AddFiles(backups, seenPaths, registryDirectory, "*.reg", "Registry");
        AddFiles(backups, seenPaths, hostsDirectory, "hosts_*.bak", "Hosts");
        if (!string.Equals(hostsDirectory, legacyHostsDirectory, StringComparison.OrdinalIgnoreCase))
            AddFiles(backups, seenPaths, legacyHostsDirectory, "hosts_*.bak", "Hosts");
        AddFiles(backups, seenPaths, sqliteDirectory, "*.bak", "SQLite");

        return backups
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public static (bool Success, string Message) Restore(BackupFileItem backup)
    {
        if (backup.BackupType.Equals("Registry", StringComparison.OrdinalIgnoreCase))
            return RegistryCleanerService.RestoreBackupFile(backup.FilePath);

        if (backup.BackupType.Equals("Hosts", StringComparison.OrdinalIgnoreCase))
            return PrivacyAndNetworkShieldService.RestoreHostsBackup(backup.FilePath);

        return (false, LocalizationService.GetText(
            "SQLite .bak yedekleri salt okunur anlık görüntülerdir; ilgili tarayıcı/IDE kapalıyken manuel kopyalanabilir.",
            "SQLite .bak backups are raw snapshots; copy them manually while the browser/IDE is closed."));
    }

    public static (int DeletedCount, long FreedBytes, string Message) DeleteBackups(IEnumerable<BackupFileItem> items)
    {
        int deleted = 0;
        long freed = 0;

        foreach (BackupFileItem item in items)
        {
            try
            {
                if (File.Exists(item.FilePath))
                {
                    long len = new FileInfo(item.FilePath).Length;
                    File.Delete(item.FilePath);
                    deleted++;
                    freed += len;
                }
            }
            catch (IOException ex)
            {
                ErrorLogService.Log("BackupManager:Delete", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ErrorLogService.Log("BackupManager:Delete", ex);
            }
        }

        string msg = LocalizationService.GetText(
            $"{deleted} yedek dosyası silindi ({CleanerRule.FormatBytes(freed)} açıldı).",
            $"Deleted {deleted} backup files ({CleanerRule.FormatBytes(freed)} freed).");

        return (deleted, freed, msg);
    }

    public static (int DeletedCount, long FreedBytes, string Message) PruneOldBackups(
        int keepLatestPerType = 10,
        int maxAgeDays = 30,
        string? dataDirectoryOverride = null,
        string? legacyHostsDirectoryOverride = null,
        DateTime? nowOverride = null)
    {
        int safeKeep = Math.Max(1, keepLatestPerType);
        DateTime now = nowOverride ?? DateTime.Now;
        List<BackupFileItem> all = ListBackups(dataDirectoryOverride, legacyHostsDirectoryOverride);

        int deleted = 0;
        long freed = 0;

        foreach (var group in all.GroupBy(b => b.BackupType, StringComparer.OrdinalIgnoreCase))
        {
            List<BackupFileItem> ordered = group.OrderByDescending(b => b.CreatedAt).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                // Always retain the most recent backup per type.
                if (i == 0)
                    continue;

                BackupFileItem item = ordered[i];
                bool exceedsCount = i >= safeKeep;
                bool exceedsAge = (now - item.CreatedAt).TotalDays > maxAgeDays;
                if (!exceedsCount && !exceedsAge)
                    continue;

                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        long len = new FileInfo(item.FilePath).Length;
                        File.Delete(item.FilePath);
                        deleted++;
                        freed += len;
                    }
                }
                catch (IOException ex)
                {
                    ErrorLogService.Log("BackupManager:Prune", ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    ErrorLogService.Log("BackupManager:Prune", ex);
                }
            }
        }

        string msg = deleted > 0
            ? LocalizationService.GetText(
                $"{deleted} eski yedek dosyası budandı ({CleanerRule.FormatBytes(freed)} açıldı).",
                $"Pruned {deleted} old backup files ({CleanerRule.FormatBytes(freed)} freed).")
            : LocalizationService.GetText(
                "Budanacak eski yedek dosyası bulunmadı.",
                "No old backup files needed pruning.");

        return (deleted, freed, msg);
    }

    private static void AddFiles(
        ICollection<BackupFileItem> target,
        ISet<string> seenPaths,
        string directory,
        string pattern,
        string type)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string filePath in Directory.EnumerateFiles(directory, pattern))
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!seenPaths.Add(info.FullName))
                    continue;

                target.Add(new BackupFileItem
                {
                    FilePath = info.FullName,
                    BackupType = type,
                    CreatedAt = info.LastWriteTime,
                    SizeBytes = info.Length
                });
            }
            catch (IOException)
            {
                // Ignore files removed while the list is being refreshed.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore inaccessible backup files.
            }
        }
    }
}
