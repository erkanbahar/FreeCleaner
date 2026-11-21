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
    // ==================== 13. 7TH GEN SECURITY, BATCH RENAMER, NET ADAPTERS, FONTS & CLIPBOARD SHIELD ====================

    private bool _onlyUnsignedProcessesFilter;
    public bool OnlyUnsignedProcessesFilter
    {
        get => _onlyUnsignedProcessesFilter;
        set
        {
            if (_onlyUnsignedProcessesFilter != value)
            {
                _onlyUnsignedProcessesFilter = value;
                OnPropertyChanged();
                _ = LoadProcessSecurityItemsAsync();
            }
        }
    }

    private string _batchRenameFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public string BatchRenameFolderPath
    {
        get => _batchRenameFolderPath;
        set
        {
            if (_batchRenameFolderPath != value)
            {
                _batchRenameFolderPath = value;
                OnPropertyChanged();
            }
        }
    }

    private string _batchRenameFindText = string.Empty;
    public string BatchRenameFindText
    {
        get => _batchRenameFindText;
        set
        {
            if (_batchRenameFindText != value)
            {
                _batchRenameFindText = value;
                OnPropertyChanged();
                GenerateBatchRenamePreview();
            }
        }
    }

    private string _batchRenameReplaceText = string.Empty;
    public string BatchRenameReplaceText
    {
        get => _batchRenameReplaceText;
        set
        {
            if (_batchRenameReplaceText != value)
            {
                _batchRenameReplaceText = value;
                OnPropertyChanged();
                GenerateBatchRenamePreview();
            }
        }
    }

    private string _batchRenamePrefixText = string.Empty;
    public string BatchRenamePrefixText
    {
        get => _batchRenamePrefixText;
        set
        {
            if (_batchRenamePrefixText != value)
            {
                _batchRenamePrefixText = value;
                OnPropertyChanged();
                GenerateBatchRenamePreview();
            }
        }
    }

    private string _batchRenameSuffixText = string.Empty;
    public string BatchRenameSuffixText
    {
        get => _batchRenameSuffixText;
        set
        {
            if (_batchRenameSuffixText != value)
            {
                _batchRenameSuffixText = value;
                OnPropertyChanged();
                GenerateBatchRenamePreview();
            }
        }
    }

    private bool _batchRenameAddNumbers;
    public bool BatchRenameAddNumbers
    {
        get => _batchRenameAddNumbers;
        set
        {
            if (_batchRenameAddNumbers != value)
            {
                _batchRenameAddNumbers = value;
                OnPropertyChanged();
                GenerateBatchRenamePreview();
            }
        }
    }

    private string _batchRenameCaseMode = "Değiştirme (Olduğu Gibi)";
    public string BatchRenameCaseMode
    {
        get => _batchRenameCaseMode;
        set
        {
            if (_batchRenameCaseMode != value)
            {
                _batchRenameCaseMode = value;
                OnPropertyChanged();
                GenerateBatchRenamePreview();
            }
        }
    }

    private string _fontSearchFilter = string.Empty;
    public string FontSearchFilter
    {
        get => _fontSearchFilter;
        set
        {
            if (_fontSearchFilter != value)
            {
                _fontSearchFilter = value;
                OnPropertyChanged();
            }
        }
    }

    private async Task LoadProcessSecurityItemsAsync()
    {
        IsBusy = true;
        ProcessSecurityItems.Clear();
        StatusMessage = "Çalışan tüm işlemlerin dijital imzaları (Authenticode X.509) ve SHA-256 özetleri denetleniyor...";
        try
        {
            var items = await SeventhGenSecurityAndPowerSuiteService.ScanRunningProcessSecurityAsync(OnlyUnsignedProcessesFilter);
            foreach (var item in items)
                ProcessSecurityItems.Add(item);

            int unsignedCount = ProcessSecurityItems.Count(x => x.IsUnsignedOrSuspicious);
            StatusMessage = $"Süreç Güvenlik Denetimi: {ProcessSecurityItems.Count} işlem incelendi ({unsignedCount} imzasız veya dikkat gerektiren işlem).";
            AddLog($"[SÜREÇ GÜVENLİĞİ] {ProcessSecurityItems.Count} işlem tarandı, {unsignedCount} imzasız/şüpheli tespit edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task KillSecurityProcessAndRefreshAsync(ProcessSecurityAuditItem item)
    {
        if (SixthGenHardwareAndDevSuiteService.KillProcessByPortPid(item.ProcessId, 0, out string msg))
        {
            AddLog($"[SÜREÇ GÜVENLİĞİ] {item.ProcessName} (PID: {item.ProcessId}) sonlandırıldı.");
            StatusMessage = $"{item.ProcessName} (PID: {item.ProcessId}) başarıyla sonlandırıldı.";
            await LoadProcessSecurityItemsAsync();
        }
        else
        {
            MessageBox.Show(msg, "İşlem Sonlandırılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BrowseRenameFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Toplu Yeniden Adlandırılacak Klasörü Seçin"
        };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            BatchRenameFolderPath = dialog.FolderName;
            GenerateBatchRenamePreview();
        }
    }

    private void GenerateBatchRenamePreview()
    {
        BatchRenameItems.Clear();
        var items = SeventhGenSecurityAndPowerSuiteService.BuildBatchRenamePreview(
            BatchRenameFolderPath,
            BatchRenameFindText,
            BatchRenameReplaceText,
            BatchRenamePrefixText,
            BatchRenameSuffixText,
            BatchRenameAddNumbers,
            BatchRenameCaseMode);

        foreach (var item in items)
            BatchRenameItems.Add(item);

        int changedCount = BatchRenameItems.Count(x => x.IsSelected);
        StatusMessage = $"PowerRename Önizleme: Klasörde {BatchRenameItems.Count} dosya bulundu ({changedCount} dosya yeniden adlandırılacak).";
    }

    private void ExecuteApplyBatchRename()
    {
        var (count, msg) = SeventhGenSecurityAndPowerSuiteService.ApplyBatchRename(BatchRenameItems);
        StatusMessage = msg;
        AddLog($"[TOPLU ADLANDIRICI] {msg}");
        GenerateBatchRenamePreview();
    }

    private void ExecuteUndoBatchRename()
    {
        var (_, msg) = SeventhGenSecurityAndPowerSuiteService.UndoLastBatchRename();
        StatusMessage = msg;
        AddLog($"[TOPLU ADLANDIRICI - GERİ AL] {msg}");
        GenerateBatchRenamePreview();
    }

    private async Task LoadNetworkAdaptersAsync()
    {
        IsBusy = true;
        NetworkAdapters.Clear();
        StatusMessage = "Fiziksel ve sanal ağ adaptörleri, MAC adresleri, IPv4, Ağ Geçidi ve DNS yapılandırmaları okunuyor...";
        try
        {
            var items = await SeventhGenSecurityAndPowerSuiteService.GetNetworkAdaptersAsync();
            foreach (var item in items)
                NetworkAdapters.Add(item);
            StatusMessage = $"Ağ Adaptörü Doktoru: {NetworkAdapters.Count} ağ arayüzü (Ethernet, Wi-Fi, Sanal Adaptör) listelendi.";
            AddLog($"[AĞ ADAPTÖRÜ DOKTORU] {NetworkAdapters.Count} ağ adaptörü incelendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteNetworkRepairActionAsync(string actionKey)
    {
        IsBusy = true;
        StatusMessage = "Ağ protokol ve önbellek onarım komutu uygulanıyor...";
        try
        {
            var (_, msg) = await SeventhGenSecurityAndPowerSuiteService.RunNetworkRepairActionAsync(actionKey);
            StatusMessage = msg;
            AddLog($"[AĞ ONARIM] {msg}");
            await LoadNetworkAdaptersAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadInstalledFontsAsync()
    {
        IsBusy = true;
        InstalledFonts.Clear();
        StatusMessage = "Windows Sistem Fontları ve Kullanıcı Fontları (.ttf, .otf, .ttc) taranıyor...";
        try
        {
            var fonts = await SeventhGenSecurityAndPowerSuiteService.ScanInstalledFontsAsync(FontSearchFilter);
            foreach (var f in fonts)
                InstalledFonts.Add(f);
            int userCount = InstalledFonts.Count(x => x.IsUserFont);
            StatusMessage = $"Font Yöneticisi: {InstalledFonts.Count} yazı tipi listelendi ({userCount} kullanıcı fontu).";
            AddLog($"[FONT YÖNETİCİSİ] {InstalledFonts.Count} yazı tipi yüklendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExecuteRemoveSelectedUserFonts()
    {
        var (_, _, msg) = SeventhGenSecurityAndPowerSuiteService.RemoveSelectedUserFonts(InstalledFonts);
        StatusMessage = msg;
        AddLog($"[FONT YÖNETİCİSİ] {msg}");
        _ = LoadInstalledFontsAsync();
    }

    private void ExecuteInspectClipboardNow()
    {
        try
        {
            string? text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            var entry = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText(text);
            ClipboardAuditItems.Insert(0, entry);
            StatusMessage = $"Pano Kalkanı: {entry.CategoryBadge} — {entry.SecurityAdvice}";
            AddLog($"[PANO KALKANI] {entry.CategoryBadge}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pano okunamadı: {ex.Message}";
        }
    }

    private void ExecuteClearClipboardNow()
    {
        try
        {
            Clipboard.Clear();
            ExecuteInspectClipboardNow();
            StatusMessage = "Windows Panosu (Clipboard) tamamen sıfırlandı ve hassas veriler temizlendi.";
            AddLog("[PANO KALKANI] Pano tamamen temizlendi.");
        }
        catch { }
    }

    // ==================== 14. 8TH GEN AI MODEL CLEANER, FIREWALL BLOCKER, AES-256 VAULT, WIN11 TWEAKER & BITLOCKER ====================

    private string _aiCustomScanFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string AiCustomScanFolder
    {
        get => _aiCustomScanFolder;
        set
        {
            if (_aiCustomScanFolder != value)
            {
                _aiCustomScanFolder = value;
                OnPropertyChanged();
            }
        }
    }

    private string _vaultTargetFilePath = string.Empty;
    public string VaultTargetFilePath
    {
        get => _vaultTargetFilePath;
        set
        {
            if (_vaultTargetFilePath != value)
            {
                _vaultTargetFilePath = value;
                OnPropertyChanged();
            }
        }
    }

    private string _vaultPasswordInput = string.Empty;
    public string VaultPasswordInput
    {
        get => _vaultPasswordInput;
        set
        {
            if (_vaultPasswordInput != value)
            {
                _vaultPasswordInput = value;
                OnPropertyChanged();
            }
        }
    }

    private string _expectedHashInput = string.Empty;
    public string ExpectedHashInput
    {
        get => _expectedHashInput;
        set
        {
            if (_expectedHashInput != value)
            {
                _expectedHashInput = value;
                OnPropertyChanged();
            }
        }
    }

    private void BrowseAiScanFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "GGUF / SafeTensors / ONNX Modelleri İçin Taranacak Klasörü Seçin"
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            AiCustomScanFolder = dlg.FolderName;
            _ = LoadAiModelCachesAsync();
        }
    }

    private async Task LoadAiModelCachesAsync()
    {
        IsBusy = true;
        AiModelCaches.Clear();
        StatusMessage = "Ollama, HuggingFace, LM Studio, PyTorch, Whisper önbellekleri ve büyük AI model dosyaları (.gguf/.safetensors) taranıyor...";
        try
        {
            var items = await EighthGenAiAndSecuritySuiteService.ScanAiModelCachesAsync(AiCustomScanFolder);
            foreach (var item in items)
                AiModelCaches.Add(item);

            long totalBytes = AiModelCaches.Sum(x => x.SizeBytes);
            StatusMessage = $"Yerel AI & LLM Avcısı: {AiModelCaches.Count} yapay zeka model/önbellek öğesi bulundu (Toplam: {CleanerRule.FormatBytes(totalBytes)}).";
            AddLog($"[AI / LLM AVCISI] {AiModelCaches.Count} öğe tespit edildi ({CleanerRule.FormatBytes(totalBytes)}).");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteCleanAiCachesAsync()
    {
        var selected = AiModelCaches.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        IsBusy = true;
        try
        {
            var (cleaned, freed) = await EighthGenAiAndSecuritySuiteService.CleanSelectedAiCachesAsync(selected);
            LastFreedBytes += freed;
            LastDeletedFiles += cleaned;
            StatusMessage = $"{cleaned} AI model/önbellek öğesi temizlendi, {CleanerRule.FormatBytes(freed)} disk alanı kurtarıldı!";
            AddLog($"[AI / LLM AVCISI] {cleaned} model önbelleği silindi ({CleanerRule.FormatBytes(freed)} kazanıldı).");
            await LoadAiModelCachesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadFirewallBlockRulesAsync()
    {
        IsBusy = true;
        FirewallBlockRules.Clear();
        StatusMessage = "Windows Defender Güvenlik Duvarı (Firewall) engelleme kuralları okunuyor...";
        try
        {
            var rules = await EighthGenAiAndSecuritySuiteService.GetFirewallBlockRulesAsync();
            foreach (var r in rules)
                FirewallBlockRules.Add(r);
            StatusMessage = $"Güvenlik Duvarı Denetçisi: {FirewallBlockRules.Count} aktif engelleme (Block) kuralı listelendi.";
            AddLog($"[FIREWALL] {FirewallBlockRules.Count} engelleme kuralı yüklendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BrowseAndBlockExecutableInFirewallAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "İnternet Erişimi Kesilecek Uygulamayı (.exe) Seçin",
            Filter = "Uygulama Dosyaları (*.exe)|*.exe|Tüm Dosyalar (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FileName))
            return;

        IsBusy = true;
        try
        {
            var (_, msg) = await EighthGenAiAndSecuritySuiteService.BlockExecutableInFirewallAsync(dlg.FileName);
            StatusMessage = msg;
            AddLog($"[FIREWALL BLOCKER] {msg}");
            await LoadFirewallBlockRulesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteRemoveSelectedFirewallRulesAsync()
    {
        IsBusy = true;
        try
        {
            var (_, msg) = await EighthGenAiAndSecuritySuiteService.RemoveSelectedFirewallRulesAsync(FirewallBlockRules);
            StatusMessage = msg;
            AddLog($"[FIREWALL BLOCKER] {msg}");
            await LoadFirewallBlockRulesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowseVaultTargetFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Şifrelenecek, Çözülecek (.fcvault) veya Hash Doğrulaması Yapılacak Dosyayı Seçin",
            Filter = "Tüm Dosyalar (*.*)|*.*|FreeCleaner Şifreli Kasa (*.fcvault)|*.fcvault"
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FileName))
        {
            VaultTargetFilePath = dlg.FileName;
            _ = ExecuteComputeMultiHashesAsync();
        }
    }

    private async Task ExecuteComputeMultiHashesAsync()
    {
        if (string.IsNullOrWhiteSpace(VaultTargetFilePath) || !File.Exists(VaultTargetFilePath))
        {
            StatusMessage = "Lütfen önce geçerli bir dosya seçin.";
            return;
        }

        IsBusy = true;
        CryptoVaultResults.Clear();
        StatusMessage = "Dosya için MD5, SHA-1, SHA-256 ve SHA-512 kriptografik özetleri hesaplanıyor...";
        try
        {
            var rows = await EighthGenAiAndSecuritySuiteService.ComputeMultiHashesAsync(VaultTargetFilePath, ExpectedHashInput);
            foreach (var r in rows)
                CryptoVaultResults.Add(r);
            StatusMessage = $"Çoklu Hash Doğrulayıcı: '{Path.GetFileName(VaultTargetFilePath)}' için 4 algoritma özeti hesaplandı.";
            AddLog($"[HASH & VAULT] '{Path.GetFileName(VaultTargetFilePath)}' MD5/SHA1/SHA256/SHA512 hesaplandı.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteEncryptVaultFileAsync()
    {
        IsBusy = true;
        try
        {
            var (ok, outPath, msg) = await EighthGenAiAndSecuritySuiteService.EncryptFileAes256GcmAsync(VaultTargetFilePath, VaultPasswordInput);
            StatusMessage = msg;
            AddLog($"[AES-256 KASA] {msg}");
            if (ok && !string.IsNullOrEmpty(outPath))
            {
                CryptoVaultResults.Insert(0, new FileHashAndVaultResultItem
                {
                    AlgorithmOrOperation = "🔒 AES-256-GCM Şifreleme",
                    FileName = Path.GetFileName(outPath),
                    HashOrOutputValue = outPath,
                    VerificationStatus = "✅ Şifreli Kasa Oluşturuldu"
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteDecryptVaultFileAsync()
    {
        IsBusy = true;
        try
        {
            var (ok, outPath, msg) = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(VaultTargetFilePath, VaultPasswordInput);
            StatusMessage = msg;
            AddLog($"[AES-256 KASA] {msg}");
            if (ok && !string.IsNullOrEmpty(outPath))
            {
                CryptoVaultResults.Insert(0, new FileHashAndVaultResultItem
                {
                    AlgorithmOrOperation = "🔓 AES-256-GCM Şifre Çözme",
                    FileName = Path.GetFileName(outPath),
                    HashOrOutputValue = outPath,
                    VerificationStatus = "✅ Orijinal Dosya Kurtarıldı"
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadWindowsTweaks()
    {
        WindowsTweaks.Clear();
        foreach (var t in EighthGenAiAndSecuritySuiteService.GetWindowsTweaksState())
            WindowsTweaks.Add(t);
        StatusMessage = $"Windows 11 İnce Ayar Merkezi: {WindowsTweaks.Count} sistem ve Explorer ince ayarı yüklendi.";
    }

    private void ExecuteToggleWindowsTweak(WindowsTweakItem tweak)
    {
        var (_, msg) = EighthGenAiAndSecuritySuiteService.ToggleWindowsTweak(tweak);
        StatusMessage = msg;
        AddLog($"[WIN11 TWEAKER] {msg}");
        LoadWindowsTweaks();
    }

    private void ExecuteRestartExplorerNow()
    {
        EighthGenAiAndSecuritySuiteService.RestartWindowsExplorer();
        StatusMessage = "Windows Gezgini (explorer.exe) yeniden başlatıldı; tüm kabuk ve görev çubuğu ayarları etkinleştirildi.";
        AddLog("[WIN11 TWEAKER] explorer.exe yeniden başlatıldı.");
    }

    private async Task LoadVolumeSecurityAndMountsAsync()
    {
        IsBusy = true;
        VolumeSecurityItems.Clear();
        StatusMessage = "Disk bölümlerinin BitLocker şifreleme durumu ve bağlı ISO/VHD sanal sürücüler taranıyor...";
        try
        {
            var items = await EighthGenAiAndSecuritySuiteService.GetVolumesAndVirtualDisksAsync();
            foreach (var item in items)
                VolumeSecurityItems.Add(item);
            StatusMessage = $"BitLocker & Sanal Disk Denetçisi: {VolumeSecurityItems.Count} aktif birim ve sanal imaj listelendi.";
            AddLog($"[BITLOCKER & VHD] {VolumeSecurityItems.Count} birim denetlendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BrowseAndMountIsoOrVhdAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sanal Sürücü Olarak Bağlanacak (Mount) ISO / VHD / VHDX İmajını Seçin",
            Filter = "Disk İmajları (*.iso;*.vhd;*.vhdx;*.img)|*.iso;*.vhd;*.vhdx;*.img|Tüm Dosyalar (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FileName))
            return;

        IsBusy = true;
        try
        {
            var (_, msg) = await EighthGenAiAndSecuritySuiteService.MountDiskImageAsync(dlg.FileName);
            StatusMessage = msg;
            AddLog($"[ISO/VHD MOUNT] {msg}");
            await LoadVolumeSecurityAndMountsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteDismountVolumeAsync(VolumeSecurityAndMountItem vol)
    {
        IsBusy = true;
        try
        {
            var (_, msg) = await EighthGenAiAndSecuritySuiteService.DismountVolumeOrImageAsync(vol);
            StatusMessage = msg;
            AddLog($"[ISO/VHD DISMOUNT] {msg}");
            await LoadVolumeSecurityAndMountsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
