using System.IO;
using System.Text.Json;

namespace FreeCleaner.Services;

public class CleanerConfigData
{
    public List<string> ExcludedPaths { get; set; } =
    [
        @"C:\Windows\System32",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")
    ];

    public bool AutoMonitorEnabled { get; set; }
    public int AutoMonitorThresholdMb { get; set; } = 1024;
    public bool MinimizeToTrayOnClose { get; set; }
    public bool UseRecycleBinSafety { get; set; } = true;
    public string RiskFilter { get; set; } = "Tümü";
    public bool ScheduledMaintenanceEnabled { get; set; }
    public int ScheduledMaintenanceIntervalHours { get; set; } = 168;
    public List<string> SelectedRuleIds { get; set; } = [];
}

public static class ExclusionAndSettingsService
{
    private static readonly string ConfigFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FreeCleaner");

    private static readonly string ConfigFilePath = Path.Combine(ConfigFolder, "settings.json");

    public static CleanerConfigData Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                var data = JsonSerializer.Deserialize<CleanerConfigData>(json);
                if (data is not null)
                    return data;
            }
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Ayar yükleme", ex);
        }
        return new CleanerConfigData();
    }

    public static void Save(CleanerConfigData config)
    {
        try
        {
            Directory.CreateDirectory(ConfigFolder);
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Ayar kaydetme", ex);
        }
    }

    public static bool IsPathExcluded(string fullPath, IReadOnlyList<string> exclusions)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || exclusions.Count == 0)
            return false;

        foreach (string ex in exclusions)
        {
            if (string.IsNullOrWhiteSpace(ex))
                continue;

            if (fullPath.StartsWith(ex, StringComparison.OrdinalIgnoreCase) ||
                fullPath.Contains(ex, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
