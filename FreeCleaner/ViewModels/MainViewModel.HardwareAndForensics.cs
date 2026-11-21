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
    // ==================== 23. 5TH GEN FORENSICS, WI-FI, PACKAGE CACHES, RESTORE POINT & FILEASSOC ====================

    public string RestorePointDescription
    {
        get => _restorePointDescription;
        set
        {
            if (_restorePointDescription != value)
            {
                _restorePointDescription = value;
                OnPropertyChanged();
            }
        }
    }

    private async Task LoadCrashAndEventLogsAsync()
    {
        IsBusy = true;
        CrashAndEventLogs.Clear();
        StatusMessage = "Mavi ekran dökümleri (.dmp), uygulama çökmeleri ve Windows Olay Günlüğü (EventLog) taranıyor...";
        try
        {
            var items = await NextGenForensicsAndRescueService.ScanCrashAndEventLogsAsync();
            foreach (var item in items)
                CrashAndEventLogs.Add(item);

            int dmpCount = items.Count(x => !string.IsNullOrWhiteSpace(x.DumpFilePath));
            StatusMessage = $"Olay Günlüğü & Çökme Analizi: {items.Count} hata/çökme kaydı ({dmpCount} adet .dmp döküm dosyası) bulundu.";
            AddLog($"[ÇÖKME ANALİZİ] {items.Count} olay kaydı ve {dmpCount} çökme dökümü tespit edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCleanCrashDumpsAsync()
    {
        var dumps = CrashAndEventLogs.Where(x => !string.IsNullOrWhiteSpace(x.DumpFilePath)).ToList();
        if (dumps.Count == 0) return;

        IsBusy = true;
        try
        {
            var (deleted, freed) = await NextGenForensicsAndRescueService.CleanCrashDumpsAsync(dumps);
            LastFreedBytes += freed;
            LastDeletedFiles += deleted;
            RefreshSystemMetrics();
            await LoadCrashAndEventLogsAsync();

            StatusMessage = $"{deleted} çökme dökümü (.dmp) silindi, {CleanerRule.FormatBytes(freed)} yer açıldı.";
            AddLog($"[ÇÖKME DÖKÜMÜ TEMİZLİĞİ] {deleted} adet .dmp dosyası temizlendi ({CleanerRule.FormatBytes(freed)}).");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadWifiAndDnsAsync()
    {
        IsBusy = true;
        WifiAndDnsItems.Clear();
        StatusMessage = "Kayıtlı Wi-Fi ağ şifreleri (netsh wlan) okunuyor ve DNS sunucularına canlı Ping testi yapılıyor...";
        try
        {
            var items = await NextGenForensicsAndRescueService.ScanWifiPasswordsAndPingDnsAsync();
            foreach (var item in items)
                WifiAndDnsItems.Add(item);

            StatusMessage = $"Wi-Fi & DNS Analizi tamamlandı ({items.Count} kayıt listelendi).";
            AddLog($"[WI-FI & DNS] {items.Count} Wi-Fi profili ve DNS ping sonucu yüklendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadGlobalPackageCachesAsync()
    {
        IsBusy = true;
        GlobalPackageCaches.Clear();
        StatusMessage = "NuGet, NPM, Pip, PNPM, Yarn, Gradle, Maven önbellekleri ve WSL2/Docker sanal diskleri (.vhdx) taranıyor...";
        try
        {
            var caches = await NextGenForensicsAndRescueService.ScanGlobalPackageCachesAsync();
            foreach (var c in caches)
                GlobalPackageCaches.Add(c);

            long totalBytes = caches.Sum(x => x.SizeBytes);
            StatusMessage = $"Küresel Paket & VHDX Analizi: {caches.Count} öğede toplam {CleanerRule.FormatBytes(totalBytes)} tespit edildi.";
            AddLog($"[PAKET ÖNBELLEKLERİ] {caches.Count} küresel önbellek/VHDX ({CleanerRule.FormatBytes(totalBytes)}) bulundu.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCleanPackageCachesAsync()
    {
        var selected = GlobalPackageCaches.Where(x => x.IsSelected && x.CanSafeDelete).ToList();
        if (selected.Count == 0) return;

        long totalSize = selected.Sum(x => x.SizeBytes);
        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} küresel paket önbelleği ({CleanerRule.FormatBytes(totalSize)}) temizlenecek.\n\nDevam edilsin mi?",
            "Küresel Paket Önbelleği Temizliği",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var (cleaned, freed) = await NextGenForensicsAndRescueService.CleanSelectedPackageCachesAsync(selected);
            LastFreedBytes += freed;
            LastDeletedFiles += cleaned;
            RecordCleanupHistory("Paket Önbellekleri", freed, cleaned);
            RefreshSystemMetrics();
            await LoadGlobalPackageCachesAsync();

            StatusMessage = $"{cleaned} küresel paket önbelleği temizlendi ({CleanerRule.FormatBytes(freed)} yer açıldı).";
            AddLog($"[PAKET ÖNBELLEKLERİ] {cleaned} önbellek temizlendi ({CleanerRule.FormatBytes(freed)}).");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCompactVhdxAsync(GlobalPackageCacheItem item)
    {
        if (IsBusy || !item.IsVhdxVirtualDisk) return;

        var confirm = MessageBox.Show(
            $"'{item.CacheName}' ({item.FormattedSize}) sanal diski DiskPart (compact vdisk) ile boş bloklardan arındırılacak.\n\n" +
            $"Yol: {item.FolderPath}\n\n" +
            "Not: Yönetici yetkisi gerekir ve ilgili uygulama (Claude Desktop / Docker Desktop / WSL2) tamamen kapalı olmalıdır. Devam edilsin mi?",
            "VHDX Sanal Disk Sıkıştırma",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        var vhdxLockSpecs = ProcessLockGuardService.ResolveTargetProcessSpecsForVhdxPath(item.FolderPath);
        var vhdxConflicts = ProcessLockGuardService.DetectRunningConflicts(vhdxLockSpecs);
        if (vhdxConflicts.Count > 0)
        {
            string conflictNames = string.Join("\n", vhdxConflicts.Select(g => $"• {g.FriendlyName} ({g.RunningInstanceCount} işlem)"));
            var closeChoice = MessageBox.Show(
                $"Bu sanal diski kilitleyen çalışan süreçler algılandı:\n\n{conflictNames}\n\n" +
                "DiskPart compact vdisk'in başarılı olması için bu süreçler (ve gerekirse WSL2) kapatılmalıdır.\n\n" +
                "• Evet: İlgili uygulamaları / WSL2'yi şimdi kapatıp sıkıştır\n" +
                "• Hayır: Kapatmadan dene\n" +
                "• İptal: Vazgeç",
                "Sanal Disk Kilit Koruması (Process Lock Guard)",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (closeChoice == MessageBoxResult.Cancel)
            {
                AddLog($"[VHDX KORUMA] Kullanıcı kilit uyarısı üzerine '{item.CacheName}' sıkıştırmasını iptal etti.");
                return;
            }

            if (closeChoice == MessageBoxResult.Yes)
            {
                StatusMessage = "Sanal diski kilitleyen süreçler kapatılıyor...";
                var (closedCount, notes) = await ProcessLockGuardService.TryCloseProcessGroupsAsync(vhdxConflicts);
                AddLog($"[VHDX KORUMA] {closedCount} süreç kapatıldı.");
                foreach (string note in notes)
                    AddLog($"[VHDX KORUMA] {note}");
            }
        }

        IsBusy = true;
        ProgressPercent = 25;
        StatusMessage = $"VHDX sıkıştırılıyor (diskpart compact vdisk): {item.FolderPath}";

        try
        {
            var (success, freed, message) = await NextGenForensicsAndRescueService.CompactVhdxVirtualDiskAsync(item.FolderPath);
            if (success && freed > 0)
            {
                LastFreedBytes += freed;
                RecordCleanupHistory("VHDX Sıkıştırma", freed, 1);
                RefreshSystemMetrics();
            }

            if (success)
                await LoadGlobalPackageCachesAsync();

            ProgressPercent = 100;
            StatusMessage = message;
            AddLog($"[VHDX SIKISTIRMA] {message}");
            if (!success)
                MessageBox.Show(message, "VHDX Sıkıştırma Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task LoadRestorePointsAsync()
    {
        IsBusy = true;
        RestorePoints.Clear();
        StatusMessage = "Windows Sistem Geri Yükleme Noktaları sorgulanıyor...";
        try
        {
            var pts = await NextGenForensicsAndRescueService.GetRestorePointsAsync();
            foreach (var p in pts)
                RestorePoints.Add(p);
            StatusMessage = $"{RestorePoints.Count} Sistem Geri Yükleme kaydı listelendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCreateRestorePointAsync()
    {
        IsBusy = true;
        StatusMessage = "Yeni Windows Sistem Geri Yükleme Noktası oluşturuluyor (UAC izni gerekebilir)...";
        try
        {
            var (ok, msg) = await NextGenForensicsAndRescueService.CreateSystemRestorePointAsync(RestorePointDescription);
            StatusMessage = msg;
            AddLog($"[GERİ YÜKLEME NOKTASI] {msg}");
            await LoadRestorePointsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExecuteOpenGodMode()
    {
        var (ok, msg) = NextGenForensicsAndRescueService.CreateAndOpenGodModeFolder();
        StatusMessage = msg;
        AddLog($"[GOD MODE] {msg}");
    }

    private async Task LoadFileAssociationsAsync()
    {
        IsBusy = true;
        FileAssociations.Clear();
        StatusMessage = "Dosya uzantıları ve varsayılan program (UserChoice / ProgId) birliktelikleri denetleniyor...";
        try
        {
            var items = await NextGenForensicsAndRescueService.ScanFileAssociationsAsync();
            foreach (var item in items)
                FileAssociations.Add(item);
            StatusMessage = $"{FileAssociations.Count} popüler dosya uzantısının kayıt defteri ilişkisi denetlendi.";
            AddLog($"[DOSYA UZANTILARI] {FileAssociations.Count} uzantı incelendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExecuteResetFileAssociations()
    {
        var selected = FileAssociations.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        int count = NextGenForensicsAndRescueService.ResetSelectedFileAssociations(selected);
        _ = LoadFileAssociationsAsync();
        StatusMessage = $"{count} dosya uzantısının OpenWith / UserChoice kaydı sıfırlandı.";
        AddLog($"[DOSYA UZANTILARI] {count} uzantı sıfırlandı.");
    }

    // ==================== 24. 6TH GEN PORT HUNTER, BLOATWARE, BENCHMARK, HARDWARE & USB ====================

    public string PortFilterText
    {
        get => _portFilterText;
        set
        {
            if (_portFilterText != value)
            {
                _portFilterText = value;
                OnPropertyChanged();
            }
        }
    }

    public string BenchmarkDriveRoot
    {
        get => _benchmarkDriveRoot;
        set
        {
            if (_benchmarkDriveRoot != value)
            {
                _benchmarkDriveRoot = value;
                OnPropertyChanged();
            }
        }
    }

    public BenchmarkDriveOption? SelectedBenchmarkDrive
    {
        get => _selectedBenchmarkDrive;
        set
        {
            if (_selectedBenchmarkDrive != value)
            {
                _selectedBenchmarkDrive = value;
                OnPropertyChanged();
                if (value is not null)
                {
                    BenchmarkDriveRoot = value.DriveRoot;
                }
            }
        }
    }

    public int BenchmarkBlockSizeMb
    {
        get => _benchmarkBlockSizeMb;
        set
        {
            if (_benchmarkBlockSizeMb != value)
            {
                _benchmarkBlockSizeMb = value;
                OnPropertyChanged();
            }
        }
    }

    private async Task LoadBenchmarkDrivesAsync()
    {
        IsBusy = true;
        AvailableBenchmarkDrives.Clear();
        StatusMessage = "Sistemdeki tüm SSD / NVMe / sabit sürücüler ve fiziksel disk modelleri taranıyor...";
        try
        {
            var drives = await SixthGenHardwareAndDevSuiteService.GetAvailableBenchmarkDrivesAsync();
            foreach (var d in drives)
                AvailableBenchmarkDrives.Add(d);

            SelectedBenchmarkDrive = AvailableBenchmarkDrives.FirstOrDefault();
            StatusMessage = $"Disk Hız Testi için {AvailableBenchmarkDrives.Count} aktif sürücü listelendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadListeningPortsAsync()
    {
        IsBusy = true;
        ListeningPorts.Clear();
        StatusMessage = "Dinlenen (LISTENING) TCP/UDP portları ve bunları tutan işlemler (PID) sorgulanıyor...";
        try
        {
            var ports = await SixthGenHardwareAndDevSuiteService.GetListeningPortsAsync(PortFilterText);
            foreach (var p in ports)
                ListeningPorts.Add(p);

            StatusMessage = $"Port Avcısı: {ListeningPorts.Count} aktif dinlenen port ve işlem listelendi.";
            AddLog($"[PORT AVCISI] {ListeningPorts.Count} dinlenen port listelendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task KillPortAndRefreshAsync(ListeningPortItem item)
    {
        var confirm = MessageBox.Show(
            $"Port {item.PortNumber} ({item.Protocol}) şu anda '{item.ProcessName}' (PID: {item.ProcessId}) tarafından kullanılıyor.\n\nBu işlemi sonlandırıp portu serbest bırakmak istiyor musunuz?",
            "Portu Boşalt (Kill Process)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (SixthGenHardwareAndDevSuiteService.KillProcessByPortPid(item.ProcessId, item.PortNumber, out string msg))
        {
            StatusMessage = msg;
            AddLog($"[PORT AVCISI] {msg}");
            await LoadListeningPortsAsync();
        }
        else
        {
            MessageBox.Show(msg, "Port Avcısı Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task LoadAppxBloatwareAsync()
    {
        IsBusy = true;
        AppxBloatwareItems.Clear();
        StatusMessage = "Windows 10/11 önyüklü mağaza uygulamaları (Get-AppxPackage) taranıyor...";
        try
        {
            var pkgs = await SixthGenHardwareAndDevSuiteService.ScanAppxBloatwareAsync();
            foreach (var pkg in pkgs)
                AppxBloatwareItems.Add(pkg);

            int bloatCount = pkgs.Count(x => x.IsSelected);
            StatusMessage = $"Bloatware Tarayıcı: {pkgs.Count} UWP paketi listelendi ({bloatCount} adet önerilen bloatware işaretlendi).";
            AddLog($"[BLOATWARE] {pkgs.Count} Appx paketi bulundu.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunRemoveSelectedAppxAsync()
    {
        var selected = AppxBloatwareItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} Windows mağaza uygulaması (Appx) kaldırılacak.\n\nDevam edilsin mi?",
            "Bloatware Kaldırıcı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusMessage = $"Seçili {selected.Count} Appx paketi kaldırılıyor (Remove-AppxPackage)...";
        try
        {
            int removed = await SixthGenHardwareAndDevSuiteService.RemoveSelectedAppxPackagesAsync(selected);
            await LoadAppxBloatwareAsync();
            StatusMessage = $"{removed} adet Windows mağaza uygulaması (Bloatware) başarıyla kaldırıldı.";
            AddLog($"[BLOATWARE] {removed} Appx paketi sistemden kaldırıldı.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteDiskBenchmarkAsync()
    {
        IsBusy = true;
        StatusMessage = $"'{BenchmarkDriveRoot}' sürücüsünde {BenchmarkBlockSizeMb} MB sıralı Yazma ve Okuma hız testi (Benchmark) yapılıyor...";
        AddLog($"[DİSK HIZ TESTİ] {BenchmarkDriveRoot} üzerinde {BenchmarkBlockSizeMb} MB okuma/yazma testi başlatıldı...");
        try
        {
            var res = await SixthGenHardwareAndDevSuiteService.RunDiskBenchmarkAsync(BenchmarkDriveRoot, BenchmarkBlockSizeMb);
            DiskBenchmarkResults.Insert(0, res);
            StatusMessage = $"Disk Hız Testi Tamamlandı! Yazma: {res.WriteFormatted} • Okuma: {res.ReadFormatted} ({res.PerformanceGrade})";
            AddLog($"[DİSK HIZ TESTİ] {res.DriveName} -> Yazma: {res.WriteFormatted}, Okuma: {res.ReadFormatted} ({res.PerformanceGrade})");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHardwareSpecsAsync()
    {
        IsBusy = true;
        HardwareSpecs.Clear();
        StatusMessage = "İşlemci, Anakart, BIOS, RAM MHz, GPU ve OEM Windows Ürün Anahtarı bilgileri okunuyor...";
        try
        {
            var specs = await SixthGenHardwareAndDevSuiteService.GetHardwareAndLicenseInventoryAsync();
            foreach (var s in specs)
                HardwareSpecs.Add(s);
            StatusMessage = $"Donanım & Lisans Envanteri: {HardwareSpecs.Count} donanım bileşeni ve lisans kaydı okundu.";
            AddLog($"[DONANIM & BIOS] {HardwareSpecs.Count} donanım bileşeni listelendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadUsbHistoryAsync()
    {
        IsBusy = true;
        UsbHistoryItems.Clear();
        StatusMessage = "Bilgisayara bugüne kadar takılmış USB depolama aygıtları (HKLM\\...\\USBSTOR) taranıyor...";
        try
        {
            var items = await SixthGenHardwareAndDevSuiteService.ScanUsbStorageHistoryAsync();
            foreach (var item in items)
                UsbHistoryItems.Add(item);
            StatusMessage = $"USB Adli Analiz: Geçmişte takılmış {UsbHistoryItems.Count} USB depolama cihazı tespit edildi.";
            AddLog($"[USB GEÇMİŞİ] {UsbHistoryItems.Count} USBSTOR cihaz kaydı bulundu.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExecuteCleanUsbHistory()
    {
        var selected = UsbHistoryItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        int cleaned = SixthGenHardwareAndDevSuiteService.CleanSelectedUsbHistory(selected);
        _ = LoadUsbHistoryAsync();
        StatusMessage = $"{cleaned} USB cihaz geçmiş kaydı temizlendi (Yönetici izni gerektirebilir).";
        AddLog($"[USB GEÇMİŞİ] {cleaned} USBSTOR kaydı silindi.");
    }
}
