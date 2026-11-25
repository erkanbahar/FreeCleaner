using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

/// <summary>
/// Resolves application data storage paths (%LocalAppData%\FreeCleaner or portable ./data)
/// and packages redacted diagnostic support bundles into .zip archives.
/// </summary>
public static partial class AppStoragePathsService
{
    public const string PortableFlagFileName = "portable.flag";
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private static readonly AsyncLocal<string?> ScopedDataDirectoryOverride = new();
    private static string? _processDataDirectoryOverride;

    /// <summary>
    /// Redirects GetDataDirectory() for the whole process (used by the UI smoke test so the user's real
    /// settings, history and logs are never written). Null restores the normal location.
    /// </summary>
    public static void SetProcessDataDirectoryOverride(string? directoryPath)
        => _processDataDirectoryOverride = string.IsNullOrWhiteSpace(directoryPath) ? null : Path.GetFullPath(directoryPath);

    private sealed class DataDirectoryScope(string? previousValue) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ScopedDataDirectoryOverride.Value = previousValue;
        }
    }

    /// <summary>
    /// Temporarily scopes GetDataDirectory() for headless unit tests so production %LocalAppData%\FreeCleaner is never mutated.
    /// </summary>
    public static IDisposable BeginScopedDataDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string? previous = ScopedDataDirectoryOverride.Value;
        ScopedDataDirectoryOverride.Value = Path.GetFullPath(directoryPath);
        return new DataDirectoryScope(previous);
    }

    public static string GetBaseAppDirectory(string? baseDirOverride = null)
        => string.IsNullOrWhiteSpace(baseDirOverride) ? AppContext.BaseDirectory : baseDirOverride;

    public static string GetPortableFlagPath(string? baseDirOverride = null)
        => Path.Combine(GetBaseAppDirectory(baseDirOverride), PortableFlagFileName);

    public static bool IsPortableModeEnabled(string? baseDirOverride = null)
    {
        try
        {
            return File.Exists(GetPortableFlagPath(baseDirOverride));
        }
        catch
        {
            return false;
        }
    }

    public static string GetStandardDataDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeCleaner");

    public static string GetPortableDataDirectory(string? baseDirOverride = null)
        => Path.Combine(GetBaseAppDirectory(baseDirOverride), "data");

    public static string GetDataDirectory(string? baseDirOverride = null)
    {
        if (string.IsNullOrWhiteSpace(baseDirOverride) &&
            !string.IsNullOrWhiteSpace(ScopedDataDirectoryOverride.Value))
        {
            return ScopedDataDirectoryOverride.Value!;
        }

        if (string.IsNullOrWhiteSpace(baseDirOverride) && _processDataDirectoryOverride is not null)
            return _processDataDirectoryOverride;

        return IsPortableModeEnabled(baseDirOverride)
            ? GetPortableDataDirectory(baseDirOverride)
            : GetStandardDataDirectory();
    }

    public static string GetRegistryBackupsDirectory(string? baseDirOverride = null)
        => Path.Combine(GetDataDirectory(baseDirOverride), "RegistryBackups");

    public static string GetSqliteBackupsDirectory(string? baseDirOverride = null)
        => Path.Combine(GetDataDirectory(baseDirOverride), "SQLiteBackups");

    public static string GetHostsBackupsDirectory(string? baseDirOverride = null)
        => Path.Combine(GetDataDirectory(baseDirOverride), "HostsBackups");

    public static string GetReportsDirectory(string? baseDirOverride = null)
        => IsPortableModeEnabled(baseDirOverride)
            ? Path.Combine(GetPortableDataDirectory(baseDirOverride), "Reports")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "FreeCleaner_Raporlar");

    public static string GetFontBackupsDirectory(string? baseDirOverride = null)
        => IsPortableModeEnabled(baseDirOverride)
            ? Path.Combine(GetPortableDataDirectory(baseDirOverride), "FontBackups")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "FreeCleaner_FontBackup");

    public static string ResolveDefaultDevWorkspaceRoot(IEnumerable<string>? candidateRootsOverride = null)
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IEnumerable<string> candidates = candidateRootsOverride ??
        [
            Path.Combine(userProfile, "source", "repos"),
            Path.Combine(userProfile, "Projects"),
            Path.Combine(userProfile, "Repos"),
            Path.Combine(userProfile, "Documents")
        ];

        foreach (string dir in candidates)
        {
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                return dir;
        }

        return !string.IsNullOrWhiteSpace(userProfile) && Directory.Exists(userProfile)
            ? userProfile
            : AppContext.BaseDirectory;
    }

    public static (bool Success, string Message) SetPortableMode(
        bool enabled,
        string? baseDirOverride = null,
        string? standardDirOverride = null)
    {
        try
        {
            string flagPath = GetPortableFlagPath(baseDirOverride);
            string standardDir = string.IsNullOrWhiteSpace(standardDirOverride)
                ? GetStandardDataDirectory()
                : standardDirOverride;
            string portableDir = GetPortableDataDirectory(baseDirOverride);

            if (enabled)
            {
                Directory.CreateDirectory(portableDir);
                CopyIfExists(Path.Combine(standardDir, "settings.json"), Path.Combine(portableDir, "settings.json"));
                CopyIfExists(Path.Combine(standardDir, "history.json"), Path.Combine(portableDir, "history.json"));
                CopyIfExists(Path.Combine(standardDir, "drive_telemetry.json"), Path.Combine(portableDir, "drive_telemetry.json"));

                File.WriteAllText(flagPath, $"FreeCleaner Pro Portable Mode enabled at {DateTime.UtcNow:O}", Encoding.UTF8);
                return (true, $"Taşınabilir mod etkinleştirildi. Veriler şu klasörde tutuluyor: {portableDir}");
            }
            else
            {
                Directory.CreateDirectory(standardDir);
                CopyIfExists(Path.Combine(portableDir, "settings.json"), Path.Combine(standardDir, "settings.json"));
                CopyIfExists(Path.Combine(portableDir, "history.json"), Path.Combine(standardDir, "history.json"));
                CopyIfExists(Path.Combine(portableDir, "drive_telemetry.json"), Path.Combine(standardDir, "drive_telemetry.json"));

                if (File.Exists(flagPath))
                    File.Delete(flagPath);

                return (true, $"Standart AppData depolama moduna dönüldü: {standardDir}");
            }
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Taşınabilir mod geçişi", ex);
            return (false, $"Taşınabilir mod değiştirilemedi: {ex.Message}");
        }
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (!File.Exists(source))
            return;

        string? destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Copy(source, destination, overwrite: true);
    }

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|apikey|api_key|secret|token)\s*[:=]\s*[^\s;,&""']+")]
    private static partial Regex SecretKeyValueRegex();

    public static string RedactSensitiveLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;

        string redacted = BearerTokenRegex().Replace(line, "$1***REDACTED***");
        redacted = SecretKeyValueRegex().Replace(redacted, "$1=***REDACTED***");
        return redacted;
    }

    public static string ExportSupportBundleZip(
        string zipPath,
        SystemMetricsSnapshot metrics,
        IEnumerable<CleanerRule> rules,
        long totalFreedBytes,
        int totalDeletedFiles,
        IEnumerable<string> logs,
        int healthScore = 100,
        string? healthBadge = null,
        long lifetimeFreedBytes = 0,
        IReadOnlyList<CleanupHistoryBarItem>? recentHistory = null,
        string? dataDirOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);

        string fullZipPath = Path.GetFullPath(zipPath);
        string? zipDir = Path.GetDirectoryName(fullZipPath);
        if (!string.IsNullOrEmpty(zipDir))
            Directory.CreateDirectory(zipDir);

        List<string> sanitizedLogs = logs.Select(RedactSensitiveLine).ToList();
        List<CleanerRule> ruleList = rules.ToList();

        string htmlReport = SystemBoosterAndReportService.BuildDiagnosticHtmlReport(
            metrics,
            ruleList,
            totalFreedBytes,
            totalDeletedFiles,
            sanitizedLogs,
            healthScore,
            healthBadge,
            lifetimeFreedBytes,
            recentHistory);

        string dataDir = string.IsNullOrWhiteSpace(dataDirOverride) ? GetDataDirectory() : dataDirOverride;
        IReadOnlyList<string> recentErrorLines = ErrorLogService.ReadRecentEntries(maxLines: 200, dataDirOverride: dataDir);

        string jsonSummary = JsonSerializer.Serialize(new
        {
            GeneratedUtc = DateTime.UtcNow,
            Version = AppUpdateService.CurrentVersion,
            PortableMode = IsPortableModeEnabled(),
            HealthScore = healthScore,
            HealthBadge = healthBadge ?? string.Empty,
            LifetimeFreedBytes = lifetimeFreedBytes,
            System = metrics,
            SessionFreedBytes = totalFreedBytes,
            SessionDeletedFiles = totalDeletedFiles,
            RuleCount = ruleList.Count,
            RuntimeDiagnostics = new
            {
                OsDescription = RuntimeInformation.OSDescription,
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                DotNetVersion = Environment.Version.ToString(),
                DataDirectoryMode = IsPortableModeEnabled() ? "Portable" : "StandardAppData",
                RecentErrorLineCount = recentErrorLines.Count
            }
        }, IndentedJsonOptions);

        using FileStream fs = new(fullZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(fs, ZipArchiveMode.Create);

        WriteTextEntry(archive, "diagnostic_report.html", htmlReport);
        WriteTextEntry(archive, "system_summary.json", jsonSummary);
        WriteTextEntry(archive, "activity_sanitized.log", string.Join(Environment.NewLine, sanitizedLogs));

        IncludeRedactedFileIfExists(archive, Path.Combine(dataDir, "errors.log"), "errors_sanitized.log");
        IncludeRedactedFileIfExists(archive, Path.Combine(dataDir, "errors.log.1"), "errors_previous_sanitized.log");
        IncludeRedactedFileIfExists(archive, Path.Combine(dataDir, "settings.json"), "settings.json");

        string? latestCrashPath = CrashGuardService.GetLatestCrashLogPath(dataDir);
        if (!string.IsNullOrWhiteSpace(latestCrashPath))
            IncludeRedactedFileIfExists(archive, latestCrashPath, "latest_crash_sanitized.log");

        return fullZipPath;
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void IncludeRedactedFileIfExists(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
            return;

        try
        {
            string[] lines = File.ReadAllLines(sourcePath, Encoding.UTF8);
            string sanitized = string.Join(Environment.NewLine, lines.TakeLast(500).Select(RedactSensitiveLine));
            WriteTextEntry(archive, entryName, sanitized);
        }
        catch
        {
            // Ignore locked optional support file.
        }
    }
}
