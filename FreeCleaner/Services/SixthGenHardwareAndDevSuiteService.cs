using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class SixthGenHardwareAndDevSuiteService
{
    private static readonly HashSet<int> CommonDevPorts =
    [
        80, 443, 1433, 1521, 3000, 3001, 3306, 4200, 5000, 5001,
        5173, 5174, 5432, 5672, 6379, 7000, 8000, 8080, 8081, 8443,
        8888, 9000, 9090, 9200, 11434, 27017
    ];

    // ==================== 1. DEVELOPER PORT HUNTER & KILLER ====================

    public static async Task<List<ListeningPortItem>> GetListeningPortsAsync(string? filterText = null)
    {
        return await Task.Run(() =>
        {
            var results = new List<ListeningPortItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    Arguments = "-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc is null) return results;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                var pidNameCache = new Dictionary<int, string>();
                foreach (var p in Process.GetProcesses())
                {
                    try { pidNameCache[p.Id] = p.ProcessName; } catch { }
                    finally { p.Dispose(); }
                }

                foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = rawLine.Trim();
                    if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("UDP", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    string proto = parts[0].ToUpperInvariant();
                    string localEndpoint = parts[1];

                    string state = proto == "TCP" && parts.Length >= 5 ? parts[3] : "BOUND";
                    if (proto == "TCP" && !state.Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string pidStr = parts[^1];
                    if (!int.TryParse(pidStr, out int pid) || pid <= 4)
                        continue;

                    int lastColon = localEndpoint.LastIndexOf(':');
                    if (lastColon <= 0) continue;

                    string ipPart = localEndpoint[..lastColon];
                    string portPart = localEndpoint[(lastColon + 1)..];
                    if (!int.TryParse(portPart, out int port)) continue;

                    string dedupKey = $"{proto}:{port}:{pid}";
                    if (!seen.Add(dedupKey)) continue;

                    string procName = pidNameCache.TryGetValue(pid, out var name) ? $"{name}.exe" : $"PID {pid}";
                    bool isDevPort = CommonDevPorts.Contains(port);
                    string badge = isDevPort ? "⚡ Geliştirici / Sunucu Portu" : (proto == "TCP" ? "TCP Dinlemede (LISTENING)" : "UDP Portu");

                    if (!string.IsNullOrWhiteSpace(filterText))
                    {
                        string f = filterText.Trim();
                        if (!port.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) &&
                            !procName.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                            !pid.ToString().Contains(f, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    results.Add(new ListeningPortItem
                    {
                        Protocol = proto,
                        LocalAddress = ipPart,
                        PortNumber = port,
                        ProcessId = pid,
                        ProcessName = procName,
                        PortBadge = badge
                    });
                }
            }
            catch { }

            return results
                .OrderByDescending(x => CommonDevPorts.Contains(x.PortNumber))
                .ThenBy(x => x.PortNumber)
                .ToList();
        });
    }

    public static bool KillProcessByPortPid(int pid, int port, out string message)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
            message = $"Port {port} serbest bırakıldı! ('{name}' - PID {pid} sonlandırıldı).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Port {port} (PID: {pid}) sonlandırılamadı: {ex.Message}";
            return false;
        }
    }

    // ==================== 2. WINDOWS 10/11 APPX BLOATWARE DEBLOATER ====================

    private static readonly string[] KnownBloatwareKeywords =
    [
        "Clipchamp", "BingNews", "BingWeather", "BingFinance", "BingSports",
        "ZuneVideo", "ZuneMusic", "WindowsMaps", "MicrosoftSolitaireCollection",
        "People", "GetHelp", "GetStarted", "WindowsFeedbackHub", "YourPhone",
        "MixedReality", "Microsoft3DViewer", "SkypeApp", "Todos", "WindowsAlarms",
        "SoundRecorder", "GamingApp", "XboxGamingOverlay", "XboxTCUI",
        "XboxSpeechToTextOverlay", "PowerAutomateDesktop", "MicrosoftStickyNotes"
    ];

    public static async Task<List<AppxBloatwareItem>> ScanAppxBloatwareAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<AppxBloatwareItem>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Get-AppxPackage | Where-Object { $_.IsFramework -eq $false -and $_.NonRemovable -eq $false } | ForEach-Object { \\\"$($_.Name)|$($_.PackageFullName)|$($_.Version)|$($_.InstallLocation)\\\" }\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc is null) return results;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(12000);

                foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 4) continue;

                    string name = parts[0].Trim();
                    string fullName = parts[1].Trim();
                    string version = parts[2].Trim();
                    string location = parts[3].Trim();

                    bool isKnownBloat = KnownBloatwareKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

                    results.Add(new AppxBloatwareItem
                    {
                        Name = name,
                        PackageFullName = fullName,
                        Version = version,
                        InstallLocation = string.IsNullOrWhiteSpace(location) ? "Sistem Mağaza Dizini" : location,
                        CategoryBadge = isKnownBloat ? "🧹 Önerilen Bloatware" : "Mağaza Uygulaması (UWP)",
                        IsSelected = isKnownBloat
                    });
                }
            }
            catch { }

            return results
                .OrderByDescending(x => x.IsSelected)
                .ThenBy(x => x.Name)
                .ToList();
        });
    }

    public static async Task<int> RemoveSelectedAppxPackagesAsync(IEnumerable<AppxBloatwareItem> items)
    {
        return await Task.Run(() =>
        {
            int removed = 0;
            foreach (var item in items.Where(x => x.IsSelected))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"Remove-AppxPackage -Package '{item.PackageFullName}' -ErrorAction Stop\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(10000);
                    if (proc?.ExitCode == 0)
                        removed++;
                }
                catch { }
            }
            return removed;
        });
    }

    // ==================== 3. LIVE SSD / NVME SEQUENTIAL READ & WRITE BENCHMARK ====================

    public static async Task<List<BenchmarkDriveOption>> GetAvailableBenchmarkDrivesAsync()
    {
        return await Task.Run(() =>
        {
            var options = new List<BenchmarkDriveOption>();
            var diskModelByLetter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Map drive letter -> Physical Disk FriendlyName & MediaType via PowerShell
            try
            {
                string ps = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Get-Partition | Where-Object { $_.DriveLetter } | ForEach-Object {
    $letter = ""$($_.DriveLetter):\""
    $disk = Get-PhysicalDisk -DeviceNumber $_.DiskNumber -ErrorAction SilentlyContinue
    if ($disk) {
        Write-Output ""$letter|$($disk.FriendlyName) ($($disk.MediaType))""
    }
}";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{ps.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 2)
                            diskModelByLetter[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            catch { }

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;

                    string root = drive.Name; // e.g. "C:\"
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Yerel Disk" : drive.VolumeLabel;
                    string freeStr = CleanerRule.FormatBytes(drive.AvailableFreeSpace);
                    string totalStr = CleanerRule.FormatBytes(drive.TotalSize);
                    string hwModel = diskModelByLetter.TryGetValue(root, out var model)
                        ? $" • {model}"
                        : $" • {drive.DriveFormat}";

                    options.Add(new BenchmarkDriveOption
                    {
                        DriveRoot = root,
                        DisplayTitle = $"{root} [{label}]{hwModel} ({freeStr} Boş / {totalStr})"
                    });
                }
                catch { }
            }

            if (options.Count == 0)
            {
                options.Add(new BenchmarkDriveOption
                {
                    DriveRoot = @"C:\",
                    DisplayTitle = @"C:\ [Sistem Sürücüsü]"
                });
            }

            return options;
        });
    }

    public static async Task<DiskBenchmarkResultItem> RunDiskBenchmarkAsync(string driveRoot = @"C:\", int blockSizeMb = 128)
    {
        return await Task.Run(() =>
        {
            string root = Path.GetPathRoot(driveRoot) ?? @"C:\";
            string tempBenchFile = ResolveWritableBenchFilePath(root);

            int totalBytes = blockSizeMb * 1024 * 1024;
            byte[] buffer = new byte[4 * 1024 * 1024]; // 4 MB chunks
            RandomNumberGenerator.Fill(buffer.AsSpan(0, 65536));

            double writeMbps = 0;
            double readMbps = 0;

            try
            {
                // 1. Sequential Write Test (WriteThrough bypasses OS write cache)
                var swWrite = Stopwatch.StartNew();
                using (var fsWrite = new FileStream(
                    tempBenchFile,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    buffer.Length,
                    FileOptions.WriteThrough | FileOptions.SequentialScan))
                {
                    int written = 0;
                    while (written < totalBytes)
                    {
                        int toWrite = Math.Min(buffer.Length, totalBytes - written);
                        fsWrite.Write(buffer, 0, toWrite);
                        written += toWrite;
                    }
                    fsWrite.Flush(flushToDisk: true);
                }
                swWrite.Stop();
                double writeSec = Math.Max(0.001, swWrite.Elapsed.TotalSeconds);
                writeMbps = blockSizeMb / writeSec;

                // 2. Sequential Read Test
                var swRead = Stopwatch.StartNew();
                using (var fsRead = new FileStream(
                    tempBenchFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    buffer.Length,
                    FileOptions.SequentialScan))
                {
                    while (fsRead.Read(buffer, 0, buffer.Length) > 0)
                    {
                        // Sequential read
                    }
                }
                swRead.Stop();
                double readSec = Math.Max(0.001, swRead.Elapsed.TotalSeconds);
                readMbps = blockSizeMb / readSec;
            }
            finally
            {
                try { if (File.Exists(tempBenchFile)) File.Delete(tempBenchFile); } catch { }
            }

            string grade = writeMbps switch
            {
                >= 2000 => "🚀 Ultra Hızlı PCIe Gen4/Gen5 NVMe SSD",
                >= 900 => "⚡ Yüksek Performanslı NVMe M.2 SSD",
                >= 350 => "✅ Sağlıklı SATA III / NVMe SSD",
                >= 120 => "ℹ️ Standart SSD / Hızlı HDD",
                _ => "⚠️ Düşük Disk Yazma Hızı (Yoğunluk / HDD)"
            };

            return new DiskBenchmarkResultItem
            {
                DriveName = root,
                TestBlockSizeMb = blockSizeMb,
                WriteSpeedMbps = writeMbps,
                ReadSpeedMbps = readMbps,
                PerformanceGrade = grade,
                TestedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
            };
        });
    }

    private static string ResolveWritableBenchFilePath(string root)
    {
        string fileName = $"FreeCleaner_DiskBench_{Guid.NewGuid():N}.tmp";

        // 1. If root matches OS temp drive (typically C:\), use user Temp folder on that drive
        string sysTemp = Path.GetTempPath();
        if (string.Equals(Path.GetPathRoot(sysTemp), root, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(sysTemp, fileName);
        }

        // 2. For secondary SSDs/drives (D:\, E:\, etc.), try root or a temporary subfolder on that exact drive
        try
        {
            string directPath = Path.Combine(root, fileName);
            using (File.Create(directPath, 4096, FileOptions.DeleteOnClose)) { }
            return directPath;
        }
        catch { }

        // 3. Try first writable top-level directory on that drive
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('$') || dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    string testPath = Path.Combine(dir, fileName);
                    using (File.Create(testPath, 4096, FileOptions.DeleteOnClose)) { }
                    return testPath;
                }
                catch { }
            }
        }
        catch { }

        return Path.Combine(sysTemp, fileName);
    }

    // ==================== 4. DEEP HARDWARE INVENTORY, BIOS & OEM PRODUCT KEY ====================

    public static async Task<List<HardwareSpecItem>> GetHardwareAndLicenseInventoryAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<HardwareSpecItem>();

            // 1. CPU from Registry + Environment
            try
            {
                using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                string cpuName = cpuKey?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Bilinmeyen İşlemci";
                string vendor = cpuKey?.GetValue("VendorIdentifier")?.ToString()?.Trim() ?? "";
                string mhz = cpuKey?.GetValue("~MHz")?.ToString() ?? "";

                list.Add(new HardwareSpecItem
                {
                    ComponentCategory = "İşlemci (CPU)",
                    ComponentName = cpuName,
                    PrimaryValue = $"{Environment.ProcessorCount} Mantıksal Çekirdek ({mhz} MHz)",
                    SecondaryDetails = $"Üretici Kimliği: {vendor} | Mimari: {Environment.OSVersion.Platform}"
                });
            }
            catch { }

            // 2. Motherboard & BIOS from Registry
            try
            {
                using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                string boardVendor = biosKey?.GetValue("BaseBoardManufacturer")?.ToString() ?? "";
                string boardProduct = biosKey?.GetValue("BaseBoardProduct")?.ToString() ?? "";
                string biosVendor = biosKey?.GetValue("BIOSVendor")?.ToString() ?? "";
                string biosVer = biosKey?.GetValue("BIOSVersion")?.ToString() ?? "";
                string biosDate = biosKey?.GetValue("BIOSReleaseDate")?.ToString() ?? "";

                list.Add(new HardwareSpecItem
                {
                    ComponentCategory = "Anakart & BIOS",
                    ComponentName = $"{boardVendor} {boardProduct}".Trim(),
                    PrimaryValue = $"BIOS: {biosVer} ({biosDate})",
                    SecondaryDetails = $"BIOS Üreticisi: {biosVendor}"
                });
            }
            catch { }

            // 3. GPU, RAM Modules & OEM Product Key via PowerShell CIM query
            try
            {
                string psScript = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$gpu = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Select-Object -First 2
foreach ($g in $gpu) { Write-Output ""GPU|$($g.Name)|$($g.DriverVersion)|$($g.CurrentHorizontalResolution)x$($g.CurrentVerticalResolution)"" }
$rams = Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue
foreach ($r in $rams) {
    $gb = [math]::Round($r.Capacity / 1GB, 1)
    Write-Output ""RAM|$($r.Manufacturer) $($r.PartNumber)|$gb GB @ $($r.Speed) MHz|Slot: $($r.DeviceLocator)""
}
$sls = Get-CimInstance SoftwareLicensingService -ErrorAction SilentlyContinue
if ($sls -and $sls.OA3xOriginalProductKey) {
    Write-Output ""KEY|OEM|$($sls.OA3xOriginalProductKey)|OA3x""
}
";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{psScript.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(8000);

                    foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 4) continue;

                        string typeTag = parts[0].Trim();
                        if (typeTag == "GPU")
                        {
                            string res = parts[3].Trim();
                            if (res == "x" || string.IsNullOrWhiteSpace(res))
                                res = "Dinamik / Harici GPU";

                            list.Add(new HardwareSpecItem
                            {
                                ComponentCategory = "Ekran Kartı (GPU)",
                                ComponentName = parts[1].Trim(),
                                PrimaryValue = $"Sürücü: {parts[2].Trim()}",
                                SecondaryDetails = $"Çözünürlük: {res}"
                            });
                        }
                        else if (typeTag == "RAM")
                        {
                            list.Add(new HardwareSpecItem
                            {
                                ComponentCategory = "Fiziksel Bellek (RAM Modülü)",
                                ComponentName = parts[1].Trim(),
                                PrimaryValue = parts[2].Trim(),
                                SecondaryDetails = parts[3].Trim()
                            });
                        }
                        else if (typeTag == "KEY")
                        {
                            list.Add(new HardwareSpecItem
                            {
                                ComponentCategory = "Windows OEM Ürün Anahtarı",
                                ComponentName = "Anakart Gömülü OEM Windows Anahtarı",
                                PrimaryValue = parts[2].Trim(),
                                SecondaryDetails = "BIOS MSDM Tablosu (OA3x)"
                            });
                        }
                    }
                }
            }
            catch { }

            // 4. Backup Product Key from Registry if OA3x wasn't present
            try
            {
                using var ntKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                string edition = ntKey?.GetValue("ProductName")?.ToString() ?? "Windows";
                string displayVer = ntKey?.GetValue("DisplayVersion")?.ToString() ?? "";
                string build = ntKey?.GetValue("CurrentBuild")?.ToString() ?? "";
                if (int.TryParse(build, out int buildNum) && buildNum >= 22000 && edition.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                {
                    edition = edition.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
                }

                using var sppKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform");
                string backupKey = sppKey?.GetValue("BackupProductKeyDefault")?.ToString() ?? "Dijital Lisans / Bulunamadı";

                list.Add(new HardwareSpecItem
                {
                    ComponentCategory = "Windows Lisans & Sürüm Kimliği",
                    ComponentName = $"{edition} ({displayVer} - Build {build})",
                    PrimaryValue = $"Kayıtlı Anahtar: {backupKey}",
                    SecondaryDetails = $"Makine Adı: {Environment.MachineName} | Kullanıcı: {Environment.UserName}"
                });
            }
            catch { }

            return list;
        });
    }

    // ==================== 5. USB STORAGE FORENSICS (USBSTOR) & TRACE CLEANER ====================

    public static async Task<List<UsbHistoryItem>> ScanUsbStorageHistoryAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<UsbHistoryItem>();
            const string usbStorPath = @"SYSTEM\CurrentControlSet\Enum\USBSTOR";

            try
            {
                using var usbStorKey = Registry.LocalMachine.OpenSubKey(usbStorPath);
                if (usbStorKey is null) return results;

                foreach (var deviceTypeKeyName in usbStorKey.GetSubKeyNames())
                {
                    try
                    {
                        using var deviceTypeKey = usbStorKey.OpenSubKey(deviceTypeKeyName);
                        if (deviceTypeKey is null) continue;

                        foreach (var serialKeyName in deviceTypeKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var instanceKey = deviceTypeKey.OpenSubKey(serialKeyName);
                                if (instanceKey is null) continue;

                                string friendly = instanceKey.GetValue("FriendlyName")?.ToString()
                                                  ?? instanceKey.GetValue("DeviceDesc")?.ToString()
                                                  ?? deviceTypeKeyName;
                                if (friendly.Contains(';'))
                                    friendly = friendly[(friendly.LastIndexOf(';') + 1)..];

                                results.Add(new UsbHistoryItem
                                {
                                    FriendlyName = friendly.Trim(),
                                    VendorAndProduct = deviceTypeKeyName.Replace("Disk&Ven_", "").Replace("&Prod_", " ").Replace("&Rev_", " Rev:"),
                                    SerialNumber = serialKeyName,
                                    RegistrySubKeyPath = $@"{usbStorPath}\{deviceTypeKeyName}\{serialKeyName}",
                                    IsSelected = false
                                });
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return results;
        });
    }

    public static int CleanSelectedUsbHistory(IEnumerable<UsbHistoryItem> items)
    {
        int deleted = 0;
        foreach (var item in items.Where(x => x.IsSelected))
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(item.RegistrySubKeyPath, throwOnMissingSubKey: false);
                deleted++;
            }
            catch { }
        }
        return deleted;
    }
}
