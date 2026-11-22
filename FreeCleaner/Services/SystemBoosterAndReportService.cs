using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static partial class SystemBoosterAndReportService
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    [LibraryImport("psapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int EmptyWorkingSet(IntPtr hwProc);

    public static List<ProcessMemoryItem> GetTopMemoryProcesses(int count = 15)
    {
        var list = new List<ProcessMemoryItem>();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.WorkingSet64 > 10 * 1024 * 1024)
                    {
                        list.Add(new ProcessMemoryItem
                        {
                            ProcessId = proc.Id,
                            ProcessName = proc.ProcessName,
                            WorkingSetBytes = proc.WorkingSet64
                        });
                    }
                }
                catch
                {
                    // Ignore protected processes
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Ignore
        }

        return list.OrderByDescending(x => x.WorkingSetBytes).Take(count).ToList();
    }

    public static async Task<(long FreedRamBytes, int TrimmedProcessCount)> OptimizeRamWorkingSetAsync()
    {
        var beforeSnap = SystemInfoService.GetMetrics();
        int trimmedCount = await Task.Run(() =>
        {
            int trimmed = 0;

            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();

                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (proc.Id <= 4) continue;
                        int res = EmptyWorkingSet(proc.Handle);
                        if (res != 0)
                        {
                            trimmed++;
                        }
                    }
                    catch
                    {
                        // Ignore system/elevated processes where handle cannot be opened
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return trimmed;
        }).ConfigureAwait(false);

        await Task.Delay(350).ConfigureAwait(false);
        var afterSnap = SystemInfoService.GetMetrics();
        long freed = Math.Max(0, afterSnap.RamAvailableBytes - beforeSnap.RamAvailableBytes);
        return (freed, trimmedCount);
    }

    public static async Task<(bool Success, string OutputMessage)> FlushDnsCacheAsync()
    {
        return await RunCommandToolAsync("ipconfig.exe", ["/flushdns"]);
    }

    public static async Task<(bool Success, string OutputMessage)> ShowArpAndNetStatsAsync()
    {
        return await RunCommandToolAsync("netsh.exe", ["interface", "ip", "delete", "destinationcache"]);
    }

    public static void LaunchWindowsDiskCleanup()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cleanmgr.exe",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }

    public static void LaunchSystemPropertiesRestore()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "SystemPropertiesProtection.exe",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }

    private static async Task<(bool Success, string OutputMessage)> RunCommandToolAsync(
        string exeName,
        IEnumerable<string> args)
    {
        try
        {
            ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(exeName, args, redirectOutput: true);
            ProcessRunResult result = await SafeProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            if (!result.Started)
            {
                return (false, LocalizationService.GetText(
                    "Komut başlatılamadı.",
                    "Could not start command."));
            }

            if (result.TimedOut)
            {
                return (false, LocalizationService.GetText(
                    "Komut zaman aşımına uğradı.",
                    "Command timed out."));
            }

            string combined = (result.StandardOutput + " " + result.StandardError).Trim().Replace("\r\n", " ");
            return (result.ExitCode == 0, string.IsNullOrWhiteSpace(combined)
                ? LocalizationService.GetText("İşlem tamamlandı.", "Operation completed.")
                : combined);
        }
        catch (Exception ex)
        {
            ErrorLogService.Log($"SystemBooster:{exeName}", ex);
            return (false, ex.Message);
        }
    }

    public static (string HtmlPath, string JsonPath) ExportDiagnosticReports(
        SystemMetricsSnapshot metrics,
        IEnumerable<CleanerRule> rules,
        long totalFreedBytes,
        int totalDeletedFiles,
        IEnumerable<string> logs,
        int healthScore = 100,
        string? healthBadge = null,
        long lifetimeFreedBytes = 0,
        IReadOnlyList<CleanupHistoryBarItem>? recentHistory = null)
    {
        string exportDir = AppStoragePathsService.GetReportsDirectory();
        Directory.CreateDirectory(exportDir);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string htmlPath = Path.Combine(exportDir, $"FreeCleaner_Rapor_{stamp}.html");
        string jsonPath = Path.Combine(exportDir, $"FreeCleaner_Rapor_{stamp}.json");

        var ruleList = rules.ToList();
        var logList = logs.Take(100).ToList();

        // 1. Export JSON Report
        var jsonPayload = new
        {
            GeneratedAt = DateTime.Now,
            Version = AppUpdateService.CurrentVersion,
            Application = $"FreeCleaner Pro v{AppUpdateService.CurrentVersion} (.NET 10)",
            HealthScore = healthScore,
            HealthGrade = healthBadge ?? string.Empty,
            LifetimeFreedBytes = lifetimeFreedBytes,
            LifetimeFreedFormatted = CleanerRule.FormatBytes(lifetimeFreedBytes),
            System = metrics,
            SessionFreedBytes = totalFreedBytes,
            SessionFreedFormatted = CleanerRule.FormatBytes(totalFreedBytes),
            SessionDeletedFiles = totalDeletedFiles,
            RecentHistory = recentHistory ?? [],
            Rules = ruleList.Select(r => new
            {
                r.Id,
                r.Name,
                r.Category,
                r.IsSelected,
                r.DetectedFileCount,
                r.DetectedSizeBytes,
                r.FormattedSize,
                r.StatusText
            }),
            RecentLogs = logList
        };

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(jsonPayload, IndentedJsonOptions), Encoding.UTF8);

        // 2. Export Rich SVG HTML Report
        string htmlContent = BuildDiagnosticHtmlReport(
            metrics,
            ruleList,
            totalFreedBytes,
            totalDeletedFiles,
            logList,
            healthScore,
            healthBadge,
            lifetimeFreedBytes,
            recentHistory);

        File.WriteAllText(htmlPath, htmlContent, Encoding.UTF8);
        return (htmlPath, jsonPath);
    }

    public static string BuildDiagnosticHtmlReport(
        SystemMetricsSnapshot metrics,
        IEnumerable<CleanerRule> rules,
        long totalFreedBytes,
        int totalDeletedFiles,
        IEnumerable<string> logs,
        int healthScore = 100,
        string? healthBadge = null,
        long lifetimeFreedBytes = 0,
        IReadOnlyList<CleanupHistoryBarItem>? recentHistory = null)
    {
        int clampedScore = Math.Clamp(healthScore, 0, 100);
        string scoreColor = clampedScore >= 85 ? "#10b981" : (clampedScore >= 60 ? "#f59e0b" : "#ef4444");
        double circumference = 2 * Math.PI * 42.0; // r=42 -> ~263.89
        double dashOffset = circumference * (1.0 - (clampedScore / 100.0));
        string encBadge = System.Net.WebUtility.HtmlEncode(healthBadge ?? (clampedScore >= 85 ? "Optimal" : "Bakım Önerilir"));

        var ruleList = rules.ToList();
        var logList = logs.Take(100).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='tr'><head><meta charset='utf-8'/>");
        sb.AppendLine($"<title>FreeCleaner Pro v{AppUpdateService.CurrentVersion} - Sistem &amp; Sağlık Raporu</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Segoe UI Variable Display','Segoe UI',sans-serif;background:#0b111b;color:#f1f5f9;margin:0;padding:28px;}");
        sb.AppendLine(".wrap{max-width:1180px;margin:0 auto;}");
        sb.AppendLine(".hero{display:flex;align-items:center;justify-content:space-between;background:linear-gradient(135deg,#131d2e,#192841);border:1px solid #2b3c58;border-radius:14px;padding:22px 26px;margin-bottom:18px;}");
        sb.AppendLine(".hero h1{margin:0 0 6px 0;font-size:22px;color:#38bdf8;} .muted{color:#94a3b8;font-size:13px;}");
        sb.AppendLine(".grid{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-bottom:18px;}");
        sb.AppendLine(".stat{background:#131b29;border:1px solid #26344b;border-radius:12px;padding:16px;}");
        sb.AppendLine(".stat .lbl{font-size:11px;text-transform:uppercase;color:#94a3b8;letter-spacing:.6px;}");
        sb.AppendLine(".stat .val{font-size:20px;font-weight:700;margin-top:6px;color:#f8fafc;}");
        sb.AppendLine(".card{background:#131b29;border:1px solid #26344b;border-radius:12px;padding:18px;margin-bottom:18px;}");
        sb.AppendLine("h2{margin:0 0 12px 0;font-size:16px;color:#34d399;}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;} th,td{padding:9px 10px;border-bottom:1px solid #223047;text-align:left;font-size:13px;}");
        sb.AppendLine("th{background:#0f1623;color:#94a3b8;font-weight:600;} tr:hover{background:#182235;}");
        sb.AppendLine(".logs{list-style:none;padding:0;margin:0;max-height:360px;overflow:auto;}");
        sb.AppendLine(".logs li{font-family:Consolas,monospace;font-size:12px;color:#93c5fd;padding:4px 0;border-bottom:1px dashed #1e293b;}");
        sb.AppendLine("</style></head><body><div class='wrap'>");

        sb.AppendLine("<div class='hero'><div>");
        sb.AppendLine($"<h1>FreeCleaner Pro v{AppUpdateService.CurrentVersion} — Sistem &amp; Temizlik Raporu</h1>");
        sb.AppendLine($"<div class='muted'>{DateTime.Now:dd.MM.yyyy HH:mm:ss} • {System.Net.WebUtility.HtmlEncode(metrics.OsName)} ({System.Net.WebUtility.HtmlEncode(metrics.CpuArchitecture)}) • {System.Net.WebUtility.HtmlEncode(metrics.DotNetVersion)}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div style='display:flex;align-items:center;gap:14px;'>");
        sb.AppendLine($"<svg width='104' height='104' viewBox='0 0 104 104' aria-label='Health Score'>");
        sb.AppendLine("<circle cx='52' cy='52' r='42' fill='none' stroke='#1e293b' stroke-width='9'/>");
        sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "<circle cx='52' cy='52' r='42' fill='none' stroke='{0}' stroke-width='9' stroke-linecap='round' stroke-dasharray='{1:F2}' stroke-dashoffset='{2:F2}' transform='rotate(-90 52 52)'/>",
            scoreColor, circumference, dashOffset));
        sb.AppendLine($"<text x='52' y='56' text-anchor='middle' fill='#f8fafc' font-size='22' font-weight='bold'>{clampedScore}</text>");
        sb.AppendLine("</svg>");
        sb.AppendLine($"<div><div class='muted'>PC SAĞLIK SKORU</div><div style='font-size:16px;font-weight:700;color:{scoreColor};'>{encBadge}</div></div>");
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='grid'>");
        sb.AppendLine($"<div class='stat'><div class='lbl'>BU OTURUMDA AÇILAN ALAN</div><div class='val' style='color:#10b981;'>{System.Net.WebUtility.HtmlEncode(CleanerRule.FormatBytes(totalFreedBytes))}</div><div class='muted'>{totalDeletedFiles:N0} öğe işlendi</div></div>");
        sb.AppendLine($"<div class='stat'><div class='lbl'>ÖMÜR BOYU KAZANILAN ALAN</div><div class='val' style='color:#38bdf8;'>{System.Net.WebUtility.HtmlEncode(CleanerRule.FormatBytes(lifetimeFreedBytes))}</div><div class='muted'>Toplam geçmiş kaydı</div></div>");
        sb.AppendLine($"<div class='stat'><div class='lbl'>SİSTEM DİSKİ ({System.Net.WebUtility.HtmlEncode(metrics.DriveName)})</div><div class='val'>{metrics.DriveUsedPercent:F0}% Dolu</div><div class='muted'>{System.Net.WebUtility.HtmlEncode(metrics.DriveSummaryText)}</div></div>");
        sb.AppendLine($"<div class='stat'><div class='lbl'>BELLEK (RAM)</div><div class='val'>{metrics.RamUsedPercent:F0}% Kullanım</div><div class='muted'>{System.Net.WebUtility.HtmlEncode(metrics.RamSummaryText)}</div></div>");
        sb.AppendLine("</div>");

        if (recentHistory is { Count: > 0 })
        {
            long maxHistBytes = Math.Max(1, recentHistory.Max(h => h.FreedBytes));
            int count = recentHistory.Count;
            int svgWidth = Math.Max(420, count * 44);
            sb.AppendLine("<div class='card'><h2>Son 14 Günlük Alan Kurtarma Grafiği</h2>");
            sb.AppendLine($"<svg width='100%' height='125' viewBox='0 0 {svgWidth} 125' xmlns='http://www.w3.org/2000/svg'>");
            for (int i = 0; i < count; i++)
            {
                var bar = recentHistory[i];
                double hPx = bar.FreedBytes > 0 ? Math.Max(8, (bar.FreedBytes * 78.0) / maxHistBytes) : 4;
                double yPx = 92 - hPx;
                int xPx = i * 44 + 10;
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "<rect x='{0}' y='{1:F1}' width='26' height='{2:F1}' rx='4' fill='#38bdf8'><title>{3}</title></rect>",
                    xPx, yPx, hPx, System.Net.WebUtility.HtmlEncode(bar.ToolTipText)));
                sb.AppendLine($"<text x='{xPx + 13}' y='112' text-anchor='middle' fill='#94a3b8' font-size='10'>{System.Net.WebUtility.HtmlEncode(bar.DayLabel)}</text>");
            }
            sb.AppendLine("</svg></div>");
        }

        sb.AppendLine("<div class='card'><h2>Temizlik Kuralları ve Tespit Özeti</h2><table><thead><tr><th>Kategori</th><th>Kural Adı</th><th>Tespit Edilen Öğe</th><th>Boyut</th><th>Durum</th></tr></thead><tbody>");
        foreach (CleanerRule r in ruleList)
        {
            sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(r.Category)}</td><td>{System.Net.WebUtility.HtmlEncode(r.Name)}</td><td>{r.DetectedFileCount:N0}</td><td>{System.Net.WebUtility.HtmlEncode(r.FormattedSize)}</td><td>{System.Net.WebUtility.HtmlEncode(r.StatusText)}</td></tr>");
        }
        sb.AppendLine("</tbody></table></div>");

        sb.AppendLine("<div class='card'><h2>Son İşlem Kayıtları</h2><ul class='logs'>");
        foreach (string log in logList)
        {
            sb.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(log)}</li>");
        }
        sb.AppendLine("</ul></div></div></body></html>");

        return sb.ToString();
    }
}
