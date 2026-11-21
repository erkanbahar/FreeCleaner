using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public partial class MainViewModel
{
    // ==================== 8. DEVCLEANER & GAMER CACHES ====================

    public string DevWorkspacePath
    {
        get => _devWorkspacePath;
        set
        {
            if (_devWorkspacePath != value)
            {
                _devWorkspacePath = value;
                OnPropertyChanged();
            }
        }
    }

    public bool VsIncludeDebug
    {
        get => _vsIncludeDebug;
        set
        {
            if (_vsIncludeDebug != value)
            {
                _vsIncludeDebug = value;
                OnPropertyChanged();
            }
        }
    }

    public bool VsIncludeRetailAndRelease
    {
        get => _vsIncludeRetailAndRelease;
        set
        {
            if (_vsIncludeRetailAndRelease != value)
            {
                _vsIncludeRetailAndRelease = value;
                OnPropertyChanged();
            }
        }
    }

    public bool VsIncludeObjAndVs
    {
        get => _vsIncludeObjAndVs;
        set
        {
            if (_vsIncludeObjAndVs != value)
            {
                _vsIncludeObjAndVs = value;
                OnPropertyChanged();
            }
        }
    }

    private async Task RunScanVsDebugRetailAsync(bool autoDeleteAfterScan)
    {
        IsBusy = true;
        ProgressPercent = 20;
        DevArtifactItems.Clear();

        StatusMessage = $"'{DevWorkspacePath}' içindeki Visual Studio projelerinin Debug / Retail / Release klasörleri taranıyor...";
        AddLog($"[VS TEMİZLEYİCİ] Visual Studio Debug & Retail klasörleri taranıyor: {DevWorkspacePath}");

        _cts = new CancellationTokenSource();
        var progress = new Progress<(string CurrentDir, int FoundCount)>(rep =>
        {
            CurrentOperationDetail = $"Bulunan VS Klasörü: {rep.FoundCount} • {rep.CurrentDir}";
        });

        try
        {
            var found = await DevAndGamerCleanerService.ScanVisualStudioDebugAndRetailAsync(
                DevWorkspacePath,
                VsIncludeDebug,
                VsIncludeRetailAndRelease,
                VsIncludeObjAndVs,
                progress,
                _cts.Token);

            foreach (var item in found)
                DevArtifactItems.Add(item);

            ProgressPercent = 100;
            long totalSize = found.Sum(x => x.SizeBytes);

            if (found.Count == 0)
            {
                StatusMessage = "Visual Studio Debug / Retail klasörü bulunamadı (Projeler zaten temiz).";
                AddLog("[VS TEMİZLEYİCİ] Silinecek Debug/Retail klasörü bulunamadı.");
                return;
            }

            StatusMessage = $"VS Temizleyici: {found.Count} adet Debug / Retail / obj klasörü bulundu ({CleanerRule.FormatBytes(totalSize)}).";
            AddLog($"[VS TEMİZLEYİCİ] {found.Count} VS çıktı klasörü ({CleanerRule.FormatBytes(totalSize)}) tespit edildi.");

            if (autoDeleteAfterScan)
            {
                int deleted = 0;
                long freed = 0;

                await Task.Run(() =>
                {
                    foreach (var item in found)
                    {
                        try
                        {
                            if (Directory.Exists(item.FullPath))
                            {
                                Directory.Delete(item.FullPath, recursive: true);
                                deleted++;
                                freed += item.SizeBytes;
                            }
                        }
                        catch
                        {
                            // Skip locked active build folder
                        }
                    }
                });

                DevArtifactItems.Clear();
                LastFreedBytes += freed;
                LastDeletedFiles += deleted;
                RefreshSystemMetrics();

                StatusMessage = $"VS Otomatik Temizlik Tamamlandı! {deleted} adet Debug/Retail klasörü silindi, {CleanerRule.FormatBytes(freed)} yer açıldı.";
                CurrentOperationDetail = $"Temizlenen kök dizin: {DevWorkspacePath}";
                AddLog($"[VS OTOMATİK SİLME] {deleted} adet Visual Studio Debug/Retail klasörü otomatik silindi ({CleanerRule.FormatBytes(freed)} kazanıldı).");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Visual Studio Debug/Retail taraması durduruldu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public int ShredPassesCount
    {
        get => _shredPassesCount;
        set
        {
            if (_shredPassesCount != value)
            {
                _shredPassesCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int FreeSpaceWipeMb
    {
        get => _freeSpaceWipeMb;
        set
        {
            if (_freeSpaceWipeMb != value)
            {
                _freeSpaceWipeMb = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewHostIp
    {
        get => _newHostIp;
        set
        {
            if (_newHostIp != value)
            {
                _newHostIp = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewHostDomain
    {
        get => _newHostDomain;
        set
        {
            if (_newHostDomain != value)
            {
                _newHostDomain = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewHostComment
    {
        get => _newHostComment;
        set
        {
            if (_newHostComment != value)
            {
                _newHostComment = value;
                OnPropertyChanged();
            }
        }
    }

    public string SpaceTreeTargetFolder
    {
        get => _spaceTreeTargetFolder;
        set
        {
            if (_spaceTreeTargetFolder != value)
            {
                _spaceTreeTargetFolder = value;
                OnPropertyChanged();
            }
        }
    }

    private void BrowseDevWorkspace()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Yazılım Projelerinin Bulunduğu Klasörü Seçin (node_modules, bin/obj, .venv taraması)",
            InitialDirectory = Directory.Exists(DevWorkspacePath) ? DevWorkspacePath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dlg.ShowDialog() == true)
        {
            DevWorkspacePath = dlg.FolderName;
        }
    }

    private async Task RunScanDevProjectsAsync()
    {
        IsBusy = true;
        ProgressPercent = 20;
        DevArtifactItems.Clear();
        StatusMessage = $"'{DevWorkspacePath}' içindeki yazılım projelerinin derleme/paket klasörleri taranıyor...";
        AddLog($"[DEVCLEANER] Proje klasörü taranıyor: {DevWorkspacePath}");

        _cts = new CancellationTokenSource();
        var progress = new Progress<(string CurrentDir, int FoundCount)>(rep =>
        {
            CurrentOperationDetail = $"Bulunan: {rep.FoundCount} • {rep.CurrentDir}";
        });

        try
        {
            var found = await DevAndGamerCleanerService.ScanWorkspaceArtifactsAsync(DevWorkspacePath, progress, _cts.Token);
            foreach (var item in found)
                DevArtifactItems.Add(item);

            ProgressPercent = 100;
            long totalSize = found.Sum(x => x.SizeBytes);
            StatusMessage = $"DevCleaner: {found.Count} derleme/paket klasörü bulundu (Toplam {CleanerRule.FormatBytes(totalSize)}).";
            AddLog($"[DEVCLEANER] {found.Count} klasör ({CleanerRule.FormatBytes(totalSize)}) tespit edildi.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "DevCleaner taraması iptal edildi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunScanGamerCachesAsync()
    {
        IsBusy = true;
        ProgressPercent = 30;
        DevArtifactItems.Clear();
        StatusMessage = "GPU Shader (NVIDIA/AMD), Steam, Epic, Pip, Yarn, Gradle ve Cargo önbellekleri taranıyor...";
        AddLog("[GAMER & GLOBAL CACHE] Oyun ve paket yöneticisi önbellekleri taranıyor...");

        _cts = new CancellationTokenSource();
        try
        {
            var found = await DevAndGamerCleanerService.ScanGlobalDevAndGamerCachesAsync(_cts.Token);
            foreach (var item in found)
                DevArtifactItems.Add(item);

            ProgressPercent = 100;
            long totalSize = found.Sum(x => x.SizeBytes);
            StatusMessage = $"Oyun & Paket Önbellekleri: {found.Count} hedef bulundu ({CleanerRule.FormatBytes(totalSize)}).";
            AddLog($"[GAMER & CACHE] {found.Count} önbellek hedefi ({CleanerRule.FormatBytes(totalSize)}) bulundu.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CleanSelectedDevArtifacts()
    {
        var selected = DevArtifactItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        long totalBytes = selected.Sum(x => x.SizeBytes);
        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} adet geliştirici/oyun önbellek klasörü ({CleanerRule.FormatBytes(totalBytes)}) temizlenecek.\n\nOnaylıyor musunuz?",
            "DevCleaner & Gamer Temizlik Onayı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        long freed = 0;
        foreach (var item in selected)
        {
            try
            {
                if (Directory.Exists(item.FullPath))
                {
                    Directory.Delete(item.FullPath, recursive: true);
                    deleted++;
                    freed += item.SizeBytes;
                    DevArtifactItems.Remove(item);
                }
            }
            catch
            {
                // Ignore locked files inside active shader cache
            }
        }

        LastFreedBytes += freed;
        LastDeletedFiles += deleted;
        RefreshSystemMetrics();
        StatusMessage = $"{deleted} geliştirici/oyun klasörü silindi ({CleanerRule.FormatBytes(freed)} alan açıldı).";
        AddLog($"[DEVCLEANER] {deleted} klasör temizlendi ({CleanerRule.FormatBytes(freed)}).");
    }

    // ==================== 9. FILE SHREDDER & FREE SPACE WIPER ====================

    private void AddFilesToShredQueue()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Kurtarılamaz Şekilde İmha Edilecek (Öğütülecek) Dosyaları Seçin",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
        {
            foreach (string file in dlg.FileNames)
            {
                if (ShredQueueItems.Any(x => x.FullPath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var fi = new FileInfo(file);
                ShredQueueItems.Add(new ShredTargetItem
                {
                    FullPath = file,
                    Name = fi.Name,
                    SizeBytes = fi.Exists ? fi.Length : 0,
                    IsDirectory = false
                });
            }
        }
    }

    private void AddFolderToShredQueue()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kurtarılamaz Şekilde İmha Edilecek Klasörü Seçin"
        };
        if (dlg.ShowDialog() == true && Directory.Exists(dlg.FolderName))
        {
            var di = new DirectoryInfo(dlg.FolderName);
            long size = di.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            ShredQueueItems.Add(new ShredTargetItem
            {
                FullPath = di.FullName,
                Name = $"[KLASÖR] {di.Name}",
                SizeBytes = size,
                IsDirectory = true
            });
        }
    }

    private async Task RunFileShredderAsync()
    {
        if (ShredQueueItems.Count == 0) return;

        var confirm = MessageBox.Show(
            $"DİKKAT! Listede bulunan {ShredQueueItems.Count} öğe ({ShredPassesCount} geçişli DoD 5220.22-M algoritmasıyla) üzerine yazılarak KALICI OLARAK İMHA EDİLECEKTİR!\n\n" +
            "Bu işlem sonrasında dosyalar hiçbir veri kurtarma yazılımıyla geri getirilemez.\n\nDevam etmek istiyor musunuz?",
            "Güvenli Dosya Öğütücü - Kalıcı İmha Onayı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Stop);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();

        var progress = new Progress<(int Percent, string CurrentItem)>(rep =>
        {
            ProgressPercent = rep.Percent;
            CurrentOperationDetail = $"Öğütülüyor ({ShredPassesCount} geçiş): {rep.CurrentItem}";
        });

        try
        {
            var (shreddedCount, bytes) = await FileShredderAndWiperService.ShredItemsAsync(
                ShredQueueItems,
                ShredPassesCount,
                progress,
                _cts.Token);

            LastFreedBytes += bytes;
            LastDeletedFiles += shreddedCount;
            StatusMessage = $"{shreddedCount} dosya kurtarılamaz şekilde imha edildi ({CleanerRule.FormatBytes(bytes)}).";
            AddLog($"[DOSYA ÖĞÜTÜCÜ] {shreddedCount} dosya ({ShredPassesCount} geçişli DoD) imha edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunFreeSpaceWiperAsync()
    {
        var confirm = MessageBox.Show(
            $"Boş disk sektörlerindeki eski silinmiş dosya kalıntılarını temizlemek için {FreeSpaceWipeMb} MB kriptografik sıfırlama işlemi uygulanacak.\n\nBaşlatılsın mı?",
            "Boş Disk Alanı Sıfırlayıcı (Free Space Wiper)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();

        var progress = new Progress<(int Percent, string Status)>(rep =>
        {
            ProgressPercent = rep.Percent;
            CurrentOperationDetail = rep.Status;
        });

        try
        {
            long wiped = await FileShredderAndWiperService.WipeDriveFreeSpaceSampleAsync(
                "C:\\",
                FreeSpaceWipeMb,
                progress,
                _cts.Token);

            StatusMessage = $"Boş alan sıfırlama tamamlandı ({CleanerRule.FormatBytes(wiped)} sektör temizlendi).";
            AddLog($"[BOŞ ALAN SİLİCİ] {CleanerRule.FormatBytes(wiped)} boş disk alanı kriptografik olarak sıfırlandı.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== 10. PRIVACY SHIELD ====================

    private async Task RunScanPrivacyTracesAsync()
    {
        IsBusy = true;
        StatusMessage = "Windows gizlilik ve etkinlik izleri (RunMRU, JumpList, Timeline, Pano) taranıyor...";
        try
        {
            await PrivacyAndNetworkShieldService.ScanPrivacyTracesAsync(PrivacyTraces);
            int totalTraces = PrivacyTraces.Sum(x => x.DetectedCount);
            StatusMessage = $"Gizlilik Taraması: {totalTraces} etkinlik ve geçmiş izi bulundu.";
            AddLog($"[GİZLİLİK KALKANI] {totalTraces} Windows kullanım izi tespit edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCleanPrivacyTracesAsync()
    {
        IsBusy = true;
        try
        {
            var (cleaned, bytes) = await PrivacyAndNetworkShieldService.CleanPrivacyTracesAsync(PrivacyTraces);
            LastFreedBytes += bytes;
            LastDeletedFiles += cleaned;
            StatusMessage = $"Gizlilik Kalkanı: {cleaned} etkinlik geçmişi/iz başarıyla temizlendi.";
            AddLog($"[GİZLİLİK KALKANI] {cleaned} Windows izi ve pano geçmişi temizlendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== 11. NETWORK CONNECTIONS & HOSTS ====================

    private async Task LoadActiveConnectionsAsync()
    {
        IsBusy = true;
        ActiveConnections.Clear();
        StatusMessage = "Canlı TCP/UDP ağ bağlantıları ve portlar listeleniyor...";
        try
        {
            var conns = await PrivacyAndNetworkShieldService.GetActiveNetworkConnectionsAsync();
            foreach (var c in conns)
                ActiveConnections.Add(c);
            StatusMessage = $"{ActiveConnections.Count} aktif TCP/UDP bağlantısı listelendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadHostsEntries()
    {
        HostsEntries.Clear();
        foreach (var h in PrivacyAndNetworkShieldService.LoadHostsFileEntries())
        {
            h.MarkSaved();
            HostsEntries.Add(h);
        }
    }

    private void AddNewHostEntry()
    {
        if (string.IsNullOrWhiteSpace(NewHostDomain)) return;
        if (PrivacyAndNetworkShieldService.AddHostsEntry(NewHostIp.Trim(), NewHostDomain.Trim(), NewHostComment.Trim(), out string msg))
        {
            NewHostDomain = string.Empty;
            LoadHostsEntries();
            StatusMessage = msg;
            AddLog($"[HOSTS] {msg}");
        }
        else
        {
            MessageBox.Show(msg, "Hosts Dosyası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveHostEntry(HostsEntryItem? item)
    {
        if (item is null) return;
        if (PrivacyAndNetworkShieldService.UpdateHostsEntry(item, out string msg))
        {
            StatusMessage = msg;
            AddLog($"[HOSTS] {msg}");
        }
        else
            MessageBox.Show(msg, "Hosts Dosyası", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DeleteHostEntry(HostsEntryItem? item)
    {
        if (item is null ||
            MessageBox.Show($"'{item.Hostname}' hosts kaydı silinecek. Devam edilsin mi?",
                "Hosts Kaydı Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (PrivacyAndNetworkShieldService.RemoveHostsEntry(item, out string msg))
        {
            HostsEntries.Remove(item);
            StatusMessage = msg;
            AddLog($"[HOSTS] {msg}");
        }
        else
            MessageBox.Show(msg, "Hosts Dosyası", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RestoreLatestHostsBackup()
    {
        string? backupPath = PrivacyAndNetworkShieldService.GetLatestHostsBackup();
        if (backupPath is null) return;

        if (MessageBox.Show(
                $"Son hosts yedeği geri yüklenecek:\n\n{backupPath}\n\nDevam edilsin mi?",
                "Hosts Yedeğini Geri Yükle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var result = PrivacyAndNetworkShieldService.RestoreHostsBackup(backupPath);
        StatusMessage = result.Message;
        AddLog($"[GERİ YÜKLEME] {result.Message}");
        if (result.Success)
            LoadHostsEntries();
        else
            MessageBox.Show(result.Message, "Hosts Geri Yükleme", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

}
