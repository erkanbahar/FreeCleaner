using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public sealed record CrashReportResult(string ReportPath, string RedactedSummary);

/// <summary>
/// Captures unhandled UI, AppDomain, and background Task exceptions globally,
/// writes redacted crash diagnostics under the active data folder, and offers
/// one-click desktop Support Bundle ZIP export on UI crashes.
/// </summary>
public static class CrashGuardService
{
    private const int MaxRetainedCrashLogs = 20;
    private static int _handlersInstalled;
    private static int _isHandlingDispatcherCrash;

    public static string GetCrashDirectory(string? dataDirOverride = null)
    {
        string baseDataDir = string.IsNullOrWhiteSpace(dataDirOverride)
            ? AppStoragePathsService.GetDataDirectory()
            : dataDirOverride;
        return Path.Combine(baseDataDir, "crashes");
    }

    public static string? GetLatestCrashLogPath(string? dataDirOverride = null)
    {
        string crashDir = GetCrashDirectory(dataDirOverride);
        if (!Directory.Exists(crashDir))
            return null;

        try
        {
            return new DirectoryInfo(crashDir)
                .GetFiles("crash-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static string FormatCrashReport(string source, Exception exception, string? appVersion = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string version = string.IsNullOrWhiteSpace(appVersion)
            ? AppUpdateService.CurrentVersion
            : appVersion;

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sb = new StringBuilder();
        sb.AppendLine("=== FreeCleaner Pro Crash Diagnostic ===");
        sb.AppendLine($"TimestampUtc : {DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"AppVersion   : v{version}");
        sb.AppendLine($"DotNet       : {Environment.Version}");
        sb.AppendLine($"OS           : {Environment.OSVersion}");
        sb.AppendLine($"PortableMode : {AppStoragePathsService.IsPortableModeEnabled()}");
        sb.AppendLine($"Source       : {source}");
        sb.AppendLine($"ExceptionType: {exception.GetType().FullName}");
        sb.AppendLine("--- Exception Details (Redacted) ---");

        string rawDetails = exception.ToString();
        using var reader = new StringReader(rawDetails);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            sb.AppendLine(RedactLineAndUserProfile(line, userProfile));
        }

        return sb.ToString();
    }

    private static string RedactLineAndUserProfile(string line, string userProfile)
    {
        string redacted = AppStoragePathsService.RedactSensitiveLine(line);
        if (!string.IsNullOrWhiteSpace(userProfile))
            redacted = redacted.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

        return redacted;
    }

    public static CrashReportResult WriteCrashReport(
        string source,
        Exception exception,
        string? dataDirOverride = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string reportText = FormatCrashReport(source, exception);
        string crashDir = GetCrashDirectory(dataDirOverride);

        string filePath = Path.Combine(
            crashDir,
            $"crash-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.log");

        try
        {
            Directory.CreateDirectory(crashDir);
            if (File.Exists(filePath))
            {
                string uniqueSuffix = Guid.NewGuid().ToString("N")[..6];
                filePath = Path.Combine(
                    crashDir,
                    $"crash-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{uniqueSuffix}.log");
            }

            File.WriteAllText(filePath, reportText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            PruneOldCrashLogs(crashDir);
        }
        catch
        {
            // Crash persistence must never mask the primary handling path.
        }

        if (string.IsNullOrWhiteSpace(dataDirOverride))
            ErrorLogService.Log($"CrashGuard:{source}", exception);

        return new CrashReportResult(filePath, reportText);
    }

    private static void PruneOldCrashLogs(string crashDir)
    {
        try
        {
            List<FileInfo> files = new DirectoryInfo(crashDir)
                .GetFiles("crash-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .ToList();

            foreach (FileInfo stale in files.Skip(MaxRetainedCrashLogs))
            {
                try
                {
                    stale.Delete();
                }
                catch
                {
                    // Ignore locked crash logs during best-effort pruning.
                }
            }
        }
        catch
        {
            // Ignore pruning enumeration errors.
        }
    }

    public static void InstallGlobalHandlers(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (Interlocked.CompareExchange(ref _handlersInstalled, 1, 0) != 0)
            return;

        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _isHandlingDispatcherCrash, 1, 0) != 0)
            return;

        try
        {
            CrashReportResult result = WriteCrashReport("DispatcherUI", e.Exception);
            e.Handled = true;

            bool isEnglish = LocalizationService.CurrentLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            string title = LocalizationService.Get("Loc_CrashDialogTitle");
            string body = string.Format(
                CultureInfo.CurrentCulture,
                LocalizationService.Get("Loc_CrashDialogBody"),
                AppStoragePathsService.RedactSensitiveLine(e.Exception.Message),
                result.ReportPath);

            MessageBoxResult choice = MessageBox.Show(
                body,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (choice == MessageBoxResult.Yes)
                ExportCrashBundleToDesktop(Application.Current, isEnglish);
        }
        catch
        {
            // Last-resort safeguard inside unhandled UI exception handler.
        }
        finally
        {
            Interlocked.Exchange(ref _isHandlingDispatcherCrash, 0);
        }
    }

    private static void ExportCrashBundleToDesktop(Application? app, bool isEnglish)
    {
        try
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string zipPath = Path.Combine(
                desktopDir,
                $"FreeCleaner-CrashBundle-{DateTime.Now:yyyyMMdd_HHmm}.zip");

            var vm = app?.MainWindow?.DataContext as ViewModels.MainViewModel;
            SystemMetricsSnapshot metrics = vm?.SystemMetrics ?? SystemInfoService.GetMetrics();
            IEnumerable<CleanerRule> rules = vm?.AllRules ?? [];
            long freedBytes = vm?.LastFreedBytes ?? 0;
            int deletedFiles = vm?.LastDeletedFiles ?? 0;
            IEnumerable<string> logs = vm?.ActivityLogs ?? [];
            int healthScore = vm?.HealthScore ?? 100;
            string? healthBadge = vm?.HealthGradeBadge;

            long lifetimeFreed = CleanupHistoryService.GetTotalLifetimeFreedBytes(CleanupHistoryService.LoadHistory());
            string savedZip = AppStoragePathsService.ExportSupportBundleZip(
                zipPath,
                metrics,
                rules,
                freedBytes,
                deletedFiles,
                logs,
                healthScore,
                healthBadge,
                lifetimeFreed,
                vm?.RecentCleanupBars.ToList());

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{savedZip}\"")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Opening Explorer is best-effort.
            }
        }
        catch (Exception exportEx)
        {
            ErrorLogService.Log("CrashGuard:SupportBundleExport", exportEx);
            string errTitle = isEnglish ? "Export Failed" : "Dışa Aktarma Başarısız";
            MessageBox.Show(exportEx.Message, errTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashReport("AppDomain", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashReport("UnobservedTask", e.Exception);
        e.SetObserved();
    }
}
