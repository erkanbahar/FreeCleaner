using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using FreeCleaner.Models;
using FreeCleaner.Services;
using FreeCleaner.ViewModels;
using Xunit;

namespace FreeCleaner.Tests;

public class V49UpgradesTests
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

    private sealed class RecordingUserDialogService : IUserDialogService
    {
        public Func<string, string, AppDialogButtons, AppDialogIcon, AppDialogResult> Handler { get; set; }
            = (_, _, _, _) => AppDialogResult.OK;

        public List<(string Message, string Title, AppDialogButtons Buttons, AppDialogIcon Icon)> Calls { get; } = [];

        public AppDialogResult Show(
            string message,
            string title,
            AppDialogButtons buttons = AppDialogButtons.OK,
            AppDialogIcon icon = AppDialogIcon.Information)
        {
            Calls.Add((message, title, buttons, icon));
            return Handler(message, title, buttons, icon);
        }
    }

    [Fact]
    public void Architecture_V49_ZeroRawServiceProcessStartsAndZeroPipeReadToEndOutsideSafeProcessRunner()
    {
        string repoRoot = FindRepoRoot();
        string servicesDir = Path.Combine(repoRoot, "FreeCleaner", "Services");
        var rawStartPattern = new Regex(@"using\s+var\s+\w+\s*=\s*Process\.Start\s*\(", RegexOptions.Compiled);

        foreach (string csFile in Directory.GetFiles(servicesDir, "*.cs", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(csFile);
            if (string.Equals(fileName, "SafeProcessRunner.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            string code = File.ReadAllText(csFile, Encoding.UTF8);
            Assert.False(rawStartPattern.IsMatch(code), $"Raw 'using var ... = Process.Start(' found in {fileName}; route through SafeProcessRunner.");
            Assert.DoesNotContain(".StandardOutput.ReadToEnd(", code, StringComparison.Ordinal);
            Assert.DoesNotContain(".StandardError.ReadToEnd(", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Architecture_V49_ZeroDirectMessageBoxInViewModels_AndThemedDialogUsesDynamicResources()
    {
        string repoRoot = FindRepoRoot();
        string vmDir = Path.Combine(repoRoot, "FreeCleaner", "ViewModels");
        string viewsDir = Path.Combine(repoRoot, "FreeCleaner", "Views");
        string dialogXamlPath = Path.Combine(repoRoot, "FreeCleaner", "Dialogs", "ThemedDialogWindow.xaml");

        foreach (string vmFile in Directory.GetFiles(vmDir, "*.cs", SearchOption.AllDirectories))
        {
            string code = File.ReadAllText(vmFile, Encoding.UTF8);
            Assert.DoesNotContain("MessageBox.Show(", code, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(dialogXamlPath), "Dialogs/ThemedDialogWindow.xaml must exist.");
        string dialogXaml = File.ReadAllText(dialogXamlPath, Encoding.UTF8);
        Assert.Contains("{DynamicResource", dialogXaml, StringComparison.Ordinal);

        string[] forbiddenDarkHexes =
        [
            "#0D1422",
            "#1D1622",
            "#4C1D24",
            "#222D40",
            "#263859",
            "#1E2B45",
            "#F87171"
        ];
        foreach (string hex in forbiddenDarkHexes)
        {
            Assert.False(dialogXaml.Contains(hex, StringComparison.OrdinalIgnoreCase),
                $"Hardcoded dark surface hex '{hex}' found in ThemedDialogWindow.xaml.");
        }

        Assert.Equal(57, Directory.GetFiles(viewsDir, "*.xaml", SearchOption.TopDirectoryOnly).Length);
    }

    [Fact]
    public void AtomicStorageV49_DiskSnapshotStorageForecastAndCliReport_WriteAtomicallyAndRecoverFromCorruptJson()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerAtomicV49_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // 1. DiskSnapshotDiffService atomic write + .bak recovery
            string snapshotDir = Path.Combine(tempRoot, "snapshots");
            string rootTarget = Path.Combine(tempRoot, "Work");
            Directory.CreateDirectory(rootTarget);

            var entry1 = new RawTreemapEntry(Path.Combine(rootTarget, "A"), "A", 1_000_000, true);
            DiskSnapshotDiffService.SaveSnapshot(rootTarget, 1_000_000, [entry1], snapshotDir);

            var entry2 = new RawTreemapEntry(Path.Combine(rootTarget, "B"), "B", 2_000_000, true);
            DiskSnapshotDiffService.SaveSnapshot(rootTarget, 2_000_000, [entry2], snapshotDir);

            string snapFile = DiskSnapshotDiffService.GetSnapshotFilePath(rootTarget, snapshotDir);
            Assert.True(File.Exists(snapFile + ".bak"));
            Assert.Empty(Directory.GetFiles(snapshotDir, "*.tmp"));

            File.WriteAllText(snapFile, "{ corrupt-snapshot", Encoding.UTF8);
            DiskFolderSnapshot? recoveredSnap = DiskSnapshotDiffService.LoadSnapshot(rootTarget, snapshotDir);
            Assert.NotNull(recoveredSnap);
            Assert.Equal(1_000_000, recoveredSnap!.TotalBytes);

            // 2. StorageForecastService atomic write + .bak recovery
            string forecastFile = Path.Combine(tempRoot, "drive_telemetry.json");
            var m1 = new SystemMetricsSnapshot("Windows 11", "X64", ".NET 10", @"C:\", 500_000_000_000L, 200_000_000_000L, 60.0, 32_000_000_000L, 16_000_000_000L, 50.0);
            var m2 = new SystemMetricsSnapshot("Windows 11", "X64", ".NET 10", @"C:\", 500_000_000_000L, 190_000_000_000L, 62.0, 32_000_000_000L, 16_000_000_000L, 50.0);

            StorageForecastService.RecordDailySample(m1, forecastFile, new DateTime(2026, 11, 20));
            StorageForecastService.RecordDailySample(m2, forecastFile, new DateTime(2026, 11, 21));
            Assert.True(File.Exists(forecastFile + ".bak"));
            Assert.Empty(Directory.GetFiles(tempRoot, "*.tmp"));

            File.WriteAllText(forecastFile, "{ broken-telemetry", Encoding.UTF8);
            List<DailyDriveSample> recoveredSamples = StorageForecastService.LoadSamples(forecastFile);
            Assert.Single(recoveredSamples);
            Assert.Equal("2026-11-20", recoveredSamples[0].DateKey);

            // 3. CLI Automation Report + Settings Export atomic write with zero temp leftovers
            string cliReportPath = Path.Combine(tempRoot, "cli_report.json");
            var report = new CliAutomationReport(
                DateTime.UtcNow,
                AppUpdateService.CurrentVersion,
                "SafeDaily",
                DryRun: true,
                TotalFiles: 3,
                TotalBytes: 4096,
                TotalSizeFormatted: "4.0 KB",
                ElapsedMilliseconds: 25,
                Categories: [new CliCategoryBreakdownItem("Temp", 3, 4096)]);

            string writtenReportPath = ShellIntegrationAndProfilesService.WriteCliAutomationReportJson(cliReportPath, report);
            Assert.True(File.Exists(writtenReportPath));
            Assert.Contains("SafeDaily", File.ReadAllText(writtenReportPath, Encoding.UTF8));

            string cfgExportPath = Path.Combine(tempRoot, "exported_settings.json");
            var cfg = new CleanerConfigData
            {
                SelectedLanguage = "en-US",
                SelectedTheme = "FluentLight",
                ExcludedPaths = [@"D:\KeepMe"]
            };
            ShellIntegrationAndProfilesService.ExportSettingsToFile(cfg, cfgExportPath);
            CleanerConfigData imported = ShellIntegrationAndProfilesService.ImportSettingsFromFile(cfgExportPath);
            Assert.Equal("en-US", imported.SelectedLanguage);
            Assert.Equal("FluentLight", imported.SelectedTheme);
            Assert.Contains(@"D:\KeepMe", imported.ExcludedPaths);
            Assert.Empty(Directory.GetFiles(tempRoot, "*.tmp"));
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
    public async Task SimilarPhotoFinderV49_NormalizesExifOrientation_ComputesFastLockBitsHash_AndClustersRotatedDuplicates()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerPhotoV49_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string uprightPath = Path.Combine(tempRoot, "upright.jpg");
            string rotatedExifPath = Path.Combine(tempRoot, "exif_rotated.jpg");

            using var bmp = new Bitmap(120, 80, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Navy);
                g.FillRectangle(Brushes.Gold, 0, 0, 58, 42);
                g.FillRectangle(Brushes.Crimson, 62, 42, 58, 38);
                g.FillEllipse(Brushes.LimeGreen, 85, 6, 26, 26);
            }

            bmp.Save(uprightPath, ImageFormat.Jpeg);

            // Rotate pixels -90 deg (270 cw) so physical dimensions become 80x120, then tag EXIF Orientation=6 (rotate 90 cw to restore 120x80).
            using var rotatedBmp = new Bitmap(bmp);
            rotatedBmp.RotateFlip(RotateFlipType.Rotate270FlipNone);

            using var ms = new MemoryStream();
            rotatedBmp.Save(ms, ImageFormat.Jpeg);
            byte[] jpegBytes = ms.ToArray();

            byte[] exifApp1 =
            [
                0xFF, 0xE1, 0x00, 0x22,
                0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
                0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
                0x01, 0x00,
                0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
                0x06, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            ];

            byte[] withExif = new byte[jpegBytes.Length + exifApp1.Length];
            Buffer.BlockCopy(jpegBytes, 0, withExif, 0, 2);
            Buffer.BlockCopy(exifApp1, 0, withExif, 2, exifApp1.Length);
            Buffer.BlockCopy(jpegBytes, 2, withExif, 2 + exifApp1.Length, jpegBytes.Length - 2);
            await File.WriteAllBytesAsync(rotatedExifPath, withExif);

            List<SimilarPhotoItem> found = await SimilarPhotoFinderService.FindSimilarPhotosAsync(
                tempRoot,
                maxHammingDistance: 4,
                includeSubfolders: false);

            Assert.Equal(2, found.Count);
            Assert.All(found, item =>
            {
                Assert.Equal(120, item.Width);
                Assert.Equal(80, item.Height);
                Assert.True(item.HammingDistance <= 4, $"Unexpected HammingDistance={item.HammingDistance}");
            });
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
    public void MainViewModelV49_InjectedDialogService_HandlesHighRiskRuleConfirmation_AndLocalizesDialogTitles()
    {
        Assert.Equal("Caution - Session Data", LocalizationService.TranslateDialogTitle("Dikkat - Oturum Verisi", "en-US"));
        Assert.Equal("Dikkat - Oturum Verisi", LocalizationService.TranslateDialogTitle("Dikkat - Oturum Verisi", "tr-TR"));
        Assert.Equal("Backup Succeeded", LocalizationService.TranslateDialogTitle("Yedekleme Başarılı", "en-US"));

        string tempDataRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerVmTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDataRoot);

        Exception? threadException = null;
        var staThread = new Thread(() =>
        {
            try
            {
                using (AppStoragePathsService.BeginScopedDataDirectory(tempDataRoot))
                {
                    var fakeDialog = new RecordingUserDialogService();
                    using var vm = new MainViewModel(fakeDialog);

                    CleanerRule? highRiskRule = vm.AllRules.FirstOrDefault(r =>
                        !r.IsSelected && (r.RequiresWarning || r.RiskLevel == RiskLevel.High) && !string.IsNullOrEmpty(r.WarningMessage));
                    Assert.NotNull(highRiskRule);

                    // 1. Declining the warning prompt must revert IsSelected back to false.
                    fakeDialog.Handler = (_, _, _, _) => AppDialogResult.No;
                    highRiskRule!.IsSelected = true;
                    Assert.False(highRiskRule.IsSelected);
                    Assert.Single(fakeDialog.Calls);
                    Assert.Equal(AppDialogButtons.YesNo, fakeDialog.Calls[0].Buttons);
                    Assert.Equal(AppDialogIcon.Warning, fakeDialog.Calls[0].Icon);

                    // 2. Accepting in English mode must translate dialog title and keep selection enabled.
                    vm.SelectedLanguage = "en-US";
                    fakeDialog.Handler = (_, _, _, _) => AppDialogResult.Yes;
                    highRiskRule.IsSelected = true;
                    Assert.True(highRiskRule.IsSelected);
                    Assert.Equal(2, fakeDialog.Calls.Count);
                    Assert.Equal("Caution - Session Data", fakeDialog.Calls[1].Title);
                    Assert.Contains("Do you still want to enable", fakeDialog.Calls[1].Message, StringComparison.Ordinal);
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
    public void Architecture_V49_AccessibilityAutomationProperties_ShortcutsOverlay_AndCiCoverageFloor_ForVersion500()
    {
        string repoRoot = FindRepoRoot();
        Assert.Equal("5.0.0", AppUpdateService.CurrentVersion);

        string appXaml = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "App.xaml"), Encoding.UTF8);
        Assert.Contains("Property=\"IsKeyboardFocused\" Value=\"True\"", appXaml, StringComparison.Ordinal);

        string mainWindowXaml = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml"), Encoding.UTF8);
        Assert.Contains("x:Name=\"ShortcutsOverlay\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Loc_ShortcutsBtn", mainWindowXaml, StringComparison.Ordinal);

        string mainWindowCs = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml.cs"), Encoding.UTF8);
        Assert.Contains("Key.F1", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("OpenShortcutsOverlay", mainWindowCs, StringComparison.Ordinal);

        string[] a11yViews =
        [
            "SettingsView.xaml",
            "CleanerView.xaml",
            "DiskTreemapView.xaml",
            "DownloadsView.xaml",
            "BatchRenamerView.xaml",
            "CryptoVaultView.xaml",
            "RegistryView.xaml",
            "NetworkView.xaml",
            "ShredderView.xaml",
            "SimilarPhotoFinderView.xaml"
        ];

        string viewsDir = Path.Combine(repoRoot, "FreeCleaner", "Views");
        foreach (string view in a11yViews)
        {
            string xaml = File.ReadAllText(Path.Combine(viewsDir, view), Encoding.UTF8);
            Assert.Contains("AutomationProperties.Name=", xaml, StringComparison.Ordinal);
        }

        string ciYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "dotnet-ci.yml"), Encoding.UTF8);
        Assert.Contains("$minLineCoverage", ciYml, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", ciYml, StringComparison.Ordinal);
    }
}
