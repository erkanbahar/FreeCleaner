using System.Diagnostics;
using System.IO;

namespace FreeCleaner.Services;

public static class ScheduledMaintenanceService
{
    private const string TaskName = "FreeCleaner Safe Maintenance";

    public static bool IsInstalled()
    {
        var result = RunSchtasks($"/Query /TN \"{TaskName}\"");
        return result.ExitCode == 0;
    }

    public static (bool Success, string Message) Install(int intervalHours)
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return (false, "FreeCleaner çalıştırılabilir dosyasının yolu bulunamadı.");

        string schedule = intervalHours == 168 ? "WEEKLY" : "DAILY";
        string arguments = intervalHours switch
        {
            48 => "/MO 2",
            168 => "/D SUN",
            _ => string.Empty
        };
        string taskCommand = $"\\\"{executablePath}\\\" --scheduled-maintenance";
        var result = RunSchtasks(
            $"/Create /TN \"{TaskName}\" /SC {schedule} {arguments} /ST 03:00 /TR \"{taskCommand}\" /F /RL LIMITED");

        return result.ExitCode == 0
            ? (true, $"Windows zamanlanmış bakım görevi oluşturuldu ({(intervalHours == 48 ? "48 saatlik" : intervalHours == 168 ? "haftalık" : "günlük")}).")
            : (false, $"Zamanlanmış görev oluşturulamadı: {result.Output}");
    }

    public static (bool Success, string Message) Remove()
    {
        var result = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        return result.ExitCode == 0
            ? (true, "Windows zamanlanmış bakım görevi kaldırıldı.")
            : (false, $"Zamanlanmış görev kaldırılamadı: {result.Output}");
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (process is null)
        {
            ErrorLogService.Log("Task Scheduler", "schtasks.exe başlatılamadı.");
            return (-1, "schtasks.exe başlatılamadı.");
        }

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }
}
