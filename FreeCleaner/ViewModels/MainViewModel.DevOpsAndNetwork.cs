using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeCleaner.Models;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public partial class MainViewModel
{
    // ==================== 15. 9TH GEN WINGET UPDATER, LAN RADAR, SQLITE OPTIMIZER, CREDENTIAL/SSH & CPU PRIORITY ====================

    private async Task LoadWingetUpgradesAsync()
    {
        IsBusy = true;
        WingetUpgrades.Clear();
        StatusMessage = "Windows Paket Yöneticisi (winget upgrade) üzerinden güncellenebilir programlar taranıyor...";
        try
        {
            var items = await NinthGenWingetAndNetworkSuiteService.ScanWingetUpgradesAsync();
            foreach (var item in items)
                WingetUpgrades.Add(item);
            StatusMessage = $"Winget Güncelleyici: Yeni sürümü çıkmış {WingetUpgrades.Count} program tespit edildi.";
            AddLog($"[WINGET UPDATER] {WingetUpgrades.Count} güncellenebilir paket bulundu.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string _wingetCurrentPackageText = "Henüz güncelleme başlatılmadı.";
    public string WingetCurrentPackageText
    {
        get => _wingetCurrentPackageText;
        set
        {
            if (_wingetCurrentPackageText != value)
            {
                _wingetCurrentPackageText = value;
                OnPropertyChanged();
            }
        }
    }

    private int _wingetProgressPercent;
    public int WingetProgressPercent
    {
        get => _wingetProgressPercent;
        set
        {
            if (_wingetProgressPercent != value)
            {
                _wingetProgressPercent = value;
                OnPropertyChanged();
            }
        }
    }

    private string _wingetProgressCounterText = "0 / 0";
    public string WingetProgressCounterText
    {
        get => _wingetProgressCounterText;
        set
        {
            if (_wingetProgressCounterText != value)
            {
                _wingetProgressCounterText = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isWingetUpgrading;
    public bool IsWingetUpgrading
    {
        get => _isWingetUpgrading;
        set
        {
            if (_isWingetUpgrading != value)
            {
                _isWingetUpgrading = value;
                OnPropertyChanged();
            }
        }
    }

    private async Task ExecuteWingetUpgradeAsync(bool upgradeAll)
    {
        var targets = (upgradeAll ? WingetUpgrades : WingetUpgrades.Where(x => x.IsSelected)).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Güncellenecek program seçilmedi.";
            return;
        }

        foreach (var t in targets)
            t.UpgradeStatus = "⏳ Sırada Bekliyor";

        IsBusy = true;
        IsWingetUpgrading = true;
        ProgressPercent = 0;
        WingetProgressPercent = 0;
        WingetProgressCounterText = $"0 / {targets.Count}";
        WingetCurrentPackageText = "Winget güncellemesi hazırlanıyor...";
        CancellationToken token = ResetCancellationTokenSource();

        // Progress<T> marshals callbacks back to the UI thread automatically
        var progress = new Progress<(int Completed, int Total, WingetUpgradeItem Item, bool IsStarting, bool Success)>(p =>
        {
            int percent = p.Total == 0 ? 0 : (int)(p.Completed * 100.0 / p.Total);

            if (p.IsStarting)
            {
                p.Item.UpgradeStatus = "🔄 Güncelleniyor...";
                string text = $"[{p.Completed + 1}/{p.Total}] Güncelleniyor: {p.Item.PackageName}  ({p.Item.InstalledVersion} → {p.Item.AvailableVersion})";
                WingetCurrentPackageText = text;
                StatusMessage = text;
                CurrentOperationDetail = $"winget upgrade --id {p.Item.PackageId}";
                AddLog($"[WINGET] Başladı: {p.Item.PackageName} ({p.Item.PackageId})");
            }
            else
            {
                p.Item.UpgradeStatus = p.Success ? "✅ Güncellendi" : "❌ Başarısız";
                AddLog(p.Success
                    ? $"[WINGET] ✅ {p.Item.PackageName} → {p.Item.AvailableVersion} sürümüne güncellendi."
                    : $"[WINGET] ❌ {p.Item.PackageName} güncellenemedi (Yönetici izni veya kurulum hatası).");
            }

            WingetProgressPercent = percent;
            ProgressPercent = percent;
            WingetProgressCounterText = $"{p.Completed} / {p.Total}  (%{percent})";
        });

        try
        {
            var (updated, failed, msg) = await NinthGenWingetAndNetworkSuiteService.UpgradeWingetPackagesWithProgressAsync(targets, progress, token);
            WingetProgressPercent = 100;
            ProgressPercent = 100;
            WingetCurrentPackageText = $"✅ Tamamlandı — {updated} başarılı, {failed} başarısız.";
            StatusMessage = msg;
            AddLog($"[WINGET UPDATER] {msg}");
        }
        catch (OperationCanceledException)
        {
            WingetCurrentPackageText = "⛔ Güncelleme kullanıcı tarafından iptal edildi.";
            StatusMessage = WingetCurrentPackageText;
        }
        finally
        {
            IsWingetUpgrading = false;
            IsBusy = false;
            CurrentOperationDetail = "Winget güncelleme oturumu sona erdi.";
        }
    }

    private async Task LoadLanDevicesAsync()
    {
        IsBusy = true;
        LanDevices.Clear();
        StatusMessage = "Yerel ağ (LAN / Wi-Fi) /24 alt ağına paralel Ping ve ARP taraması yapılıyor...";
        try
        {
            var items = await NinthGenWingetAndNetworkSuiteService.ScanLocalNetworkDevicesAsync();
            foreach (var item in items)
                LanDevices.Add(item);
            StatusMessage = $"Yerel Ağ Cihaz Radarı: Ağınızda {LanDevices.Count} aktif cihaz ve ARP kaydı tespit edildi.";
            AddLog($"[LAN RADAR] {LanDevices.Count} yerel ağ cihazı listelendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSqliteDatabasesAsync()
    {
        IsBusy = true;
        SqliteDatabases.Clear();
        StatusMessage = "Chrome, Edge, Brave, Firefox, VS Code ve Cursor SQLite3 veritabanları analiz ediliyor...";
        try
        {
            var items = await NinthGenWingetAndNetworkSuiteService.ScanSqliteDatabasesAsync();
            foreach (var item in items)
                SqliteDatabases.Add(item);
            long totalReclaim = SqliteDatabases.Sum(x => x.ReclaimableBytes);
            StatusMessage = $"SQLite Sıkıştırıcı: {SqliteDatabases.Count} veritabanı incelendi ({CleanerRule.FormatBytes(totalReclaim)} boş sayfa / WAL şişkinliği).";
            AddLog($"[SQLITE OPTIMIZER] {SqliteDatabases.Count} veritabanı bulundu ({CleanerRule.FormatBytes(totalReclaim)} kazanılabilir).");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteOptimizeSqliteDatabasesAsync()
    {
        IsBusy = true;
        try
        {
            var (count, freed, msg) = await NinthGenWingetAndNetworkSuiteService.OptimizeSelectedSqliteDatabasesAsync(SqliteDatabases);
            LastFreedBytes += freed;
            LastDeletedFiles += count;
            StatusMessage = msg;
            AddLog($"[SQLITE OPTIMIZER] {msg}");
            await LoadSqliteDatabasesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCredentialsAndSshAsync()
    {
        IsBusy = true;
        CredentialsAndSshItems.Clear();
        StatusMessage = "Windows Kimlik Bilgisi Yöneticisi (cmdkey) ve ~/.ssh anahtarları denetleniyor...";
        try
        {
            var items = await NinthGenWingetAndNetworkSuiteService.ScanCredentialsAndSshKeysAsync();
            foreach (var item in items)
                CredentialsAndSshItems.Add(item);
            StatusMessage = $"Kimlik & SSH Denetçisi: {CredentialsAndSshItems.Count} kayıtlı kimlik hedefi ve SSH anahtarı listelendi.";
            AddLog($"[KİMLİK & SSH] {CredentialsAndSshItems.Count} öğe denetlendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExecuteDeleteSelectedCredentials()
    {
        int deleted = NinthGenWingetAndNetworkSuiteService.DeleteSelectedCredentials(CredentialsAndSshItems);
        StatusMessage = $"{deleted} kayıtlı kimlik bilgisi Windows Kasası'ndan (cmdkey) silindi.";
        AddLog($"[KİMLİK KASASI] {deleted} kimlik kaydı temizlendi.");
        _ = LoadCredentialsAndSshAsync();
    }

    private async Task LoadProcessPriorityItemsAsync()
    {
        IsBusy = true;
        ProcessPriorityItems.Clear();
        StatusMessage = "Çalışan uygulamaların canlı CPU önceliği ve bellek kullanımları okunuyor...";
        try
        {
            var items = await NinthGenWingetAndNetworkSuiteService.GetActiveProcessesForPriorityAsync();
            foreach (var item in items)
                ProcessPriorityItems.Add(item);
            StatusMessage = $"Canlı CPU Önceliği & EcoQoS Yöneticisi: {ProcessPriorityItems.Count} aktif işlem listelendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ChangeProcessPriorityAndRefreshAsync(ProcessPriorityItem item, ProcessPriorityClass newPriority)
    {
        var (_, msg) = NinthGenWingetAndNetworkSuiteService.SetProcessPriorityLevel(item.ProcessId, newPriority);
        StatusMessage = msg;
        AddLog($"[CPU ÖNCELİĞİ] {msg}");
        await LoadProcessPriorityItemsAsync();
    }

    // ==================== 16. 10TH GEN BOOT TIME, DOCKER/WSL2, DEFENDER AUDITOR, MONITOR/HZ & GIT GC ====================

    private string _gitWorkspacePath = AppStoragePathsService.ResolveDefaultDevWorkspaceRoot();
    public string GitWorkspacePath
    {
        get => _gitWorkspacePath;
        set
        {
            if (_gitWorkspacePath != value)
            {
                _gitWorkspacePath = value;
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(value))
                    SaveCurrentSettings();
            }
        }
    }

    private async Task LoadBootPerformanceAsync()
    {
        IsBusy = true;
        BootPerformanceItems.Clear();
        StatusMessage = "Windows Diagnostics-Performance açılış günlükleri (Event ID 100/101) ve sistem çalışma süresi analiz ediliyor...";
        try
        {
            var items = await TenthGenMilestoneSuiteService.AnalyzeBootPerformanceAsync();
            foreach (var item in items)
                BootPerformanceItems.Add(item);
            StatusMessage = $"Açılış Süresi Analizcisi: {BootPerformanceItems.Count} önyükleme metriği ve darboğaz kaydı listelendi.";
            AddLog($"[BOOT ANALYZER] {BootPerformanceItems.Count} açılış metriği incelendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDockerAndWslItemsAsync()
    {
        IsBusy = true;
        DockerAndWslItems.Clear();
        StatusMessage = "WSL2 Linux dağıtımları ve Docker konteynerleri taranıyor...";
        try
        {
            var items = await TenthGenMilestoneSuiteService.ScanDockerAndWslItemsAsync();
            foreach (var item in items)
                DockerAndWslItems.Add(item);
            StatusMessage = $"WSL2 & Docker Yöneticisi: {DockerAndWslItems.Count} dağıtım/konteyner listelendi.";
            AddLog($"[WSL2 & DOCKER] {DockerAndWslItems.Count} öğe yüklendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteShutdownWslAsync()
    {
        IsBusy = true;
        try
        {
            var (_, msg) = await TenthGenMilestoneSuiteService.ShutdownWslAndFreeRamAsync();
            StatusMessage = msg;
            AddLog($"[WSL2 SHUTDOWN] {msg}");
            await LoadDockerAndWslItemsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecutePruneDockerSystemAsync()
    {
        IsBusy = true;
        try
        {
            var (_, msg) = await TenthGenMilestoneSuiteService.PruneDockerSystemAsync();
            StatusMessage = msg;
            AddLog($"[DOCKER PRUNE] {msg}");
            await LoadDockerAndWslItemsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDefenderAuditAsync()
    {
        IsBusy = true;
        DefenderAuditItems.Clear();
        StatusMessage = "Windows Defender gerçek zamanlı koruma kalkanları ve gizli istisna klasörleri (ExclusionPath) denetleniyor...";
        try
        {
            var items = await TenthGenMilestoneSuiteService.AuditWindowsDefenderAsync();
            foreach (var item in items)
                DefenderAuditItems.Add(item);
            StatusMessage = $"Defender Güvenlik Denetçisi: {DefenderAuditItems.Count} koruma kalkanı ve istisna kaydı incelendi.";
            AddLog($"[DEFENDER DENETÇİSİ] {DefenderAuditItems.Count} kayıt kontrol edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteDefenderActionAsync(bool quickScan)
    {
        IsBusy = true;
        StatusMessage = quickScan
            ? "Windows Defender Hızlı Zararlı Yazılım Taraması (QuickScan) yürütülüyor..."
            : "Windows Defender Antivirüs imza veritabanı güncelleniyor...";
        try
        {
            var (_, msg) = await TenthGenMilestoneSuiteService.RunDefenderActionAsync(quickScan);
            StatusMessage = msg;
            AddLog($"[DEFENDER] {msg}");
            await LoadDefenderAuditAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadMonitorsAndGpuAsync()
    {
        IsBusy = true;
        MonitorAndDisplayItems.Clear();
        StatusMessage = "Monitör EDID donanım kimlikleri, çözünürlük, yenileme hızı (Hz) ve GPU çıkışları okunuyor...";
        try
        {
            var items = await TenthGenMilestoneSuiteService.ScanMonitorsAndGpuOutputsAsync();
            foreach (var item in items)
                MonitorAndDisplayItems.Add(item);
            StatusMessage = $"Monitör & GPU Doktoru: {MonitorAndDisplayItems.Count} ekran paneli ve görüntü bağdaştırıcısı listelendi.";
            AddLog($"[MONİTÖR & HZ] {MonitorAndDisplayItems.Count} görüntü birimi incelendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteRestartGpuDriverAsync()
    {
        IsBusy = true;
        try
        {
            var (_, msg) = await TenthGenMilestoneSuiteService.RestartGpuDisplayDriverAsync();
            StatusMessage = msg;
            AddLog($"[GPU RESET] {msg}");
            await LoadMonitorsAndGpuAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowseGitWorkspace()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Git Projelerinin (.git) Taranacağı Ana Klasörü Seçin"
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            GitWorkspacePath = dlg.FolderName;
            _ = LoadGitReposAsync();
        }
    }

    private async Task LoadGitReposAsync()
    {
        IsBusy = true;
        GitRepoItems.Clear();
        StatusMessage = $"'{GitWorkspacePath}' altındaki tüm Git depoları (.git), branch ve çalışma ağacı durumları taranıyor...";
        try
        {
            var repos = await TenthGenMilestoneSuiteService.ScanGitRepositoriesAsync(GitWorkspacePath);
            foreach (var r in repos)
                GitRepoItems.Add(r);
            long totalGitBytes = GitRepoItems.Sum(x => x.GitDirSizeBytes);
            StatusMessage = $"Git Depo Radarı: {GitRepoItems.Count} Git projesi bulundu (Toplam .git boyutu: {CleanerRule.FormatBytes(totalGitBytes)}).";
            AddLog($"[GIT RADAR] {GitRepoItems.Count} Git deposu listelendi ({CleanerRule.FormatBytes(totalGitBytes)}).");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCompressSelectedGitReposAsync()
    {
        IsBusy = true;
        StatusMessage = "Seçili Git depolarında 'git gc --prune=now' nesne sıkıştırması yürütülüyor...";
        try
        {
            var (count, saved, msg) = await TenthGenMilestoneSuiteService.CompressSelectedGitReposAsync(GitRepoItems);
            LastFreedBytes += saved;
            LastDeletedFiles += count;
            StatusMessage = msg;
            AddLog($"[GIT GC] {msg}");
            await LoadGitReposAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenFileLocation(object? parameter)
    {
        if (parameter is ScannedFileItem item && !item.IsRecycleBinItem)
        {
            OpenExplorerPath(item.FullPath);
        }
    }

    private static void OpenExplorerPath(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{fullPath}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{fullPath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                string? dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    private void AddLog(string message)
    {
        string timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ActivityLogs.Insert(0, timestamped));
        }
        else
        {
            ActivityLogs.Insert(0, timestamped);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ActivityLogPath)!);
            File.AppendAllText(ActivityLogPath, timestamped + Environment.NewLine);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"FreeCleaner işlem günlüğü yazılamadı: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"FreeCleaner işlem günlüğü için erişim reddedildi: {ex.Message}");
        }
    }

    private void LoadActivityLogs()
    {
        try
        {
            if (!File.Exists(ActivityLogPath))
                return;

            foreach (var line in File.ReadLines(ActivityLogPath).Reverse().Take(200))
                ActivityLogs.Add(line);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"FreeCleaner işlem günlüğü okunamadı: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"FreeCleaner işlem günlüğü için erişim reddedildi: {ex.Message}");
        }
    }

    private void OpenRecycleBin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:RecycleBinFolder",
                UseShellExecute = true
            });
            AddLog("Geri Dönüşüm Kutusu açıldı.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Geri Dönüşüm Kutusu açılamadı: {ex.Message}";
            AddLog($"[HATA] Geri Dönüşüm Kutusu açılamadı: {ex.Message}");
        }
    }
}
