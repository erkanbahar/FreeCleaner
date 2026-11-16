using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class PrivacyAndNetworkShieldService
{
    public static List<PrivacyTraceItem> GetPrivacyCategories()
    {
        return
        [
            new PrivacyTraceItem
            {
                Id = "prv_run_mru",
                Name = "Windows Çalıştır (Win+R) Komut Geçmişi",
                Category = "Windows Gezgini",
                Description = "Başlat > Çalıştır penceresinde yazılan komutların kayıt defteri geçmişi (RunMRU)"
            },
            new PrivacyTraceItem
            {
                Id = "prv_typed_paths",
                Name = "Dosya Gezgini Adres Çubuğu Geçmişi",
                Category = "Windows Gezgini",
                Description = "Windows Gezgini adres çubuğuna elle yazılan klasör ve ağ yolları (TypedPaths)"
            },
            new PrivacyTraceItem
            {
                Id = "prv_wordwheel",
                Name = "Dosya Gezgini Arama Kutusu Geçmişi",
                Category = "Windows Arama",
                Description = "Klasörlerde aranan kelime ve dosya adı geçmişi (WordWheelQuery)"
            },
            new PrivacyTraceItem
            {
                Id = "prv_comdlg32",
                Name = "Aç / Kaydet Diyalog Penceresi Geçmişi (OpenSavePidlMRU)",
                Category = "Uygulama Diyalogları",
                Description = "Uygulamalarda Dosya Aç / Farklı Kaydet pencerelerinde son ziyaret edilen klasörler"
            },
            new PrivacyTraceItem
            {
                Id = "prv_jumplists",
                Name = "Görev Çubuğu & Başlat Menüsü JumpList Geçmişi",
                Category = "Kabuk İzleri",
                Description = "Görev çubuğu simgelerine sağ tıklandığında görünen son açılan dosyalar (Automatic/CustomDestinations)"
            },
            new PrivacyTraceItem
            {
                Id = "prv_activity_cache",
                Name = "Windows Etkinlik Geçmişi (ConnectedDevicesPlatform)",
                Category = "Telemetri & Etkinlik",
                Description = "Windows Timeline ve uygulama kullanım geçmişi veritabanı (ActivityCache.db)"
            },
            new PrivacyTraceItem
            {
                Id = "prv_clipboard",
                Name = "Sistem Panosu (Clipboard) Verileri",
                Category = "Anlık Gizlilik",
                Description = "Bellekte kopyalanmış metin, parola veya görsel pano içeriği"
            }
        ];
    }

    public static async Task ScanPrivacyTracesAsync(IEnumerable<PrivacyTraceItem> items)
    {
        var list = items.ToList();
        await Task.Run(() =>
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            foreach (var item in list)
            {
                int count = 0;
                long bytes = 0;

                switch (item.Id)
                {
                    case "prv_run_mru":
                        count = CountRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
                        break;
                    case "prv_typed_paths":
                        count = CountRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths");
                        break;
                    case "prv_wordwheel":
                        count = CountRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery");
                        break;
                    case "prv_comdlg32":
                        count = CountRegistrySubKeyValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU")
                              + CountRegistrySubKeyValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU");
                        break;
                    case "prv_jumplists":
                        (count, bytes) = CountFolderFiles(Path.Combine(appData, "Microsoft", "Windows", "Recent", "AutomaticDestinations"));
                        var (c2, b2) = CountFolderFiles(Path.Combine(appData, "Microsoft", "Windows", "Recent", "CustomDestinations"));
                        count += c2;
                        bytes += b2;
                        break;
                    case "prv_activity_cache":
                        (count, bytes) = CountFolderFiles(Path.Combine(localAppData, "ConnectedDevicesPlatform"));
                        break;
                    case "prv_clipboard":
                        count = 1;
                        break;
                }

                item.DetectedCount = count;
                item.DetectedBytes = bytes;
                item.StatusText = count > 0 ? $"{count} iz bulundu" : "Temiz";
            }
        });
    }

    public static async Task<(int CleanedTraces, long FreedBytes)> CleanPrivacyTracesAsync(IEnumerable<PrivacyTraceItem> items)
    {
        var selected = items.Where(x => x.IsSelected).ToList();
        int cleanedTotal = 0;
        long freedBytes = 0;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        foreach (var item in selected)
        {
            switch (item.Id)
            {
                case "prv_run_mru":
                    cleanedTotal += ClearRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
                    break;
                case "prv_typed_paths":
                    cleanedTotal += ClearRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths");
                    break;
                case "prv_wordwheel":
                    cleanedTotal += ClearRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery");
                    break;
                case "prv_comdlg32":
                    cleanedTotal += ClearRegistrySubKeys(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU");
                    cleanedTotal += ClearRegistrySubKeys(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU");
                    break;
                case "prv_jumplists":
                    var (c1, b1) = await Task.Run(() => DeleteFolderFiles(Path.Combine(appData, "Microsoft", "Windows", "Recent", "AutomaticDestinations")));
                    var (c2, b2) = await Task.Run(() => DeleteFolderFiles(Path.Combine(appData, "Microsoft", "Windows", "Recent", "CustomDestinations")));
                    cleanedTotal += c1 + c2;
                    freedBytes += b1 + b2;
                    break;
                case "prv_activity_cache":
                    var (ca, ba) = await Task.Run(() => DeleteFolderFiles(Path.Combine(localAppData, "ConnectedDevicesPlatform")));
                    cleanedTotal += ca;
                    freedBytes += ba;
                    break;
                case "prv_clipboard":
                    try
                    {
                        Application.Current?.Dispatcher?.Invoke(Clipboard.Clear);
                        cleanedTotal++;
                    }
                    catch { }
                    break;
            }

            item.DetectedCount = 0;
            item.DetectedBytes = 0;
            item.StatusText = "Temizlendi";
        }

        return (cleanedTotal, freedBytes);
    }

    private static int CountRegistryValues(string subKeyPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            return key?.GetValueNames().Length ?? 0;
        }
        catch { return 0; }
    }

    private static int CountRegistrySubKeyValues(string subKeyPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            if (key is null) return 0;
            int total = key.GetValueNames().Length;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var sk = key.OpenSubKey(sub, writable: false);
                total += sk?.GetValueNames().Length ?? 0;
            }
            return total;
        }
        catch { return 0; }
    }

    private static int ClearRegistryValues(string subKeyPath)
    {
        int count = 0;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
            if (key is null) return 0;
            foreach (string val in key.GetValueNames())
            {
                key.DeleteValue(val, throwOnMissingValue: false);
                count++;
            }
        }
        catch { }
        return count;
    }

    private static int ClearRegistrySubKeys(string subKeyPath)
    {
        int count = 0;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
            if (key is null) return 0;
            foreach (string sub in key.GetSubKeyNames())
            {
                key.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
                count++;
            }
        }
        catch { }
        return count;
    }

    private static (int Count, long Bytes) CountFolderFiles(string folder)
    {
        if (!Directory.Exists(folder)) return (0, 0);
        int count = 0;
        long bytes = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).GetFiles("*", SearchOption.AllDirectories))
            {
                count++;
                bytes += file.Length;
            }
        }
        catch { }
        return (count, bytes);
    }

    private static (int Count, long Bytes) DeleteFolderFiles(string folder)
    {
        if (!Directory.Exists(folder)) return (0, 0);
        int count = 0;
        long bytes = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).GetFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    long len = file.Length;
                    file.Delete();
                    count++;
                    bytes += len;
                }
                catch { }
            }
        }
        catch { }
        return (count, bytes);
    }

    // ==================== ACTIVE NETWORK CONNECTIONS & HOSTS ====================

    public static async Task<List<ActiveConnectionItem>> GetActiveNetworkConnectionsAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<ActiveConnectionItem>();
            var pidToName = new Dictionary<int, string>();

            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try { pidToName[p.Id] = p.ProcessName; } catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    Arguments = "-ano",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(4000);

                    foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = line.Trim();
                        if (!trimmed.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) &&
                            !trimmed.StartsWith("UDP", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            string proto = parts[0];
                            string local = parts[1];
                            string remote = parts[2];
                            string state = proto == "TCP" && parts.Length >= 5 ? parts[3] : "DINLENIYOR";
                            string pidStr = parts[^1];

                            if (int.TryParse(pidStr, out int pid))
                            {
                                string procName = pidToName.GetValueOrDefault(pid, pid == 0 ? "System Idle" : $"PID:{pid}");
                                results.Add(new ActiveConnectionItem
                                {
                                    Protocol = proto,
                                    LocalAddress = local,
                                    RemoteAddress = remote,
                                    State = state,
                                    ProcessId = pid,
                                    ProcessName = procName
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to IPGlobalProperties if netstat blocked
                try
                {
                    var ipProps = IPGlobalProperties.GetIPGlobalProperties();
                    foreach (var tcp in ipProps.GetActiveTcpConnections())
                    {
                        results.Add(new ActiveConnectionItem
                        {
                            Protocol = "TCP",
                            LocalAddress = tcp.LocalEndPoint.ToString(),
                            RemoteAddress = tcp.RemoteEndPoint.ToString(),
                            State = tcp.State.ToString(),
                            ProcessId = 0,
                            ProcessName = "Sistem"
                        });
                    }
                }
                catch { }
            }

            return results
                .OrderByDescending(x => x.State == "ESTABLISHED")
                .ThenBy(x => x.ProcessName)
                .Take(300)
                .ToList();
        });
    }

    public static List<HostsEntryItem> LoadHostsFileEntries()
    {
        var entries = new List<HostsEntryItem>();
        string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
        if (!File.Exists(hostsPath))
            return entries;

        try
        {
            foreach (string rawLine in File.ReadAllLines(hostsPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Pure comment header lines
                if (line.StartsWith('#'))
                {
                    string uncommented = line.TrimStart('#').Trim();
                    string[] tokens = uncommented.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 2 && (tokens[0].Contains('.') || tokens[0].Contains(':')))
                    {
                        entries.Add(new HostsEntryItem
                        {
                            IsEnabled = false,
                            IpAddress = tokens[0],
                            Hostname = tokens[1],
                            Comment = tokens.Length > 2 ? string.Join(" ", tokens.Skip(2)) : "Devre dışı kural"
                        });
                    }
                    continue;
                }

                string comment = string.Empty;
                int hashIdx = line.IndexOf('#');
                if (hashIdx > 0)
                {
                    comment = line[(hashIdx + 1)..].Trim();
                    line = line[..hashIdx].Trim();
                }

                string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    entries.Add(new HostsEntryItem
                    {
                        IsEnabled = true,
                        IpAddress = parts[0],
                        Hostname = parts[1],
                        Comment = comment
                    });
                }
            }
        }
        catch { }

        return entries;
    }

    public static bool AddHostsEntry(string ip, string hostname, string comment, out string message)
    {
        string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
        try
        {
            string backupPath = BackupHostsFile();
            string entryLine = $"\r\n{ip}\t{hostname}\t# {comment} (FreeCleaner)";
            File.AppendAllText(hostsPath, entryLine);
            message = $"'{hostname}' -> {ip} kuralı hosts dosyasına eklendi. Yedek: {backupPath}";
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            message = "Windows hosts dosyasını düzenlemek için uygulamayı Yönetici (Administrator) olarak çalıştırmalısınız.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Hosts yazma hatası: {ex.Message}";
            return false;
        }
    }

    public static string BackupHostsFile()
    {
        string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
        if (!File.Exists(hostsPath))
            throw new FileNotFoundException("Windows hosts dosyası bulunamadı.", hostsPath);

        string backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "FreeCleaner_Backups");
        Directory.CreateDirectory(backupDirectory);

        string backupPath = Path.Combine(
            backupDirectory,
            $"hosts_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.bak");
        File.Copy(hostsPath, backupPath, overwrite: false);
        return backupPath;
    }

    public static string? GetLatestHostsBackup()
    {
        string backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "FreeCleaner_Backups");
        return Directory.Exists(backupDirectory)
            ? Directory.EnumerateFiles(backupDirectory, "hosts_*.bak")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
    }

    public static (bool Success, string Message) RestoreHostsBackup(string backupPath)
    {
        string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
        if (!File.Exists(backupPath))
            return (false, "Hosts yedek dosyası bulunamadı.");

        try
        {
            File.Copy(backupPath, hostsPath, overwrite: true);
            return (true, $"Hosts yedeği geri yüklendi: {backupPath}");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Hosts dosyasını geri yüklemek için uygulamayı Yönetici olarak çalıştırmalısınız.");
        }
        catch (Exception ex)
        {
            return (false, $"Hosts geri yükleme hatası: {ex.Message}");
        }
    }

    // ==================== SCHEDULED TASKS & FOLDER SPACE TREE ====================

    public static async Task<List<ScheduledTaskItem>> GetScheduledTasksAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<ScheduledTaskItem>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"chcp 65001 >nul && schtasks.exe /Query /FO CSV /NH\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(6000);

                    foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] cols = line.Split("\",\"");
                        if (cols.Length >= 3)
                        {
                            string fullTaskPath = cols[0].Trim('"');
                            string nextRun = cols[1].Trim('"');
                            string status = cols[2].Trim('"');

                            if (fullTaskPath.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
                                continue; // Show 3rd party & user tasks first for clarity

                            string name = Path.GetFileName(fullTaskPath);
                            list.Add(new ScheduledTaskItem(status)
                            {
                                TaskName = string.IsNullOrEmpty(name) ? fullTaskPath : name,
                                TaskPath = fullTaskPath,
                                NextRunTime = nextRun,
                                Author = "Kullanıcı / Uygulama"
                            });
                        }
                    }
                }
            }
            catch { }

            return list.OrderBy(x => x.TaskName).Take(200).ToList();
        });
    }

    public static async Task<List<FolderSpaceBarItem>> AnalyzeFolderSpaceBreakdownAsync(string parentFolder)
    {
        return await Task.Run(() =>
        {
            var items = new List<(string Name, string FullPath, long Size)>();
            if (string.IsNullOrWhiteSpace(parentFolder) || !Directory.Exists(parentFolder))
                return [];

            try
            {
                var rootDir = new DirectoryInfo(parentFolder);
                long rootFilesSize = 0;
                try
                {
                    foreach (var f in rootDir.GetFiles())
                        rootFilesSize += f.Length;
                }
                catch { }

                if (rootFilesSize > 0)
                {
                    items.Add(("[Kök Dizindeki Dosyalar]", parentFolder, rootFilesSize));
                }

                foreach (var sub in rootDir.GetDirectories())
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    long subSize = QuickDirectorySize(sub, maxDepth: 3);
                    if (subSize > 0)
                    {
                        items.Add((sub.Name, sub.FullName, subSize));
                    }
                }
            }
            catch { }

            long grandTotal = Math.Max(1, items.Sum(x => x.Size));
            return items
                .OrderByDescending(x => x.Size)
                .Select(x => new FolderSpaceBarItem
                {
                    FolderName = x.Name,
                    FullPath = x.FullPath,
                    SizeBytes = x.Size,
                    PercentageOfParent = (x.Size / (double)grandTotal) * 100.0
                })
                .ToList();
        });
    }

    private static long QuickDirectorySize(DirectoryInfo dir, int maxDepth)
    {
        if (maxDepth < 0) return 0;
        long total = 0;
        try
        {
            foreach (var f in dir.GetFiles())
                total += f.Length;
            if (maxDepth > 0)
            {
                foreach (var sub in dir.GetDirectories())
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                        total += QuickDirectorySize(sub, maxDepth - 1);
                }
            }
        }
        catch { }
        return total;
    }
}
