using System.IO;
using System.Windows;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public partial class MainViewModel
{
    // ==================== 2. STARTUP MANAGER ====================

    private void LoadStartupItems()
    {
        StartupItems.Clear();
        foreach (var item in StartupManagerService.GetStartupItems())
        {
            StartupItems.Add(item);
        }
        StatusMessage = $"Başlangıç Yöneticisi: {StartupItems.Count} otomatik başlayan uygulama listelendi.";
        AddLog($"[BAŞLANGIÇ] {StartupItems.Count} başlangıç öğesi yüklendi.");
    }

    private void ToggleStartupItem(StartupItemModel item)
    {
        bool targetState = !item.IsEnabled;
        if (StartupManagerService.ToggleStartupItem(item, targetState, out string msg))
        {
            StatusMessage = msg;
            AddLog($"[BAŞLANGIÇ] {msg}");
        }
        else
        {
            MessageBox.Show(msg, "Başlangıç Yöneticisi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        LoadStartupItems();
    }

    private void DeleteStartupItem(StartupItemModel item)
    {
        var confirm = MessageBox.Show(
            $"'{item.Name}' öğesini başlangıç listesinden kalıcı olarak kaldırmak istiyor musunuz?",
            "Başlangıç Öğesini Kaldır",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (StartupManagerService.DeleteStartupItem(item, out string msg))
        {
            StartupItems.Remove(item);
            StatusMessage = msg;
            AddLog($"[BAŞLANGIÇ] {msg}");
        }
        else
        {
            MessageBox.Show(msg, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 3. REGISTRY CLEANER ====================

    private async Task RunRegistryScanAsync()
    {
        IsBusy = true;
        ProgressPercent = 30;
        RegistryIssues.Clear();
        StatusMessage = "Kayıt Defteri (Registry) bozuk yollar ve yetim girdiler için taranıyor...";
        AddLog("[KAYIT DEFTERİ] Güvenli kayıt defteri taraması başlatıldı...");

        CancellationToken token = ResetCancellationTokenSource();
        try
        {
            var found = await RegistryCleanerService.ScanRegistryIssuesAsync(token);
            foreach (var issue in found)
            {
                RegistryIssues.Add(issue);
            }
            ProgressPercent = 100;
            StatusMessage = $"Kayıt Defteri Taraması Tamamlandı: {RegistryIssues.Count} geçersiz/yetim kayıt bulundu.";
            CurrentOperationDetail = "Onarmadan önce otomatik .reg yedeği alınır.";
            AddLog($"[KAYIT DEFTERİ] {RegistryIssues.Count} sorunlu kayıt tespit edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Kayıt defteri tarama hatası: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RunRegistryBackupOnly()
    {
        var selected = RegistryIssues.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        string backupFile = RegistryCleanerService.ExportBackupFile(selected);
        StatusMessage = $"Kayıt defteri yedeği oluşturuldu: {backupFile}";
        AddLog($"[YEDEK] .reg yedeği kaydedildi: {backupFile}");
        MessageBox.Show($"Seçili {selected.Count} kayıt için yedek dosyası oluşturuldu:\n\n{backupFile}", "Yedekleme Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RunRegistryFix()
    {
        var selected = RegistryIssues.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var res = MessageBox.Show(
            $"Seçili {selected.Count} adet geçersiz kayıt defteri girdisi temizlenecek.\n\n" +
            "İşlem öncesinde otomatik olarak .reg yedek dosyası oluşturulacaktır. Devam edilsin mi?",
            "Kayıt Defteri Onarımı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res != MessageBoxResult.Yes) return;

        var (fixedCount, skippedCount, backupPath) = RegistryCleanerService.FixRegistryIssues(selected);
        foreach (var item in selected.ToList())
        {
            RegistryIssues.Remove(item);
        }

        StatusMessage = $"{fixedCount} kayıt defteri sorunu onarıldı ({skippedCount} atlandı).";
        CurrentOperationDetail = $"Otomatik yedek: {backupPath}";
        AddLog($"[KAYIT DEFTERİ] {fixedCount} kayıt temizlendi. Yedek: {backupPath}");
    }

    private void RestoreLatestRegistryBackup()
    {
        string? backupPath = RegistryCleanerService.GetLatestBackupFile();
        if (backupPath is null) return;

        if (MessageBox.Show(
                $"Son registry yedeği geri yüklenecek:\n\n{backupPath}\n\nDevam edilsin mi?",
                "Registry Yedeğini Geri Yükle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var result = RegistryCleanerService.RestoreBackupFile(backupPath);
        StatusMessage = result.Message;
        AddLog($"[GERİ YÜKLEME] {result.Message}");
        if (!result.Success)
            MessageBox.Show(result.Message, "Registry Geri Yükleme", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void LoadBackups()
    {
        BackupFiles.Clear();
        foreach (var backup in BackupManagerService.ListBackups())
            BackupFiles.Add(backup);
        StatusMessage = LocalizationService.GetText(
            $"{BackupFiles.Count} yedek dosyası listelendi.",
            $"Listed {BackupFiles.Count} backup files.",
            _selectedLanguage);
    }

    private void RestoreSelectedBackup()
    {
        BackupFileItem? backup = BackupFiles.FirstOrDefault(x => x.IsSelected);
        if (backup is null) return;

        if (MessageBox.Show(
                $"Seçilen {backup.BackupType} yedeği geri yüklenecek:\n\n{backup.FileName}\n\nDevam edilsin mi?",
                "Yedeği Geri Yükle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var result = BackupManagerService.Restore(backup);
        StatusMessage = result.Message;
        AddLog($"[GERİ YÜKLEME] {result.Message}");
        if (!result.Success)
            MessageBox.Show(result.Message, "Yedek Geri Yükleme", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            LoadBackups();
    }

    // ==================== 4. DISK ANALYZER (LARGE & DUPLICATE FILES) ====================

    private void BrowseDiskTargetFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Analiz Edilecek Klasörü veya Sürücüyü Seçin",
            InitialDirectory = DiskScanTargetFolder
        };

        if (dlg.ShowDialog() == true)
        {
            DiskScanTargetFolder = dlg.FolderName;
        }
    }

    private async Task RunScanLargeFilesAsync()
    {
        IsBusy = true;
        ProgressPercent = 20;
        DiskAnalyzerItems.Clear();
        long minBytes = SelectedMinSizeMb * 1024L * 1024L;

        StatusMessage = $"'{DiskScanTargetFolder}' içinde {SelectedMinSizeMb} MB üzeri büyük dosyalar aranıyor...";
        AddLog($"[DİSK ANALİZİ] Büyük dosya taraması başlatıldı (Min: {SelectedMinSizeMb} MB)...");

        CancellationToken token = ResetCancellationTokenSource();
        var progress = new Progress<(string CurrentDir, int FoundCount)>(rep =>
        {
            CurrentOperationDetail = $"Bulunan: {rep.FoundCount} • Taranan: {rep.CurrentDir}";
        });

        try
        {
            var items = await DiskAnalyzerService.ScanLargeFilesAsync(
                DiskScanTargetFolder,
                minBytes,
                ExcludedPaths.ToList(),
                progress,
                token);

            foreach (var item in items)
                DiskAnalyzerItems.Add(item);

            ProgressPercent = 100;
            long totalSize = items.Sum(x => x.SizeBytes);
            StatusMessage = $"Büyük Dosya Taraması: {items.Count} büyük dosya bulundu (Toplam {CleanerRule.FormatBytes(totalSize)}).";
            AddLog($"[DİSK ANALİZİ] {items.Count} büyük dosya listelendi ({CleanerRule.FormatBytes(totalSize)}).");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Büyük dosya taraması durduruldu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunScanDuplicatesAsync()
    {
        IsBusy = true;
        ProgressPercent = 25;
        DiskAnalyzerItems.Clear();
        long minBytes = Math.Max(1, SelectedMinSizeMb) * 1024L * 1024L;

        StatusMessage = $"'{DiskScanTargetFolder}' içinde SHA-256 ile birebir kopya (duplicate) dosyalar aranıyor...";
        AddLog($"[KOPYA BULUCU] SHA-256 kopya dosya analizi başlatıldı (Min: {SelectedMinSizeMb} MB)...");

        CancellationToken token = ResetCancellationTokenSource();
        var progress = new Progress<(string StatusText, int FoundDuplicates)>(rep =>
        {
            CurrentOperationDetail = $"{rep.StatusText} (Toplam {rep.FoundDuplicates})";
        });

        try
        {
            var items = await DiskAnalyzerService.ScanDuplicateFilesAsync(
                DiskScanTargetFolder,
                minBytes,
                ExcludedPaths.ToList(),
                progress,
                token);

            foreach (var item in items)
                DiskAnalyzerItems.Add(item);

            ProgressPercent = 100;
            long savable = items.Where(x => x.IsSelected).Sum(x => x.SizeBytes);
            StatusMessage = $"Kopya Analizi Tamamlandı: {items.Count} kopya öğe bulundu. Orijinaller korundu, {CleanerRule.FormatBytes(savable)} fazlalık seçildi.";
            AddLog($"[KOPYA BULUCU] {items.Count} kopya dosya tespit edildi ({CleanerRule.FormatBytes(savable)} kazanılabilir).");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Kopya dosya taraması durduruldu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DeleteSelectedDiskAnalyzerFiles()
    {
        var selected = DiskAnalyzerItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        long totalBytes = selected.Sum(x => x.SizeBytes);
        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} dosya ({CleanerRule.FormatBytes(totalBytes)}) Geri Dönüşüm Kutusu'na taşınarak silinecek.\n\nOnaylıyor musunuz?",
            "Disk Analizörü - Dosya Silme Onayı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        long freed = 0;
        foreach (var item in selected)
        {
            if (RecycleBinHelper.MoveFileToRecycleBin(item.FullPath))
            {
                deleted++;
                freed += item.SizeBytes;
                DiskAnalyzerItems.Remove(item);
            }
        }

        LastFreedBytes += freed;
        LastDeletedFiles += deleted;
        RefreshSystemMetrics();
        StatusMessage = $"{deleted} dosya Geri Dönüşüm Kutusu'na taşındı ({CleanerRule.FormatBytes(freed)} açıldı).";
        AddLog($"[DİSK ANALİZİ] {deleted} dosya temizlendi ({CleanerRule.FormatBytes(freed)}).");
    }

    // ==================== 5. UNINSTALLER & ORPHAN FOLDERS ====================

    private async Task LoadInstalledAppsAsync()
    {
        IsBusy = true;
        InstalledApps.Clear();
        StatusMessage = "Yüklü programlar listeleniyor...";

        try
        {
            var apps = await UninstallerAndOrphanService.GetInstalledAppsAsync();
            foreach (var app in apps)
                InstalledApps.Add(app);

            StatusMessage = $"{InstalledApps.Count} yüklü uygulama bulundu.";
            AddLog($"[PROGRAMLAR] {InstalledApps.Count} kurulu uygulama yüklendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunScanOrphanFoldersAsync()
    {
        IsBusy = true;
        OrphanFolders.Clear();
        StatusMessage = "AppData içinde kaldırılmış programlardan kalan yetim (orphan) klasörler taranıyor...";

        CancellationToken token = ResetCancellationTokenSource();
        try
        {
            if (InstalledApps.Count == 0)
            {
                var apps = await UninstallerAndOrphanService.GetInstalledAppsAsync();
                foreach (var a in apps) InstalledApps.Add(a);
            }

            var orphans = await UninstallerAndOrphanService.ScanOrphanFoldersAsync(InstalledApps, token);
            foreach (var orphan in orphans)
                OrphanFolders.Add(orphan);

            long totalBytes = orphans.Sum(x => x.SizeBytes);
            StatusMessage = $"Yetim Klasör Taraması: {orphans.Count} olası kalıntı klasör bulundu ({CleanerRule.FormatBytes(totalBytes)}).";
            AddLog($"[KALINTI TARAMASI] {orphans.Count} yetim AppData klasörü tespit edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CleanSelectedOrphanFolders()
    {
        var selected = OrphanFolders.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        long bytes = selected.Sum(x => x.SizeBytes);
        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} yetim uygulama klasörü ({CleanerRule.FormatBytes(bytes)}) silinecek.\n\nDevam etmek istiyor musunuz?",
            "Yetim Klasör Temizliği",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        long freed = 0;
        foreach (var folder in selected)
        {
            try
            {
                if (Directory.Exists(folder.FullPath))
                {
                    Directory.Delete(folder.FullPath, recursive: true);
                    deleted++;
                    freed += folder.SizeBytes;
                    OrphanFolders.Remove(folder);
                }
            }
            catch
            {
                // Skip locked folder
            }
        }

        LastFreedBytes += freed;
        LastDeletedFiles += deleted;
        StatusMessage = $"{deleted} yetim klasör temizlendi ({CleanerRule.FormatBytes(freed)}).";
        AddLog($"[KALINTI TEMİZLİĞİ] {deleted} yetim klasör silindi ({CleanerRule.FormatBytes(freed)}).");
    }

}
