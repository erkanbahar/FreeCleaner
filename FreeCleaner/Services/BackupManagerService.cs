using System.IO;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class BackupManagerService
{
    public static List<BackupFileItem> ListBackups()
    {
        var backups = new List<BackupFileItem>();
        string registryDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeCleaner",
            "RegistryBackups");
        string hostsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "FreeCleaner_Backups");

        AddFiles(backups, registryDirectory, "*.reg", "Registry");
        AddFiles(backups, hostsDirectory, "hosts_*.bak", "Hosts");

        return backups
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public static (bool Success, string Message) Restore(BackupFileItem backup)
        => backup.BackupType == "Registry"
            ? RegistryCleanerService.RestoreBackupFile(backup.FilePath)
            : PrivacyAndNetworkShieldService.RestoreHostsBackup(backup.FilePath);

    private static void AddFiles(
        ICollection<BackupFileItem> target,
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
