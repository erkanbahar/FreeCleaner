using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeCleaner.Models;
using FreeCleaner.Services;
using Xunit;

namespace FreeCleaner.Tests;

public class V43UpgradesTests
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
    public void AppUpdateService_CurrentVersion_MatchesAssemblyMetadataVersion()
    {
        Version? asmVer = typeof(AppUpdateService).Assembly.GetName().Version;
        Assert.NotNull(asmVer);
        string expected = $"{asmVer.Major}.{asmVer.Minor}.{Math.Max(0, asmVer.Build)}";
        Assert.Equal(expected, AppUpdateService.CurrentVersion);
        Assert.Equal("5.2.0", AppUpdateService.CurrentVersion);
    }

    [Fact]
    public void CrashGuardService_FormatsRedactedReport_PrunesOldLogs_AndExportsIntoSupportBundle()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerCrashTest_{Guid.NewGuid():N}");
        string dataDir = Path.Combine(tempRoot, "data");
        string outZip = Path.Combine(tempRoot, "bundle.zip");

        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string sensitivePath = Path.Combine(userProfile, "SecretDocs", "key.txt");
            var ex = new InvalidOperationException(
                $"Failed auth for {sensitivePath} with Bearer myTopSecretJwtToken token=hiddenApiKey99 password=pass123");

            string formatted = CrashGuardService.FormatCrashReport("DispatcherUI", ex);
            Assert.Contains($"v{AppUpdateService.CurrentVersion}", formatted);
            Assert.Contains("***REDACTED***", formatted);
            Assert.DoesNotContain("myTopSecretJwtToken", formatted);
            Assert.DoesNotContain("hiddenApiKey99", formatted);
            Assert.DoesNotContain("pass123", formatted);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                Assert.DoesNotContain(userProfile, formatted, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("%USERPROFILE%", formatted);
            }

            // Write 24 crash logs and verify pruning keeps at most 20 recent logs
            for (int i = 0; i < 24; i++)
            {
                CrashReportResult r = CrashGuardService.WriteCrashReport(
                    $"TestCrash_{i}",
                    new InvalidOperationException($"Crash sample #{i} token=secretVal{i}"),
                    dataDirOverride: dataDir);
                Assert.True(File.Exists(r.ReportPath));
            }

            string crashDir = CrashGuardService.GetCrashDirectory(dataDir);
            string[] retained = Directory.GetFiles(crashDir, "crash-*.log");
            Assert.Equal(20, retained.Length);

            string? latestCrash = CrashGuardService.GetLatestCrashLogPath(dataDir);
            Assert.NotNull(latestCrash);
            Assert.True(File.Exists(latestCrash));

            var metrics = new SystemMetricsSnapshot("Windows 11", "X64", ".NET 10", @"C:\", 500_000_000_000, 100_000_000_000, 80.0, 32_000_000_000, 16_000_000_000, 50.0);
            AppStoragePathsService.ExportSupportBundleZip(
                outZip,
                metrics,
                [],
                totalFreedBytes: 0,
                totalDeletedFiles: 0,
                logs: ["CrashGuard verification"],
                dataDirOverride: dataDir);

            using ZipArchive archive = ZipFile.OpenRead(outZip);
            ZipArchiveEntry? crashEntry = archive.GetEntry("latest_crash_sanitized.log");
            Assert.NotNull(crashEntry);
            using var reader = new StreamReader(crashEntry!.Open());
            string bundledCrash = reader.ReadToEnd();
            Assert.Contains("Crash sample #", bundledCrash);
            Assert.DoesNotContain("secretVal", bundledCrash);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void StorageForecastService_EvaluateProactiveAlert_RespectsThresholdsCooldownAndLanguages()
    {
        var today = new DateTime(2026, 11, 21);
        var criticalForecast = new StorageForecastSummary(
            AvgDailyGrowthBytes: 5_000_000_000,
            EstimatedDaysTo90Percent: 10,
            HeadlineText: "Test",
            DetailText: "Detail",
            SampleCount: 5);

        // Disabled -> never notify
        StorageAlertEvaluation disabled = StorageForecastService.EvaluateProactiveAlert(
            criticalForecast, 75.0, alertsEnabled: false, thresholdDays: 15, lastAlertDateKey: null, nowOverride: today, languageCode: "en-US");
        Assert.False(disabled.ShouldNotify);

        // Healthy (> threshold days & < 90%) -> no notify
        var healthyForecast = criticalForecast with { EstimatedDaysTo90Percent = 45 };
        StorageAlertEvaluation healthy = StorageForecastService.EvaluateProactiveAlert(
            healthyForecast, 65.0, alertsEnabled: true, thresholdDays: 15, lastAlertDateKey: null, nowOverride: today, languageCode: "tr-TR");
        Assert.False(healthy.ShouldNotify);

        // Within threshold days -> fires in EN & TR
        StorageAlertEvaluation firedEn = StorageForecastService.EvaluateProactiveAlert(
            criticalForecast, 75.0, alertsEnabled: true, thresholdDays: 15, lastAlertDateKey: null, nowOverride: today, languageCode: "en-US");
        StorageAlertEvaluation firedTr = StorageForecastService.EvaluateProactiveAlert(
            criticalForecast, 75.0, alertsEnabled: true, thresholdDays: 15, lastAlertDateKey: null, nowOverride: today, languageCode: "tr-TR");
        Assert.True(firedEn.ShouldNotify);
        Assert.True(firedTr.ShouldNotify);
        Assert.Contains("~10 day", firedEn.Message);
        Assert.Contains("~10 gün", firedTr.Message);
        Assert.NotEqual(firedEn.Message, firedTr.Message);

        // Same day cooldown suppresses duplicate alert
        StorageAlertEvaluation sameDay = StorageForecastService.EvaluateProactiveAlert(
            criticalForecast, 92.5, alertsEnabled: true, thresholdDays: 15, lastAlertDateKey: firedEn.TodayDateKey, nowOverride: today, languageCode: "en-US");
        Assert.False(sameDay.ShouldNotify);

        // Next day with >=90% used triggers critical alert
        StorageAlertEvaluation nextDay = StorageForecastService.EvaluateProactiveAlert(
            healthyForecast, 92.5, alertsEnabled: true, thresholdDays: 15, lastAlertDateKey: firedEn.TodayDateKey, nowOverride: today.AddDays(1), languageCode: "en-US");
        Assert.True(nextDay.ShouldNotify);
        Assert.Contains("92.5%", nextDay.Message);
    }

    [Fact]
    public void LocalizationService_AllXamlDynamicResourceKeysExistInBothTurkishAndEnglish()
    {
        var trKeys = LocalizationService.GetAllKeys("tr-TR").ToHashSet(StringComparer.Ordinal);
        var enKeys = LocalizationService.GetAllKeys("en-US").ToHashSet(StringComparer.Ordinal);

        Assert.Equal(trKeys.Count, enKeys.Count);
        Assert.True(trKeys.SetEquals(enKeys));

        string repoRoot = FindRepoRoot();
        List<string> xamlFiles =
        [
            Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml"),
            .. Directory.GetFiles(Path.Combine(repoRoot, "FreeCleaner", "Views"), "*.xaml")
        ];

        var missingKeys = new List<string>();
        foreach (string file in xamlFiles)
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{DynamicResource (Loc_[^}]+)\}"))
            {
                string key = m.Groups[1].Value;
                if (!trKeys.Contains(key) ||
                    !enKeys.Contains(key) ||
                    string.IsNullOrWhiteSpace(LocalizationService.Get(key, "tr-TR")) ||
                    string.IsNullOrWhiteSpace(LocalizationService.Get(key, "en-US")))
                {
                    missingKeys.Add($"{Path.GetFileName(file)}:{key}");
                }
            }
        }

        Assert.Empty(missingKeys);
    }

    [Fact]
    public void Architecture_All60SectionKeysUseLazyViews_AndMainWindowHasZeroInlineSectionBlocks()
    {
        string repoRoot = FindRepoRoot();
        string mainXaml = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml"));
        string mainXamlCs = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml.cs"));
        string vmCs = string.Concat(
            Directory.GetFiles(Path.Combine(repoRoot, "FreeCleaner", "ViewModels"), "MainViewModel*.cs")
                .Select(File.ReadAllText));

        // MainWindow.xaml must contain zero inline Is*Section visibility panels
        Assert.DoesNotMatch(@"Visibility=""\{Binding Is[A-Za-z0-9]+Section", mainXaml);

        List<string> vmSectionKeys = Regex.Matches(vmCs, @"public bool Is([A-Za-z0-9]+)Section\s*=>")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(60, vmSectionKeys.Count);

        HashSet<string> registeredKeys = Regex.Matches(mainXamlCs, @"_lazyViews\.Register\(""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(60, registeredKeys.Count);
        foreach (string key in vmSectionKeys)
            Assert.Contains(key, registeredKeys);

        int viewFilesCount = Directory.GetFiles(Path.Combine(repoRoot, "FreeCleaner", "Views"), "*.xaml").Length;
        Assert.Equal(57, viewFilesCount);
    }

    [Fact]
    public void ShellIntegrationAndProfilesService_ParsesCliDryRunProfileAndWritesJsonReport()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerCliTest_{Guid.NewGuid():N}");
        string reportPath = Path.Combine(tempRoot, "reports", "audit.json");

        try
        {
            CliLaunchAction parsedInline = ShellIntegrationAndProfilesService.ParseStartupArguments(
                ["--dry-run", "--profile=DeveloperDeep", $"--report-json={reportPath}", "--exit"]);
            Assert.Equal(CliLaunchActionKind.DryRunReport, parsedInline.Kind);
            Assert.True(parsedInline.DryRun);
            Assert.Equal(CleaningProfileKind.DeveloperDeep, parsedInline.Profile);
            Assert.Equal(reportPath, parsedInline.ReportJsonPath);
            Assert.True(parsedInline.ExitWhenFinished);

            CliLaunchAction parsedSpaced = ShellIntegrationAndProfilesService.ParseStartupArguments(
                ["--scheduled-maintenance", "--profile", "SafeDaily", "--dry-run", "--report-json", reportPath, "--headless"]);
            Assert.Equal(CliLaunchActionKind.ScheduledMaintenance, parsedSpaced.Kind);
            Assert.True(parsedSpaced.DryRun);
            Assert.Equal(CleaningProfileKind.SafeDaily, parsedSpaced.Profile);
            Assert.Equal(reportPath, parsedSpaced.ReportJsonPath);
            Assert.True(parsedSpaced.ExitWhenFinished);

            var report = new CliAutomationReport(
                GeneratedUtc: DateTime.UtcNow,
                AppVersion: AppUpdateService.CurrentVersion,
                Profile: "DeveloperDeep",
                DryRun: true,
                TotalFiles: 42,
                TotalBytes: 2_097_152,
                TotalSizeFormatted: "2,00 MB",
                ElapsedMilliseconds: 150,
                Categories: [new CliCategoryBreakdownItem("Temp", 42, 2_097_152)]);

            string written = ShellIntegrationAndProfilesService.WriteCliAutomationReportJson(reportPath, report);
            Assert.True(File.Exists(written));

            string json = File.ReadAllText(written);
            CliAutomationReport? roundTrip = JsonSerializer.Deserialize<CliAutomationReport>(json);
            Assert.NotNull(roundTrip);
            Assert.Equal(42, roundTrip!.TotalFiles);
            Assert.True(roundTrip.DryRun);
            Assert.Equal("DeveloperDeep", roundTrip.Profile);
            Assert.Single(roundTrip.Categories);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FileShredderAndWiperService_ShredsIsolatedTempFileAndDirectoryCompletely()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerShredTest_{Guid.NewGuid():N}");
        string secretFile = Path.Combine(tempRoot, "secret.bin");
        string subDir = Path.Combine(tempRoot, "nested");
        string nestedFile = Path.Combine(subDir, "inner.txt");

        Directory.CreateDirectory(subDir);
        await File.WriteAllBytesAsync(secretFile, new byte[8192]);
        File.SetAttributes(secretFile, FileAttributes.ReadOnly);
        await File.WriteAllTextAsync(nestedFile, "Confidential test payload for DoD 3-pass shred");

        try
        {
            var items = new List<ShredTargetItem>
            {
                new() { FullPath = secretFile, Name = "secret.bin", SizeBytes = 8192, IsDirectory = false },
                new() { FullPath = subDir, Name = "nested", SizeBytes = 44, IsDirectory = true }
            };

            var (shreddedCount, totalBytes) = await FileShredderAndWiperService.ShredItemsAsync(items, passes: 3);

            Assert.Equal(2, shreddedCount);
            Assert.Equal(8236, totalBytes);
            Assert.False(File.Exists(secretFile));
            Assert.False(Directory.Exists(subDir));
            Assert.Contains("İmha", items[0].Status);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ExclusionAndSettingsService_And_BackupManager_BehaveSafely()
    {
        string[] exclusions = [@"C:\Sensitive\Vault", "KeepMe"];
        Assert.True(ExclusionAndSettingsService.IsPathExcluded(@"c:\sensitive\vault\data.db", exclusions));
        Assert.True(ExclusionAndSettingsService.IsPathExcluded(@"D:\Temp\KeepMe_Cache\item.tmp", exclusions));
        Assert.False(ExclusionAndSettingsService.IsPathExcluded(@"C:\Windows\Temp\junk.tmp", exclusions));

        var config = new CleanerConfigData
        {
            ProactiveStorageAlertsEnabled = false,
            StorageAlertThresholdDays = 7,
            LastStorageForecastAlertDate = "2026-11-21",
            SelectedLanguage = "en-US"
        };

        string json = JsonSerializer.Serialize(config);
        CleanerConfigData? deserialized = JsonSerializer.Deserialize<CleanerConfigData>(json);
        Assert.NotNull(deserialized);
        Assert.False(deserialized!.ProactiveStorageAlertsEnabled);
        Assert.Equal(7, deserialized.StorageAlertThresholdDays);
        Assert.Equal("2026-11-21", deserialized.LastStorageForecastAlertDate);

        List<BackupFileItem> backups = BackupManagerService.ListBackups();
        Assert.NotNull(backups);
    }
}
