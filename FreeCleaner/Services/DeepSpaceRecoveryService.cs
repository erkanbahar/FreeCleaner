using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

/// <summary>
/// Analyzes and safely reclaims multi-gigabyte Windows system storage (Hibernation, WinSxS Component Store, CompactOS, GPU/Windows.old installers).
/// </summary>
public static partial class DeepSpaceRecoveryService
{
    public static bool ParseCompactOsQueryOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        if (output.Contains("not in the Compact state", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Compact durumunda değil", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("sıkıştırılmış durumda değil", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return output.Contains("is in the Compact state", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Compact durumunda", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("sıkıştırılmış durumda", StringComparison.OrdinalIgnoreCase);
    }

    public static string ParseDismReclaimableSummary(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "DISM bileşen deposu analiz çıktısı alınamadı.";

        foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Contains("Backups and Disabled Features", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Yedekler ve Devre Dışı Bırakılan Özellikler", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Reclaimable Packages", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Geri Kazanılabilir", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Component Store Cleanup Recommended", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            ?? "DISM analizi tamamlandı.";
    }

    public static async Task<List<DeepSpaceItem>> ScanDeepSpaceOpportunitiesAsync(CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var items = new List<DeepSpaceItem>();
            bool isAdmin = UltimateSystemSuiteService.IsRunningAsAdministrator();

            // 1. Hibernation file (C:\hiberfil.sys)
            string hiberPath = @"C:\hiberfil.sys";
            long hiberSize = 0;
            try
            {
                if (File.Exists(hiberPath))
                    hiberSize = new FileInfo(hiberPath).Length;
            }
            catch { }

            items.Add(new DeepSpaceItem
            {
                ItemId = "HIBERFIL_OFF",
                Category = "Hazırda Bekletme (Hibernate)",
                Title = "hiberfil.sys Dosyasını Kapat (Tam Kurtarma)",
                SizeBytes = hiberSize,
                StatusBadge = hiberSize > 0 ? $"⚠️ Aktif ({CleanerRule.FormatBytes(hiberSize)})" : "✅ Zaten Kapalı (0 B)",
                ActionLabel = "🛑 Hibernate Kapat",
                Description = "powercfg /h off ile hazırda bekletme dosyasını tamamen kaldırır (Uyku modu çalışmaya devam eder).",
                TargetPath = hiberPath,
                CanExecuteAction = hiberSize > 0
            });

            items.Add(new DeepSpaceItem
            {
                ItemId = "HIBERFIL_REDUCED",
                Category = "Hazırda Bekletme (Hibernate)",
                Title = "hiberfil.sys Boyutunu Yarıya İndir (Reduced Mod)",
                SizeBytes = hiberSize / 2,
                StatusBadge = hiberSize > 0 ? "🗜️ Küçültülebilir (~%50)" : "✅ Devre Dışı",
                ActionLabel = "🗜️ Yarı Boyuta İndir",
                Description = "powercfg /h /type reduced ile Hızlı Başlatmayı (Fast Startup) koruyup dosya boyutunu yarıya düşürür.",
                TargetPath = hiberPath,
                CanExecuteAction = hiberSize > 0
            });

            // 2. WinSxS Component Store
            string winSxS = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS");
            string dismStatus = isAdmin ? "✅ DISM Temizliğe Hazır" : "🛡️ Yönetici Yetkisi Gerekir";
            if (isAdmin)
            {
                var (_, stdout, _) = await RunCmdCaptureAsync("dism /Online /Cleanup-Image /AnalyzeComponentStore", ct);
                dismStatus = ParseDismReclaimableSummary(stdout);
            }

            items.Add(new DeepSpaceItem
            {
                ItemId = "WINSXS_CLEANUP",
                Category = "Windows Bileşen Deposu",
                Title = "WinSxS Eski Güncelleme Yedeklerini Temizle (DISM)",
                SizeBytes = 0,
                StatusBadge = dismStatus,
                ActionLabel = "🧹 DISM Cleanup Çalıştır",
                Description = "dism /Online /Cleanup-Image /StartComponentCleanup ile yerini yenisi almış eski Windows Update bileşenlerini temizler (3–10 GB potansiyel).",
                TargetPath = winSxS,
                CanExecuteAction = true
            });

            // 3. CompactOS lossless OS binary compression
            var (_, compactOut, _) = await RunCmdCaptureAsync("compact /compactos:query", ct);
            bool isCompactActive = ParseCompactOsQueryOutput(compactOut);

            items.Add(new DeepSpaceItem
            {
                ItemId = "COMPACT_OS",
                Category = "Windows Sistem Sıkıştırma",
                Title = "Windows CompactOS Kayıpsız Sıkıştırma (2–4 GB Tasarruf)",
                SizeBytes = 0,
                StatusBadge = isCompactActive ? "✅ CompactOS Zaten Aktif" : "🗜️ Sıkıştırmaya Hazır (Kapalı)",
                ActionLabel = "⚡ CompactOS Etkinleştir",
                Description = "compact /compactos:always komutuyla Windows sistem dosyalarını sıfır veri kaybı ve performans kaybı olmadan sıkıştırır.",
                TargetPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                CanExecuteAction = !isCompactActive
            });

            // 4. NVIDIA / AMD / Intel Extracted Installers + Windows.old
            var gpuDirs = new[]
            {
                @"C:\NVIDIA",
                @"C:\AMD",
                @"C:\Intel",
                @"C:\Program Files\NVIDIA Corporation\Installer2",
                @"C:\Windows.old"
            };

            long gpuTotalBytes = 0;
            int foundCount = 0;
            foreach (string d in gpuDirs)
            {
                if (!Directory.Exists(d))
                    continue;
                foundCount++;
                gpuTotalBytes += ComputeDirectoryBytesFast(d, ct);
            }

            items.Add(new DeepSpaceItem
            {
                ItemId = "GPU_INSTALLERS",
                Category = "Eski Sürücü & Yükseltme Artığı",
                Title = "NVIDIA / AMD / Intel Çıkarılmış Kurulum & Windows.old Paketleri",
                SizeBytes = gpuTotalBytes,
                StatusBadge = foundCount > 0 ? $"⚠️ {foundCount} klasör bulundu" : "✅ Artık Kurulum Dosyası Yok",
                ActionLabel = "🗑️ Kurulum Artıklarını Sil",
                Description = "Sürücü güncellemelerinden ve eski Windows sürümünden kalan çıkarılmış paketleri temizler (NVIDIA/AMD/Installer2/Windows.old).",
                TargetPath = @"C:\NVIDIA",
                CanExecuteAction = gpuTotalBytes > 0
            });

            return items;
        }, ct);
    }

    public static async Task<(bool Success, long FreedBytes, string Message)> ExecuteDeepSpaceActionAsync(
        string itemId,
        CancellationToken ct = default)
    {
        if (!UltimateSystemSuiteService.IsRunningAsAdministrator())
            return (false, 0, "Bu derin sistem işlemi için sağ üstteki '🛡️ Yönetici' butonuyla Yönetici Modu gereklidir.");

        switch (itemId)
        {
            case "HIBERFIL_OFF":
            {
                long before = SafeFileLength(@"C:\hiberfil.sys");
                var (code, stdout, stderr) = await RunCmdCaptureAsync("powercfg /h off", ct);
                long after = SafeFileLength(@"C:\hiberfil.sys");
                long freed = Math.Max(0, before - after);
                if (code == 0)
                    return (true, freed, $"Hazırda Bekletme kapatıldı. Kazanılan alan: {CleanerRule.FormatBytes(freed)}");
                return (false, 0, $"powercfg hatası: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim()}");
            }

            case "HIBERFIL_REDUCED":
            {
                long before = SafeFileLength(@"C:\hiberfil.sys");
                var (code, stdout, stderr) = await RunCmdCaptureAsync("powercfg /h /type reduced", ct);
                long after = SafeFileLength(@"C:\hiberfil.sys");
                long freed = Math.Max(0, before - after);
                if (code == 0)
                    return (true, freed, $"hiberfil.sys yarı boyuta (Reduced) indirildi. Kazanılan alan: {CleanerRule.FormatBytes(freed)}");
                return (false, 0, $"powercfg hatası: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim()}");
            }

            case "WINSXS_CLEANUP":
            {
                var sysDrive = new DriveInfo(@"C:\");
                long freeBefore = sysDrive.AvailableFreeSpace;
                var (code, stdout, stderr) = await RunCmdCaptureAsync("dism /Online /Cleanup-Image /StartComponentCleanup", ct);
                long freeAfter = new DriveInfo(@"C:\").AvailableFreeSpace;
                long freed = Math.Max(0, freeAfter - freeBefore);
                if (code == 0)
                    return (true, freed, $"WinSxS DISM temizliği tamamlandı! Açılan tahmini alan: {CleanerRule.FormatBytes(freed)}");
                return (false, 0, $"DISM hatası: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim()}");
            }

            case "COMPACT_OS":
            {
                var sysDrive = new DriveInfo(@"C:\");
                long freeBefore = sysDrive.AvailableFreeSpace;
                var (code, stdout, stderr) = await RunCmdCaptureAsync("compact /compactos:always", ct);
                long freeAfter = new DriveInfo(@"C:\").AvailableFreeSpace;
                long freed = Math.Max(0, freeAfter - freeBefore);
                if (code == 0)
                    return (true, freed, $"CompactOS başarıyla etkinleştirildi! Açılan disk alanı: {CleanerRule.FormatBytes(freed)}");
                return (false, 0, $"CompactOS hatası: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim()}");
            }

            case "GPU_INSTALLERS":
            {
                return await Task.Run(() =>
                {
                    var targets = new[]
                    {
                        @"C:\NVIDIA",
                        @"C:\AMD",
                        @"C:\Intel",
                        @"C:\Program Files\NVIDIA Corporation\Installer2",
                        @"C:\Windows.old"
                    };

                    long freed = 0;
                    int deletedDirs = 0;
                    foreach (string dir in targets)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!Directory.Exists(dir)) continue;

                        long dirBytes = ComputeDirectoryBytesFast(dir, ct);
                        if (RecycleBinHelper.MovePathToRecycleBin(dir) || TryDeleteDirectorySafe(dir))
                        {
                            freed += dirBytes;
                            deletedDirs++;
                        }
                    }

                    return deletedDirs > 0
                        ? (true, freed, $"{deletedDirs} eski sürücü/kurulum klasörü temizlendi ({CleanerRule.FormatBytes(freed)} yer açıldı).")
                        : (false, 0L, "Temizlenecek GPU kurulum klasörü bulunamadı veya dosyalar kullanımda.");
                }, ct);
            }

            default:
                return (false, 0, "Bilinmeyen derin temizlik komutu.");
        }
    }

    private static bool TryDeleteDirectorySafe(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long SafeFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long ComputeDirectoryBytesFast(string directory, CancellationToken ct)
    {
        long total = 0;
        try
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                RecurseSubdirectories = true
            };
            using var en = new FileSizeOnlyEnumerator(directory, opts, ct);
            while (en.MoveNext())
            {
                total += en.Current;
            }
        }
        catch { }
        return total;
    }

    private sealed class FileSizeOnlyEnumerator(string dir, EnumerationOptions opts, CancellationToken ct)
        : FileSystemEnumerator<long>(dir, opts)
    {
        protected override long TransformEntry(ref FileSystemEntry entry) => entry.Length;

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            ct.ThrowIfCancellationRequested();
            return !entry.IsDirectory;
        }

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
            => entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) == 0;

        protected override bool ContinueOnError(int error) => true;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCmdCaptureAsync(string cmdLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"chcp 65001 >nul && {cmdLine}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null)
            return (-1, string.Empty, "İşlem başlatılamadı.");

        Task<string> outTask = proc.StandardOutput.ReadToEndAsync(ct);
        Task<string> errTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, await outTask, await errTask);
    }
}
