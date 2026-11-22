using System.IO;
using System.Text;

namespace FreeCleaner.Services;

public static class ErrorLogService
{
    private static readonly object SyncRoot = new();
    public const long MaxLogFileBytes = 2 * 1024 * 1024;

    public static string GetLogPath(string? dataDirOverride = null)
        => Path.Combine(
            string.IsNullOrWhiteSpace(dataDirOverride) ? AppStoragePathsService.GetDataDirectory() : dataDirOverride,
            "errors.log");

    public static void Log(
        string area,
        Exception exception,
        string? dataDirOverride = null,
        long maxBytes = MaxLogFileBytes)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string raw = $"{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}";
        Log(area, raw, dataDirOverride, maxBytes);
    }

    public static void Log(
        string area,
        string message,
        string? dataDirOverride = null,
        long maxBytes = MaxLogFileBytes)
    {
        try
        {
            string logPath = GetLogPath(dataDirOverride);
            string sanitizedArea = SafeProcessRunner.SanitizeSingleLine(area);
            string redactedBody = AppStoragePathsService.RedactSensitiveLine(message);
            string entry = $"[{DateTime.Now:O}] [{sanitizedArea}] {redactedBody}{Environment.NewLine}";

            lock (SyncRoot)
            {
                string? dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                RotateIfOversized(logPath, maxBytes);
                File.AppendAllText(logPath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must not interrupt the original operation.
        }
    }

    public static IReadOnlyList<string> ReadRecentEntries(
        int maxLines = 200,
        string? dataDirOverride = null)
    {
        if (maxLines <= 0)
            return [];

        try
        {
            string logPath = GetLogPath(dataDirOverride);
            lock (SyncRoot)
            {
                if (!File.Exists(logPath))
                    return [];

                string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
                return lines.TakeLast(maxLines).ToList();
            }
        }
        catch
        {
            return [];
        }
    }

    private static void RotateIfOversized(string logPath, long maxBytes)
    {
        if (maxBytes <= 0 || !File.Exists(logPath))
            return;

        var info = new FileInfo(logPath);
        if (info.Length < maxBytes)
            return;

        string archivePath = logPath + ".1";
        File.Move(logPath, archivePath, overwrite: true);
    }
}
