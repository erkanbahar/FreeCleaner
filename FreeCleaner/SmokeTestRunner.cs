using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeCleaner.Services;
using FreeCleaner.ViewModels;

namespace FreeCleaner;

/// <summary>
/// UI smoke test, started with <c>FreeCleaner.exe --smoke-test</c> (CI runs it on every push; it can be run
/// locally with scripts\smoke-test.ps1). It opens the real window, visits every section in Turkish and English,
/// checks that each section's page is shown, and collects data-binding errors and unhandled exceptions.
/// It writes smoke-report.txt and exits with 0 (passed) or 1 (failed).
/// Dialogs are answered "No"/"Cancel" automatically and settings/logs go to a temporary data folder,
/// so nothing is deleted and the user's real settings are never touched.
/// </summary>
internal static class SmokeTestRunner
{
    private static readonly List<string> UnhandledExceptions = [];

    public static void RecordUnhandled(string source, Exception exception)
    {
        lock (UnhandledExceptions)
            UnhandledExceptions.Add($"[{source}] {exception.GetType().Name}: {exception.Message}");
    }

    /// <summary>
    /// Points the data folder at a throw-away copy so the run reads the user's settings but never writes them.
    /// Must run before the main window (and its view model) is created.
    /// </summary>
    public static string PrepareIsolatedDataDirectory(SmokeTestOptions options)
    {
        string realDataDir = AppStoragePathsService.GetDataDirectory();
        string isolated = Path.Combine(options.ReportDirectory, "data");
        Directory.CreateDirectory(isolated);
        try
        {
            string settings = Path.Combine(realDataDir, "settings.json");
            if (File.Exists(settings))
                File.Copy(settings, Path.Combine(isolated, "settings.json"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Defaults are fine for the smoke test.
        }

        AppStoragePathsService.SetProcessDataDirectoryOverride(isolated);
        return isolated;
    }

    public static async Task<int> RunAsync(MainWindow window, MainViewModel vm, SmokeTestOptions options, UnattendedDialogService dialogs)
    {
        var stopwatch = Stopwatch.StartNew();
        var failures = new List<string>();
        var notes = new List<string>();
        int visited = 0;

        Directory.CreateDirectory(options.ReportDirectory);
        string screensDir = Path.Combine(options.ReportDirectory, "screens");
        if (options.Screenshots)
            Directory.CreateDirectory(screensDir);

        string originalLanguage = vm.SelectedLanguage;
        var sectionKeys = new List<string> { "Dashboard" };
        sectionKeys.AddRange(vm.AllModuleCatalog.Select(m => m.SectionKey));
        sectionKeys.AddRange(window.LazySectionKeys);
        sectionKeys.Add("Settings");
        sectionKeys = sectionKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        try
        {
            window.WindowState = WindowState.Normal;
            window.Width = 1440;
            window.Height = 900;
            await Idle();

            foreach (string language in new[] { "tr-TR", "en-US" })
            {
                vm.SelectedLanguage = language;
                await Idle();

                int index = 0;
                foreach (string key in sectionKeys)
                {
                    index++;
                    vm.ActiveSection = key;
                    // The first pass lets each page start loading its data so row templates get exercised too.
                    await Task.Delay(language == "tr-TR" ? 600 : 150);
                    await Idle();
                    visited++;

                    if (!string.Equals(vm.ActiveSection, key, StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add($"[{language}] '{key}' could not be selected (active: {vm.ActiveSection}).");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(vm.SectionTitle))
                        failures.Add($"[{language}] '{key}': section title is empty.");

                    if (window.HasLazySectionView(key) && !window.IsLazySectionShown(key))
                        failures.Add($"[{language}] '{key}': page is not displayed.");

                    if (options.Screenshots)
                        TrySaveScreenshot(window, Path.Combine(screensDir, $"{language}-{index:00}-{key}.png"), notes);
                }
            }

            // Background loads started by the sections count too (their row bindings are checked as well).
            var wait = Stopwatch.StartNew();
            while (vm.IsBusy && wait.Elapsed < TimeSpan.FromSeconds(60))
                await Task.Delay(500);
            await Task.Delay(1500);
            await Idle();
        }
        catch (Exception ex)
        {
            failures.Add($"The smoke test itself failed: {ex}");
        }
        finally
        {
            try
            {
                vm.SelectedLanguage = originalLanguage;
            }
            catch (Exception ex)
            {
                notes.Add($"Could not restore language: {ex.Message}");
            }
        }

        var bindingErrors = BindingErrorListener.Instance?.Errors.ToList() ?? [];
        List<string> unhandled;
        lock (UnhandledExceptions)
            unhandled = UnhandledExceptions.ToList();
        notes.AddRange(dialogs.Log.Select(d => "Dialog " + d));

        bool passed = failures.Count == 0 && bindingErrors.Count == 0 && unhandled.Count == 0;
        WriteReport(options, passed, stopwatch.Elapsed, visited, sectionKeys.Count, failures, bindingErrors, unhandled, notes);
        return passed ? 0 : 1;
    }

    private static Task Idle()
        => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static void TrySaveScreenshot(Window window, string path, List<string> notes)
    {
        try
        {
            if (window.Content is not FrameworkElement root || root.ActualWidth < 1 || root.ActualHeight < 1)
                return;

            var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            notes.Add($"Screenshot failed ({Path.GetFileName(path)}): {ex.Message}");
        }
    }

    private static void WriteReport(
        SmokeTestOptions options,
        bool passed,
        TimeSpan elapsed,
        int visited,
        int sectionCount,
        List<string> failures,
        List<string> bindingErrors,
        List<string> unhandled,
        List<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"FreeCleaner UI smoke test - {(passed ? "PASSED" : "FAILED")}");
        sb.AppendLine($"Version: {AppUpdateService.CurrentVersion} • Time: {elapsed.TotalSeconds:F0}s • Sections: {sectionCount} × 2 languages ({visited} visits) • Administrator: {UltimateSystemSuiteService.IsRunningAsAdministrator()}");
        sb.AppendLine();
        AppendSection(sb, "Navigation / page failures", failures);
        AppendSection(sb, "Data-binding errors", bindingErrors);
        AppendSection(sb, "Unhandled exceptions", unhandled);
        AppendSection(sb, "Notes (auto-answered dialogs etc., informational)", notes);
        sb.AppendLine($"Isolated data folder (settings/logs of this run): {AppStoragePathsService.GetDataDirectory()}");

        try
        {
            File.WriteAllText(Path.Combine(options.ReportDirectory, "smoke-report.txt"), sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLogService.Log("Smoke Test", ex);
        }
    }

    private static void AppendSection(StringBuilder sb, string title, List<string> items)
    {
        sb.AppendLine($"== {title}: {items.Count}");
        foreach (string item in items)
            sb.AppendLine("  - " + item);
        sb.AppendLine();
    }
}
