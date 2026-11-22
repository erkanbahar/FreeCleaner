using System.IO;
using System.Text;
using System.Text.Json;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public enum CliLaunchActionKind
{
    None,
    ScheduledMaintenance,
    OpenTreemap,
    OpenExifScrubber,
    OpenSimilarPhotos,
    OpenDeepSpace,
    QuickHealthCheck,
    DryRunReport
}

public sealed record CliLaunchAction(
    CliLaunchActionKind Kind,
    string? TargetPath = null,
    bool StartMinimized = false,
    bool DryRun = false,
    string? ReportJsonPath = null,
    CleaningProfileKind? Profile = null,
    bool ExitWhenFinished = false,
    string? CustomProfileName = null);

public sealed record CliCategoryBreakdownItem(string Category, int FileCount, long Bytes);

public sealed record CliAutomationReport(
    DateTime GeneratedUtc,
    string AppVersion,
    string Profile,
    bool DryRun,
    int TotalFiles,
    long TotalBytes,
    string TotalSizeFormatted,
    long ElapsedMilliseconds,
    List<CliCategoryBreakdownItem> Categories);

public enum CleaningProfileKind
{
    SafeDaily,
    DeveloperDeep,
    GamerBoost,
    MaxPrivacy
}

public static class ShellIntegrationAndProfilesService
{
    private const string DirShellSubKey = @"Software\Classes\Directory\shell\FreeCleanerPro";
    private const string BgShellSubKey = @"Software\Classes\Directory\Background\shell\FreeCleanerPro";
    private const string RunAutoStartSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunAutoStartValueName = "FreeCleanerPro";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static CliLaunchAction ParseStartupArguments(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
            return new CliLaunchAction(CliLaunchActionKind.None);

        CliLaunchActionKind kind = CliLaunchActionKind.None;
        string? targetPath = null;
        bool startMinimized = false;
        bool dryRun = false;
        string? reportJsonPath = null;
        CleaningProfileKind? profile = null;
        bool exitWhenFinished = false;
        string? customProfileName = null;

        for (int i = 0; i < args.Count; i++)
        {
            string raw = args[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                continue;

            string flag = raw;
            string? inlineValue = null;
            int eqIndex = raw.IndexOf('=');
            if (eqIndex > 0)
            {
                flag = raw[..eqIndex];
                inlineValue = CleanCliPath(raw[(eqIndex + 1)..]);
            }

            if (flag.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                flag.Equals("--tray", StringComparison.OrdinalIgnoreCase))
            {
                startMinimized = true;
                continue;
            }

            if (flag.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            if (flag.Equals("--exit", StringComparison.OrdinalIgnoreCase) ||
                flag.Equals("--headless", StringComparison.OrdinalIgnoreCase))
            {
                exitWhenFinished = true;
                continue;
            }

            if (flag.Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                string? rawProfile = inlineValue;
                if (string.IsNullOrWhiteSpace(rawProfile))
                {
                    rawProfile = ReadNextPathArg(args, i);
                    if (rawProfile is not null)
                        i++;
                }

                if (Enum.TryParse<CleaningProfileKind>(rawProfile, ignoreCase: true, out CleaningProfileKind parsedProfile))
                {
                    profile = parsedProfile;
                    customProfileName = null;
                }
                else if (!string.IsNullOrWhiteSpace(rawProfile))
                {
                    customProfileName = rawProfile;
                    profile = null;
                }
                continue;
            }

            if (flag.Equals("--report-json", StringComparison.OrdinalIgnoreCase))
            {
                string? rawReport = inlineValue;
                if (string.IsNullOrWhiteSpace(rawReport))
                {
                    rawReport = ReadNextPathArg(args, i);
                    if (rawReport is not null)
                        i++;
                }

                if (!string.IsNullOrWhiteSpace(rawReport))
                    reportJsonPath = rawReport;
                continue;
            }

            if (kind != CliLaunchActionKind.None)
                continue;

            if (flag.Equals("--scheduled-maintenance", StringComparison.OrdinalIgnoreCase))
                kind = CliLaunchActionKind.ScheduledMaintenance;
            else if (flag.Equals("--deep-space", StringComparison.OrdinalIgnoreCase))
                kind = CliLaunchActionKind.OpenDeepSpace;
            else if (flag.Equals("--health-check", StringComparison.OrdinalIgnoreCase))
                kind = CliLaunchActionKind.QuickHealthCheck;
            else if (flag.Equals("--treemap", StringComparison.OrdinalIgnoreCase))
            {
                kind = CliLaunchActionKind.OpenTreemap;
                targetPath = inlineValue ?? ReadNextPathArg(args, i);
            }
            else if (flag.Equals("--exif", StringComparison.OrdinalIgnoreCase))
            {
                kind = CliLaunchActionKind.OpenExifScrubber;
                targetPath = inlineValue ?? ReadNextPathArg(args, i);
            }
            else if (flag.Equals("--similar", StringComparison.OrdinalIgnoreCase))
            {
                kind = CliLaunchActionKind.OpenSimilarPhotos;
                targetPath = inlineValue ?? ReadNextPathArg(args, i);
            }
        }

        if (kind == CliLaunchActionKind.None && (dryRun || !string.IsNullOrWhiteSpace(reportJsonPath)))
        {
            kind = CliLaunchActionKind.DryRunReport;
            dryRun = true;
        }

        return new CliLaunchAction(
            kind,
            targetPath,
            startMinimized,
            dryRun,
            reportJsonPath,
            profile,
            exitWhenFinished,
            customProfileName);
    }

    public static string WriteCliAutomationReportJson(string outputPath, CliAutomationReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(report);

        string fullPath = Path.GetFullPath(outputPath);
        AtomicFileStorageService.WriteAllTextAtomic(
            fullPath,
            JsonSerializer.Serialize(report, JsonOptions),
            Encoding.UTF8,
            createBackup: false);
        return fullPath;
    }

    public static bool IsAutoStartWithWindowsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunAutoStartSubKey, writable: false);
            return key?.GetValue(RunAutoStartValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, string Message) SetAutoStartWithWindows(bool enabled, string? executablePath = null)
    {
        try
        {
            if (enabled)
            {
                string exe = string.IsNullOrWhiteSpace(executablePath)
                    ? (Environment.ProcessPath ?? AppContext.BaseDirectory)
                    : executablePath;

                if (!File.Exists(exe) && exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Path.ChangeExtension(exe, ".exe");
                    if (File.Exists(candidate))
                        exe = candidate;
                }

                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunAutoStartSubKey, writable: true);
                key.SetValue(RunAutoStartValueName, $"\"{exe}\" --minimized", RegistryValueKind.String);
                return (true, "Windows açılışında sistem tepsisinde sessiz başlatma etkinleştirildi.");
            }
            else
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunAutoStartSubKey, writable: true);
                key?.DeleteValue(RunAutoStartValueName, throwOnMissingValue: false);
                return (true, "Windows açılışında otomatik başlatma devre dışı bırakıldı.");
            }
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Otomatik başlatma ayarı", ex);
            return (false, $"Otomatik başlatma ayarı güncellenemedi: {ex.Message}");
        }
    }

    private static string? ReadNextPathArg(IReadOnlyList<string> args, int flagIndex)
    {
        if (flagIndex + 1 >= args.Count)
            return null;

        string next = args[flagIndex + 1]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(next) || next.StartsWith("--", StringComparison.Ordinal))
            return null;

        return CleanCliPath(next);
    }

    private static string? CleanCliPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string cleaned = value.Trim().Trim('"');
        if (cleaned.Length == 2 && char.IsAsciiLetter(cleaned[0]) && cleaned[1] == ':')
            cleaned += Path.DirectorySeparatorChar;

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    public static bool IsExplorerContextMenuInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DirShellSubKey);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, string Message) InstallExplorerContextMenu(string? executablePath = null)
    {
        try
        {
            string exe = string.IsNullOrWhiteSpace(executablePath)
                ? (Environment.ProcessPath ?? AppContext.BaseDirectory)
                : executablePath;

            if (!File.Exists(exe) && exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = Path.ChangeExtension(exe, ".exe");
                if (File.Exists(candidate))
                    exe = candidate;
            }

            WriteCascadingShellEntry(DirShellSubKey, exe);
            WriteCascadingShellEntry(BgShellSubKey, exe);
            return (true, "Windows Gezgini sağ tık menüsü (HKCU) başarıyla yüklendi.");
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Explorer bağlam menüsü yükleme", ex);
            return (false, $"Bağlam menüsü eklenemedi: {ex.Message}");
        }
    }

    public static (bool Success, string Message) RemoveExplorerContextMenu()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(DirShellSubKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(BgShellSubKey, throwOnMissingSubKey: false);
            return (true, "Windows Gezgini sağ tık menüsü kaldırıldı.");
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Explorer bağlam menüsü kaldırma", ex);
            return (false, $"Bağlam menüsü kaldırılamadı: {ex.Message}");
        }
    }

    private static void WriteCascadingShellEntry(string rootSubKey, string exePath)
    {
        string iconValue = $"\"{exePath}\",0";
        using var root = Registry.CurrentUser.CreateSubKey(rootSubKey, writable: true);
        root.SetValue("MUIVerb", "FreeCleaner Pro ile Analiz Et");
        root.SetValue("Icon", iconValue);
        root.SetValue("SubCommands", string.Empty);

        WriteSubCommand(rootSubKey, "01_Treemap", "📊 Disk Treemap (Yer Kaplayanlar)", iconValue, $"\"{exePath}\" --treemap \"%V\"");
        WriteSubCommand(rootSubKey, "02_Exif", "📷 Fotoğraf EXIF / GPS Temizle", iconValue, $"\"{exePath}\" --exif \"%V\"");
        WriteSubCommand(rootSubKey, "03_Similar", "🖼️ Benzer Fotoğrafları Bul", iconValue, $"\"{exePath}\" --similar \"%V\"");
    }

    private static void WriteSubCommand(string rootSubKey, string childName, string label, string iconValue, string commandLine)
    {
        string itemPath = $@"{rootSubKey}\shell\{childName}";
        using var itemKey = Registry.CurrentUser.CreateSubKey(itemPath, writable: true);
        itemKey.SetValue("MUIVerb", label);
        itemKey.SetValue("Icon", iconValue);

        using var cmdKey = itemKey.CreateSubKey("command", writable: true);
        cmdKey.SetValue(string.Empty, commandLine);
    }

    public static (int SelectedCount, string ProfileDescription) ApplyProfileToRules(
        IEnumerable<CleanerRule> rules,
        CleaningProfileKind profile)
    {
        int count = 0;
        foreach (CleanerRule rule in rules)
        {
            bool shouldSelect = ShouldSelectRuleForProfile(rule, profile);
            rule.IsSelected = shouldSelect;
            if (shouldSelect)
                count++;
        }

        string desc = profile switch
        {
            CleaningProfileKind.SafeDaily => $"🛡️ Güvenli Günlük profili uygulandı ({count} düşük riskli kural seçildi).",
            CleaningProfileKind.DeveloperDeep => $"🛠️ Geliştirici Derin profili uygulandı ({count} SDK/önbellek + sistem kuralı seçildi).",
            CleaningProfileKind.GamerBoost => $"🎮 Oyuncu Boost profili uygulandı ({count} shader/temp/önbellek kuralı seçildi).",
            CleaningProfileKind.MaxPrivacy => $"🕵️ Maksimum Gizlilik profili uygulandı ({count} tarayıcı/geçmiş/telemetri kuralı seçildi).",
            _ => $"{count} kural seçildi."
        };

        return (count, desc);
    }

    private static bool ShouldSelectRuleForProfile(CleanerRule rule, CleaningProfileKind profile)
    {
        string text = $"{rule.Id} {rule.Name}".ToLowerInvariant();
        bool isPrivacySensitive = text.Contains("cookie", StringComparison.Ordinal) ||
                                  text.Contains("history", StringComparison.Ordinal) ||
                                  text.Contains("gecmis", StringComparison.Ordinal) ||
                                  text.Contains("geçmiş", StringComparison.Ordinal) ||
                                  text.Contains("session", StringComparison.Ordinal) ||
                                  text.Contains("password", StringComparison.Ordinal) ||
                                  text.Contains("oturum", StringComparison.Ordinal);

        return profile switch
        {
            CleaningProfileKind.SafeDaily =>
                rule.RiskLevel == RiskLevel.Low &&
                rule.Group != RuleGroup.DeveloperAndApps &&
                !isPrivacySensitive,

            CleaningProfileKind.DeveloperDeep =>
                rule.Group == RuleGroup.DeveloperAndApps ||
                (rule.Group == RuleGroup.System && rule.RiskLevel != RiskLevel.High),

            CleaningProfileKind.GamerBoost =>
                (rule.Group == RuleGroup.System && rule.RiskLevel == RiskLevel.Low) ||
                text.Contains("shader", StringComparison.Ordinal) ||
                text.Contains("directx", StringComparison.Ordinal) ||
                text.Contains("nvidia", StringComparison.Ordinal) ||
                text.Contains("amd", StringComparison.Ordinal) ||
                text.Contains("gpu", StringComparison.Ordinal) ||
                text.Contains("discord", StringComparison.Ordinal) ||
                text.Contains("steam", StringComparison.Ordinal) ||
                text.Contains("xbox", StringComparison.Ordinal) ||
                text.Contains("prefetch", StringComparison.Ordinal) ||
                text.Contains("dump", StringComparison.Ordinal) ||
                text.Contains("temp", StringComparison.Ordinal),

            CleaningProfileKind.MaxPrivacy =>
                rule.Group == RuleGroup.Browsers ||
                isPrivacySensitive ||
                text.Contains("telemetry", StringComparison.Ordinal) ||
                text.Contains("telemetri", StringComparison.Ordinal) ||
                text.Contains("recent", StringComparison.Ordinal) ||
                text.Contains("son ", StringComparison.Ordinal) ||
                text.Contains("clipboard", StringComparison.Ordinal) ||
                text.Contains("pano", StringComparison.Ordinal) ||
                (rule.Group == RuleGroup.System && rule.RiskLevel == RiskLevel.Low),

            _ => false
        };
    }

    public static void ExportSettingsToFile(CleanerConfigData config, string filePath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string json = JsonSerializer.Serialize(config, JsonOptions);
        AtomicFileStorageService.WriteAllTextAtomic(filePath, json, Encoding.UTF8, createBackup: true);
    }

    public static CleanerConfigData ImportSettingsFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string json = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Ayar dosyası boş.");

        var config = JsonSerializer.Deserialize<CleanerConfigData>(json)
            ?? throw new InvalidDataException("Ayar JSON formatı geçersiz.");

        config.ExcludedPaths ??= [];
        config.SelectedRuleIds ??= [];
        config.FavoriteSections ??= ["Dashboard", "All", "LiveMonitor", "DiskBench", "WingetUpdater"];
        config.CustomProfiles ??= [];
        config.SelectedLanguage = LocalizationService.NormalizeLanguageCode(config.SelectedLanguage);
        if (string.IsNullOrWhiteSpace(config.SelectedTheme))
            config.SelectedTheme = "ObsidianNeon";

        return config;
    }

    public static (int SelectedCount, string ProfileDescription) ApplyCustomProfileToRules(
        IEnumerable<CleanerRule> rules,
        CustomCleaningProfile profile)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(profile);

        HashSet<string> ruleIdSet = (profile.RuleIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int selectedCount = 0;
        foreach (CleanerRule rule in rules)
        {
            bool match = ruleIdSet.Contains(rule.Id);
            rule.IsSelected = match;
            if (match)
                selectedCount++;
        }

        bool isEn = LocalizationService.NormalizeLanguageCode(LocalizationService.CurrentLanguage) == "en-US";
        string desc = isEn
            ? $"⭐ Applied custom profile '{profile.Name}' ({selectedCount} rules selected)."
            : $"⭐ '{profile.Name}' özel profili uygulandı ({selectedCount} kural seçildi).";

        return (selectedCount, desc);
    }
}
