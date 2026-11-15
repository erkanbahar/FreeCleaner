using System.Diagnostics;
using System.IO;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class UltimateSystemSuiteService
{
    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx", ".vsix", ".bat"
    };

    private static readonly HashSet<string> ArchiveAndDiskImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".iso", ".img", ".cab"
    };

    // ==================== 1. DOWNLOADS & EMPTY FOLDER CLEANER ====================

    public static async Task<List<DownloadOrEmptyItem>> ScanOldInstallersAndArchivesAsync(
        int minAgeDays,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<DownloadOrEmptyItem>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloadsDir = Path.Combine(userProfile, "Downloads");
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            string[] roots = [downloadsDir, desktopDir];
            DateTime cutoff = DateTime.Now.AddDays(-Math.Max(0, minAgeDays));

            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var file in new DirectoryInfo(root).GetFiles("*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (file.LastWriteTime > cutoff) continue;

                        bool isInstaller = InstallerExtensions.Contains(file.Extension);
                        bool isArchive = ArchiveAndDiskImageExtensions.Contains(file.Extension);
                        if (!isInstaller && !isArchive) continue;

                        int ageDays = (int)Math.Max(0, (DateTime.Now - file.LastWriteTime).TotalDays);
                        results.Add(new DownloadOrEmptyItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            ItemType = isInstaller ? $"Kurulum Paketi ({file.Extension})" : $"Arşiv / İmaj ({file.Extension})",
                            SizeBytes = file.Length,
                            LastModified = file.LastWriteTime,
                            AgeDays = ageDays,
                            IsEmptyFolder = false,
                            IsSelected = true
                        });
                    }
                }
                catch { }
            }

            return results.OrderByDescending(x => x.SizeBytes).ToList();
        }, cancellationToken);
    }

    public static async Task<List<DownloadOrEmptyItem>> ScanEmptyDirectoriesAsync(
        string rootFolderPath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var emptyDirs = new List<DownloadOrEmptyItem>();
            if (string.IsNullOrWhiteSpace(rootFolderPath) || !Directory.Exists(rootFolderPath))
                return emptyDirs;

            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(rootFolderPath));

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = stack.Pop();

                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if (current.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    current.Name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                    current.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var files = current.GetFiles();
                    var subDirs = current.GetDirectories();

                    if (files.Length == 0 && subDirs.Length == 0 && !current.FullName.Equals(rootFolderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        int ageDays = (int)Math.Max(0, (DateTime.Now - current.LastWriteTime).TotalDays);
                        emptyDirs.Add(new DownloadOrEmptyItem
                        {
                            Name = current.Name,
                            FullPath = current.FullName,
                            ItemType = "Boş Klasör (Empty Dir)",
                            SizeBytes = 0,
                            LastModified = current.LastWriteTime,
                            AgeDays = ageDays,
                            IsEmptyFolder = true,
                            IsSelected = true
                        });
                    }
                    else
                    {
                        foreach (var sub in subDirs)
                            stack.Push(sub);
                    }
                }
                catch { }
            }

            return emptyDirs.OrderBy(x => x.FullPath).Take(500).ToList();
        }, cancellationToken);
    }

    // ==================== 2. SHELL CONTEXT MENU MANAGER ====================

    public static List<ContextMenuEntryItem> GetContextMenuEntries()
    {
        var list = new List<ContextMenuEntryItem>();
        var scopes = new (string ScopeLabel, string RegSubPath)[]
        {
            ("Tüm Dosyalar (*)", @"*\shellex\ContextMenuHandlers"),
            ("Klasörler (Directory)", @"Directory\shellex\ContextMenuHandlers"),
            ("Masaüstü & Klasör Arka Planı", @"Directory\Background\shellex\ContextMenuHandlers"),
            ("Klasör Komutları (Shell)", @"Directory\shell"),
            ("Arka Plan Komutları (Shell)", @"Directory\Background\shell")
        };

        foreach (var (scopeLabel, subPath) in scopes)
        {
            ReadContextMenuScope(Registry.ClassesRoot, scopeLabel, subPath, list);
        }

        return list.OrderBy(x => x.TargetScope).ThenBy(x => x.Name).ToList();
    }

    private static void ReadContextMenuScope(
        RegistryKey root,
        string scopeLabel,
        string baseSubPath,
        List<ContextMenuEntryItem> target)
    {
        try
        {
            using var baseKey = root.OpenSubKey(baseSubPath, writable: false);
            if (baseKey is null) return;

            foreach (string subName in baseKey.GetSubKeyNames())
            {
                if (string.IsNullOrWhiteSpace(subName)) continue;
                try
                {
                    using var itemKey = baseKey.OpenSubKey(subName, writable: false);
                    string handlerVal = itemKey?.GetValue(null)?.ToString() ?? string.Empty;

                    // If it's a shell command key, check subkey "command"
                    if (string.IsNullOrEmpty(handlerVal) && itemKey is not null)
                    {
                        using var cmdKey = itemKey.OpenSubKey("command", writable: false);
                        handlerVal = cmdKey?.GetValue(null)?.ToString() ?? subName;
                    }

                    target.Add(new ContextMenuEntryItem
                    {
                        Name = subName,
                        TargetScope = scopeLabel,
                        HandlerValue = handlerVal,
                        RegistrySubKeyPath = $"{baseSubPath}\\{subName}",
                        IsSelected = false
                    });
                }
                catch { }
            }
        }
        catch { }
    }

    public static (int DeletedCount, int FailedCount) DeleteContextMenuEntries(IEnumerable<ContextMenuEntryItem> items)
    {
        int deleted = 0;
        int failed = 0;
        foreach (var item in items.Where(x => x.IsSelected))
        {
            try
            {
                Registry.ClassesRoot.DeleteSubKeyTree(item.RegistrySubKeyPath, throwOnMissingSubKey: false);
                deleted++;
            }
            catch
            {
                failed++;
            }
        }
        return (deleted, failed);
    }

    // ==================== 3. WINDOWS SERVICES & TELEMETRY OPTIMIZER ====================

    public static async Task<List<WindowsServiceOptItem>> GetOptimizableServicesAsync()
    {
        return await Task.Run(() =>
        {
            var catalog = new (string SvcName, string Title, string Cat, string Desc)[]
            {
                ("DiagTrack", "Bağlı Kullanıcı Deneyimleri ve Telemetri", "Telemetri & Gizlilik", "Arka planda Microsoft'a tanılama ve kullanım telemetrisi gönderir."),
                ("dmwappushservice", "WAP Push Mesaj Yönlendirme (Telemetri)", "Telemetri & Gizlilik", "Telemetri veri toplama ve yönlendirme yardımcı servisi."),
                ("SysMain", "SysMain (Superfetch)", "Disk & Bellek", "Sık kullanılan uygulamaları RAM'e önceden yükler; eski disklerde yüksek I/O oluşturabilir."),
                ("WSearch", "Windows Search (Dizin Oluşturucu)", "Disk & Arama", "Dosya indeksleme servisi; arka planda disk kullanımı yapabilir."),
                ("Spooler", "Yazdırma Biriktiricisi (Print Spooler)", "Donanım", "Yazıcı kullanmıyorsanız durdurarak bellek kazanabilirsiniz."),
                ("Fax", "Windows Faks Servisi", "Donanım", "Eski faks gönderme/alma servisi."),
                ("XblAuthManager", "Xbox Live Kimlik Doğrulama Yöneticisi", "Oyun (Xbox)", "PC'de Xbox Live servislerini kullanmıyorsanız durdurulabilir."),
                ("XblGameSave", "Xbox Live Oyun Kaydetme", "Oyun (Xbox)", "Xbox bulut kayıt senkronizasyon servisi."),
                ("XboxNetApiSvc", "Xbox Live Ağ Servisi", "Oyun (Xbox)", "Xbox Live çok oyunculu ağ servisi."),
                ("MapsBroker", "İndirilen Haritalar Yöneticisi", "Uygulama", "Çevrimdışı Bing haritalar arka plan servisi."),
                ("WerSvc", "Windows Hata Raporlama Servisi", "Tanılama", "Uygulama çökmelerinde hata dökümü ve rapor gönderir.")
            };

            var results = new List<WindowsServiceOptItem>();
            foreach (var (svcName, title, cat, desc) in catalog)
            {
                var (status, startType) = QueryServiceState(svcName);
                results.Add(new WindowsServiceOptItem
                {
                    ServiceName = svcName,
                    DisplayName = title,
                    Category = cat,
                    Description = desc,
                    StartType = startType,
                    Status = status
                });
            }

            return results;
        });
    }

    private static (string Status, string StartType) QueryServiceState(string serviceName)
    {
        string startType = "Bilinmiyor";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
            if (key is null)
                return ("Kurulu Değil", "-");

            int startVal = Convert.ToInt32(key.GetValue("Start", -1));
            startType = startVal switch
            {
                2 => "Otomatik",
                3 => "Elle (Manual)",
                4 => "Devre Dışı",
                _ => "Sistem"
            };
        }
        catch { }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{serviceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);
                if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                    return ("Çalışıyor (Running)", startType);
                if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                    return ("Durduruldu (Stopped)", startType);
            }
        }
        catch { }

        return ("Bilinmiyor", startType);
    }

    public static async Task<(bool Success, string Message)> ControlWindowsServiceAsync(string serviceName, bool start)
    {
        return await Task.Run(() =>
        {
            try
            {
                string action = start ? "start" : "stop";
                if (IsRunningAsAdministrator())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"{action} \"{serviceName}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc is null)
                        return (false, "sc.exe başlatılamadı.");

                    string outText = proc.StandardOutput.ReadToEnd() + " " + proc.StandardError.ReadToEnd();
                    proc.WaitForExit(5000);
                    return (proc.ExitCode == 0, $"Servis komutu ({action} {serviceName}) uygulandı.");
                }
                else
                {
                    var elevatedPsi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"{action} \"{serviceName}\"",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var proc = Process.Start(elevatedPsi);
                    proc?.WaitForExit(5000);
                    return (true, $"Servis komutu ({action} {serviceName}) yönetici yetkisiyle gönderildi.");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    // ==================== 4. S.M.A.R.T. DISK HEALTH & POWER PLANS ====================

    public static async Task<List<PhysicalDiskHealthItem>> GetPhysicalDisksHealthAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<PhysicalDiskHealthItem>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-PhysicalDisk | Select-Object FriendlyName,MediaType,HealthStatus,OperationalStatus,Size,BusType | ConvertTo-Json -Compress\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string json = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(5000);

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var elem in doc.RootElement.EnumerateArray())
                                ParseDiskElement(elem, list);
                        }
                        else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            ParseDiskElement(doc.RootElement, list);
                        }
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                // Fallback to DriveInfo
                foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
                {
                    list.Add(new PhysicalDiskHealthItem
                    {
                        FriendlyName = $"Mantıksal Sürücü ({d.Name})",
                        MediaType = d.DriveType.ToString(),
                        HealthStatus = "Sağlıklı (Healthy)",
                        OperationalStatus = "OK",
                        SizeFormatted = CleanerRule.FormatBytes(d.TotalSize),
                        BusType = d.DriveFormat
                    });
                }
            }

            return list;
        });

        static void ParseDiskElement(System.Text.Json.JsonElement elem, List<PhysicalDiskHealthItem> target)
        {
            string name = elem.TryGetProperty("FriendlyName", out var pName) ? pName.ToString() : "Disk";
            string media = elem.TryGetProperty("MediaType", out var pMedia) ? pMedia.ToString() : "SSD/HDD";
            string health = elem.TryGetProperty("HealthStatus", out var pHealth) ? pHealth.ToString() : "Healthy";
            string op = elem.TryGetProperty("OperationalStatus", out var pOp) ? pOp.ToString() : "OK";
            string bus = elem.TryGetProperty("BusType", out var pBus) ? pBus.ToString() : "NVMe/SATA";
            long size = elem.TryGetProperty("Size", out var pSize) && pSize.TryGetInt64(out long s) ? s : 0;

            target.Add(new PhysicalDiskHealthItem
            {
                FriendlyName = name,
                MediaType = media,
                HealthStatus = health == "Healthy" ? "Sağlıklı (Healthy)" : health,
                OperationalStatus = op,
                SizeFormatted = CleanerRule.FormatBytes(size),
                BusType = bus
            });
        }
    }

    public static async Task<List<PowerPlanItem>> GetPowerPlansAsync()
    {
        return await Task.Run(() =>
        {
            var plans = new List<PowerPlanItem>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/list",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        int guidIdx = line.IndexOf("GUID:", StringComparison.OrdinalIgnoreCase);
                        if (guidIdx < 0) continue;

                        string afterGuid = line[(guidIdx + 5)..].Trim();
                        string[] parts = afterGuid.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0) continue;

                        string guid = parts[0].Trim();
                        bool isActive = line.TrimEnd().EndsWith('*');

                        int openParen = line.IndexOf('(');
                        int closeParen = line.LastIndexOf(')');
                        string planName = (openParen > 0 && closeParen > openParen)
                            ? line[(openParen + 1)..closeParen]
                            : guid;

                        plans.Add(new PowerPlanItem
                        {
                            Guid = guid,
                            Name = planName,
                            IsActive = isActive
                        });
                    }
                }
            }
            catch { }

            return plans;
        });
    }

    public static async Task<bool> SetActivePowerPlanAsync(string planGuid)
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = $"/setactive {planGuid}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public static async Task<bool> UnlockUltimatePerformancePlanAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    // ==================== 5. SYSTEM REPAIR CENTER (DISM / SFC / WINDOWS.OLD / FONTCACHE) ====================

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RestartAppAsAdministrator()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> RunDismCheckHealthAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (IsRunningAsAdministrator())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dism.exe",
                        Arguments = "/Online /Cleanup-Image /CheckHealth",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc is null) return "DISM başlatılamadı.";
                    string output = proc.StandardOutput.ReadToEnd() + " " + proc.StandardError.ReadToEnd();
                    proc.WaitForExit(15000);
                    return output.Replace("\r\n", " ").Trim();
                }

                // If the app itself is not running as Admin, execute DISM via elevated UAC prompt and capture output to temp file
                string tempOutFile = Path.Combine(Path.GetTempPath(), $"FreeCleaner_DISM_{Guid.NewGuid():N}.txt");
                try
                {
                    var elevatedPsi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c dism.exe /Online /Cleanup-Image /CheckHealth > \"{tempOutFile}\" 2>&1",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using var elevatedProc = Process.Start(elevatedPsi);
                    if (elevatedProc is null)
                        return "Yönetici izni (UAC) alınamadı.";

                    elevatedProc.WaitForExit(20000);

                    if (File.Exists(tempOutFile))
                    {
                        string resultText = File.ReadAllText(tempOutFile).Replace("\r\n", " ").Trim();
                        if (!string.IsNullOrWhiteSpace(resultText))
                            return resultText;
                    }
                    return "DISM CheckHealth yönetici yetkisiyle tamamlandı.";
                }
                finally
                {
                    if (File.Exists(tempOutFile))
                    {
                        try { File.Delete(tempOutFile); } catch { }
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "Kullanıcı UAC (Yönetici İzni) onayını iptal etti.";
            }
            catch (Exception ex)
            {
                return $"Hata: {ex.Message}";
            }
        });
    }

    public static void LaunchElevatedSfcScannow()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k echo FreeCleaner Sistem Dosyasi Onarici (SFC /SCANNOW) && sfc /scannow",
                Verb = "runas",
                UseShellExecute = true
            });
        }
        catch { }
    }

    public static async Task<(long WindowsOldBytes, long FontCacheBytes)> CheckWindowsOldAndFontCacheAsync()
    {
        return await Task.Run(() =>
        {
            long winOldBytes = 0;
            if (Directory.Exists(@"C:\Windows.old"))
            {
                try
                {
                    foreach (var f in new DirectoryInfo(@"C:\Windows.old").GetFiles("*", SearchOption.TopDirectoryOnly))
                        winOldBytes += f.Length;
                }
                catch { }
            }

            long fontCacheBytes = 0;
            string fontCacheDir = @"C:\Windows\ServiceProfiles\LocalService\AppData\Local\FontCache";

            if (Directory.Exists(fontCacheDir))
            {
                try
                {
                    foreach (var f in new DirectoryInfo(fontCacheDir).GetFiles("*.dat", SearchOption.TopDirectoryOnly))
                        fontCacheBytes += f.Length;
                }
                catch { }
            }

            return (winOldBytes, fontCacheBytes);
        });
    }

    public static async Task<(int DeletedFiles, long FreedBytes)> CleanFontCacheAndWindowsOldAsync()
    {
        return await Task.Run(() =>
        {
            int deleted = 0;
            long freed = 0;

            string fontCacheDir = @"C:\Windows\ServiceProfiles\LocalService\AppData\Local\FontCache";

            if (Directory.Exists(fontCacheDir))
            {
                try
                {
                    foreach (var f in new DirectoryInfo(fontCacheDir).GetFiles("*FontCache*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            long len = f.Length;
                            f.Delete();
                            deleted++;
                            freed += len;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            string localFontCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "FontCache");

            if (Directory.Exists(localFontCache))
            {
                try
                {
                    foreach (var f in new DirectoryInfo(localFontCache).GetFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            long len = f.Length;
                            f.Delete();
                            deleted++;
                            freed += len;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return (deleted, freed);
        });
    }
}
