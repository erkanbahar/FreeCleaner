using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class NextGenForensicsAndRescueService
{
    // ==================== 1. CRASH, BSOD & EVENTLOG ANALYZER ====================

    public static async Task<List<CrashOrEventLogItem>> ScanCrashAndEventLogsAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<CrashOrEventLogItem>();

            // A) Scan Crash Dump files (.dmp) in Minidump, MEMORY.DMP, and %LOCALAPPDATA%\CrashDumps
            var dumpDirs = new[]
            {
                @"C:\Windows\Minidump",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps")
            };

            foreach (var dir in dumpDirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.EnumerateFiles(dir, "*.dmp", SearchOption.TopDirectoryOnly))
                    {
                        var fi = new FileInfo(file);
                        results.Add(new CrashOrEventLogItem
                        {
                            Timestamp = fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm"),
                            EventType = dir.Contains("Minidump", StringComparison.OrdinalIgnoreCase)
                                ? "Mavi Ekran Dökümü (BSOD .dmp)"
                                : "Uygulama Çökme Dökümü (.dmp)",
                            SourceOrApp = fi.Name,
                            EventIdOrSize = CleanerRule.FormatBytes(fi.Length),
                            MessageSummary = $"Döküm Dosyası: {fi.FullName}",
                            DumpFilePath = fi.FullName,
                            DumpSizeBytes = fi.Length
                        });
                    }
                }
                catch { }
            }

            try
            {
                string memoryDmp = @"C:\Windows\MEMORY.DMP";
                if (File.Exists(memoryDmp))
                {
                    var fi = new FileInfo(memoryDmp);
                    results.Add(new CrashOrEventLogItem
                    {
                        Timestamp = fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm"),
                        EventType = "Tam Bellek Çökme Dökümü (MEMORY.DMP)",
                        SourceOrApp = "MEMORY.DMP",
                        EventIdOrSize = CleanerRule.FormatBytes(fi.Length),
                        MessageSummary = $"Tam Sistem Çökme Dökümü ({CleanerRule.FormatBytes(fi.Length)})",
                        DumpFilePath = fi.FullName,
                        DumpSizeBytes = fi.Length
                    });
                }
            }
            catch { }

            // B) Read recent Application & System Error events from Windows EventLog
            ReadRecentErrorsFromLog("Application", "Uygulama Çökmesi / Hatası", results, maxCount: 25);
            ReadRecentErrorsFromLog("System", "Sistem / Kernel Hatası", results, maxCount: 25);

            return results;
        });
    }

    private static void ReadRecentErrorsFromLog(string logName, string defaultLabel, List<CrashOrEventLogItem> list, int maxCount)
    {
        try
        {
            using var eventLog = new EventLog(logName);
            var entries = eventLog.Entries;
            int total = entries.Count;
            int added = 0;

            for (int i = total - 1; i >= 0 && added < maxCount; i--)
            {
                try
                {
                    var entry = entries[i];
                    if (entry.EntryType != EventLogEntryType.Error && entry.EntryType != EventLogEntryType.FailureAudit)
                        continue;

                    long eventId = entry.InstanceId & 0xFFFF;
                    string eventType = eventId switch
                    {
                        1000 => "Uygulama Çökmesi (AppCrash 1000)",
                        1002 => "Uygulama Yanıt Vermiyor (AppHang 1002)",
                        41 => "Kritik Güç / Ani Kapanma (Kernel-Power 41)",
                        1001 => "Mavi Ekran / BugCheck (1001)",
                        6008 => "Beklenmeyen Sistem Kapanışı (6008)",
                        _ => defaultLabel
                    };

                    string rawMsg = (entry.Message ?? string.Empty)
                        .Replace("\r\n", " ")
                        .Replace("\n", " ")
                        .Trim();
                    if (rawMsg.Length > 180)
                        rawMsg = rawMsg[..180] + "...";

                    list.Add(new CrashOrEventLogItem
                    {
                        Timestamp = entry.TimeGenerated.ToString("dd.MM.yyyy HH:mm:ss"),
                        EventType = eventType,
                        SourceOrApp = entry.Source ?? logName,
                        EventIdOrSize = $"Event ID: {eventId}",
                        MessageSummary = string.IsNullOrWhiteSpace(rawMsg) ? "Detay mesajı yok" : rawMsg
                    });

                    added++;
                }
                catch
                {
                    // Skip unreadable entry
                }
            }
        }
        catch
        {
            // Log might require elevation or be inaccessible
        }
    }

    public static async Task<(int DeletedCount, long FreedBytes)> CleanCrashDumpsAsync(IEnumerable<CrashOrEventLogItem> items)
    {
        return await Task.Run(() =>
        {
            int count = 0;
            long freed = 0;

            foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.DumpFilePath)))
            {
                try
                {
                    if (File.Exists(item.DumpFilePath))
                    {
                        long len = item.DumpSizeBytes;
                        File.Delete(item.DumpFilePath);
                        count++;
                        freed += len;
                    }
                }
                catch { }
            }

            return (count, freed);
        });
    }

    // ==================== 2. SAVED WI-FI PASSWORDS & LIVE DNS PING BENCHMARK ====================

    public static async Task<List<WifiProfileAndDnsItem>> ScanWifiPasswordsAndPingDnsAsync()
    {
        return await Task.Run(async () =>
        {
            var results = new List<WifiProfileAndDnsItem>();

            // A) Live DNS Ping Benchmark
            var dnsServers = new (string Name, string Ip)[]
            {
                ("Cloudflare Primary DNS", "1.1.1.1"),
                ("Cloudflare Secondary DNS", "1.0.0.1"),
                ("Google Public DNS Primary", "8.8.8.8"),
                ("Google Public DNS Secondary", "8.8.4.4"),
                ("Quad9 Security DNS", "9.9.9.9"),
                ("OpenDNS Cisco Umbrella", "208.67.222.222")
            };

            var dnsMetrics = new List<(string Name, string Ip, long AvgMs, bool Success)>();
            foreach (var dns in dnsServers)
            {
                long totalMs = 0;
                int okCount = 0;
                try
                {
                    using var ping = new Ping();
                    for (int i = 0; i < 2; i++)
                    {
                        var reply = await ping.SendPingAsync(dns.Ip, 1200);
                        if (reply.Status == IPStatus.Success)
                        {
                            totalMs += reply.RoundtripTime;
                            okCount++;
                        }
                    }
                }
                catch { }

                dnsMetrics.Add((dns.Name, dns.Ip, okCount > 0 ? totalMs / okCount : 9999, okCount > 0));
            }

            long bestMs = dnsMetrics.Where(x => x.Success).Select(x => x.AvgMs).DefaultIfEmpty(9999).Min();

            foreach (var d in dnsMetrics.OrderBy(x => x.AvgMs))
            {
                results.Add(new WifiProfileAndDnsItem
                {
                    ItemCategory = "DNS Gecikme (Ping) Testi",
                    NameOrSsid = d.Name,
                    SecurityOrIp = d.Ip,
                    PasswordOrLatency = d.Success ? $"{d.AvgMs} ms" : "Zaman Aşımı",
                    StatusBadge = d.Success && d.AvgMs == bestMs
                        ? "⚡ EN HIZLI DNS SUNUCUSU"
                        : (d.Success ? "Çevrimiçi" : "Ulaşılamadı")
                });
            }

            // B) Saved Wi-Fi Profiles & Cleartext Passwords via netsh wlan
            try
            {
                string profilesOut = RunNetshOutput("wlan", "show", "profiles");
                var profileNames = new List<string>();

                foreach (var rawLine in profilesOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colonIdx = rawLine.IndexOf(':');
                    if (colonIdx > 0 &&
                        (rawLine.Contains("User Profile", StringComparison.OrdinalIgnoreCase) ||
                         rawLine.Contains("Kullanıcı Profili", StringComparison.OrdinalIgnoreCase) ||
                         rawLine.Contains("All User Profile", StringComparison.OrdinalIgnoreCase) ||
                         rawLine.Contains("Tüm Kullanıcı", StringComparison.OrdinalIgnoreCase)))
                    {
                        string ssid = rawLine[(colonIdx + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(ssid))
                            profileNames.Add(ssid);
                    }
                }

                foreach (var ssid in profileNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string detailOut = RunNetshOutput("wlan", "show", "profile", $"name={ssid}", "key=clear");
                    string auth = "WPA2/WPA3";
                    string password = "(Şifresiz / Açık Ağ veya Bulunamadı)";

                    foreach (var line in detailOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int idx = line.IndexOf(':');
                        if (idx <= 0) continue;

                        string key = line[..idx].Trim();
                        string val = line[(idx + 1)..].Trim();

                        if (key.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
                            key.Contains("Kimlik Doğrulama", StringComparison.OrdinalIgnoreCase))
                        {
                            auth = val;
                        }
                        else if (key.Contains("Key Content", StringComparison.OrdinalIgnoreCase) ||
                                 key.Contains("Anahtar İçeriği", StringComparison.OrdinalIgnoreCase))
                        {
                            password = val;
                        }
                    }

                    results.Add(new WifiProfileAndDnsItem
                    {
                        ItemCategory = "Kayıtlı Wi-Fi Ağı",
                        NameOrSsid = ssid,
                        SecurityOrIp = auth,
                        PasswordOrLatency = password,
                        StatusBadge = "Kayıtlı Profil"
                    });
                }
            }
            catch { }

            return results;
        });
    }

    private static string RunNetshOutput(params string[] arguments)
    {
        try
        {
            ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo("netsh.exe", arguments, redirectOutput: true);
            ProcessRunResult result = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(4));
            return result.Started && !result.TimedOut ? result.StandardOutput : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ==================== 3. WSL2 / DOCKER VHDX & GLOBAL PACKAGE CACHES ====================

    public static async Task<List<GlobalPackageCacheItem>> ScanGlobalPackageCachesAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<GlobalPackageCacheItem>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var cacheCandidates = new (string Ecosystem, string Name, string Path, bool SafeDelete)[]
            {
                ("NuGet (.NET)", "NuGet Küresel Paketler (~/.nuget/packages)", Path.Combine(userProfile, ".nuget", "packages"), true),
                ("NuGet (.NET)", "NuGet HTTP Önbelleği (v3-cache)", Path.Combine(localAppData, "NuGet", "v3-cache"), true),
                ("NuGet (.NET)", "NuGet Temp Önbelleği", Path.Combine(Path.GetTempPath(), "NuGetScratch"), true),
                ("Node.js (NPM)", "NPM İndirme Önbelleği (Local)", Path.Combine(localAppData, "npm-cache"), true),
                ("Node.js (NPM)", "NPM İndirme Önbelleği (Roaming)", Path.Combine(roamingAppData, "npm-cache"), true),
                ("Node.js (PNPM/Yarn)", "PNPM Store Önbelleği", Path.Combine(localAppData, "pnpm-store"), true),
                ("Node.js (PNPM/Yarn)", "Yarn Küresel Önbelleği", Path.Combine(localAppData, "Yarn", "Cache"), true),
                ("Python (Pip)", "Pip Tekerlek/Paket Önbelleği", Path.Combine(localAppData, "pip", "Cache"), true),
                ("Java / Android", "Gradle Küresel Önbelleği (~/.gradle/caches)", Path.Combine(userProfile, ".gradle", "caches"), true),
                ("Java / Maven", "Maven Yerel Deposu (~/.m2/repository)", Path.Combine(userProfile, ".m2", "repository"), true),
                ("Rust (Cargo)", "Cargo Kayıt Önbelleği (~/.cargo/registry/cache)", Path.Combine(userProfile, ".cargo", "registry", "cache"), true)
            };

            foreach (var cand in cacheCandidates)
            {
                try
                {
                    if (!Directory.Exists(cand.Path)) continue;
                    long size = CalculateDirSizeFast(cand.Path);
                    if (size <= 0) continue;

                    results.Add(new GlobalPackageCacheItem
                    {
                        Ecosystem = cand.Ecosystem,
                        CacheName = cand.Name,
                        FolderPath = cand.Path,
                        SizeBytes = size,
                        CanSafeDelete = cand.SafeDelete,
                        IsSelected = cand.SafeDelete
                    });
                }
                catch { }
            }

            // Scan WSL2, Docker & Claude Desktop VM .vhdx virtual disks
            string roamingAppData2 = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var vhdxSearchRoots = new (string RootPath, string EcosystemLabel)[]
            {
                (Path.Combine(localAppData, "Docker", "wsl"), "Docker Desktop VHDX"),
                (Path.Combine(localAppData, "Packages"), "WSL2 / UWP VHDX"),
                (Path.Combine(localAppData, "Claude-Data"), "Claude Desktop VM (VHDX)"),
                (Path.Combine(localAppData, "AnthropicClaude"), "Claude Desktop VM (VHDX)"),
                (Path.Combine(roamingAppData2, "Claude"), "Claude Desktop VM (VHDX)")
            };

            foreach (var (root, ecoLabel) in vhdxSearchRoots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var vhdx in Directory.EnumerateFiles(root, "*.vhdx", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MaxRecursionDepth = 5
                    }))
                    {
                        var fi = new FileInfo(vhdx);
                        results.Add(new GlobalPackageCacheItem
                        {
                            Ecosystem = ecoLabel,
                            CacheName = $"Sanal Disk: {fi.Name}",
                            FolderPath = fi.FullName,
                            SizeBytes = fi.Length,
                            CanSafeDelete = false,
                            IsSelected = false
                        });
                    }
                }
                catch { }
            }

            return results.OrderByDescending(x => x.SizeBytes).ToList();
        });
    }

    public static string BuildDiskpartCompactScript(string vhdxPath)
    {
        return string.Join(Environment.NewLine,
        [
            $"select vdisk file=\"{vhdxPath}\"",
            "attach vdisk readonly",
            "compact vdisk",
            "detach vdisk",
            "exit"
        ]);
    }

    public static async Task<(bool Success, long FreedBytes, string Message)> CompactVhdxVirtualDiskAsync(string vhdxPath)
    {
        if (string.IsNullOrWhiteSpace(vhdxPath) ||
            !vhdxPath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(vhdxPath))
        {
            return (false, 0, "Geçerli bir .vhdx sanal disk dosyası bulunamadı.");
        }

        if (!UltimateSystemSuiteService.IsRunningAsAdministrator())
            return (false, 0, "VHDX sıkıştırma (diskpart compact vdisk) için sağ üstteki '🛡️ Yönetici' butonu ile yönetici yetkisi gerekir.");

        try
        {
            using (File.Open(vhdxPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // Verify file is not currently mounted or locked by Claude Desktop / WSL2 / Docker.
            }
        }
        catch (IOException)
        {
            return (false, 0, "Dosya şu anda kullanımda (kilitli). Lütfen önce Claude Desktop, Docker Desktop veya WSL2'yi (wsl --shutdown) tamamen kapatın.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return (false, 0, $"Dosya erişimi reddedildi: {ex.Message}");
        }

        long beforeBytes = new FileInfo(vhdxPath).Length;
        string scriptPath = Path.Combine(Path.GetTempPath(), $"freecleaner_compact_{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(scriptPath, BuildDiskpartCompactScript(vhdxPath), Encoding.ASCII);

            ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
                "diskpart.exe",
                ["/s", scriptPath],
                redirectOutput: true);

            ProcessRunResult runResult = await SafeProcessRunner.RunAsync(psi, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            if (!runResult.Started)
                return (false, 0, "DiskPart işlemi başlatılamadı.");

            string stdout = runResult.StandardOutput;
            string stderr = runResult.StandardError;

            long afterBytes = File.Exists(vhdxPath) ? new FileInfo(vhdxPath).Length : beforeBytes;
            long freed = Math.Max(0, beforeBytes - afterBytes);

            if (!runResult.TimedOut && runResult.ExitCode == 0)
            {
                string msg = freed > 0
                    ? $"VHDX sıkıştırıldı! {CleanerRule.FormatBytes(beforeBytes)} → {CleanerRule.FormatBytes(afterBytes)} ({CleanerRule.FormatBytes(freed)} yer açıldı)."
                    : $"DiskPart işlemi tamamlandı ({CleanerRule.FormatBytes(afterBytes)} — boş sıfır blok bulunamadı veya tam sıkışmış).";
                return (true, freed, msg);
            }

            string errDetail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (false, 0, $"DiskPart hatası (Kod {runResult.ExitCode}): {errDetail.Trim()}");
        }
        catch (Exception ex)
        {
            return (false, 0, $"VHDX sıkıştırma hatası: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(scriptPath))
                    File.Delete(scriptPath);
            }
            catch { }
        }
    }

    public static async Task<(int CleanedCount, long FreedBytes)> CleanSelectedPackageCachesAsync(IEnumerable<GlobalPackageCacheItem> items)
    {
        return await Task.Run(() =>
        {
            int count = 0;
            long freed = 0;

            foreach (var item in items.Where(x => x.IsSelected && x.CanSafeDelete))
            {
                try
                {
                    if (Directory.Exists(item.FolderPath))
                    {
                        Directory.Delete(item.FolderPath, recursive: true);
                        count++;
                        freed += item.SizeBytes;
                    }
                }
                catch { }
            }

            return (count, freed);
        });
    }

    private static long CalculateDirSizeFast(string dirPath)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dirPath, "*", new EnumerationOptions
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

    // ==================== 4. SYSTEM RESTORE POINTS & WINDOWS GOD MODE ====================

    public static async Task<List<RestorePointItem>> GetRestorePointsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<RestorePointItem>();
            try
            {
                const string restoreQueryPs = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Get-ComputerRestorePoint | ForEach-Object { \"$($_.SequenceNumber)|$($_.Description)|$($_.CreationTime)|$($_.RestorePointType)\" }";
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(restoreQueryPs, runAsAdmin: false, redirectOutput: true);
                ProcessRunResult runResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(8));
                if (runResult.Started && !runResult.TimedOut)
                {
                    string output = runResult.StandardOutput;
                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 4)
                        {
                            list.Add(new RestorePointItem
                            {
                                SequenceNumber = $"#{parts[0].Trim()}",
                                Description = parts[1].Trim(),
                                CreationTime = parts[2].Trim(),
                                RestorePointType = parts[3].Trim()
                            });
                        }
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new RestorePointItem
                {
                    SequenceNumber = "-",
                    Description = "Kayıtlı geri yükleme noktası bulunamadı veya Yönetici izni gerekiyor.",
                    CreationTime = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
                    RestorePointType = "Bilgi"
                });
            }

            return list;
        });
    }

    public static async Task<(bool Success, string Message)> CreateSystemRestorePointAsync(string description)
    {
        return await Task.Run(() =>
        {
            try
            {
                string safeDesc = string.IsNullOrWhiteSpace(description)
                    ? $"FreeCleaner Pro Guvenlik Noktasi ({DateTime.Now:dd.MM.yyyy HH:mm})"
                    : SafeProcessRunner.SanitizeSingleLine(description);

                string restoreScript = $"Enable-ComputerRestore -Drive 'C:\\' -ErrorAction SilentlyContinue; Checkpoint-Computer -Description {SafeProcessRunner.QuotePowerShellSingle(safeDesc)} -RestorePointType 'MODIFY_SETTINGS'";
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(restoreScript, runAsAdmin: true);
                ProcessRunResult runResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(20));
                if (!runResult.Started)
                    return (false, "Geri yükleme noktası işlemi başlatılamadı.");

                return (true, $"Sistem Geri Yükleme Noktası oluşturuldu: '{safeDesc}' (Windows 24 saat içinde tek nokta sınırı uygulayabilir).");
            }
            catch (Exception ex)
            {
                return (false, $"Geri yükleme noktası oluşturulamadı: {ex.Message}");
            }
        });
    }

    public static (bool Success, string PathOrMessage) CreateAndOpenGodModeFolder()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string godModeDir = Path.Combine(desktop, "FreeCleaner_GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}");
            if (!Directory.Exists(godModeDir))
            {
                Directory.CreateDirectory(godModeDir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{godModeDir}\"",
                UseShellExecute = true
            });

            return (true, "Windows Tanrı Modu (God Mode - 200+ Gelişmiş Denetim Masası Aracı) Masaüstünde oluşturuldu ve açıldı!");
        }
        catch (Exception ex)
        {
            return (false, $"God Mode açılamadı: {ex.Message}");
        }
    }

    public static void LaunchSystemUtility(string toolCommand)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = toolCommand,
                UseShellExecute = true
            });
        }
        catch { }
    }

    // ==================== 5. FILE ASSOCIATION (USERCHOICE) DOCTOR ====================

    public static async Task<List<FileAssociationItem>> ScanFileAssociationsAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<FileAssociationItem>();
            var targetExts = new[]
            {
                ".pdf", ".zip", ".rar", ".7z", ".mp4", ".mkv", ".mp3",
                ".jpg", ".png", ".webp", ".svg", ".html", ".json",
                ".xml", ".txt", ".log", ".md", ".cs", ".py", ".js", ".ts", ".iso"
            };

            const string baseKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

            foreach (var ext in targetExts)
            {
                try
                {
                    string extKeyPath = $@"{baseKeyPath}\{ext}\UserChoice";
                    using var ucKey = Registry.CurrentUser.OpenSubKey(extKeyPath);
                    string? progId = ucKey?.GetValue("ProgId")?.ToString();

                    if (string.IsNullOrWhiteSpace(progId))
                    {
                        results.Add(new FileAssociationItem
                        {
                            Extension = ext,
                            CurrentProgId = "(Atanmamış / Varsayılan)",
                            HandlerDescription = "Kullanıcı özel seçimi yok",
                            StatusText = "Standart",
                            RegistryKeyPath = $@"{baseKeyPath}\{ext}",
                            IsSelected = false
                        });
                        continue;
                    }

                    // Check if ProgId exists in HKCR
                    using var crKey = Registry.ClassesRoot.OpenSubKey(progId);
                    bool existsInCr = crKey is not null || progId.StartsWith("AppX", StringComparison.OrdinalIgnoreCase);
                    string friendlyDesc = crKey?.GetValue("")?.ToString() ?? progId;

                    results.Add(new FileAssociationItem
                    {
                        Extension = ext,
                        CurrentProgId = progId,
                        HandlerDescription = friendlyDesc,
                        StatusText = existsInCr ? "Sağlıklı İlişkilendirme" : "⚠️ Yetim / Silinmiş Program Kaydı",
                        RegistryKeyPath = $@"{baseKeyPath}\{ext}",
                        IsSelected = !existsInCr
                    });
                }
                catch { }
            }

            return results;
        });
    }

    public static int ResetSelectedFileAssociations(IEnumerable<FileAssociationItem> items)
    {
        int resetCount = 0;
        foreach (var item in items.Where(x => x.IsSelected))
        {
            try
            {
                using var extKey = Registry.CurrentUser.OpenSubKey(item.RegistryKeyPath, writable: true);
                if (extKey is not null)
                {
                    extKey.DeleteSubKeyTree("OpenWithList", throwOnMissingSubKey: false);
                    try { extKey.DeleteSubKeyTree("UserChoice", throwOnMissingSubKey: false); } catch { }
                    resetCount++;
                }
            }
            catch { }
        }
        return resetCount;
    }
}
