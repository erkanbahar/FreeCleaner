using System.IO;
using System.Runtime.InteropServices;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public record SystemMetricsSnapshot(
    string OsName,
    string CpuArchitecture,
    string DotNetVersion,
    string DriveName,
    long DriveTotalBytes,
    long DriveFreeBytes,
    double DriveUsedPercent,
    long RamTotalBytes,
    long RamAvailableBytes,
    double RamUsedPercent)
{
    public string DriveSummaryText =>
        $"{DriveName} {CleanerRule.FormatBytes(DriveTotalBytes - DriveFreeBytes)} / {CleanerRule.FormatBytes(DriveTotalBytes)} ({DriveUsedPercent:F0}% Dolu, {CleanerRule.FormatBytes(DriveFreeBytes)} Boş)";

    public string RamSummaryText =>
        $"{CleanerRule.FormatBytes(RamTotalBytes - RamAvailableBytes)} / {CleanerRule.FormatBytes(RamTotalBytes)} ({RamUsedPercent:F0}% Kullanımda)";
}

public static class SystemInfoService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public static SystemMetricsSnapshot GetMetrics()
    {
        string osName = RuntimeInformation.OSDescription;
        string arch = RuntimeInformation.OSArchitecture.ToString();
        string dotnetVer = $".NET {Environment.Version}";

        string systemDriveRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        long driveTotal = 0;
        long driveFree = 0;
        double drivePct = 0;

        try
        {
            var drive = new DriveInfo(systemDriveRoot);
            if (drive.IsReady)
            {
                driveTotal = drive.TotalSize;
                driveFree = drive.AvailableFreeSpace;
                if (driveTotal > 0)
                {
                    drivePct = ((double)(driveTotal - driveFree) / driveTotal) * 100.0;
                }
            }
        }
        catch
        {
            // Ignore drive access errors
        }

        long ramTotal = 0;
        long ramAvail = 0;
        double ramPct = 0;

        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                ramTotal = (long)mem.ullTotalPhys;
                ramAvail = (long)mem.ullAvailPhys;
                ramPct = mem.dwMemoryLoad;
            }
        }
        catch
        {
            // Ignore P/Invoke error
        }

        return new SystemMetricsSnapshot(
            osName,
            arch,
            dotnetVer,
            systemDriveRoot.TrimEnd('\\'),
            driveTotal,
            driveFree,
            drivePct,
            ramTotal,
            ramAvail,
            ramPct);
    }

    public static List<string> GetRunningBrowsers()
    {
        string[] browserProcessNames = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi"];
        var active = new List<string>();

        foreach (string procName in browserProcessNames)
        {
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName(procName).Length > 0)
                {
                    active.Add(procName switch
                    {
                        "chrome" => "Google Chrome",
                        "msedge" => "Microsoft Edge",
                        "firefox" => "Mozilla Firefox",
                        "brave" => "Brave",
                        "opera" => "Opera",
                        "vivaldi" => "Vivaldi",
                        _ => procName
                    });
                }
            }
            catch
            {
                // Ignore process query errors
            }
        }

        return active;
    }
}
