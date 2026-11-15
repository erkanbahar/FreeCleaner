using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class SystemBoosterAndReportService
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

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
        return await Task.Run(() =>
        {
            var beforeSnap = SystemInfoService.GetMetrics();
            int trimmedCount = 0;

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
                            trimmedCount++;
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

            Thread.Sleep(350);
            var afterSnap = SystemInfoService.GetMetrics();
            long freed = Math.Max(0, afterSnap.RamAvailableBytes - beforeSnap.RamAvailableBytes);
            return (freed, trimmedCount);
        });
    }

    public static async Task<(bool Success, string OutputMessage)> FlushDnsCacheAsync()
    {
        return await RunCommandToolAsync("ipconfig.exe", "/flushdns");
    }

    public static async Task<(bool Success, string OutputMessage)> ShowArpAndNetStatsAsync()
    {
        return await RunCommandToolAsync("netsh.exe", "interface ip delete destinationcache");
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

    private static async Task<(bool Success, string OutputMessage)> RunCommandToolAsync(string exeName, string args)
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exeName,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                    return (false, "Komut başlatılamadı.");

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(8000);

                string combined = (stdout + " " + stderr).Trim().Replace("\r\n", " ");
                return (proc.ExitCode == 0, string.IsNullOrWhiteSpace(combined) ? "İşlem tamamlandı." : combined);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    public static (string HtmlPath, string JsonPath) ExportDiagnosticReports(
        SystemMetricsSnapshot metrics,
        IEnumerable<CleanerRule> rules,
        long totalFreedBytes,
        int totalDeletedFiles,
        IEnumerable<string> logs)
    {
        string exportDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "FreeCleaner_Raporlar");
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
            Application = "FreeCleaner (.NET 10)",
            System = metrics,
            SessionFreedBytes = totalFreedBytes,
            SessionFreedFormatted = CleanerRule.FormatBytes(totalFreedBytes),
            SessionDeletedFiles = totalDeletedFiles,
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

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(jsonPayload, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        // 2. Export Rich HTML Report
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='tr'><head><meta charset='utf-8'/>");
        sb.AppendLine("<title>FreeCleaner (.NET 10) Sistem ve Temizlik Raporu</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Segoe UI',sans-serif;background:#0f141c;color:#f8fafc;margin:30px;}");
        sb.AppendLine(".card{background:#1d2636;border:1px solid #2c384e;border-radius:10px;padding:18px;margin-bottom:20px;}");
        sb.AppendLine("h1{color:#3b82f6;margin-top:0;} h2{color:#10b981;font-size:18px;}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin-top:10px;}");
        sb.AppendLine("th,td{padding:10px;border-bottom:1px solid #2c384e;text-align:left;font-size:13px;}");
        sb.AppendLine("th{background:#161d29;color:#94a3b8;}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>FreeCleaner (.NET 10) Sistem & Temizlik Raporu</h1>");
        sb.AppendLine($"<div class='card'><h2>Sistem Özeti ({DateTime.Now:dd.MM.yyyy HH:mm:ss})</h2>");
        sb.AppendLine($"<p><b>İşletim Sistemi:</b> {metrics.OsName} ({metrics.CpuArchitecture}) • {metrics.DotNetVersion}</p>");
        sb.AppendLine($"<p><b>Sistem Diski:</b> {metrics.DriveSummaryText}</p>");
        sb.AppendLine($"<p><b>RAM Durumu:</b> {metrics.RamSummaryText}</p>");
        sb.AppendLine($"<p><b>Bu Oturumda Kazanılan Alan:</b> <span style='color:#10b981;font-weight:bold;'>{CleanerRule.FormatBytes(totalFreedBytes)} ({totalDeletedFiles:N0} dosya)</span></p></div>");

        sb.AppendLine("<div class='card'><h2>Temizlik Kategorileri Durumu</h2><table><thead><tr><th>Kategori</th><th>Kural Adı</th><th>Tespit Edilen Dosya</th><th>Boyut</th><th>Durum</th></tr></thead><tbody>");
        foreach (var r in ruleList)
        {
            sb.AppendLine($"<tr><td>{r.Category}</td><td>{r.Name}</td><td>{r.DetectedFileCount:N0}</td><td>{r.FormattedSize}</td><td>{r.StatusText}</td></tr>");
        }
        sb.AppendLine("</tbody></table></div>");

        sb.AppendLine("<div class='card'><h2>Son İşlem Kayıtları</h2><ul>");
        foreach (var log in logList)
        {
            sb.AppendLine($"<li style='font-family:Consolas,monospace;font-size:12px;color:#94a3b8;margin-bottom:4px;'>{System.Net.WebUtility.HtmlEncode(log)}</li>");
        }
        sb.AppendLine("</ul></div></body></html>");

        File.WriteAllText(htmlPath, sb.ToString(), Encoding.UTF8);
        return (htmlPath, jsonPath);
    }
}
