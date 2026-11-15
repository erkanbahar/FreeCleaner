using System.IO;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class StartupManagerService
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run_FreeCleanerDisabled";

    public static List<StartupItemModel> GetStartupItems()
    {
        var items = new List<StartupItemModel>();

        // 1. Current User Registry Run (Enabled & Disabled by FreeCleaner)
        ReadRegistryStartupKey(Registry.CurrentUser, RunSubKey, isCurrentUser: true, isEnabled: true, items);
        ReadRegistryStartupKey(Registry.CurrentUser, DisabledRunSubKey, isCurrentUser: true, isEnabled: false, items);

        // 2. Local Machine Registry Run
        ReadRegistryStartupKey(Registry.LocalMachine, RunSubKey, isCurrentUser: false, isEnabled: true, items);
        ReadRegistryStartupKey(Registry.LocalMachine, DisabledRunSubKey, isCurrentUser: false, isEnabled: false, items);

        // 3. User Startup Folder Shortcuts
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (Directory.Exists(startupFolder))
        {
            try
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    string ext = Path.GetExtension(file);
                    if (ext.Equals(".ini", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isDisabled = ext.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                    string cleanName = isDisabled
                        ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file))
                        : Path.GetFileNameWithoutExtension(file);

                    items.Add(new StartupItemModel
                    {
                        Name = cleanName,
                        Command = file,
                        Location = "Başlangıç Klasörü (Startup)",
                        RegistryKeyPath = startupFolder,
                        IsCurrentUser = true,
                        IsFolderShortcut = true,
                        ImpactLevel = EstimateImpact(cleanName, file),
                        IsEnabled = !isDisabled,
                        StatusText = !isDisabled ? "Etkin" : "Devre Dışı"
                    });
                }
            }
            catch
            {
                // Ignore folder read errors
            }
        }

        return items.OrderByDescending(x => x.IsEnabled).ThenBy(x => x.Name).ToList();
    }

    private static void ReadRegistryStartupKey(
        RegistryKey root,
        string subKeyPath,
        bool isCurrentUser,
        bool isEnabled,
        List<StartupItemModel> targetList)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null) return;

            foreach (string valueName in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                string command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(command)) continue;

                targetList.Add(new StartupItemModel
                {
                    Name = valueName,
                    Command = command,
                    Location = isCurrentUser ? "HKCU\\...\\Run (Kullanıcı)" : "HKLM\\...\\Run (Sistem)",
                    RegistryKeyPath = $"{(isCurrentUser ? "HKCU" : "HKLM")}\\{subKeyPath}",
                    IsCurrentUser = isCurrentUser,
                    IsFolderShortcut = false,
                    ImpactLevel = EstimateImpact(valueName, command),
                    IsEnabled = isEnabled,
                    StatusText = isEnabled ? "Etkin" : "Devre Dışı"
                });
            }
        }
        catch
        {
            // Ignore registry permission errors
        }
    }

    public static bool ToggleStartupItem(StartupItemModel item, bool enable, out string message)
    {
        try
        {
            if (item.IsFolderShortcut)
            {
                string currentPath = item.Command;
                if (!File.Exists(currentPath))
                {
                    message = "Başlangıç kısayol dosyası bulunamadı.";
                    return false;
                }

                string newPath;
                if (enable && currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    newPath = currentPath[..^".disabled".Length];
                }
                else if (!enable && !currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    newPath = currentPath + ".disabled";
                }
                else
                {
                    message = "Durum zaten güncel.";
                    return true;
                }

                File.Move(currentPath, newPath, overwrite: true);
                item.IsEnabled = enable;
                message = $"{item.Name} başlangıç durumu '{(enable ? "Etkin" : "Devre Dışı")}' olarak güncellendi.";
                return true;
            }
            else
            {
                RegistryKey root = item.IsCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
                string sourceSubKey = enable ? DisabledRunSubKey : RunSubKey;
                string targetSubKey = enable ? RunSubKey : DisabledRunSubKey;

                using var srcKey = root.OpenSubKey(sourceSubKey, writable: true);
                using var dstKey = root.CreateSubKey(targetSubKey, writable: true);

                if (srcKey is null || dstKey is null)
                {
                    message = "Kayıt defteri anahtarına yazma izni yok (Yönetici olarak çalıştırın).";
                    return false;
                }

                object? val = srcKey.GetValue(item.Name);
                if (val is null)
                {
                    message = "Kayıt defteri değeri bulunamadı.";
                    return false;
                }

                dstKey.SetValue(item.Name, val);
                srcKey.DeleteValue(item.Name, throwOnMissingValue: false);

                item.IsEnabled = enable;
                message = $"{item.Name} başlangıç durumu '{(enable ? "Etkin" : "Devre Dışı")}' olarak güncellendi.";
                return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            message = "Bu başlangıç öğesini değiştirmek için Yönetici (Administrator) izni gerekiyor.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Hata: {ex.Message}";
            return false;
        }
    }

    public static bool DeleteStartupItem(StartupItemModel item, out string message)
    {
        try
        {
            if (item.IsFolderShortcut)
            {
                if (File.Exists(item.Command))
                {
                    RecycleBinHelper.MoveFileToRecycleBin(item.Command);
                }
                message = $"{item.Name} başlangıç klasöründen kaldırıldı.";
                return true;
            }
            else
            {
                RegistryKey root = item.IsCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
                string subKey = item.IsEnabled ? RunSubKey : DisabledRunSubKey;
                using var key = root.OpenSubKey(subKey, writable: true);
                key?.DeleteValue(item.Name, throwOnMissingValue: false);
                message = $"{item.Name} başlangıç listesinden silindi.";
                return true;
            }
        }
        catch (Exception ex)
        {
            message = $"Silme hatası: {ex.Message}";
            return false;
        }
    }

    private static string EstimateImpact(string name, string command)
    {
        string lower = (name + " " + command).ToLowerInvariant();
        if (lower.Contains("steam") || lower.Contains("epic") || lower.Contains("discord") ||
            lower.Contains("teams") || lower.Contains("adobe") || lower.Contains("docker") ||
            lower.Contains("spotify") || lower.Contains("onedrive"))
        {
            return "Yüksek";
        }

        if (lower.Contains("update") || lower.Contains("helper") || lower.Contains("tray") || lower.Contains("audio"))
        {
            return "Orta";
        }

        return "Düşük";
    }
}
