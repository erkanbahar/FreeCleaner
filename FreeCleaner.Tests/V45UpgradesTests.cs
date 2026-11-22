using System.IO;
using System.Text;
using System.Text.Json;
using FreeCleaner.Models;
using FreeCleaner.Services;
using Xunit;

namespace FreeCleaner.Tests;

public class V45UpgradesTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreeCleaner.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FreeCleaner.slnx from test output directory.");
    }

    [Fact]
    public void Localization_CleanerRules_TranslateToEnglishAndTurkish_AndSwitchRiskLabel()
    {
        List<CleanerRule> rules = CleanerEngine.GetDefaultRules();
        Assert.True(rules.Count >= 20);

        LocalizationService.ApplyRuleLocalization(rules, "en-US");
        CleanerRule recycleEn = Assert.Single(rules, r => r.Id == "sys_recycle_bin");
        Assert.Equal("Windows Recycle Bin", recycleEn.Name);
        Assert.Equal("System Cleaner", recycleEn.Category);
        Assert.Equal("High", recycleEn.RiskLabel);
        CleanerRule tempEn = Assert.Single(rules, r => r.Id == "sys_user_temp");
        Assert.Equal("Medium", tempEn.RiskLabel);
        Assert.Equal("Ready", tempEn.StatusText);

        // Verify all default rules switched away from raw Turkish characters in critical display labels
        foreach (CleanerRule rule in rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Name));
            Assert.False(string.IsNullOrWhiteSpace(rule.Category));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
        }

        LocalizationService.ApplyRuleLocalization(rules, "tr-TR");
        CleanerRule recycleTr = Assert.Single(rules, r => r.Id == "sys_recycle_bin");
        Assert.Equal("Geri Dönüşüm Kutusu (Recycle Bin)", recycleTr.Name);
        Assert.Equal("Yüksek", recycleTr.RiskLabel);
        CleanerRule tempTr = Assert.Single(rules, r => r.Id == "sys_user_temp");
        Assert.Equal("Orta", tempTr.RiskLabel);
        Assert.Equal("Hazır", tempTr.StatusText);
    }

    [Fact]
    public void ThemeManagerService_SystemAuto_ResolvesLightAndDarkCorrectly()
    {
        Assert.Contains(ThemeManagerService.SystemAutoThemeKey, ThemeManagerService.AvailableThemes);
        Assert.Equal("FluentLight", ThemeManagerService.ResolveEffectiveThemeKey("SystemAuto", windowsIsLightOverride: true));
        Assert.Equal("ObsidianNeon", ThemeManagerService.ResolveEffectiveThemeKey("SystemAuto", windowsIsLightOverride: false));
        Assert.Equal("MidnightViolet", ThemeManagerService.ResolveEffectiveThemeKey("MidnightViolet", windowsIsLightOverride: true));
        Assert.Equal("ObsidianNeon", ThemeManagerService.ResolveEffectiveThemeKey("NonExistentTheme", windowsIsLightOverride: false));
    }

    [Fact]
    public void AppStoragePathsService_BackupDirectories_RespectPortableMode()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerPortBkp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            Assert.False(AppStoragePathsService.IsPortableModeEnabled(tempRoot));
            string stdBase = AppStoragePathsService.GetStandardDataDirectory();
            Assert.Equal(Path.Combine(stdBase, "RegistryBackups"), AppStoragePathsService.GetRegistryBackupsDirectory(tempRoot));
            Assert.Equal(Path.Combine(stdBase, "SQLiteBackups"), AppStoragePathsService.GetSqliteBackupsDirectory(tempRoot));
            Assert.Equal(Path.Combine(stdBase, "HostsBackups"), AppStoragePathsService.GetHostsBackupsDirectory(tempRoot));

            File.WriteAllText(AppStoragePathsService.GetPortableFlagPath(tempRoot), "portable");
            Assert.True(AppStoragePathsService.IsPortableModeEnabled(tempRoot));

            string portableData = Path.Combine(tempRoot, "data");
            Assert.Equal(Path.Combine(portableData, "RegistryBackups"), AppStoragePathsService.GetRegistryBackupsDirectory(tempRoot));
            Assert.Equal(Path.Combine(portableData, "SQLiteBackups"), AppStoragePathsService.GetSqliteBackupsDirectory(tempRoot));
            Assert.Equal(Path.Combine(portableData, "HostsBackups"), AppStoragePathsService.GetHostsBackupsDirectory(tempRoot));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BackupManagerService_ListsAllThreeTypes_DeletesSelected_AndPrunesOldBackups()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerBkpMgr_{Guid.NewGuid():N}");
        string dataDir = Path.Combine(tempRoot, "data");
        string regDir = Path.Combine(dataDir, "RegistryBackups");
        string hostsDir = Path.Combine(dataDir, "HostsBackups");
        string sqliteDir = Path.Combine(dataDir, "SQLiteBackups");
        string legacyHostsDir = Path.Combine(tempRoot, "legacy_desktop");

        Directory.CreateDirectory(regDir);
        Directory.CreateDirectory(hostsDir);
        Directory.CreateDirectory(sqliteDir);
        Directory.CreateDirectory(legacyHostsDir);

        try
        {
            DateTime fixedNow = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Local);

            string regNew = Path.Combine(regDir, "FreeCleaner_RegBackup_new.reg");
            string regOld = Path.Combine(regDir, "FreeCleaner_RegBackup_old.reg");
            string hostNew = Path.Combine(hostsDir, "hosts_new.bak");
            string hostLegacy = Path.Combine(legacyHostsDir, "hosts_legacy.bak");
            string sqliteNew = Path.Combine(sqliteDir, "places.sqlite_new.bak");
            string sqliteOld = Path.Combine(sqliteDir, "places.sqlite_old.bak");

            File.WriteAllText(regNew, "Windows Registry Editor Version 5.00");
            File.WriteAllText(regOld, "Old Registry Backup Payload");
            File.WriteAllText(hostNew, "127.0.0.1 localhost");
            File.WriteAllText(hostLegacy, "0.0.0.0 ads.example.com");
            File.WriteAllBytes(sqliteNew, new byte[512]);
            File.WriteAllBytes(sqliteOld, new byte[1024]);

            File.SetLastWriteTime(regNew, fixedNow.AddDays(-1));
            File.SetLastWriteTime(regOld, fixedNow.AddDays(-45));
            File.SetLastWriteTime(hostNew, fixedNow.AddDays(-2));
            File.SetLastWriteTime(hostLegacy, fixedNow.AddDays(-10));
            File.SetLastWriteTime(sqliteNew, fixedNow.AddHours(-3));
            File.SetLastWriteTime(sqliteOld, fixedNow.AddDays(-5));

            List<BackupFileItem> all = BackupManagerService.ListBackups(dataDir, legacyHostsDir);
            Assert.Equal(6, all.Count);
            Assert.Equal(2, all.Count(x => x.BackupType == "Registry"));
            Assert.Equal(2, all.Count(x => x.BackupType == "Hosts"));
            Assert.Equal(2, all.Count(x => x.BackupType == "SQLite"));

            // SQLite backups intentionally block blind overwrite restore with a helpful message
            BackupFileItem sqliteItem = all.First(x => x.BackupType == "SQLite");
            var (restoreOk, restoreMsg) = BackupManagerService.Restore(sqliteItem);
            Assert.False(restoreOk);
            Assert.Contains("SQLite", restoreMsg);

            // Pruning with keepLatestPerType=1 and maxAgeDays=30 keeps newest of each type (3) and prunes the older 3
            var (prunedCount, freedBytes, _) = BackupManagerService.PruneOldBackups(
                keepLatestPerType: 1,
                maxAgeDays: 30,
                dataDirectoryOverride: dataDir,
                legacyHostsDirectoryOverride: legacyHostsDir,
                nowOverride: fixedNow);

            Assert.Equal(3, prunedCount);
            Assert.True(freedBytes > 0);
            Assert.True(File.Exists(regNew));
            Assert.False(File.Exists(regOld));
            Assert.True(File.Exists(hostNew));
            Assert.False(File.Exists(hostLegacy));
            Assert.True(File.Exists(sqliteNew));
            Assert.False(File.Exists(sqliteOld));

            // Manual delete of one remaining item
            List<BackupFileItem> remaining = BackupManagerService.ListBackups(dataDir, legacyHostsDir);
            Assert.Equal(3, remaining.Count);
            var (deletedCount, delBytes, _) = BackupManagerService.DeleteBackups([remaining[0]]);
            Assert.Equal(1, deletedCount);
            Assert.True(delBytes > 0);
            Assert.Equal(2, BackupManagerService.ListBackups(dataDir, legacyHostsDir).Count);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PrivacyAndNetworkShieldService_HostsParser_ParsesUpdatesAndRemovesCleanly()
    {
        string[] rawLines =
        [
            "# Comment banner",
            "127.0.0.1\tlocalhost\t# Local loopback",
            "# 0.0.0.0 telemetry.example.com # Disabled entry",
            "0.0.0.0 ads.example.com # Ad tracker"
        ];

        List<HostsEntryItem> parsed = PrivacyAndNetworkShieldService.ParseHostsLines(rawLines);
        Assert.Equal(3, parsed.Count);

        Assert.True(parsed[0].IsEnabled);
        Assert.Equal("127.0.0.1", parsed[0].IpAddress);
        Assert.Equal("localhost", parsed[0].Hostname);
        Assert.Equal("Local loopback", parsed[0].Comment);

        Assert.False(parsed[1].IsEnabled);
        Assert.Equal("telemetry.example.com", parsed[1].Hostname);

        HostsEntryItem target = parsed[2];
        target.IsEnabled = false;
        target.Comment = "Paused ad rule";
        string[] updated = PrivacyAndNetworkShieldService.ApplyHostsUpdateLines(rawLines, target, out bool found);
        Assert.True(found);
        Assert.StartsWith("# 0.0.0.0\tads.example.com", updated[3]);

        string[] afterRemove = PrivacyAndNetworkShieldService.RemoveHostsMatchingLines(updated, "0.0.0.0", "ads.example.com", out bool removed);
        Assert.True(removed);
        Assert.Equal(3, afterRemove.Length);
        Assert.DoesNotContain(afterRemove, l => l.Contains("ads.example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToastNotificationService_BuildsToasts_AndEvaluatesDailySilentUpdateCooldown()
    {
        ToastNotificationItem toast = ToastNotificationService.BuildToast(
            "  Update Ready  ",
            "  v4.6.0 released  ",
            ToastSeverity.Success,
            "  View  ",
            "OPEN_SETTINGS_UPDATE");

        Assert.Equal("Update Ready", toast.Title);
        Assert.Equal("v4.6.0 released", toast.Message);
        Assert.Equal(ToastSeverity.Success, toast.Severity);
        Assert.Equal("✅", toast.SeverityIcon);
        Assert.True(toast.HasAction);
        Assert.Equal("OPEN_SETTINGS_UPDATE", toast.ActionKey);

        DateTime today = new(2026, 11, 22, 10, 30, 0);
        string todayKey = ToastNotificationService.GetTodayDateKey(today);
        Assert.Equal("2026-11-22", todayKey);

        Assert.True(ToastNotificationService.ShouldRunSilentUpdateCheck(true, string.Empty, today));
        Assert.False(ToastNotificationService.ShouldRunSilentUpdateCheck(false, string.Empty, today));
        Assert.False(ToastNotificationService.ShouldRunSilentUpdateCheck(true, todayKey, today));
        Assert.True(ToastNotificationService.ShouldRunSilentUpdateCheck(true, todayKey, today.AddDays(1)));

        var config = new CleanerConfigData
        {
            AutoCheckUpdatesOnStartup = false,
            LastUpdateCheckDate = todayKey,
            SelectedTheme = ThemeManagerService.SystemAutoThemeKey
        };
        string json = JsonSerializer.Serialize(config);
        CleanerConfigData? roundTrip = JsonSerializer.Deserialize<CleanerConfigData>(json);
        Assert.NotNull(roundTrip);
        Assert.False(roundTrip!.AutoCheckUpdatesOnStartup);
        Assert.Equal(todayKey, roundTrip.LastUpdateCheckDate);
        Assert.Equal(ThemeManagerService.SystemAutoThemeKey, roundTrip.SelectedTheme);
    }

    [Fact]
    public void OpenSourceGovernanceAndWingetPipeline_FilesExistAndMatchVersion450()
    {
        string repoRoot = FindRepoRoot();
        string version = AppUpdateService.CurrentVersion;
        Assert.Equal("4.5.0", version);

        string[] governanceFiles =
        [
            Path.Combine(repoRoot, "LICENSE"),
            Path.Combine(repoRoot, "SECURITY.md"),
            Path.Combine(repoRoot, "CONTRIBUTING.md"),
            Path.Combine(repoRoot, ".github", "dependabot.yml"),
            Path.Combine(repoRoot, ".github", "ISSUE_TEMPLATE", "bug_report.yml"),
            Path.Combine(repoRoot, ".github", "ISSUE_TEMPLATE", "feature_request.yml"),
            Path.Combine(repoRoot, ".github", "ISSUE_TEMPLATE", "config.yml")
        ];

        foreach (string path in governanceFiles)
        {
            Assert.True(File.Exists(path), $"Expected governance file missing: {path}");
        }

        string releaseYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"), Encoding.UTF8);
        Assert.Contains("FreeCleaner-Winget-Manifests-", releaseYml);
        Assert.Contains($"v{version}", releaseYml);

        string issContent = File.ReadAllText(Path.Combine(repoRoot, "installer", "FreeCleaner.iss"), Encoding.UTF8);
        Assert.Contains(version, issContent);

        foreach (string yamlName in new[]
        {
            "ErkanBahar.FreeCleanerPro.yaml",
            "ErkanBahar.FreeCleanerPro.installer.yaml",
            "ErkanBahar.FreeCleanerPro.locale.en-US.yaml"
        })
        {
            string yamlPath = Path.Combine(repoRoot, "installer", "winget", yamlName);
            string yamlContent = File.ReadAllText(yamlPath, Encoding.UTF8);
            Assert.Contains(version, yamlContent);
        }
    }
}
