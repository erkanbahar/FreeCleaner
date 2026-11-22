using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using FreeCleaner.Models;
using FreeCleaner.Services;
using FreeCleaner.ViewModels;
using Xunit;

namespace FreeCleaner.Tests;

public class V51UpgradesTests
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

    private sealed class StubUserDialogService : IUserDialogService
    {
        public AppDialogResult Show(
            string message,
            string title,
            AppDialogButtons buttons = AppDialogButtons.OK,
            AppDialogIcon icon = AppDialogIcon.Information) => AppDialogResult.OK;
    }

    [Fact]
    public void MainViewModelV51_NavigationHistory_FavoriteUnpin_AndExpandedGroupsPersist()
    {
        string tempDataRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerV51Vm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDataRoot);

        Exception? threadException = null;
        var staThread = new Thread(() =>
        {
            try
            {
                using (LocalizationService.BeginScopedLanguage("tr-TR"))
                using (AppStoragePathsService.BeginScopedDataDirectory(tempDataRoot))
                {
                    using var vm = new MainViewModel(new StubUserDialogService());

                    Assert.Equal("Dashboard", vm.ActiveSection);
                    Assert.False(vm.CanNavigateBack);
                    Assert.False(vm.CanNavigateForward);

                    vm.ActiveSection = "All";
                    vm.ActiveSection = "System";
                    vm.ActiveSection = "Settings";

                    Assert.True(vm.CanNavigateBack);
                    Assert.False(vm.CanNavigateForward);

                    Assert.True(vm.NavigateBack());
                    Assert.Equal("System", vm.ActiveSection);
                    Assert.True(vm.CanNavigateForward);

                    Assert.True(vm.NavigateBack());
                    Assert.Equal("All", vm.ActiveSection);

                    Assert.True(vm.NavigateForward());
                    Assert.Equal("System", vm.ActiveSection);

                    // Navigating to a new section clears forward stack
                    vm.ActiveSection = "Browsers";
                    Assert.False(vm.CanNavigateForward);
                    Assert.True(vm.CanNavigateBack);

                    // Unpin favorite cleanly
                    Assert.Contains(vm.FavoriteNavItems, f => f.SectionKey == "All");
                    vm.RemoveFavoriteSection("All");
                    Assert.DoesNotContain(vm.FavoriteNavItems, f => f.SectionKey == "All");

                    // Expanded navigation groups persist via ExclusionAndSettingsService
                    string[] customGroups = ["NavCatStorageExpander", "NavCatDevAiExpander"];
                    vm.SaveExpandedNavGroups(customGroups);
                    CleanerConfigData reloaded = ExclusionAndSettingsService.Load();
                    Assert.Equal(customGroups, reloaded.ExpandedNavGroups);
                }
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        try
        {
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            Assert.True(staThread.Join(TimeSpan.FromSeconds(20)), "STA ViewModel test timed out.");
            if (threadException is not null)
                throw new Xunit.Sdk.XunitException($"STA ViewModel test threw exception: {threadException}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDataRoot))
                    Directory.Delete(tempDataRoot, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public void WingetParserV51_ParsesEnglishAndTurkishOutput_FiltersNoise_AndAppliesDefaultSource()
    {
        Assert.Empty(NinthGenWingetAndNetworkSuiteService.ParseWingetUpgradeOutput(null));
        Assert.Empty(NinthGenWingetAndNetworkSuiteService.ParseWingetUpgradeOutput("No header separator line"));

        string sampleOutput = string.Join(Environment.NewLine,
        [
            "Ad                  Kimlik                   Sürüm    Kullanılabilir Kaynak",
            "---------------------------------------------------------------------------",
            "7-Zip 24.08         7zip.7zip                24.07    24.08          winget",
            "Notepad++           Notepad++.Notepad++      8.6.9    8.7.0",
            "Windows Terminal    Microsoft.WindowsTerm    1.20     1.21           msstore",
            "Invalid Row         NoDotId                  1.0      2.0",
            "---",
            "3 yükseltme mevcut.",
            "2 upgrades available."
        ]);

        List<WingetUpgradeItem> parsed = NinthGenWingetAndNetworkSuiteService.ParseWingetUpgradeOutput(sampleOutput);
        Assert.Equal(3, parsed.Count);

        Assert.Equal("7zip.7zip", parsed[0].PackageId);
        Assert.Equal("24.08", parsed[0].AvailableVersion);
        Assert.Equal("winget", parsed[0].SourceName);

        Assert.Equal("Notepad++.Notepad++", parsed[1].PackageId);
        Assert.Equal("winget", parsed[1].SourceName);

        Assert.Equal("Microsoft.WindowsTerm", parsed[2].PackageId);
        Assert.Equal("msstore", parsed[2].SourceName);
    }

    [Fact]
    public void ClipboardShieldV51_InspectAndSanitizeText_StripsTrackers_AndDetectsSecrets()
    {
        ClipboardAuditEntryItem empty = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText("   ");
        Assert.Contains("Boş", empty.CategoryBadge, StringComparison.Ordinal);

        ClipboardAuditEntryItem tracked = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText(
            "https://shop.example.com/product?id=42&utm_source=newsletter&fbclid=abc123&gclid=xyz999");
        Assert.Contains("Takip Parametresi", tracked.CategoryBadge, StringComparison.Ordinal);
        Assert.Equal("https://shop.example.com/product?id=42", tracked.SanitizedContent);

        ClipboardAuditEntryItem cleanUrl = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText(
            "https://example.com/docs?page=3");
        Assert.Contains("Temiz Web Bağlantısı", cleanUrl.CategoryBadge, StringComparison.Ordinal);
        Assert.Equal("https://example.com/docs?page=3", cleanUrl.SanitizedContent);

        ClipboardAuditEntryItem secret = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText(
            "sk-secretToken9876543210abcdef");
        Assert.Contains("API Anahtarı", secret.CategoryBadge, StringComparison.Ordinal);
        Assert.Equal(string.Empty, secret.SanitizedContent);
        Assert.DoesNotContain("secretToken9876543210", secret.OriginalPreview, StringComparison.Ordinal);

        ClipboardAuditEntryItem iban = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText(
            "TR33 0006 1005 1978 6457 8413 26");
        Assert.Contains("IBAN", iban.CategoryBadge, StringComparison.Ordinal);
        Assert.Equal("TR330006100519786457841326", iban.SanitizedContent);
    }

    [Fact]
    public async Task VisualDiskAndMediaV51_TreemapLayout_AndExifScanScrubWithBackup_WorkEndToEnd()
    {
        Assert.Empty(VisualDiskAndMediaService.ComputeTreemapLayout([], 600, 400));
        Assert.Empty(VisualDiskAndMediaService.ComputeTreemapLayout([new RawTreemapEntry("a", @"C:\a", 100, false)], 2, 2));

        List<RawTreemapEntry> entries =
        [
            new("BigFolder", @"C:\Data\BigFolder", 600, true),
            new("MidFile.bin", @"C:\Data\MidFile.bin", 300, false),
            new("Small.log", @"C:\Data\Small.log", 100, false)
        ];
        List<TreemapNodeItem> nodes = VisualDiskAndMediaService.ComputeTreemapLayout(entries, 600, 400);
        Assert.Equal(3, nodes.Count);
        Assert.Equal(100.0, nodes.Sum(n => n.SharePercent), 1);
        Assert.All(nodes, n =>
        {
            Assert.True(n.X >= 0 && n.Y >= 0);
            Assert.True(n.X + n.Width <= 600.5);
            Assert.True(n.Y + n.Height <= 400.5);
        });

        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerExifV51_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string jpgPath = Path.Combine(tempRoot, "gps_sample.jpg");
            using var bmp = new Bitmap(80, 60, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.SteelBlue);
                g.FillRectangle(Brushes.Orange, 10, 10, 30, 20);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            byte[] jpegBytes = ms.ToArray();

            // Minimal APP1 Exif TIFF header (little-endian): Make (0x010F)="TestCam\0" + GPS IFD (0x8825) -> GPSLatitudeRef (0x0001)="N\0"
            byte[] exifApp1WithGps =
            [
                0xFF, 0xE1, 0x00, 0x48,
                0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
                0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
                0x02, 0x00,
                0x0F, 0x01, 0x02, 0x00, 0x08, 0x00, 0x00, 0x00, 0x26, 0x00, 0x00, 0x00,
                0x25, 0x88, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x2E, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x54, 0x65, 0x73, 0x74, 0x43, 0x61, 0x6D, 0x00,
                0x01, 0x00,
                0x01, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00, 0x00, 0x4E, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            ];

            byte[] withExif = new byte[jpegBytes.Length + exifApp1WithGps.Length];
            Buffer.BlockCopy(jpegBytes, 0, withExif, 0, 2);
            Buffer.BlockCopy(exifApp1WithGps, 0, withExif, 2, exifApp1WithGps.Length);
            Buffer.BlockCopy(jpegBytes, 2, withExif, 2 + exifApp1WithGps.Length, jpegBytes.Length - 2);
            await File.WriteAllBytesAsync(jpgPath, withExif);

            List<PhotoMetadataItem> scanned = await VisualDiskAndMediaService.ScanPhotoMetadataAsync(tempRoot, includeSubfolders: false);
            PhotoMetadataItem item = Assert.Single(scanned);
            Assert.True(item.HasGpsData);
            Assert.Contains("TestCam", item.CameraInfo, StringComparison.Ordinal);
            Assert.True(item.TagCount >= 2);

            var scrubResult = await VisualDiskAndMediaService.ScrubPhotoMetadataAsync(item, createBackup: true);
            Assert.True(scrubResult.Success, scrubResult.Message);

            string backupDir = Path.Combine(tempRoot, "FreeCleaner_PhotoBackup");
            Assert.True(Directory.Exists(backupDir));
            Assert.Single(Directory.GetFiles(backupDir));

            PhotoMetadataItem afterScrub = VisualDiskAndMediaService.InspectSinglePhotoMetadata(jpgPath);
            Assert.False(afterScrub.HasGpsData);
            Assert.Equal(0, afterScrub.TagCount);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public void SmartDiskAdvisorV51_ProtectsSystemPaths_AndClassifiesDeveloperTempAndMediaPaths()
    {
        Assert.True(SmartDiskAdvisorService.IsProtectedSystemPath(@"C:\"));
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.True(SmartDiskAdvisorService.IsProtectedSystemPath(windowsDir));
        Assert.True(SmartDiskAdvisorService.IsProtectedSystemPath(Path.Combine(windowsDir, "System32")));
        Assert.False(SmartDiskAdvisorService.IsProtectedSystemPath(@"C:\Workspaces\SampleProject\node_modules"));

        SmartFileAdvice sysAdvice = SmartDiskAdvisorService.AnalyzePath(@"C:\hiberfil.sys", "hiberfil.sys", isDirectory: false);
        Assert.False(sysAdvice.CanSafeDeleteToRecycleBin);

        SmartFileAdvice nodeAdvice = SmartDiskAdvisorService.AnalyzePath(@"C:\Workspaces\App\node_modules", "node_modules", isDirectory: true);
        Assert.True(nodeAdvice.CanSafeDeleteToRecycleBin);
        Assert.Contains("Önbellek", nodeAdvice.BadgeText, StringComparison.Ordinal);

        SmartFileAdvice aiAdvice = SmartDiskAdvisorService.AnalyzePath(@"C:\Users\Demo\.ollama\models\llama.gguf", "llama.gguf", isDirectory: false);
        Assert.True(aiAdvice.CanSafeDeleteToRecycleBin);
        Assert.Contains("AI", aiAdvice.BadgeText, StringComparison.Ordinal);

        SmartFileAdvice vhdxAdvice = SmartDiskAdvisorService.AnalyzePath(@"C:\Users\Demo\claudevm.bundle\rootfs.vhdx", "rootfs.vhdx", isDirectory: false);
        Assert.True(vhdxAdvice.CanSafeDeleteToRecycleBin);
        Assert.Contains("VHDX", vhdxAdvice.BadgeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiModelCleanerV51_IsStandaloneAiWeightFile_AndCleanSelectedAiCaches_WorkSafely()
    {
        Assert.True(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\models\tiny.gguf", 10L * 1024 * 1024));
        Assert.False(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\models\tiny.gguf", 9L * 1024 * 1024));
        Assert.True(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\ai\weights.bin", 50L * 1024 * 1024));
        Assert.False(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\ai\weights.bin", 49L * 1024 * 1024));
        Assert.False(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\notes\readme.txt", 100L * 1024 * 1024));

        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerAiV51_{Guid.NewGuid():N}");
        string selDir = Path.Combine(tempRoot, "sel_cache");
        string selFile = Path.Combine(tempRoot, "sel_model.gguf");
        string keepFile = Path.Combine(tempRoot, "keep_model.gguf");
        Directory.CreateDirectory(selDir);
        await File.WriteAllBytesAsync(Path.Combine(selDir, "shard.bin"), new byte[128]);
        await File.WriteAllBytesAsync(selFile, new byte[256]);
        await File.WriteAllBytesAsync(keepFile, new byte[64]);

        try
        {
            List<AiModelCacheItem> items =
            [
                new AiModelCacheItem { AiFramework = "TestAI", ModelOrCacheName = "Selected Cache", FullPath = selDir, IsDirectory = true, SizeBytes = 128, IsSelected = true },
                new AiModelCacheItem { AiFramework = "TestAI", ModelOrCacheName = "Selected Model", FullPath = selFile, IsDirectory = false, SizeBytes = 256, IsSelected = true },
                new AiModelCacheItem { AiFramework = "TestAI", ModelOrCacheName = "Kept Model", FullPath = keepFile, IsDirectory = false, SizeBytes = 64, IsSelected = false }
            ];

            var (cleanedCount, freedBytes) = await EighthGenAiAndSecuritySuiteService.CleanSelectedAiCachesAsync(items);
            Assert.Equal(2, cleanedCount);
            Assert.Equal(384, freedBytes);
            Assert.False(Directory.Exists(selDir));
            Assert.False(File.Exists(selFile));
            Assert.True(File.Exists(keepFile));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public async Task CleanerEngineV51_DefaultRulesInvariants_AndMultiRuleAnalyzeClean()
    {
        List<CleanerRule> defaults = CleanerEngine.GetDefaultRules();
        Assert.True(defaults.Count >= 20);
        Assert.Equal(defaults.Count, defaults.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(defaults, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Id));
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
            Assert.False(string.IsNullOrWhiteSpace(r.Category));
            Assert.False(string.IsNullOrWhiteSpace(r.RiskLabel));
        });

        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerEngineV51_{Guid.NewGuid():N}");
        string selectedDir = Path.Combine(tempRoot, "selected");
        string excludedDir = Path.Combine(selectedDir, "protected");
        string unselectedDir = Path.Combine(tempRoot, "unselected");
        Directory.CreateDirectory(excludedDir);
        Directory.CreateDirectory(unselectedDir);

        string cleanTargetFile = Path.Combine(selectedDir, "old.tmp");
        string protectedFile = Path.Combine(excludedDir, "keep.tmp");
        string unselectedFile = Path.Combine(unselectedDir, "untouched.tmp");

        await File.WriteAllTextAsync(cleanTargetFile, "reclaimable-data");
        await File.WriteAllTextAsync(protectedFile, "protected-data");
        await File.WriteAllTextAsync(unselectedFile, "unselected-data");

        DateTime staleUtc = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(cleanTargetFile, staleUtc);
        File.SetLastWriteTimeUtc(protectedFile, staleUtc);
        File.SetLastWriteTimeUtc(unselectedFile, staleUtc);

        try
        {
            var selectedRule = new CleanerRule
            {
                Id = "v51_selected",
                Name = "Selected Temp",
                Category = "Test",
                Description = "Selected rule",
                IconGlyph = string.Empty,
                Group = RuleGroup.System,
                Risk = RiskLevel.Low,
                IsSelected = true,
                Targets = [new ScanTarget(selectedDir, "*", Recursive: true)]
            };
            var unselectedRule = new CleanerRule
            {
                Id = "v51_unselected",
                Name = "Unselected Temp",
                Category = "Test",
                Description = "Unselected rule",
                IconGlyph = string.Empty,
                Group = RuleGroup.System,
                Risk = RiskLevel.Low,
                IsSelected = false,
                Targets = [new ScanTarget(unselectedDir, "*", Recursive: true)]
            };

            var engine = new CleanerEngine();
            List<ScannedFileItem> found = await engine.AnalyzeAsync([selectedRule, unselectedRule], [excludedDir]);

            ScannedFileItem target = Assert.Single(found);
            Assert.Equal(cleanTargetFile, target.FullPath);

            CleanSummaryResult summary = await engine.CleanAsync(
                found,
                [selectedRule, unselectedRule],
                moveToRecycleBinInsteadOfPermanentDelete: false);

            Assert.Equal(1, summary.DeletedFilesCount);
            Assert.Equal(target.SizeBytes, summary.FreedBytes);
            Assert.False(File.Exists(cleanTargetFile));
            Assert.True(File.Exists(protectedFile));
            Assert.True(File.Exists(unselectedFile));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public void Architecture_V51_ReleaseWingetRegex_AttestV4_NavigationPartial_AndCoverageFloor()
    {
        string repoRoot = FindRepoRoot();
        Assert.Equal("5.1.0", AppUpdateService.CurrentVersion);

        string navVmPath = Path.Combine(repoRoot, "FreeCleaner", "ViewModels", "MainViewModel.Navigation.cs");
        Assert.True(File.Exists(navVmPath), "Expected MainViewModel.Navigation.cs to exist.");
        string navVm = File.ReadAllText(navVmPath, Encoding.UTF8);
        Assert.Equal(60, Regex.Count(navVm, @"public bool Is([A-Za-z0-9]+)Section\s*=>"));
        Assert.Contains("public bool NavigateBack()", navVm, StringComparison.Ordinal);
        Assert.Contains("public bool NavigateForward()", navVm, StringComparison.Ordinal);
        Assert.Contains("public void SaveExpandedNavGroups(", navVm, StringComparison.Ordinal);

        string releaseYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"), Encoding.UTF8);
        Assert.DoesNotContain(@"4\.8\.0", releaseYml, StringComparison.Ordinal);
        Assert.Contains("PackageVersion:", releaseYml, StringComparison.Ordinal);
        Assert.Contains("actions/attest-build-provenance@v4", releaseYml, StringComparison.Ordinal);

        string publishScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Publish-Portable.ps1"), Encoding.UTF8);
        Assert.DoesNotContain(@"4\.8\.0", publishScript, StringComparison.Ordinal);
        Assert.Contains("PackageVersion:", publishScript, StringComparison.Ordinal);

        string mainXaml = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml"), Encoding.UTF8);
        Assert.Contains("NavNoSearchResultsHint", mainXaml, StringComparison.Ordinal);
        Assert.Contains("RemoveFavoriteCommand", mainXaml, StringComparison.Ordinal);

        string mainCs = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml.cs"), Encoding.UTF8);
        Assert.Contains("ModifierKeys.Alt", mainCs, StringComparison.Ordinal);
        Assert.Contains("SaveExpandedNavGroups", mainCs, StringComparison.Ordinal);

        string ciYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "dotnet-ci.yml"), Encoding.UTF8);
        Assert.Contains("$minLineCoverage = 39.0", ciYml, StringComparison.Ordinal);
        Match floorMatch = Regex.Match(ciYml, @"\$minLineCoverage\s*=\s*(\d+(?:\.\d+)?)");
        Assert.True(floorMatch.Success);
        double floor = double.Parse(floorMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(floor >= 39.0, $"Expected CI coverage floor >= 39.0, found {floor}");
    }
}
