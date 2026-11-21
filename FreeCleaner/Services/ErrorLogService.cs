using System.IO;
using System.Text;

namespace FreeCleaner.Services;

public static class ErrorLogService
{
    private static string LogPath => Path.Combine(
        AppStoragePathsService.GetDataDirectory(),
        "errors.log");

    public static void Log(string area, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            string entry = $"[{DateTime.Now:O}] [{area}] {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}";
            File.AppendAllText(LogPath, entry, Encoding.UTF8);
        }
        catch
        {
            // Logging must not interrupt the original operation.
        }
    }

    public static void Log(string area, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] [{area}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // Logging must not interrupt the original operation.
        }
    }
}
