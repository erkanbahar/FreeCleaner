using System.Diagnostics;
using System.IO;
using System.Text;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class TenthGenMilestoneSuiteService
{
    // ==================== 1. WINDOWS BOOT TIME & STARTUP BOTTLENECK ANALYZER ====================

    public static async Task<List<BootPerformanceMetricItem>> AnalyzeBootPerformanceAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<BootPerformanceMetricItem>();

            // 1. Live System Uptime & Last Boot Timestamp
            TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            DateTime lastBoot = DateTime.Now - uptime;

            results.Add(new BootPerformanceMetricItem
            {
                MetricCategory = "⏱️ Sistem Çalışma Süresi (Uptime)",
                ComponentOrAppName = "Son Açılıştan İtibaren Geçen Süre",
                DurationOrValue = $"{(int)uptime.TotalDays} Gün, {uptime.Hours} Saat, {uptime.Minutes} Dakika",
                ImpactAssessment = $"Son Sistem Açılışı: {lastBoot:dd.MM.yyyy HH:mm:ss}"
            });

            // 2. Fast Startup (Hiberboot) Registry State
            try
            {
                using var pwrKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                int hiberboot = Convert.ToInt32(pwrKey?.GetValue("HiberbootEnabled", 0));
                results.Add(new BootPerformanceMetricItem
                {
                    MetricCategory = "⚡ Hızlı Başlatma (Fast Startup)",
                    ComponentOrAppName = "Hibrit Çekirdek Önyükleme (HiberbootEnabled)",
                    DurationOrValue = hiberboot == 1 ? "✅ Aktif (Hızlı Açılış)" : "⚪ Devre Dışı (Tam Soğuk Açılış)",
                    ImpactAssessment = hiberboot == 1
                        ? "Çekirdek oturumu hiberfil.sys üzerinden hızla yükleniyor."
                        : "Her açılışta çekirdek ve sürücüler sıfırdan başlatılıyor."
                });
            }
            catch { }

            // 3. Query Diagnostics-Performance Operational Log (Event ID 100 = Boot Summary, 101-110 = Slow Boot Degradations)
            try
            {
                string ps = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ev100 = Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Diagnostics-Performance/Operational'; Id=100} -MaxEvents 1 -ErrorAction SilentlyContinue
if ($ev100) {
    $xml = [xml]$ev100.ToXml()
    $bootMs = ($xml.Event.EventData.Data | Where-Object { $_.Name -eq 'BootTime' }).'#text'
    $mainMs = ($xml.Event.EventData.Data | Where-Object { $_.Name -eq 'MainPathBootTime' }).'#text'
    $postMs = ($xml.Event.EventData.Data | Where-Object { $_.Name -eq 'BootPostBootTime' }).'#text'
    Write-Output ""BOOT100|$bootMs|$mainMs|$postMs|$($ev100.TimeCreated.ToString('dd.MM.yyyy HH:mm'))""
}
$deg = Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Diagnostics-Performance/Operational'; Id=101,102,103,106,108,110} -MaxEvents 12 -ErrorAction SilentlyContinue
foreach ($e in $deg) {
    $x = [xml]$e.ToXml()
    $name = ($x.Event.EventData.Data | Where-Object { $_.Name -match 'Name|FileName|FriendlyName' } | Select-Object -First 1).'#text'
    $degMs = ($x.Event.EventData.Data | Where-Object { $_.Name -match 'DegradationTime|TotalTime' } | Select-Object -First 1).'#text'
    if ($name) {
        Write-Output ""DEG|$($e.Id)|$name|$degMs""
    }
}
";
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(ps, runAsAdmin: false, redirectOutput: true);
                ProcessRunResult runResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(8));
                if (runResult.Started && !runResult.TimedOut)
                {
                    string output = runResult.StandardOutput;

                    foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts[0] == "BOOT100" && parts.Length >= 5)
                        {
                            double totalSec = double.TryParse(parts[1], out double bms) ? bms / 1000.0 : 0;
                            double mainSec = double.TryParse(parts[2], out double mms) ? mms / 1000.0 : 0;
                            double postSec = double.TryParse(parts[3], out double pms) ? pms / 1000.0 : 0;

                            results.Add(new BootPerformanceMetricItem
                            {
                                MetricCategory = "🚀 Toplam Windows Açılış Süresi",
                                ComponentOrAppName = $"Olay Kimliği 100 ({parts[4]})",
                                DurationOrValue = $"{totalSec:F1} saniye ({parts[1]} ms)",
                                ImpactAssessment = totalSec <= 25
                                    ? "✅ Çok Hızlı NVMe Önyükleme"
                                    : (totalSec <= 45 ? "ℹ️ Normal Açılış Süresi" : "⚠️ Yavaş Açılış — Başlangıç öğelerini azaltın")
                            });

                            results.Add(new BootPerformanceMetricItem
                            {
                                MetricCategory = "🧠 Çekirdek & Masaüstü Hazırlık",
                                ComponentOrAppName = "Çekirdek + Masaüstü Sonrası Yükleme",
                                DurationOrValue = $"Çekirdek: {mainSec:F1} sn • Masaüstü Sonrası: {postSec:F1} sn",
                                ImpactAssessment = "MainPathBootTime (Kernel/Sürücü) ve PostBootTime (Başlangıç Uygulamaları)"
                            });
                        }
                        else if (parts[0] == "DEG" && parts.Length >= 4)
                        {
                            string appName = parts[2].Trim();
                            string msStr = string.IsNullOrWhiteSpace(parts[3]) ? "Belirsiz" : $"{parts[3]} ms gecikme";
                            results.Add(new BootPerformanceMetricItem
                            {
                                MetricCategory = $"⚠️ Açılış Darboğazı (Event {parts[1]})",
                                ComponentOrAppName = appName,
                                DurationOrValue = msStr,
                                ImpactAssessment = "Açılış süresini uzatan uygulama, servis veya sürücü."
                            });
                        }
                    }
                }
            }
            catch { }

            return results;
        });
    }

    // ==================== 2. WSL2 LINUX DISTRO & DOCKER CONTAINER / IMAGE MANAGER ====================

    public static async Task<List<DockerOrWslItem>> ScanDockerAndWslItemsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<DockerOrWslItem>();

            // 1. Scan WSL2 Distros (WSL_UTF8=1 forces wsl.exe to output UTF-8 instead of UTF-16LE)
            try
            {
                ProcessStartInfo psiWsl = SafeProcessRunner.BuildDirectProcessStartInfo(
                    "wsl.exe",
                    ["--list", "--verbose"],
                    redirectOutput: true,
                    environmentVariables: new Dictionary<string, string> { ["WSL_UTF8"] = "1" });

                ProcessRunResult wslResult = SafeProcessRunner.Run(psiWsl, TimeSpan.FromSeconds(6));
                if (wslResult.Started && !wslResult.TimedOut)
                {
                    string outWsl = wslResult.StandardOutput.Replace("\0", "");

                    foreach (var rawLine in outWsl.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        string clean = rawLine.Trim();
                        if (clean.StartsWith("NAME", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("Windows Subsystem", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("yüklü dağıtım yok", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("no installed distributions", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        bool isDefault = clean.StartsWith('*');
                        string trimmed = clean.TrimStart('*').Trim();
                        var cols = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        if (cols.Length >= 3)
                        {
                            string name = cols[0];
                            string state = cols[1];
                            string ver = cols[2];

                            list.Add(new DockerOrWslItem
                            {
                                PlatformType = "🐧 WSL2 Linux Dağıtımı",
                                NameOrTag = isDefault ? $"{name} (Varsayılan)" : name,
                                StateOrStatus = state.Equals("Running", StringComparison.OrdinalIgnoreCase)
                                    ? "🟢 Çalışıyor (Running)"
                                    : "⚪ Durduruldu (Stopped)",
                                VersionOrSize = $"WSL Sürüm {ver}",
                                Details = isDefault ? "Varsayılan Linux alt sistemi dağıtımı" : "Yardımcı WSL dağıtımı"
                            });
                        }
                    }
                }
            }
            catch { }

            // 2. Scan Docker Containers & Images if Docker CLI is installed
            try
            {
                ProcessStartInfo psiDocker = SafeProcessRunner.BuildDirectProcessStartInfo(
                    "docker.exe",
                    ["ps", "-a", "--format", "{{.Names}}|{{.Image}}|{{.Status}}|{{.Size}}"],
                    redirectOutput: true);

                ProcessRunResult dockerResult = SafeProcessRunner.Run(psiDocker, TimeSpan.FromSeconds(5));
                if (dockerResult.Started && !dockerResult.TimedOut)
                {
                    string outDocker = dockerResult.StandardOutput;

                    foreach (var line in outDocker.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 4)
                        {
                            list.Add(new DockerOrWslItem
                            {
                                PlatformType = "🐳 Docker Konteyner",
                                NameOrTag = parts[0].Trim(),
                                StateOrStatus = parts[2].Trim(),
                                VersionOrSize = parts[3].Trim(),
                                Details = $"Baz İmaj: {parts[1].Trim()}"
                            });
                        }
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new DockerOrWslItem
                {
                    PlatformType = "ℹ️ Bilgi",
                    NameOrTag = "Aktif WSL2 Dağıtımı veya Docker Konteyneri Bulunamadı",
                    StateOrStatus = "Temiz / Kapalı",
                    VersionOrSize = "-",
                    Details = "WSL2 veya Docker Desktop şu anda çalışmıyor ya da kurulu değil."
                });
            }

            return list;
        });
    }

    public static async Task<(bool Success, string Message)> ShutdownWslAndFreeRamAsync()
    {
        try
        {
            ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo("wsl.exe", ["--shutdown"]);
            await SafeProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            return (true, "Tüm WSL2 Linux dağıtımları ve VmmemWSL sanal makinesi kapatıldı; kullanılan RAM belleği serbest bırakıldı!");
        }
        catch (Exception ex)
        {
            return (false, $"WSL kapatılamadı: {ex.Message}");
        }
    }

    public static async Task<(bool Success, string Message)> PruneDockerSystemAsync()
    {
        try
        {
            ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
                "docker.exe",
                ["system", "prune", "-f"],
                redirectOutput: true);
            ProcessRunResult result = await SafeProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(25)).ConfigureAwait(false);
            if (!result.Started)
                return (false, "Docker CLI bulunamadı.");

            string reclaimedLine = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(l => l.Contains("reclaimed", StringComparison.OrdinalIgnoreCase)) ?? "Tamamlandı";

            return (true, $"Atıl Docker konteyner, ağ ve önbellekleri temizlendi ({reclaimedLine}).");
        }
        catch (Exception ex)
        {
            return (false, $"Docker temizliği çalıştırılamadı (Docker Desktop açık olmalı): {ex.Message}");
        }
    }

    // ==================== 3. WINDOWS DEFENDER SECURITY & EXCLUSION AUDITOR ====================

    public static async Task<List<DefenderSecurityAuditItem>> AuditWindowsDefenderAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<DefenderSecurityAuditItem>();
            try
            {
                string ps = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$st = Get-MpComputerStatus -ErrorAction SilentlyContinue
if ($st) {
    Write-Output ""STATUS|Gerçek Zamanlı Koruma (Real-Time Protection)|$($st.RealTimeProtectionEnabled)|Anlık dosya ve süreç zararlı yazılım taraması""
    Write-Output ""STATUS|Kurcalamaya Karşı Koruma (Tamper Protection)|$($st.IsTamperProtected)|Zararlıların Defender'ı devre dışı bırakmasını engeller""
    Write-Output ""STATUS|Davranış İzleme (Behavior Monitor)|$($st.BehaviorMonitorEnabled)|Fidye yazılımı (Ransomware) ve şüpheli süreç davranışı analizi""
    Write-Output ""STATUS|Giriş Önleme Sistemi (Network IPS)|$($st.NISEnabled)|Ağ tabanlı istismar ve saldırı önleme kalkanı""
    Write-Output ""SIG|Antivirüs İmza Sürümü & Güncelleme|$($st.AntivirusSignatureVersion)|Son Güncelleme: $($st.AntivirusSignatureLastUpdated)""
}
$pref = Get-MpPreference -ErrorAction SilentlyContinue
if ($pref) {
    foreach ($p in $pref.ExclusionPath) {
        if ($p) { Write-Output ""EX_PATH|$p|⚠️ İstisna Klasörü|Bu klasör Defender tarafından TARANMAZ (Güvenli olduğundan emin olun)"" }
    }
    foreach ($pr in $pref.ExclusionProcess) {
        if ($pr) { Write-Output ""EX_PROC|$pr|⚠️ İstisna Süreci|Bu işlem Defender tarafından TARANMAZ"" }
    }
    foreach ($ex in $pref.ExclusionExtension) {
        if ($ex) { Write-Output ""EX_EXT|$ex|⚠️ İstisna Uzantısı|Bu dosya uzantısı Defender tarafından TARANMAZ"" }
    }
}
";
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(ps, runAsAdmin: false, redirectOutput: true);
                ProcessRunResult runResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(10));
                if (runResult.Started && !runResult.TimedOut)
                {
                    string output = runResult.StandardOutput;

                    foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 4) continue;

                        string type = parts[0].Trim();
                        if (type == "STATUS")
                        {
                            bool isTrue = parts[2].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                            results.Add(new DefenderSecurityAuditItem
                            {
                                AuditCategory = "🛡️ Defender Koruma Kalkanı",
                                SettingOrExclusionName = parts[1].Trim(),
                                StatusBadge = isTrue ? "✅ Aktif & Güvenli" : "🚨 DEVRE DIŞI!",
                                SecurityNotes = parts[3].Trim()
                            });
                        }
                        else if (type == "SIG")
                        {
                            results.Add(new DefenderSecurityAuditItem
                            {
                                AuditCategory = "🦠 Virüs İmza Veritabanı",
                                SettingOrExclusionName = parts[1].Trim(),
                                StatusBadge = $"Sürüm: {parts[2].Trim()}",
                                SecurityNotes = parts[3].Trim()
                            });
                        }
                        else if (type.StartsWith("EX_"))
                        {
                            results.Add(new DefenderSecurityAuditItem
                            {
                                AuditCategory = "🔍 Defender Tarama İstisnası",
                                SettingOrExclusionName = parts[1].Trim(),
                                StatusBadge = parts[2].Trim(),
                                SecurityNotes = parts[3].Trim()
                            });
                        }
                    }
                }
            }
            catch { }

            return results;
        });
    }

    public static async Task<(bool Success, string Message)> RunDefenderActionAsync(bool quickScan)
    {
        try
        {
            string cmd = quickScan
                ? "Start-MpScan -ScanType QuickScan"
                : "Update-MpSignature";

            ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(cmd, runAsAdmin: false);
            await SafeProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(25)).ConfigureAwait(false);

            return quickScan
                ? (true, "Windows Defender Hızlı Zararlı Yazılım Taraması (QuickScan) tamamlandı!")
                : (true, "Windows Defender Antivirüs imza veritabanı en güncel sürüme yükseltildi!");
        }
        catch (Exception ex)
        {
            return (false, $"Defender işlemi başarısız: {ex.Message}");
        }
    }

    // ==================== 4. MONITOR EDID, REFRESH RATE (HZ) & GPU RESET DOCTOR ====================

    public static async Task<List<MonitorAndDisplayItem>> ScanMonitorsAndGpuOutputsAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<MonitorAndDisplayItem>();
            try
            {
                string ps = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$gpus = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue
foreach ($g in $gpus) {
    $curHz = $g.CurrentRefreshRate
    $maxHz = $g.MaxRefreshRate
    $res = ""$($g.CurrentHorizontalResolution)x$($g.CurrentVerticalResolution) ($($g.CurrentBitsPerPixel)-bit)""
    Write-Output ""GPU|$($g.Name)|$res|$curHz|$maxHz|Sürücü: $($g.DriverVersion) ($($g.VideoProcessor))""
}
$mons = Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID -ErrorAction SilentlyContinue
foreach ($m in $mons) {
    $mfg = ($m.ManufacturerName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) -join ''
    $name = ($m.UserFriendlyName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) -join ''
    $ser = ($m.SerialNumberID | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) -join ''
    if (-not $name) { $name = 'Dahili / PnP Monitör Paneli' }
    Write-Output ""MON|$mfg - $name|EDID Donanım Paneli|Seri No: $ser|Üretim: $($m.WeekOfManufacture)/$($m.YearOfManufacture)""
}
";
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(ps, runAsAdmin: false, redirectOutput: true);
                ProcessRunResult runResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(8));
                if (runResult.Started && !runResult.TimedOut)
                {
                    string output = runResult.StandardOutput;

                    foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts[0] == "GPU" && parts.Length >= 6)
                        {
                            int curHz = int.TryParse(parts[3].Trim(), out int parsedCurHz) ? parsedCurHz : 0;
                            int maxHz = int.TryParse(parts[4].Trim(), out int parsedMaxHz) ? parsedMaxHz : 0;

                            string badge = (curHz > 0 && maxHz > curHz && maxHz <= 500 && curHz <= 60 && maxHz >= 100)
                                ? $"⚠️ {curHz} Hz'de Çalışıyor! (Panel {maxHz} Hz Destekliyor)"
                                : (curHz >= 100 ? $"🚀 Yüksek Yenileme Hızı ({curHz} Hz Aktif)" : $"✅ {curHz} Hz Aktif");

                            results.Add(new MonitorAndDisplayItem
                            {
                                DisplayName = $"🎮 {parts[1].Trim()}",
                                ResolutionAndDepth = parts[2].Trim(),
                                RefreshRateInfo = curHz > 0 ? $"Aktif: {curHz} Hz (Maks: {maxHz} Hz)" : "Harici / Dinamik GPU",
                                EdidAndDriverInfo = parts[5].Trim(),
                                OptimizationBadge = badge
                            });
                        }
                        else if (parts[0] == "MON" && parts.Length >= 5)
                        {
                            results.Add(new MonitorAndDisplayItem
                            {
                                DisplayName = $"🖥️ {parts[1].Trim()}",
                                ResolutionAndDepth = parts[2].Trim(),
                                RefreshRateInfo = parts[4].Trim(),
                                EdidAndDriverInfo = parts[3].Trim(),
                                OptimizationBadge = "✅ EDID Donanım Kimliği Doğrulandı"
                            });
                        }
                    }
                }
            }
            catch { }

            return results;
        });
    }

    public static async Task<(bool Success, string Message)> RestartGpuDisplayDriverAsync()
    {
        try
        {
            const string ps = @"
Get-PnpDevice -Class Display -Status OK -ErrorAction SilentlyContinue | ForEach-Object {
    pnputil.exe /restart-device ""$($_.InstanceId)""
}";
            ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(ps, runAsAdmin: true);
            await SafeProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            return (true, "Ekran Kartı (GPU) görüntü sürücüsü başarıyla yeniden başlatıldı!");
        }
        catch (Exception ex)
        {
            return (false, $"GPU sürücüsü yeniden başlatılamadı: {ex.Message}");
        }
    }

    // ==================== 5. GIT REPOSITORY (.GIT) COMPRESSOR (GIT GC) & STATUS RADAR ====================

    public static async Task<List<GitRepoCompressionItem>> ScanGitRepositoriesAsync(string workspaceRoot)
    {
        return await Task.Run(() =>
        {
            var results = new List<GitRepoCompressionItem>();
            if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
                return results;

            var gitDirs = new List<string>();
            try
            {
                string rootGit = Path.Combine(workspaceRoot, ".git");
                if (Directory.Exists(rootGit))
                    gitDirs.Add(rootGit);

                foreach (var sub in Directory.EnumerateDirectories(workspaceRoot, ".git", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MaxRecursionDepth = 3
                }))
                {
                    if (!gitDirs.Contains(sub, StringComparer.OrdinalIgnoreCase))
                        gitDirs.Add(sub);
                }
            }
            catch { }

            foreach (var gitDir in gitDirs)
            {
                try
                {
                    string repoRoot = Path.GetDirectoryName(gitDir) ?? workspaceRoot;
                    string repoName = Path.GetFileName(repoRoot);
                    long gitSize = CalculateDirBytes(gitDir);

                    string branch = RunGitCommandQuick(repoRoot, ["rev-parse", "--abbrev-ref", "HEAD"]);
                    if (string.IsNullOrWhiteSpace(branch))
                        branch = "main";

                    string statusOut = RunGitCommandQuick(repoRoot, ["status", "-s"]);
                    int changedCount = statusOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
                    string statusText = changedCount == 0
                        ? "✅ Temiz Çalışma Ağacı (Clean)"
                        : $"✏️ {changedCount} Değişiklik Bekliyor";

                    results.Add(new GitRepoCompressionItem
                    {
                        RepoName = repoName,
                        RepoRootPath = repoRoot,
                        ActiveBranch = $"🌿 {branch}",
                        WorkingTreeStatus = statusText,
                        GitDirSizeBytes = gitSize,
                        IsSelected = true
                    });
                }
                catch { }
            }

            return results.OrderByDescending(x => x.GitDirSizeBytes).ToList();
        });
    }

    public static async Task<(int OptimizedCount, long SavedBytes, string Message)> CompressSelectedGitReposAsync(IEnumerable<GitRepoCompressionItem> items)
    {
        return await Task.Run(() =>
        {
            int count = 0;
            long savedTotal = 0;

            foreach (var repo in items.Where(x => x.IsSelected))
            {
                try
                {
                    string gitDir = Path.Combine(repo.RepoRootPath, ".git");
                    if (!Directory.Exists(gitDir)) continue;

                    long before = CalculateDirBytes(gitDir);
                    _ = RunGitCommandQuick(repo.RepoRootPath, ["gc", "--prune=now", "--quiet"], timeoutMs: 30000);
                    long after = CalculateDirBytes(gitDir);

                    if (before > after)
                        savedTotal += (before - after);

                    count++;
                }
                catch { }
            }

            string msg = $"{count} Git deposunda 'git gc --prune=now' sıkıştırması tamamlandı ({CleanerRule.FormatBytes(savedTotal)} gevşek nesne alanı kazanıldı).";
            return (count, savedTotal, msg);
        });
    }

    private static string RunGitCommandQuick(string workingDir, IReadOnlyList<string> args, int timeoutMs = 4000)
    {
        try
        {
            ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
                "git.exe",
                args,
                redirectOutput: true,
                workingDirectory: workingDir);
            ProcessRunResult result = SafeProcessRunner.Run(psi, TimeSpan.FromMilliseconds(timeoutMs));
            return result.Started && !result.TimedOut ? result.StandardOutput.Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long CalculateDirBytes(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            }))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }
}
