using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using FreeCleaner.Models;
using FreeCleaner.Services;
using FreeCleaner.ViewModels;
using Xunit;

namespace FreeCleaner.Tests;

public class V52UpgradesTests
{
    private static CleanerRule MakeRule(string id, string name, params ScanTarget[] targets) => new()
    {
        Id = id,
        Name = name,
        Category = "Test",
        Description = "Test",
        IconGlyph = "\uE74D",
        Group = RuleGroup.System,
        IsSelected = true,
        Targets = [.. targets]
    };

    private static ScannedFileItem MakeItem(string rule, long size, bool recycleBinItem = false, string? path = null) => new()
    {
        RuleId = rule,
        RuleName = rule,
        Category = "Test",
        FullPath = path ?? $"C:\\x\\{Guid.NewGuid():N}.tmp",
        FileName = "f.tmp",
        SizeBytes = size,
        LastModified = DateTime.Now,
        IsRecycleBinItem = recycleBinItem
    };

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"FreeCleanerV52_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020), DeleteFailureKind.InUse)]
    [InlineData(unchecked((int)0x80070021), DeleteFailureKind.InUse)]
    [InlineData(unchecked((int)0x80070005), DeleteFailureKind.AccessDenied)]
    [InlineData(unchecked((int)0x80070070), DeleteFailureKind.Other)]
    public void ClassifyDeleteFailure_MapsWin32Codes(int hresult, DeleteFailureKind expected)
    {
        var ex = new IOException("test", hresult);
        Assert.Equal(expected, CleanerEngine.ClassifyDeleteFailure(ex));
    }

    [Fact]
    public void ClassifyDeleteFailure_UnauthorizedAccess_IsAccessDenied()
    {
        Assert.Equal(DeleteFailureKind.AccessDenied, CleanerEngine.ClassifyDeleteFailure(new UnauthorizedAccessException()));
        Assert.Equal(DeleteFailureKind.Other, CleanerEngine.ClassifyDeleteFailure(new InvalidOperationException()));
    }

    [Theory]
    [InlineData(@"C:\Windows\Temp", true)]
    [InlineData(@"C:\WINDOWS\SoftwareDistribution\Download", true)]
    [InlineData(@"C:\Windows", true)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\WER", true)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\WER\ReportQueue", true)]
    [InlineData(@"C:\WindowsApps\Temp", false)]
    [InlineData(@"C:\Users\erkan\AppData\Local\Temp", false)]
    [InlineData(@"C:\Users\erkan\AppData\Local\Microsoft\Windows\WER", false)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\WERX", false)]
    public void IsAdminOnlyPath_RecognizesWindowsAndMachineWer(string path, bool expected)
    {
        Assert.Equal(expected, CleanerEngine.IsAdminOnlyPath(path, @"C:\Windows", @"C:\ProgramData"));
    }

    [Fact]
    public async Task AnalyzeAsync_TempAgeFilter_KeepsRecentTempFiles_AndReportsThem()
    {
        string dir = NewTempDir();
        try
        {
            string oldFile = Path.Combine(dir, "old.tmp");
            string newFile = Path.Combine(dir, "new.tmp");
            await File.WriteAllTextAsync(oldFile, "old-old");
            await File.WriteAllTextAsync(newFile, "new");
            File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-3));
            // Older than the engine's built-in 60-second guard for folders named "Temp", newer than the 24-hour filter.
            File.SetLastWriteTime(newFile, DateTime.Now.AddHours(-2));

            var engine = new CleanerEngine { TempMinimumAge = TimeSpan.FromHours(24), IsElevatedOverride = true };

            var tempRule = MakeRule("sys_user_temp", "Temp", new ScanTarget(dir));
            var found = await engine.AnalyzeAsync([tempRule]);
            Assert.Single(found);
            Assert.Equal(oldFile, found[0].FullPath);
            Assert.Equal(1, engine.LastAnalysisNotes.SkippedRecentTempFiles);
            Assert.Equal(3, engine.LastAnalysisNotes.SkippedRecentTempBytes);

            // Other rules are not age-filtered.
            var otherRule = MakeRule("dev_other", "Other", new ScanTarget(dir));
            Assert.Equal(2, (await engine.AnalyzeAsync([otherRule])).Count);
            Assert.Equal(0, engine.LastAnalysisNotes.SkippedRecentTempFiles);

            // Filter off → everything is found.
            engine.TempMinimumAge = TimeSpan.Zero;
            Assert.Equal(2, (await engine.AnalyzeAsync([MakeRule("sys_user_temp", "Temp", new ScanTarget(dir))])).Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CleanAsync_MarksItemsCleaned_IndependentOfLanguage()
    {
        string dir = NewTempDir();
        try
        {
            string file = Path.Combine(dir, "a.tmp");
            await File.WriteAllTextAsync(file, "abc");
            // Files written in the last 60 seconds inside a "Temp" folder are skipped by design.
            File.SetLastWriteTime(file, DateTime.Now.AddHours(-1));
            var engine = new CleanerEngine { IsElevatedOverride = true };
            var rule = MakeRule("dev_other", "Other", new ScanTarget(dir));

            using (LocalizationService.BeginScopedLanguage("en-US"))
            {
                var items = await engine.AnalyzeAsync([rule]);
                var gone = MakeItem("dev_other", 1, path: Path.Combine(dir, "missing.tmp"));
                items.Add(gone);

                var summary = await engine.CleanAsync(items, [rule], moveToRecycleBinInsteadOfPermanentDelete: false);

                Assert.All(items, i => Assert.True(i.IsCleaned, i.FullPath));
                Assert.Equal(2, summary.DeletedFilesCount);
                Assert.Equal(0, summary.InUseCount);
                Assert.Equal(0, summary.AccessDeniedCount);
                Assert.False(File.Exists(file));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CleanupPlanSummary_GroupsByRule_LargestFirst_AndSplitsRecycleBin()
    {
        var items = new[]
        {
            MakeItem("Small", 1_000),
            MakeItem("Big", 5_000_000),
            MakeItem("Big", 5_000_000),
            MakeItem("Bin", 2_000_000, recycleBinItem: true)
        };

        string text = CleanupPlanSummary.Describe(items, useRecycleBin: true, language: "en-US");
        string[] lines = text.Split('\n');
        Assert.StartsWith("• Big:", lines[0]);
        Assert.Contains("2 files", lines[0]);
        Assert.StartsWith("• Bin:", lines[1]);
        Assert.StartsWith("• Small:", lines[2]);
        Assert.Contains("Moved to the Recycle Bin (can be undone)", text);
        Assert.Contains("Deleted permanently", text);
        Assert.Contains("includes Recycle Bin contents", text);

        string permanentOnly = CleanupPlanSummary.Describe(items, useRecycleBin: false, language: "tr-TR");
        Assert.DoesNotContain("Geri Dönüşüm Kutusu'na taşınacak", permanentOnly);
        Assert.Contains("Kalıcı olarak silinecek", permanentOnly);

        Assert.Equal(string.Empty, CleanupPlanSummary.Describe([], useRecycleBin: true));
    }

    [Fact]
    public void CleanupPlanSummary_CollapsesManyRules()
    {
        var items = Enumerable.Range(1, 9).Select(i => MakeItem($"R{i}", i * 100)).ToList();
        string text = CleanupPlanSummary.Describe(items, useRecycleBin: true, language: "en-US", maxRules: 6);
        Assert.Contains("+3 more rules", text);
        Assert.Equal(7, text.Split('\n').Count(l => l.StartsWith('•')));
    }

    [Fact]
    public void CleanupPlanSummary_DescribesSkipReasons_AndAnalysisNotes()
    {
        var summary = new CleanSummaryResult { SkippedFilesCount = 6, InUseCount = 3, AccessDeniedCount = 2 };
        Assert.Equal(
            "3 files in use • 2 files need administrator rights • 1 files skipped",
            CleanupPlanSummary.DescribeSkipped(summary, "en-US"));
        Assert.Equal(string.Empty, CleanupPlanSummary.DescribeSkipped(new CleanSummaryResult(), "en-US"));

        var notes = new AnalysisNotes { SkippedRecentTempFiles = 4, SkippedRecentTempBytes = 2048 };
        notes.SkippedAdminOnlyPaths.Add(@"C:\Windows\Temp");
        notes.SkippedAdminOnlyPaths.Add(@"c:\windows\temp");
        string text = CleanupPlanSummary.DescribeAnalysisNotes(notes, "tr-TR");
        Assert.Contains("4 geçici dosya", text);
        Assert.Contains("1 sistem klasörü", text);
        Assert.Equal(string.Empty, CleanupPlanSummary.DescribeAnalysisNotes(new AnalysisNotes(), "tr-TR"));
    }

    [Fact]
    public void Settings_OnlyOldTempFiles_DefaultsOn_ForOldSettingsFiles()
    {
        Assert.True(new CleanerConfigData().OnlyOldTempFiles);
        var fromOldFile = JsonSerializer.Deserialize<CleanerConfigData>("{\"UseRecycleBinSafety\":false}");
        Assert.NotNull(fromOldFile);
        Assert.True(fromOldFile.OnlyOldTempFiles);
        Assert.False(fromOldFile.UseRecycleBinSafety);
    }

    [Fact]
    public void SmokeTestOptions_Parse()
    {
        Assert.False(SmokeTestOptions.Parse(null).Enabled);
        Assert.False(SmokeTestOptions.Parse(["--minimized"]).Enabled);

        var o = SmokeTestOptions.Parse(["--smoke-test", "--smoke-report", "\"C:\\out dir\"", "--smoke-screenshots"]);
        Assert.True(o.Enabled);
        Assert.True(o.Screenshots);
        Assert.Equal("C:\\out dir", o.ReportDirectory);

        var d = SmokeTestOptions.Parse(["--SMOKE-TEST"]);
        Assert.True(d.Enabled);
        Assert.False(d.Screenshots);
        Assert.Equal(SmokeTestOptions.DefaultReportDirectory, d.ReportDirectory);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("   ", true)]
    [InlineData("System.Windows.Data Error: 4 : Cannot find source for binding with reference 'RelativeSource FindAncestor, AncestorType='System.Windows.Controls.ItemsControl''. BindingExpression:Path=HorizontalContentAlignment;", true)]
    [InlineData("Cannot get 'Name' value (type 'String') from '' (type 'NamedObject'). {DisconnectedItem}", true)]
    [InlineData("System.Windows.Data Error: 40 : BindingExpression path error: 'TotalSizeX' property not found on 'object' ''MainViewModel'", false)]
    public void BindingErrorFilter_IgnoresOnlyWpfTemplateNoise(string? message, bool benign)
    {
        Assert.Equal(benign, BindingErrorFilter.IsBenign(message));
    }

    [Fact]
    public void UnattendedDialogService_NeverConfirms_AndRecordsEveryDialog()
    {
        var dialogs = new UnattendedDialogService();
        Assert.Equal(AppDialogResult.No, dialogs.Show("Sil?", "Onay", AppDialogButtons.YesNo, AppDialogIcon.Warning));
        Assert.Equal(AppDialogResult.Cancel, dialogs.Show("Kapat?", "Süreç", AppDialogButtons.YesNoCancel));
        Assert.Equal(AppDialogResult.OK, dialogs.Show("Bilgi\nsatır 2", "Bilgi"));
        Assert.Equal(3, dialogs.Log.Count);
        Assert.Contains("Bilgi satır 2", dialogs.Log[2]);
    }

    [Fact]
    public void NewUiStrings_ExistInBothLanguages()
    {
        foreach (string key in new[] { "Loc_SettingsOpenDataFolderBtn", "Loc_SettingsCleanSafetyTitle", "Loc_SettingsCleanSafetyDesc", "Loc_SettingsOnlyOldTempCheck" })
        {
            string tr = LocalizationService.Get(key, "tr-TR");
            string en = LocalizationService.Get(key, "en-US");
            Assert.False(string.IsNullOrWhiteSpace(tr), key);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(key, tr);
            Assert.NotEqual(key, en);
            Assert.NotEqual(tr, en);
        }
    }

    [Fact]
    public void MainViewModelV52_OnlyOldTempFiles_DefaultsOn_AndPersists()
    {
        string tempDataRoot = NewTempDir();
        Exception? threadException = null;
        var staThread = new Thread(() =>
        {
            try
            {
                using (LocalizationService.BeginScopedLanguage("tr-TR"))
                using (AppStoragePathsService.BeginScopedDataDirectory(tempDataRoot))
                {
                    using var vm = new MainViewModel(new UnattendedDialogService());
                    Assert.True(vm.OnlyOldTempFiles);

                    vm.OnlyOldTempFiles = false;
                    Assert.False(ExclusionAndSettingsService.Load().OnlyOldTempFiles);

                    vm.OnlyOldTempFiles = true;
                    Assert.True(ExclusionAndSettingsService.Load().OnlyOldTempFiles);
                    Assert.NotNull(vm.OpenDataFolderCommand);
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
                Directory.Delete(tempDataRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }
        }
    }
}
