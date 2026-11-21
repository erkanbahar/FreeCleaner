using System.IO;
using System.Text.Json;

namespace FreeCleaner.Services;

public sealed class CustomCleaningProfile
{
    public string Name { get; set; } = string.Empty;
    public List<string> RuleIds { get; set; } = [];
}

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
    public List<string> FavoriteSections { get; set; } = ["Dashboard", "All", "LiveMonitor", "DiskBench", "WingetUpdater"];
    public string SelectedTheme { get; set; } = "ObsidianNeon";
    public string SelectedLanguage { get; set; } = "tr-TR";
    public bool ProactiveStorageAlertsEnabled { get; set; } = true;
    public int StorageAlertThresholdDays { get; set; } = 15;
    public string LastStorageForecastAlertDate { get; set; } = string.Empty;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool HasCompletedOnboarding { get; set; }
    public List<CustomCleaningProfile> CustomProfiles { get; set; } = [];
}

public static class ExclusionAndSettingsService
{
    private static string ConfigFolder => AppStoragePathsService.GetDataDirectory();

    private static string ConfigFilePath => Path.Combine(ConfigFolder, "settings.json");

    public static CleanerConfigData Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                var data = JsonSerializer.Deserialize<CleanerConfigData>(json);
                if (data is not null)
                {
                    if (!json.Contains("\"HasCompletedOnboarding\"", StringComparison.Ordinal))
                        data.HasCompletedOnboarding = true;

                    data.CustomProfiles ??= [];
                    return data;
                }
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
