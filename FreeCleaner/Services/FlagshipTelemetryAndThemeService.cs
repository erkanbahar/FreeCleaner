using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class FlagshipTelemetryAndThemeService
{
    /// <summary>
    /// Computes an overall 0-100 PC Health Score from disk occupancy, RAM load, detected junk, and startup count.
    /// </summary>
    public static (int Score, string GradeBadge, string Advice) ComputeHealthScore(
        SystemMetricsSnapshot metrics,
        long detectedJunkBytes,
        int startupCount)
    {
        int score = 100;

        if (metrics.DriveUsedPercent > 90) score -= 25;
        else if (metrics.DriveUsedPercent > 80) score -= 15;
        else if (metrics.DriveUsedPercent > 70) score -= 7;

        if (metrics.RamUsedPercent > 90) score -= 20;
        else if (metrics.RamUsedPercent > 80) score -= 12;
        else if (metrics.RamUsedPercent > 70) score -= 5;

        if (detectedJunkBytes > 4L * 1024 * 1024 * 1024) score -= 20;
        else if (detectedJunkBytes > 1024L * 1024 * 1024) score -= 12;
        else if (detectedJunkBytes > 300L * 1024 * 1024) score -= 6;
        else if (detectedJunkBytes > 50L * 1024 * 1024) score -= 2;

        if (startupCount > 20) score -= 12;
        else if (startupCount > 12) score -= 7;
        else if (startupCount > 7) score -= 3;

        score = Math.Clamp(score, 10, 100);

        (string badge, string advice) = score switch
        {
            >= 90 => ("🌟 Mükemmel (A+)", "Sisteminiz çok sağlıklı, disk ve bellek dengesi ideal seviyede."),
            >= 75 => ("✅ Çok İyi (A)", "Sistem genel durumu iyi; periyodik temizlik ile yüksek hızı koruyabilirsiniz."),
            >= 60 => ("⚡ Orta (B)", "Biriken gereksiz dosyaları temizlemeniz ve başlangıç uygulamalarını azaltmanız önerilir."),
            >= 40 => ("⚠️ Bakım Önerilir (C)", "Disk veya bellek yükü yüksek! Tek Tıkla Sağlık Kontrolü ile sistemi rahatlatın."),
            _ => ("🚨 Acil Optimizasyon (D)", "Sistem kritik yük altında; disk temizliği, RAM boşaltma ve başlangıç azaltımı başlatın.")
        };

        return (score, badge, advice);
    }

    /// <summary>
    /// Converts a numeric sample history into WPF Polyline PointCollection normalized cleanly inside the canvas.
    /// </summary>
    public static PointCollection BuildSparklinePoints(
        IReadOnlyList<double> values,
        double maxVal,
        double width,
        double height)
    {
        var points = new PointCollection();
        if (values is null || values.Count == 0 || width <= 0 || height <= 0)
            return points;

        double safeMax = maxVal > 0 ? maxVal : 1.0;
        const double padY = 4.0;
        double usableHeight = Math.Max(1.0, height - (padY * 2.0));

        if (values.Count == 1)
        {
            double norm = Math.Clamp(values[0] / safeMax, 0.0, 1.0);
            double y = padY + (usableHeight * (1.0 - norm));
            points.Add(new Point(0, y));
            points.Add(new Point(width, y));
            return points;
        }

        double stepX = width / (values.Count - 1);
        for (int i = 0; i < values.Count; i++)
        {
            double x = i * stepX;
            double norm = Math.Clamp(values[i] / safeMax, 0.0, 1.0);
            double y = padY + (usableHeight * (1.0 - norm));
            points.Add(new Point(x, y));
        }

        return points;
    }

    public static List<LiveProcessMetricItem> GetTopProcessesSnapshot(int maxCount = 12)
    {
        var items = new List<(long Bytes, LiveProcessMetricItem Item)>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id <= 4) continue;
                long ws = proc.WorkingSet64;
                string prio = "Normal";
                try
                {
                    prio = proc.PriorityClass switch
                    {
                        ProcessPriorityClass.High => "⚡ Yüksek",
                        ProcessPriorityClass.AboveNormal => "🔼 Normal Üstü",
                        ProcessPriorityClass.BelowNormal => "🔽 Düşük",
                        ProcessPriorityClass.Idle => "🍃 Eco (Idle)",
                        ProcessPriorityClass.RealTime => "🔥 Gerçek Zamanlı",
                        _ => "🟢 Normal"
                    };
                }
                catch { }

                items.Add((ws, new LiveProcessMetricItem
                {
                    ProcessId = proc.Id,
                    ProcessName = $"{proc.ProcessName}.exe",
                    RamFormatted = CleanerRule.FormatBytes(ws),
                    Threads = proc.Threads.Count,
                    PriorityText = prio
                }));
            }
            catch { }
            finally
            {
                proc.Dispose();
            }
        }

        return items
            .OrderByDescending(x => x.Bytes)
            .Take(maxCount)
            .Select(x => x.Item)
            .ToList();
    }

    public sealed record CriticalHealthAlert(
        bool TriggerDiskAlert,
        bool TriggerRamAlert,
        string? Title,
        string? Message);

    public static CriticalHealthAlert EvaluateCriticalAlerts(
        double ramPercent,
        double diskUsedPercent,
        double freeDiskGb,
        DateTime utcNow,
        ref DateTime lastDiskAlertUtc,
        ref DateTime lastRamAlertUtc,
        int cooldownMinutes = 30)
    {
        TimeSpan cooldown = TimeSpan.FromMinutes(Math.Max(1, cooldownMinutes));
        bool fireDisk = diskUsedPercent >= 90.0 &&
                        (lastDiskAlertUtc == DateTime.MinValue || (utcNow - lastDiskAlertUtc) >= cooldown);
        bool fireRam = ramPercent >= 92.0 &&
                       (lastRamAlertUtc == DateTime.MinValue || (utcNow - lastRamAlertUtc) >= cooldown);

        if (fireDisk)
            lastDiskAlertUtc = utcNow;
        if (fireRam)
            lastRamAlertUtc = utcNow;

        if (!fireDisk && !fireRam)
            return new CriticalHealthAlert(false, false, null, null);

        string title = (fireDisk, fireRam) switch
        {
            (true, true) => "Kritik Sistem Uyarısı (Disk & RAM)",
            (true, false) => "Kritik Disk Alanı Uyarısı",
            _ => "Kritik Bellek (RAM) Uyarısı"
        };

        string message = (fireDisk, fireRam) switch
        {
            (true, true) => $"C: diski %{diskUsedPercent:F0} dolu ({freeDiskGb:F1} GB boş) ve RAM kullanımı %{ramPercent:F0}. Hızlı temizlik önerilir.",
            (true, false) => $"Sistem sürücüsü %{diskUsedPercent:F0} doluluğa ulaştı (kalan: {freeDiskGb:F1} GB). Derin temizlik veya Treemap başlatın.",
            _ => $"Bellek kullanımı %{ramPercent:F0} seviyesine yükseldi. Tek tıkla RAM Optimizasyonu yapabilirsiniz."
        };

        return new CriticalHealthAlert(fireDisk, fireRam, title, message);
    }
}

/// <summary>
/// Stateful real-time delta sampler for system-wide CPU % (Win32 GetSystemTimes), RAM %, and Network KB/s.
/// </summary>
public sealed class LiveSystemTelemetrySampler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasCpuBaseline;

    private long _prevBytesReceived;
    private long _prevBytesSent;
    private DateTime _prevNetSampleUtc;
    private bool _hasNetBaseline;

    public (double CpuPercent, double RamPercent, double DownKbps, double UpKbps) SampleNext()
    {
        double cpuPct = SampleCpuPercent();
        double ramPct = SystemInfoService.GetMetrics().RamUsedPercent;
        var (downKbps, upKbps) = SampleNetworkKbps();
        return (cpuPct, ramPct, downKbps, upKbps);
    }

    private double SampleCpuPercent()
    {
        if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            return 0;

        ulong idle = idleFt.ToUInt64();
        ulong kernel = kernelFt.ToUInt64();
        ulong user = userFt.ToUInt64();

        if (!_hasCpuBaseline)
        {
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasCpuBaseline = true;
            return 0;
        }

        ulong idleDelta = idle >= _prevIdle ? idle - _prevIdle : 0;
        ulong kernelDelta = kernel >= _prevKernel ? kernel - _prevKernel : 0;
        ulong userDelta = user >= _prevUser ? user - _prevUser : 0;

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;

        // In Windows GetSystemTimes, kernel time includes idle time.
        ulong total = kernelDelta + userDelta;
        if (total == 0)
            return 0;

        ulong busy = total >= idleDelta ? total - idleDelta : 0;
        return Math.Clamp((busy * 100.0) / total, 0.0, 100.0);
    }

    private (double DownKbps, double UpKbps) SampleNetworkKbps()
    {
        long totalRx = 0;
        long totalTx = 0;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var stats = nic.GetIPStatistics();
                totalRx += stats.BytesReceived;
                totalTx += stats.BytesSent;
            }
        }
        catch { }

        DateTime now = DateTime.UtcNow;
        if (!_hasNetBaseline)
        {
            _prevBytesReceived = totalRx;
            _prevBytesSent = totalTx;
            _prevNetSampleUtc = now;
            _hasNetBaseline = true;
            return (0, 0);
        }

        double sec = Math.Max(0.2, (now - _prevNetSampleUtc).TotalSeconds);
        long rxDelta = Math.Max(0, totalRx - _prevBytesReceived);
        long txDelta = Math.Max(0, totalTx - _prevBytesSent);

        _prevBytesReceived = totalRx;
        _prevBytesSent = totalTx;
        _prevNetSampleUtc = now;

        return ((rxDelta / 1024.0) / sec, (txDelta / 1024.0) / sec);
    }
}
