using System.Diagnostics;
using System.IO;

namespace FreeCleaner.Services;

public static class ScheduledMaintenanceService
{
    private const string TaskName = "FreeCleaner Safe Maintenance";

    public static bool IsInstalled()
    {
        (int exitCode, _) = RunSchtasks(["/Query", "/TN", TaskName]);
        return exitCode == 0;
    }

    public static (bool Success, string Message) Install(int intervalHours)
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return (false, LocalizationService.GetText(
                "FreeCleaner çalıştırılabilir dosyasının yolu bulunamadı.",
                "Could not locate FreeCleaner executable path."));
        }

        string schedule = intervalHours == 168 ? "WEEKLY" : "DAILY";
        string taskCommand = $"\"{executablePath}\" --scheduled-maintenance";

        var args = new List<string>
        {
            "/Create",
            "/TN", TaskName,
            "/SC", schedule
        };

        if (intervalHours == 48)
        {
            args.Add("/MO");
            args.Add("2");
        }
        else if (intervalHours == 168)
        {
            args.Add("/D");
            args.Add("SUN");
        }

        args.AddRange(["/ST", "03:00", "/TR", taskCommand, "/F", "/RL", "LIMITED"]);

        (int exitCode, string output) = RunSchtasks(args);

        if (exitCode == 0)
        {
            string cadenceTr = intervalHours == 48 ? "48 saatlik" : intervalHours == 168 ? "haftalık" : "günlük";
            string cadenceEn = intervalHours == 48 ? "every 48 hours" : intervalHours == 168 ? "weekly" : "daily";
            return (true, LocalizationService.GetText(
                $"Windows zamanlanmış bakım görevi oluşturuldu ({cadenceTr}).",
                $"Windows scheduled maintenance task created ({cadenceEn})."));
        }

        ErrorLogService.Log("TaskScheduler:Install", output);
        return (false, LocalizationService.GetText(
            $"Zamanlanmış görev oluşturulamadı: {output}",
            $"Failed to create scheduled task: {output}"));
    }

    public static (bool Success, string Message) Remove()
    {
        (int exitCode, string output) = RunSchtasks(["/Delete", "/TN", TaskName, "/F"]);
        if (exitCode == 0)
        {
            return (true, LocalizationService.GetText(
                "Windows zamanlanmış bakım görevi kaldırıldı.",
                "Windows scheduled maintenance task removed."));
        }

        ErrorLogService.Log("TaskScheduler:Remove", output);
        return (false, LocalizationService.GetText(
            $"Zamanlanmış görev kaldırılamadı: {output}",
            $"Failed to remove scheduled task: {output}"));
    }

    private static (int ExitCode, string Output) RunSchtasks(IEnumerable<string> arguments)
    {
        ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
            "schtasks.exe",
            arguments,
            redirectOutput: true);

        ProcessRunResult result = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(15));
        if (!result.Started)
        {
            ErrorLogService.Log("TaskScheduler", "schtasks.exe could not be started.");
            return (-1, LocalizationService.GetText("schtasks.exe başlatılamadı.", "schtasks.exe could not be started."));
        }

        string combined = (result.StandardOutput + Environment.NewLine + result.StandardError).Trim();
        return (result.ExitCode, combined);
    }
}
