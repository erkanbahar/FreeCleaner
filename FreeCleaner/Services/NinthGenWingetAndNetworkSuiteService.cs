using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using FreeCleaner.Models;
using Microsoft.Data.Sqlite;

namespace FreeCleaner.Services;

public static partial class NinthGenWingetAndNetworkSuiteService
{
    [GeneratedRegex(@"\s{2,}", RegexOptions.None, 1000)]
    private static partial Regex WingetColumnSplitRegex();

    // ==================== 1. WINGET SOFTWARE UPDATER ====================

    public static async Task<List<WingetUpgradeItem>> ScanWingetUpgradesAsync()
    {
        var results = new List<WingetUpgradeItem>();
        try
        {
            ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
                "winget.exe",
                ["upgrade", "--include-unknown", "--accept-source-agreements"],
                redirectOutput: true);

            ProcessRunResult runResult = await SafeProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(25)).ConfigureAwait(false);
            if (runResult.Started && !runResult.TimedOut)
                results = ParseWingetUpgradeOutput(runResult.StandardOutput);
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Winget:Scan", ex);
        }

        return results;
    }

    public static List<WingetUpgradeItem> ParseWingetUpgradeOutput(string? output)
    {
        var results = new List<WingetUpgradeItem>();
        if (string.IsNullOrWhiteSpace(output))
            return results;

        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Trim().Length > 5)
            .ToList();

        int sepIndex = lines.FindIndex(l => l.StartsWith("---", StringComparison.Ordinal));
        if (sepIndex <= 0 || sepIndex >= lines.Count - 1)
            return results;

        for (int i = sepIndex + 1; i < lines.Count; i++)
        {
            string row = lines[i].Trim();
            if (row.StartsWith("---", StringComparison.Ordinal) ||
                row.Contains("upgrades available", StringComparison.OrdinalIgnoreCase) ||
                row.Contains("yükseltme mevcut", StringComparison.OrdinalIgnoreCase) ||
                row.Contains("paket", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Split by 2 or more spaces to reliably parse winget columns regardless of localization
            var cols = WingetColumnSplitRegex().Split(row)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToArray();

            if (cols.Length >= 4)
            {
                string name = cols[0].Trim();
                string id = cols[1].Trim();
                string curVer = cols[2].Trim();
                string availVer = cols[3].Trim();
                string src = cols.Length >= 5 ? cols[4].Trim() : "winget";

                if (!id.Contains('.') && cols.Length == 4)
                    continue;

                results.Add(new WingetUpgradeItem
                {
                    PackageName = name,
                    PackageId = id,
                    InstalledVersion = curVer,
                    AvailableVersion = availVer,
                    SourceName = src,
                    IsSelected = true
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Upgrades packages one by one so the UI can show which app is being updated and overall progress.
    /// progress reports: (completedCount, totalCount, currentItem, isStarting)
    /// </summary>
    public static async Task<(int UpdatedCount, int FailedCount, string Message)> UpgradeWingetPackagesWithProgressAsync(
        IReadOnlyList<WingetUpgradeItem> targets,
        IProgress<(int Completed, int Total, WingetUpgradeItem Item, bool IsStarting, bool Success)>? progress,
        CancellationToken cancellationToken = default)
    {
        int total = targets.Count;
        int updated = 0;
        int failed = 0;

        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var pkg = targets[i];
            progress?.Report((i, total, pkg, true, false));

            bool success = false;
            try
            {
                ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
                    "winget.exe",
                    [
                        "upgrade",
                        "--id",
                        pkg.PackageId,
                        "--exact",
                        "--silent",
                        "--accept-package-agreements",
                        "--accept-source-agreements",
                        "--disable-interactivity"
                    ],
                    redirectOutput: true);

                ProcessRunResult runResult = await SafeProcessRunner
                    .RunAsync(psi, TimeSpan.FromMinutes(10), cancellationToken)
                    .ConfigureAwait(false);
                success = runResult.Started && !runResult.TimedOut && runResult.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorLogService.Log($"Winget:Upgrade:{pkg.PackageId}", ex);
            }

            if (success) updated++; else failed++;
            progress?.Report((i + 1, total, pkg, false, success));
        }

        string msg = $"Winget güncelleme tamamlandı: {updated} başarılı, {failed} başarısız (Toplam {total} program).";
        return (updated, failed, msg);
    }

    // ==================== 2. LOCAL NETWORK (LAN / WI-FI) DEVICE RADAR ====================

    public static async Task<List<LanDeviceItem>> ScanLocalNetworkDevicesAsync()
    {
        return await Task.Run(async () =>
        {
            var results = new List<LanDeviceItem>();
            string localIp = "127.0.0.1";
            string gatewayIp = string.Empty;
            string localMac = "-";

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var props = nic.GetIPProperties();
                    var gw = props.GatewayAddresses
                        .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
                    var ip = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();

                    if (!string.IsNullOrEmpty(gw) && !string.IsNullOrEmpty(ip))
                    {
                        localIp = ip;
                        gatewayIp = gw;
                        byte[] mb = nic.GetPhysicalAddress().GetAddressBytes();
                        if (mb.Length > 0)
                            localMac = string.Join(":", mb.Select(b => b.ToString("X2")));
                        break;
                    }
                }
            }
            catch { }

            var pingTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // Ping sweep across /24 subnet to populate ARP cache & measure live latency
            int lastDot = localIp.LastIndexOf('.');
            if (lastDot > 0 && !localIp.StartsWith("127."))
            {
                string subnetPrefix = localIp[..(lastDot + 1)];
                var tasks = Enumerable.Range(1, 254).Select(async hostNum =>
                {
                    string targetIp = $"{subnetPrefix}{hostNum}";
                    try
                    {
                        using var ping = new Ping();
                        var reply = await ping.SendPingAsync(targetIp, 350);
                        if (reply.Status == IPStatus.Success)
                        {
                            lock (pingTimes)
                            {
                                pingTimes[targetIp] = reply.RoundtripTime;
                            }
                        }
                    }
                    catch { }
                });

                await Task.WhenAll(tasks);
            }

            // Add This PC first
            results.Add(new LanDeviceItem
            {
                IpAddress = localIp,
                MacAddress = localMac,
                Hostname = Environment.MachineName,
                VendorName = GuessMacVendor(localMac),
                PingStatus = "0 ms (Yerel)",
                DeviceRoleBadge = "🖥️ Bu Bilgisayar"
            });

            // Parse ARP table (arp -a)
            try
            {
                ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
                    "arp.exe",
                    ["-a"],
                    redirectOutput: true);

                ProcessRunResult arpResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(5));
                if (arpResult.Started && !arpResult.TimedOut)
                {
                    string arpOut = arpResult.StandardOutput;

                    var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { localIp };

                    foreach (var rawLine in arpOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var tokens = rawLine.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length < 3) continue;

                        string ip = tokens[0].Trim();
                        string macRaw = tokens[1].Trim().Replace('-', ':').ToUpperInvariant();

                        if (!IPAddress.TryParse(ip, out var parsedIp) || parsedIp.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        // Skip multicast / broadcast addresses
                        if (ip.EndsWith(".255") || ip.StartsWith("224.") || ip.StartsWith("239.") || macRaw == "FF:FF:FF:FF:FF:FF")
                            continue;

                        if (!seenIps.Add(ip))
                            continue;

                        bool isGateway = string.Equals(ip, gatewayIp, StringComparison.OrdinalIgnoreCase);
                        bool respondedToPing = pingTimes.TryGetValue(ip, out long ms);

                        // Skip stale offline ARP entries that neither responded to ping nor are gateway/dynamic
                        if (!respondedToPing && !isGateway && macRaw.StartsWith("00:00:00"))
                            continue;

                        string role = isGateway
                            ? "🌐 Ağ Geçidi (Modem / Router)"
                            : (respondedToPing ? "🟢 Aktif Ağ Cihazı" : "⚪ ARP Önbelleğinde (Uykuda)");

                        results.Add(new LanDeviceItem
                        {
                            IpAddress = ip,
                            MacAddress = macRaw,
                            Hostname = isGateway ? "Gateway / Router" : ip,
                            VendorName = GuessMacVendor(macRaw),
                            PingStatus = respondedToPing ? $"{ms} ms" : "ARP Kaydı",
                            DeviceRoleBadge = role
                        });
                    }
                }
            }
            catch { }

            return results
                .OrderBy(x =>
                {
                    var parts = x.IpAddress.Split('.');
                    return parts.Length == 4 && int.TryParse(parts[3], out int last) ? last : 999;
                })
                .ToList();
        });
    }

    private static string GuessMacVendor(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac) || mac.Length < 8)
            return "Bilinmeyen / Özel MAC";

        string prefix = mac[..8].ToUpperInvariant();

        // Check locally administered randomized MAC bit (second hex digit is 2, 6, A, or E)
        char secondHex = prefix[1];
        if (secondHex is '2' or '6' or 'A' or 'E')
            return "Gizli / Rastgele MAC (iOS / Android / Win11)";

        return prefix switch
        {
            "00:15:5D" => "Microsoft Hyper-V / WSL Sanal Ağ",
            "00:50:56" or "00:0C:29" => "VMware Sanal Makine",
            "08:00:27" => "Oracle VirtualBox",
            "DC:A6:32" or "B8:27:EB" or "E4:5F:01" => "Raspberry Pi Foundation",
            _ => "Standart Donanım Üreticisi (IEEE OUI)"
        };
    }

    // ==================== 3. BROWSER & IDE SQLITE DATABASE OPTIMIZER ====================

    private static readonly byte[] SqliteMagicBytes = "SQLite format 3\0"u8.ToArray();

    public static async Task<List<SqliteDatabaseItem>> ScanSqliteDatabasesAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<SqliteDatabaseItem>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var searchTargets = new (string AppName, string DirPath)[]
            {
                ("Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default")),
                ("Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default")),
                ("Brave Browser", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default")),
                ("VS Code (Global Storage)", Path.Combine(roamingAppData, "Code", "User", "globalStorage")),
                ("Cursor AI IDE", Path.Combine(roamingAppData, "Cursor", "User", "globalStorage")),
                ("Mozilla Firefox", Path.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles"))
            };

            foreach (var (appName, dirPath) in searchTargets)
            {
                try
                {
                    if (!Directory.Exists(dirPath)) continue;

                    foreach (var file in Directory.EnumerateFiles(dirPath, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MaxRecursionDepth = 2
                    }))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length < 128 * 1024) continue; // Only inspect files >= 128 KB
                            if (fi.Extension.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                                fi.Name.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ||
                                fi.Name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase) ||
                                fi.Name.EndsWith("-journal", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!TryInspectSqliteHeader(file, out int pageSize, out uint freePages))
                                continue;

                            long freeBytes = (long)pageSize * freePages;
                            long walBytes = 0;
                            string walPath = file + "-wal";
                            if (File.Exists(walPath))
                                walBytes = new FileInfo(walPath).Length;

                            long reclaimable = freeBytes + walBytes;
                            string details = $"Sayfa Boyutu: {pageSize} B • Boş Sayfa: {freePages} ({CleanerRule.FormatBytes(freeBytes)})" +
                                             (walBytes > 0 ? $" + WAL Günlüğü: {CleanerRule.FormatBytes(walBytes)}" : "");

                            results.Add(new SqliteDatabaseItem
                            {
                                AppName = appName,
                                DatabaseName = fi.Name,
                                FullFilePath = fi.FullName,
                                TotalSizeBytes = fi.Length + walBytes,
                                ReclaimableBytes = reclaimable,
                                HealthDetails = details,
                                IsSelected = reclaimable > 0
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return results
                .OrderByDescending(x => x.ReclaimableBytes)
                .ThenByDescending(x => x.TotalSizeBytes)
                .ToList();
        });
    }

    private static bool TryInspectSqliteHeader(string filePath, out int pageSize, out uint freePages)
    {
        pageSize = 4096;
        freePages = 0;

        try
        {
            Span<byte> header = stackalloc byte[44];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Read(header) < 40)
                return false;

            if (!header[..16].SequenceEqual(SqliteMagicBytes))
                return false;

            ushort rawPageSize = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(16, 2));
            pageSize = rawPageSize == 1 ? 65536 : rawPageSize;
            freePages = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(36, 4));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(int OptimizedCount, long FreedBytes, string Message)> OptimizeSelectedSqliteDatabasesAsync(IEnumerable<SqliteDatabaseItem> items)
    {
        return await Task.Run(() =>
        {
            int count = 0;
            long freed = 0;
            int failed = 0;

            foreach (var db in items.Where(x => x.IsSelected))
            {
                List<(string Source, string Backup)>? backupFiles = null;
                try
                {
                    long beforeSize = GetDatabaseFootprint(db.FullFilePath);
                    backupFiles = BackupSqliteDatabase(db.FullFilePath);
                    var connectionString = new SqliteConnectionStringBuilder
                    {
                        DataSource = db.FullFilePath,
                        Mode = SqliteOpenMode.ReadWrite,
                        Cache = SqliteCacheMode.Private,
                        DefaultTimeout = 10
                    }.ToString();

                    using var connection = new SqliteConnection(connectionString);
                    connection.Open();
                    ExecuteSqliteCommand(connection, "PRAGMA busy_timeout = 10000;");
                    ExecuteSqliteCommand(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
                    ExecuteSqliteCommand(connection, "VACUUM;");
                    ExecuteSqliteCommand(connection, "PRAGMA optimize;");
                    count++;
                    long afterSize = GetDatabaseFootprint(db.FullFilePath);
                    freed += Math.Max(0, beforeSize - afterSize);
                }
                catch (Exception ex)
                {
                    failed++;
                    if (backupFiles is not null)
                    {
                        try
                        {
                            RestoreSqliteBackup(db.FullFilePath, backupFiles);
                        }
                        catch (Exception restoreException)
                        {
                            ErrorLogService.Log("SQLite yedek geri yükleme", restoreException);
                        }
                    }
                    ErrorLogService.Log("SQLite optimizasyonu", ex);
                }
            }

            string msg = count > 0
                ? $"{count} SQLite veritabanı VACUUM/checkpoint ile optimize edildi ({CleanerRule.FormatBytes(freed)} kazanıldı)." +
                  (failed > 0 ? $" {failed} veritabanı kullanıldığı için atlandı." : string.Empty)
                : "Seçili veritabanları optimize edilemedi. İlgili tarayıcı/IDE uygulamalarını kapatıp tekrar deneyin.";

            return (count, freed, msg);
        });
    }

    private static void ExecuteSqliteCommand(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        command.ExecuteNonQuery();
    }

    private static long GetDatabaseFootprint(string databasePath)
    {
        long total = File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0;
        foreach (string suffix in new[] { "-wal", "-shm", "-journal" })
        {
            string sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
                total += new FileInfo(sidecar).Length;
        }

        return total;
    }

    private static List<(string Source, string Backup)> BackupSqliteDatabase(string databasePath)
    {
        string backupDirectory = AppStoragePathsService.GetSqliteBackupsDirectory();
        Directory.CreateDirectory(backupDirectory);
        string backupPrefix = Path.Combine(
            backupDirectory,
            $"{Path.GetFileName(databasePath)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        var backups = new List<(string Source, string Backup)>();

        foreach (string source in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", databasePath + "-journal" })
        {
            if (!File.Exists(source))
                continue;

            string suffix = source.Length == databasePath.Length
                ? string.Empty
                : source[databasePath.Length..];
            string backupPath = backupPrefix + (string.IsNullOrEmpty(suffix) ? string.Empty : suffix) + ".bak";
            File.Copy(source, backupPath, overwrite: false);
            backups.Add((source, backupPath));
        }

        return backups;
    }

    private static void RestoreSqliteBackup(
        string databasePath,
        IEnumerable<(string Source, string Backup)> backups)
    {
        foreach (string suffix in new[] { "-wal", "-shm", "-journal" })
        {
            string sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
                File.Delete(sidecar);
        }

        foreach ((string source, string backup) in backups)
            File.Copy(backup, source, overwrite: true);
    }

    // ==================== 4. WINDOWS CREDENTIAL VAULT (CMDKEY) & SSH / GIT KEY AUDITOR ====================

    public static async Task<List<CredentialOrSshItem>> ScanCredentialsAndSshKeysAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<CredentialOrSshItem>();

            // 1. Scan ~/.ssh keys and config
            try
            {
                string sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
                if (Directory.Exists(sshDir))
                {
                    foreach (var file in Directory.EnumerateFiles(sshDir))
                    {
                        var fi = new FileInfo(file);
                        bool isPub = fi.Extension.Equals(".pub", StringComparison.OrdinalIgnoreCase);
                        string details = $"Dosya Boyutu: {CleanerRule.FormatBytes(fi.Length)} • Son Değişiklik: {fi.LastWriteTime:dd.MM.yyyy}";

                        if (isPub)
                        {
                            try
                            {
                                details = File.ReadAllText(file).Trim();
                            }
                            catch { }
                        }

                        results.Add(new CredentialOrSshItem
                        {
                            Category = isPub ? "🔑 SSH Açık Anahtarı (Public Key)" : "🔒 SSH Özel Anahtar / Yapılandırma",
                            TargetOrKeyName = fi.Name,
                            UserOrType = sshDir,
                            DetailsOrPublicKey = details,
                            CanDeleteFromVault = false,
                            IsSelected = false
                        });
                    }
                }
            }
            catch { }

            // 2. Scan Windows Credential Manager via cmdkey /list
            try
            {
                ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
                    "cmdkey.exe",
                    ["/list"],
                    redirectOutput: true);

                ProcessRunResult cmdkeyResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(5));
                if (cmdkeyResult.Started && !cmdkeyResult.TimedOut)
                {
                    string output = cmdkeyResult.StandardOutput;

                    string currentTarget = string.Empty;
                    string currentType = string.Empty;
                    string currentUser = string.Empty;

                    foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = rawLine.Trim();
                        int colon = line.IndexOf(':');
                        if (colon <= 0) continue;

                        string key = line[..colon].Trim();
                        string val = line[(colon + 1)..].Trim();

                        if (key.Contains("Target", StringComparison.OrdinalIgnoreCase) ||
                            key.Contains("Hedef", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(currentTarget))
                            {
                                results.Add(new CredentialOrSshItem
                                {
                                    Category = "🛡️ Windows Kimlik Kasası (Credential)",
                                    TargetOrKeyName = currentTarget,
                                    UserOrType = string.IsNullOrEmpty(currentUser) ? currentType : currentUser,
                                    DetailsOrPublicKey = $"Tür: {currentType} (cmdkey ile silinebilir)",
                                    CanDeleteFromVault = true,
                                    IsSelected = false
                                });
                            }

                            int eqIdx = val.IndexOf('=');
                            currentTarget = eqIdx >= 0 ? val[(eqIdx + 1)..].Trim() : val;
                            currentType = string.Empty;
                            currentUser = string.Empty;
                        }
                        else if (key.Contains("Type", StringComparison.OrdinalIgnoreCase) ||
                                 key.Contains("Tür", StringComparison.OrdinalIgnoreCase))
                        {
                            currentType = val;
                        }
                        else if (key.Contains("User", StringComparison.OrdinalIgnoreCase) ||
                                 key.Contains("Kullanıcı", StringComparison.OrdinalIgnoreCase))
                        {
                            currentUser = val;
                        }
                    }

                    if (!string.IsNullOrEmpty(currentTarget))
                    {
                        results.Add(new CredentialOrSshItem
                        {
                            Category = "🛡️ Windows Kimlik Kasası (Credential)",
                            TargetOrKeyName = currentTarget,
                            UserOrType = string.IsNullOrEmpty(currentUser) ? currentType : currentUser,
                            DetailsOrPublicKey = $"Tür: {currentType} (cmdkey ile silinebilir)",
                            CanDeleteFromVault = true,
                            IsSelected = false
                        });
                    }
                }
            }
            catch { }

            return results;
        });
    }

    public static int DeleteSelectedCredentials(IEnumerable<CredentialOrSshItem> items)
    {
        int deleted = 0;
        foreach (var item in items.Where(x => x.IsSelected && x.CanDeleteFromVault))
        {
            try
            {
                ProcessStartInfo psi = SafeProcessRunner.BuildDirectProcessStartInfo(
                    "cmdkey.exe",
                    [$"/delete:{SafeProcessRunner.SanitizeSingleLine(item.TargetOrKeyName)}"]);
                ProcessRunResult delResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(5));
                if (delResult.Started && !delResult.TimedOut && delResult.ExitCode == 0)
                    deleted++;
            }
            catch (Exception ex)
            {
                ErrorLogService.Log("CredentialVault:Delete", ex);
            }
        }
        return deleted;
    }

    // ==================== 5. LIVE CPU PRIORITY & ECOQOS EFFICIENCY MANAGER ====================

    public static async Task<List<ProcessPriorityItem>> GetActiveProcessesForPriorityAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<ProcessPriorityItem>();

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id <= 4) continue;
                    long ws = p.WorkingSet64;
                    if (ws < 25 * 1024 * 1024) continue; // Show processes using >= 25 MB RAM

                    ProcessPriorityClass prio = p.PriorityClass;
                    string badge = prio switch
                    {
                        ProcessPriorityClass.RealTime => "🔥 Gerçek Zamanlı (RealTime)",
                        ProcessPriorityClass.High => "⚡ Yüksek Öncelik (High Boost)",
                        ProcessPriorityClass.AboveNormal => "🔼 Normalin Üstü (AboveNormal)",
                        ProcessPriorityClass.BelowNormal => "🔽 Normalin Altı",
                        ProcessPriorityClass.Idle => "🍃 Verimlilik / Düşük Mod (Idle/Eco)",
                        _ => "🟢 Normal Öncelik"
                    };

                    TimeSpan cpu = TimeSpan.Zero;
                    try { cpu = p.TotalProcessorTime; } catch { }

                    list.Add(new ProcessPriorityItem
                    {
                        ProcessId = p.Id,
                        ProcessName = $"{p.ProcessName}.exe",
                        PriorityBadge = badge,
                        ThreadCount = p.Threads.Count,
                        RamFormatted = CleanerRule.FormatBytes(ws),
                        CpuTimeFormatted = $"{(int)cpu.TotalMinutes:D2}:{cpu.Seconds:D2}"
                    });
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            return list
                .OrderByDescending(x => x.PriorityBadge.StartsWith('⚡') || x.PriorityBadge.StartsWith("🍃"))
                .ThenByDescending(x => x.ThreadCount)
                .Take(120)
                .ToList();
        });
    }

    public static (bool Success, string Message) SetProcessPriorityLevel(int pid, ProcessPriorityClass newPriority)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            proc.PriorityClass = newPriority;
            string label = newPriority switch
            {
                ProcessPriorityClass.High => "Yüksek Öncelik (High Boost)",
                ProcessPriorityClass.Idle => "Düşük / Eco Verimlilik Modu (Idle)",
                _ => "Normal Öncelik"
            };
            return (true, $"'{name}.exe' (PID: {pid}) işlemci önceliği '{label}' olarak ayarlandı!");
        }
        catch (Exception ex)
        {
            return (false, $"Öncelik değiştirilemedi (Yönetici izni gerekebilir): {ex.Message}");
        }
    }
}
