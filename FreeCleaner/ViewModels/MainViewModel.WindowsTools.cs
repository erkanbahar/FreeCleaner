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
    // ==================== 12. TASK SCHEDULER & FOLDER SPACE TREE ====================

    private async Task LoadScheduledTasksAsync()
    {
        IsBusy = true;
        ScheduledTasks.Clear();
        StatusMessage = "Windows Görev Zamanlayıcı (Task Scheduler) görevleri okunuyor...";
        try
        {
            var tasks = await PrivacyAndNetworkShieldService.GetScheduledTasksAsync();
            foreach (var t in tasks)
                ScheduledTasks.Add(t);
            StatusMessage = $"{ScheduledTasks.Count} zamanlanmış görev listelendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowseSpaceTreeFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Klasör Boyutu Dağılımı Analiz Edilecek Dizini Seçin",
            InitialDirectory = SpaceTreeTargetFolder
        };
        if (dlg.ShowDialog() == true)
        {
            SpaceTreeTargetFolder = dlg.FolderName;
            _ = RunAnalyzeFolderSpaceTreeAsync();
        }
    }

    private async Task RunAnalyzeFolderSpaceTreeAsync()
    {
        IsBusy = true;
        FolderSpaceItems.Clear();
        StatusMessage = $"'{SpaceTreeTargetFolder}' klasör dağılımı hesaplanıyor...";
        try
        {
            var items = await PrivacyAndNetworkShieldService.AnalyzeFolderSpaceBreakdownAsync(SpaceTreeTargetFolder);
            foreach (var item in items)
                FolderSpaceItems.Add(item);
            StatusMessage = $"Klasör Görselleştirici: {items.Count} alt dizin boyutu hesaplandı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== 13. DOWNLOADS ASSISTANT & EMPTY FOLDER CLEANER ====================

    public int InstallerMinAgeDays
    {
        get => _installerMinAgeDays;
        set
        {
            if (_installerMinAgeDays != value)
            {
                _installerMinAgeDays = value;
                OnPropertyChanged();
            }
        }
    }

    public string EmptyFolderScanTarget
    {
        get => _emptyFolderScanTarget;
        set
        {
            if (_emptyFolderScanTarget != value)
            {
                _emptyFolderScanTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public string RepairCenterStatus
    {
        get => _repairCenterStatus;
        set
        {
            if (_repairCenterStatus != value)
            {
                _repairCenterStatus = value;
                OnPropertyChanged();
            }
        }
    }

    private async Task RunScanOldInstallersAsync()
    {
        IsBusy = true;
        DownloadsAndEmptyItems.Clear();
        StatusMessage = $"İndirilenler ve Masaüstü klasörlerinde {InstallerMinAgeDays} günden eski kurulum (.exe, .msi) ve arşiv (.zip, .iso) dosyaları aranıyor...";

        _cts = new CancellationTokenSource();
        try
        {
            var found = await UltimateSystemSuiteService.ScanOldInstallersAndArchivesAsync(InstallerMinAgeDays, _cts.Token);
            foreach (var item in found)
                DownloadsAndEmptyItems.Add(item);

            long totalSize = found.Sum(x => x.SizeBytes);
            StatusMessage = $"İndirilenler Asistanı: {found.Count} eski kurulum/arşiv dosyası bulundu ({CleanerRule.FormatBytes(totalSize)}).";
            AddLog($"[İNDİRİLENLER] {found.Count} eski kurulum/arşiv dosyası ({CleanerRule.FormatBytes(totalSize)}) tespit edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowseEmptyFolderTarget()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Boş Alt Klasörleri Taranacak Dizini Seçin",
            InitialDirectory = EmptyFolderScanTarget
        };
        if (dlg.ShowDialog() == true)
        {
            EmptyFolderScanTarget = dlg.FolderName;
        }
    }

    private async Task RunScanEmptyFoldersAsync()
    {
        IsBusy = true;
        DownloadsAndEmptyItems.Clear();
        StatusMessage = $"'{EmptyFolderScanTarget}' altındaki içi boş klasörler taranıyor...";

        _cts = new CancellationTokenSource();
        try
        {
            var found = await UltimateSystemSuiteService.ScanEmptyDirectoriesAsync(EmptyFolderScanTarget, _cts.Token);
            foreach (var item in found)
                DownloadsAndEmptyItems.Add(item);

            StatusMessage = $"Boş Klasör Taraması: {found.Count} adet tamamen boş klasör bulundu.";
            AddLog($"[BOŞ KLASÖR] {found.Count} adet boş klasör listelendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CleanSelectedDownloadsAndEmpty()
    {
        var selected = DownloadsAndEmptyItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        long totalBytes = selected.Sum(x => x.SizeBytes);
        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} öğe ({CleanerRule.FormatBytes(totalBytes)}) temizlenecek.\n\n" +
            "Kurulum/arşiv dosyaları Geri Dönüşüm Kutusu'na taşınır, boş klasörler kaldırılır. Devam edilsin mi?",
            "İndirilenler & Boş Klasör Temizliği",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        long freed = 0;
        foreach (var item in selected)
        {
            try
            {
                if (item.IsEmptyFolder)
                {
                    if (Directory.Exists(item.FullPath))
                    {
                        Directory.Delete(item.FullPath, recursive: false);
                        deleted++;
                        DownloadsAndEmptyItems.Remove(item);
                    }
                }
                else
                {
                    if (RecycleBinHelper.MoveFileToRecycleBin(item.FullPath))
                    {
                        deleted++;
                        freed += item.SizeBytes;
                        DownloadsAndEmptyItems.Remove(item);
                    }
                }
            }
            catch { }
        }

        LastFreedBytes += freed;
        LastDeletedFiles += deleted;
        RefreshSystemMetrics();
        StatusMessage = $"{deleted} öğe temizlendi ({CleanerRule.FormatBytes(freed)} yer açıldı).";
        AddLog($"[İNDİRİLENLER / BOŞ KLASÖR] {deleted} öğe temizlendi ({CleanerRule.FormatBytes(freed)}).");
    }

    // ==================== 14. SHELL CONTEXT MENU MANAGER ====================

    private void LoadContextMenuEntries()
    {
        ContextMenuEntries.Clear();
        foreach (var entry in UltimateSystemSuiteService.GetContextMenuEntries())
            ContextMenuEntries.Add(entry);
        StatusMessage = $"Sağ Tık Menüsü Yöneticisi: {ContextMenuEntries.Count} kabuk uzantısı (Shell Extension) listelendi.";
    }

    private void DeleteSelectedContextMenuEntries()
    {
        var selected = ContextMenuEntries.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} sağ tık bağlam menüsü öğesi Kayıt Defteri'nden (HKCR) kaldırılacak.\n\nOnaylıyor musunuz?",
            "Sağ Tık Menüsü Öğesini Kaldır",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var (deleted, failed) = UltimateSystemSuiteService.DeleteContextMenuEntries(selected);
        LoadContextMenuEntries();
        StatusMessage = $"{deleted} sağ tık menüsü öğesi kaldırıldı ({failed} öğe yönetici izni gerektirdi).";
        AddLog($"[SAĞ TIK MENÜSÜ] {deleted} öğe silindi, {failed} başarısız.");
    }

    // ==================== 15. WINDOWS SERVICES & TELEMETRY OPTIMIZER ====================

    private async Task LoadOptimizableServicesAsync()
    {
        IsBusy = true;
        OptimizableServices.Clear();
        StatusMessage = "Windows arka plan ve telemetri servisleri sorgulanıyor...";
        try
        {
            var services = await UltimateSystemSuiteService.GetOptimizableServicesAsync();
            foreach (var svc in services)
                OptimizableServices.Add(svc);
            StatusMessage = $"{OptimizableServices.Count} kritik optimizasyon ve telemetri servisi listelendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ControlServiceAsync(WindowsServiceOptItem svc, bool start)
    {
        IsBusy = true;
        try
        {
            var (ok, msg) = await UltimateSystemSuiteService.ControlWindowsServiceAsync(svc.ServiceName, start);
            StatusMessage = msg;
            AddLog($"[SERVİSLER] {msg}");
            if (!ok)
            {
                MessageBox.Show(msg, "Servis Kontrolü", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            await LoadOptimizableServicesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== 16. S.M.A.R.T. DISK HEALTH & POWER PLANS ====================

    private async Task LoadDiskAndPowerAsync()
    {
        IsBusy = true;
        PhysicalDisks.Clear();
        PowerPlans.Clear();
        StatusMessage = "Fiziksel disk S.M.A.R.T. sağlığı ve Windows güç planları okunuyor...";
        try
        {
            var disks = await UltimateSystemSuiteService.GetPhysicalDisksHealthAsync();
            foreach (var d in disks)
                PhysicalDisks.Add(d);

            var plans = await UltimateSystemSuiteService.GetPowerPlansAsync();
            foreach (var p in plans)
                PowerPlans.Add(p);

            StatusMessage = $"{PhysicalDisks.Count} fiziksel disk ve {PowerPlans.Count} güç planı yüklendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ActivatePowerPlanAsync(PowerPlanItem plan)
    {
        if (await UltimateSystemSuiteService.SetActivePowerPlanAsync(plan.Guid))
        {
            StatusMessage = $"Aktif güç planı '{plan.Name}' olarak değiştirildi.";
            AddLog($"[GÜÇ PLANI] '{plan.Name}' etkinleştirildi.");
            await LoadDiskAndPowerAsync();
        }
    }

    private async Task UnlockUltimatePowerPlanAsync()
    {
        if (await UltimateSystemSuiteService.UnlockUltimatePerformancePlanAsync())
        {
            StatusMessage = "Gizli 'Nihai Performans (Ultimate Performance)' güç planı sisteme eklendi!";
            AddLog("[GÜÇ PLANI] Nihai Performans güç şeması etkinleştirildi.");
            await LoadDiskAndPowerAsync();
        }
    }

    // ==================== 17. SYSTEM REPAIR CENTER ====================

    private async Task RunDismHealthCheckAsync()
    {
        IsBusy = true;
        RepairCenterStatus = "DISM /Online /Cleanup-Image /CheckHealth çalıştırılıyor...";
        StatusMessage = "Windows bileşen deposu sağlığı (DISM CheckHealth) denetleniyor...";
        try
        {
            string output = await UltimateSystemSuiteService.RunDismCheckHealthAsync();
            RepairCenterStatus = $"DISM Sonucu: {output}";
            StatusMessage = "DISM sağlık kontrolü tamamlandı.";
            AddLog($"[DISM CHECKHEALTH] {output}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CleanFontCacheAndWindowsOldAsync()
    {
        IsBusy = true;
        StatusMessage = "Windows Yazı Tipi Önbelleği (FontCache) temizleniyor...";
        try
        {
            var (deleted, freed) = await UltimateSystemSuiteService.CleanFontCacheAndWindowsOldAsync();
            LastFreedBytes += freed;
            LastDeletedFiles += deleted;
            RepairCenterStatus = $"FontCache temizlendi: {deleted} önbellek dosyası silindi ({CleanerRule.FormatBytes(freed)}).";
            StatusMessage = RepairCenterStatus;
            AddLog($"[ONARIM MERKEZİ] {RepairCenterStatus}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== 18. FILE UNLOCKER (WIN32 RESTART MANAGER) ====================

    public string UnlockerTargetPath
    {
        get => _unlockerTargetPath;
        set
        {
            if (_unlockerTargetPath != value)
            {
                _unlockerTargetPath = value;
                OnPropertyChanged();
            }
        }
    }

    private void BrowseUnlockerFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Kilidi Çözülecek (Hangi İşlem Kullanıyor?) Dosyayı Seçin"
        };
        if (dlg.ShowDialog() == true)
        {
            UnlockerTargetPath = dlg.FileName;
            _ = RunScanLockingProcessesAsync();
        }
    }

    private void BrowseUnlockerFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kilidi Çözülecek Klasörü Seçin"
        };
        if (dlg.ShowDialog() == true)
        {
            UnlockerTargetPath = dlg.FolderName;
            _ = RunScanLockingProcessesAsync();
        }
    }

    private async Task RunScanLockingProcessesAsync()
    {
        if (string.IsNullOrWhiteSpace(UnlockerTargetPath)) return;

        IsBusy = true;
        LockingProcesses.Clear();
        StatusMessage = $"'{UnlockerTargetPath}' dosyasını/klasörünü kilitleyen işlemler (Win32 Restart Manager) sorgulanıyor...";
        try
        {
            var found = await EnterprisePowerToolsService.FindLockingProcessesAsync(UnlockerTargetPath);
            foreach (var p in found)
                LockingProcesses.Add(p);

            StatusMessage = found.Count > 0
                ? $"Kilit Çözücü: Bu hedefi şu anda {found.Count} işlem kilitliyor!"
                : "Kilit Çözücü: Bu dosya veya klasörü kilitleyen aktif bir işlem bulunamadı (Serbest).";
            AddLog($"[KİLİT ÇÖZÜCÜ] {found.Count} kilitleyici işlem tespit edildi ({UnlockerTargetPath}).");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task KillLockingProcessAndRescanAsync(LockingProcessItem proc)
    {
        var confirm = MessageBox.Show(
            $"'{proc.ProcessName}' (PID: {proc.ProcessId}) işlemini sonlandırıp dosya kilidini açmak istiyor musunuz?",
            "Kilitli Dosya Çözücü",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (EnterprisePowerToolsService.KillLockingProcess(proc.ProcessId, out string msg))
        {
            StatusMessage = msg;
            AddLog($"[KİLİT ÇÖZÜCÜ] {msg}");
            await RunScanLockingProcessesAsync();
        }
        else
        {
            MessageBox.Show(msg, "Kilit Çözücü Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ==================== 19. BROWSER EXTENSIONS AUDITOR ====================

    private async Task LoadBrowserExtensionsAsync()
    {
        IsBusy = true;
        BrowserExtensions.Clear();
        StatusMessage = "Chrome, Edge, Brave ve Vivaldi tarayıcı eklentileri (Extensions) taranıyor...";
        try
        {
            var exts = await EnterprisePowerToolsService.ScanBrowserExtensionsAsync();
            foreach (var ext in exts)
                BrowserExtensions.Add(ext);
            StatusMessage = $"{BrowserExtensions.Count} kurulu tarayıcı eklentisi (uzantı) listelendi.";
            AddLog($"[TARAYICI EKLENTİLERİ] {BrowserExtensions.Count} uzantı tespit edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RemoveSelectedBrowserExtensions()
    {
        var selected = BrowserExtensions.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} tarayıcı eklenti klasörü silinecek (Tarayıcının kapalı olduğundan emin olun).\n\nDevam edilsin mi?",
            "Tarayıcı Eklentisini Kaldır",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        long freed = 0;
        foreach (var ext in selected)
        {
            try
            {
                if (Directory.Exists(ext.FullPath))
                {
                    Directory.Delete(ext.FullPath, recursive: true);
                    deleted++;
                    freed += ext.SizeBytes;
                    BrowserExtensions.Remove(ext);
                }
            }
            catch { }
        }

        LastFreedBytes += freed;
        LastDeletedFiles += deleted;
        StatusMessage = $"{deleted} tarayıcı eklentisi kaldırıldı ({CleanerRule.FormatBytes(freed)}).";
        AddLog($"[TARAYICI EKLENTİLERİ] {deleted} uzantı klasörü temizlendi.");
    }

    // ==================== 20. WINDOWS HOTFIXES & DRIVERSTORE ====================

    private async Task LoadDriversAndHotfixesAsync()
    {
        IsBusy = true;
        DriversAndHotfixes.Clear();
        StatusMessage = "Windows Güncelleme Geçmişi (HotFix KB) ve 3. Parti Sürücü Deposu (pnputil) okunuyor...";
        try
        {
            var items = await EnterprisePowerToolsService.GetHotfixesAndDriversAsync();
            foreach (var item in items)
                DriversAndHotfixes.Add(item);
            StatusMessage = $"{DriversAndHotfixes.Count} yüklü Windows güncellemesi ve OEM sürücü paketi listelendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== 21. PATH DOCTOR & BROKEN SHORTCUTS ====================

    private async Task LoadPathAndShortcutIssuesAsync()
    {
        IsBusy = true;
        PathAndShortcutIssues.Clear();
        StatusMessage = "Kullanıcı/Sistem PATH ortam değişkenleri ve Masaüstü/Başlat Menüsü kırık kısayolları (.lnk) taranıyor...";
        try
        {
            var issues = await EnterprisePowerToolsService.ScanPathAndBrokenShortcutsAsync();
            foreach (var issue in issues)
                PathAndShortcutIssues.Add(issue);
            StatusMessage = $"PATH & Kısayol Doktoru: {PathAndShortcutIssues.Count} bozuk/tekrarlanan kayıt bulundu.";
            AddLog($"[PATH & KISAYOL DOKTORU] {PathAndShortcutIssues.Count} sorun tespit edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void FixSelectedPathAndShortcuts()
    {
        var selected = PathAndShortcutIssues.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var (fixedPath, deletedShortcuts) = EnterprisePowerToolsService.FixPathAndShortcuts(selected);
        _ = LoadPathAndShortcutIssuesAsync();

        StatusMessage = $"Onarım Tamamlandı: {fixedPath} geçersiz/kopya PATH girdisi temizlendi, {deletedShortcuts} kırık kısayol silindi.";
        AddLog($"[PATH & KISAYOL DOKTORU] {fixedPath} PATH onarıldı, {deletedShortcuts} kırık kısayol silindi.");
    }

    // ==================== 22. GAME BOOSTER & BATTERY HEALTH REPORT ====================

    private async Task RunGameBoosterAsync()
    {
        IsBusy = true;
        StatusMessage = "Tek Tıkla Oyun & Performans Modu etkinleştiriliyor...";
        try
        {
            string summary = await EnterprisePowerToolsService.ActivateGameAndPerformanceBoostAsync();
            RefreshSystemMetrics();
            StatusMessage = summary;
            AddLog($"[OYUN & PERFORMANS MODU] {summary}");
            await LoadDiskAndPowerAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunGenerateBatteryReportAsync()
    {
        IsBusy = true;
        StatusMessage = "Windows Pil Sağlık Raporu (powercfg /batteryreport) oluşturuluyor...";
        try
        {
            var (ok, path, msg) = await EnterprisePowerToolsService.GenerateBatteryHealthReportAsync();
            StatusMessage = msg;
            AddLog($"[PİL RAPORU] {msg}");
            if (ok && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            else if (!ok)
            {
                MessageBox.Show(msg, "Pil Sağlık Raporu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
