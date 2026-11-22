using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public partial class MainViewModel
{
    // ==================== 18. DISK TREEMAP, EXIF SCRUBBER, LANGUAGE SWITCHER & APP UPDATE CHECKER ====================

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            string normalized = LocalizationService.NormalizeLanguageCode(value);
            if (_selectedLanguage != normalized)
            {
                _selectedLanguage = normalized;
                LocalizationService.ApplyLanguage(_selectedLanguage);
                LocalizationService.ApplyRuleLocalization(AllRules, _selectedLanguage);
                FilteredRulesView.Refresh();
                RefreshExplorerContextMenuStatus();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SectionTitle));
                OnPropertyChanged(nameof(AdminStatusBadge));
                OnPropertyChanged(nameof(FavoriteButtonText));
                OnPropertyChanged(nameof(ActiveDataDirectoryDisplay));
                PopulateModuleCatalog();
                RebuildFavoriteNavItems();
                SaveCurrentSettings();
                StatusMessage = _selectedLanguage == "en-US"
                    ? "Language switched to English (en-US)."
                    : "Uygulama dili Türkçe (tr-TR) olarak güncellendi.";
                ShowToast("FreeCleaner Pro", StatusMessage, ToastSeverity.Info, durationSeconds: 4);
                AddLog($"[DİL / LANGUAGE] Arayüz dili '{_selectedLanguage}' olarak değiştirildi.");
            }
        }
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        set { if (_updateStatusText != value) { _updateStatusText = value; OnPropertyChanged(); } }
    }

    public string TreemapCurrentPath
    {
        get => _treemapCurrentPath;
        set { if (_treemapCurrentPath != value) { _treemapCurrentPath = value; OnPropertyChanged(); } }
    }

    public string TreemapSummaryText
    {
        get => _treemapSummaryText;
        set { if (_treemapSummaryText != value) { _treemapSummaryText = value; OnPropertyChanged(); } }
    }

    public string ExifScanFolderPath
    {
        get => _exifScanFolderPath;
        set { if (_exifScanFolderPath != value) { _exifScanFolderPath = value; OnPropertyChanged(); } }
    }

    public bool ExifIncludeSubfolders
    {
        get => _exifIncludeSubfolders;
        set { if (_exifIncludeSubfolders != value) { _exifIncludeSubfolders = value; OnPropertyChanged(); } }
    }

    public bool ExifCreateBackup
    {
        get => _exifCreateBackup;
        set { if (_exifCreateBackup != value) { _exifCreateBackup = value; OnPropertyChanged(); } }
    }

    public bool ExifOptimizeJpeg
    {
        get => _exifOptimizeJpeg;
        set { if (_exifOptimizeJpeg != value) { _exifOptimizeJpeg = value; OnPropertyChanged(); } }
    }

    public string ExifSummaryText
    {
        get => _exifSummaryText;
        set { if (_exifSummaryText != value) { _exifSummaryText = value; OnPropertyChanged(); } }
    }

    public bool IsTreemapScanning
    {
        get => _isTreemapScanning;
        set { if (_isTreemapScanning != value) { _isTreemapScanning = value; OnPropertyChanged(); } }
    }

    public int TreemapScanPercent
    {
        get => _treemapScanPercent;
        set { if (_treemapScanPercent != value) { _treemapScanPercent = value; OnPropertyChanged(); } }
    }

    public bool IsTreemapIndeterminate
    {
        get => _isTreemapIndeterminate;
        set { if (_isTreemapIndeterminate != value) { _isTreemapIndeterminate = value; OnPropertyChanged(); } }
    }

    public string TreemapScanStatusDetail
    {
        get => _treemapScanStatusDetail;
        set { if (_treemapScanStatusDetail != value) { _treemapScanStatusDetail = value; OnPropertyChanged(); } }
    }

    public string TreemapActiveScanPath
    {
        get => _treemapActiveScanPath;
        set { if (_treemapActiveScanPath != value) { _treemapActiveScanPath = value; OnPropertyChanged(); } }
    }

    public void UpdateTreemapViewport(double width, double height)
    {
        if (width <= 32 || height <= 32)
            return;

        if (Math.Abs(_treemapCanvasWidth - width) < 2.0 && Math.Abs(_treemapCanvasHeight - height) < 2.0)
            return;

        _treemapCanvasWidth = width;
        _treemapCanvasHeight = height;

        if (_lastTreemapRawEntries.Count == 0)
            return;

        var recomputed = VisualDiskAndMediaService.ComputeTreemapLayout(_lastTreemapRawEntries, width, height);
        TreemapNodes.Clear();
        foreach (TreemapNodeItem node in recomputed)
            TreemapNodes.Add(node);
    }

    private async Task SelectTreemapPresetAsync(string preset)
    {
        string target = preset switch
        {
            "C" => @"C:\",
            "User" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "AppData" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _ => TreemapCurrentPath
        };

        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            StatusMessage = $"Klasör bulunamadı: {target}";
            return;
        }

        TreemapCurrentPath = target;
        await LoadTreemapAsync(forceRefresh: false);
    }

    private void BrowseTreemapFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Görsel Disk Haritası (Treemap) İçin Klasör Seçin",
            InitialDirectory = Directory.Exists(TreemapCurrentPath)
                ? TreemapCurrentPath
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            TreemapCurrentPath = dlg.FolderName;
            _ = LoadTreemapAsync(forceRefresh: false);
        }
    }

    private async Task NavigateTreemapParentAsync()
    {
        if (string.IsNullOrWhiteSpace(TreemapCurrentPath))
            return;

        DirectoryInfo? parent = Directory.GetParent(TreemapCurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent is null || !parent.Exists)
        {
            StatusMessage = "Zaten sürücü kök dizinindesiniz.";
            return;
        }

        TreemapCurrentPath = parent.FullName;
        await LoadTreemapAsync(forceRefresh: false);
    }

    private async Task LoadTreemapAsync(bool forceRefresh = false)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(TreemapCurrentPath) || !Directory.Exists(TreemapCurrentPath))
        {
            StatusMessage = "Geçerli bir klasör yolu seçin.";
            return;
        }

        IsBusy = true;
        IsTreemapScanning = true;
        IsTreemapIndeterminate = true;
        TreemapScanPercent = 0;
        ProgressPercent = 5;
        TreemapActiveScanPath = TreemapCurrentPath;
        TreemapScanStatusDetail = "⏳ Klasör yapısı hızlı Win32 tarayıcı ile okunuyor...";
        TreemapSummaryText = "⏳ Taranıyor...";
        StatusMessage = $"Disk Treemap taranıyor: {TreemapCurrentPath}";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        _lastTreemapRawEntries = [];
        TreemapNodes.Clear();

        var progressReporter = new Progress<TreemapScanProgress>(p =>
        {
            if (token.IsCancellationRequested)
                return;

            if (p.TotalUnits > 0)
            {
                IsTreemapIndeterminate = false;
                int pct = Math.Clamp((int)Math.Round(p.CompletedUnits * 100.0 / p.TotalUnits), 2, 99);
                TreemapScanPercent = pct;
                ProgressPercent = pct;
            }
            else
            {
                IsTreemapIndeterminate = true;
            }

            string elapsedStr = $"{p.Elapsed.TotalSeconds:F1}s";
            string bytesStr = CleanerRule.FormatBytes(p.ScannedBytes);
            TreemapScanStatusDetail = p.TotalUnits > 0
                ? $"🔍 Taranıyor: %{TreemapScanPercent} ({p.CompletedUnits}/{p.TotalUnits} alt dal) • {p.ScannedFiles:N0} dosya • {bytesStr} • {elapsedStr}"
                : $"🔍 Taranıyor: {p.ScannedFiles:N0} dosya • {bytesStr} • {elapsedStr}";
            TreemapSummaryText = $"⏳ {bytesStr} ({p.ScannedFiles:N0} dosya)";
            TreemapActiveScanPath = p.CurrentPath;

            if (p.PartialEntries is { Count: > 0 })
            {
                _lastTreemapRawEntries = [.. p.PartialEntries];
                var liveNodes = VisualDiskAndMediaService.ComputeTreemapLayout(p.PartialEntries, _treemapCanvasWidth, _treemapCanvasHeight);
                TreemapNodes.Clear();
                foreach (TreemapNodeItem n in liveNodes)
                    TreemapNodes.Add(n);
            }
        });

        try
        {
            TreemapAnalysisResult result = await VisualDiskAndMediaService.AnalyzeTreemapFolderAsync(
                TreemapCurrentPath,
                _treemapCanvasWidth,
                _treemapCanvasHeight,
                progressReporter,
                forceRefresh,
                token);

            _lastTreemapRawEntries = result.RawEntries;
            TreemapNodes.Clear();
            foreach (TreemapNodeItem n in result.Nodes)
                TreemapNodes.Add(n);

            IsTreemapIndeterminate = false;
            TreemapScanPercent = 100;
            ProgressPercent = 100;

            string timeBadge = result.UsedCache
                ? "⚡ Önbellek (<0.1s)"
                : $"⏱️ {result.Elapsed.TotalSeconds:F1}s";
            TreemapSummaryText = $"{TreemapNodes.Count} blok • {CleanerRule.FormatBytes(result.TotalBytes)} • {result.ScannedFileCount:N0} dosya • {timeBadge}";
            StatusMessage = $"Treemap hazır: {TreemapCurrentPath} ({CleanerRule.FormatBytes(result.TotalBytes)}, {timeBadge})";
            AddLog($"[TREEMAP] '{TreemapCurrentPath}' haritalandı ({CleanerRule.FormatBytes(result.TotalBytes)}, {result.ScannedFileCount:N0} dosya, {timeBadge}).");
        }
        catch (OperationCanceledException)
        {
            IsTreemapIndeterminate = false;
            TreemapSummaryText = TreemapNodes.Count > 0
                ? $"⏹ Kısmi Sonuç ({TreemapNodes.Count} blok)"
                : "⏹ İptal edildi";
            StatusMessage = "Treemap analizi durduruldu — şimdiye kadar bulunan bloklar ekranda tutuldu.";
            AddLog("[TREEMAP] Tarama kullanıcı tarafından durduruldu.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Treemap hatası: {ex.Message}";
            AddLog($"[HATA] Disk Treemap analizi başarısız: {ex.Message}");
        }
        finally
        {
            IsTreemapScanning = false;
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void BrowseExifFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "EXIF / GPS Gizlilik Taraması Yapılacak Fotoğraf Klasörünü Seçin",
            InitialDirectory = Directory.Exists(ExifScanFolderPath)
                ? ExifScanFolderPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            ExifScanFolderPath = dlg.FolderName;
            _ = LoadPhotoMetadataAsync();
        }
    }

    private async Task LoadPhotoMetadataAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(ExifScanFolderPath) || !Directory.Exists(ExifScanFolderPath))
        {
            StatusMessage = "Geçerli bir fotoğraf klasörü seçin.";
            return;
        }

        IsBusy = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        ProgressPercent = 15;
        StatusMessage = $"Fotoğraf EXIF/GPS verileri taranıyor: {ExifScanFolderPath}";

        try
        {
            var photos = await VisualDiskAndMediaService.ScanPhotoMetadataAsync(
                ExifScanFolderPath,
                ExifIncludeSubfolders,
                _cts.Token);

            PhotoMetadataItems.Clear();
            foreach (PhotoMetadataItem item in photos)
                PhotoMetadataItems.Add(item);

            int gpsCount = photos.Count(p => p.HasGpsData);
            int metaCount = photos.Count(p => p.TagCount > 0);
            ExifSummaryText = $"{photos.Count:N0} fotoğraf • {gpsCount} GPS riski • {metaCount} EXIF etiketli";
            ProgressPercent = 100;
            StatusMessage = $"Fotoğraf taraması tamamlandı: {photos.Count:N0} dosya ({gpsCount} GPS verili).";
            AddLog($"[EXIF TARAMA] '{ExifScanFolderPath}' içinde {photos.Count} fotoğraf incelendi ({gpsCount} GPS koordinatlı).");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "EXIF taraması iptal edildi.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"EXIF tarama hatası: {ex.Message}";
            AddLog($"[HATA] Fotoğraf EXIF taraması başarısız: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScrubSelectedPhotosAsync()
    {
        if (IsBusy) return;

        var targets = PhotoMetadataItems.Where(p => p.IsSelected).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Temizlenecek fotoğraf seçilmedi.";
            return;
        }

        IsBusy = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        long totalSaved = 0;
        int cleanedCount = 0;
        int totalTagsRemoved = 0;

        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                PhotoMetadataItem item = targets[i];
                ProgressPercent = (int)Math.Round((i + 1) * 100.0 / targets.Count);
                StatusMessage = $"Temizleniyor ({i + 1}/{targets.Count}): {item.FileName}";

                var (ok, saved, removedTags, _) = await VisualDiskAndMediaService.ScrubPhotoMetadataAsync(
                    item,
                    ExifCreateBackup,
                    ExifOptimizeJpeg,
                    jpegQuality: 88,
                    _cts.Token);

                if (ok)
                {
                    cleanedCount++;
                    totalSaved += saved;
                    totalTagsRemoved += removedTags;
                }
            }

            if (totalSaved > 0)
                LastFreedBytes += totalSaved;

            LastDeletedFiles += cleanedCount;
            RecordCleanupHistory("EXIF / GPS Temizliği", totalSaved, cleanedCount);

            string summary = $"{cleanedCount}/{targets.Count} fotoğraf EXIF/GPS verilerinden temizlendi ({totalTagsRemoved} etiket silindi, {CleanerRule.FormatBytes(totalSaved)} tasarruf).";
            ExifSummaryText = summary;
            StatusMessage = summary;
            AddLog($"[EXIF SCRUBBER] {summary}");
            TrayNotificationRequested?.Invoke("FreeCleaner Pro — EXIF/GPS Temizliği", summary, false);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "EXIF temizleme işlemi iptal edildi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set
        {
            if (_isDownloadingUpdate != value)
            {
                _isDownloadingUpdate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDownloadUpdate));
            }
        }
    }

    public int UpdateDownloadPercent
    {
        get => _updateDownloadPercent;
        private set
        {
            if (_updateDownloadPercent != value)
            {
                _updateDownloadPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CanDownloadUpdate =>
        !IsBusy &&
        !IsDownloadingUpdate &&
        !string.IsNullOrWhiteSpace(_latestDownloadAssetUrl);

    private async Task CheckForUpdatesAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        UpdateStatusText = LocalizationService.GetText(
            "GitHub Releases kontrol ediliyor...",
            "Checking GitHub Releases...",
            _selectedLanguage);
        StatusMessage = LocalizationService.GetText(
            "GitHub üzerinden yeni sürüm kontrol ediliyor...",
            "Checking for a new version on GitHub...",
            _selectedLanguage);

        try
        {
            UpdateCheckResult result = await AppUpdateService.CheckForUpdatesAsync();
            _latestReleaseUrl = result.ReleaseUrl;
            _latestDownloadAssetUrl = result.DownloadAssetUrl;
            _latestDownloadAssetName = result.DownloadAssetName;
            _latestExpectedSha256 = result.ExpectedSha256;
            _latestChecksumsAssetUrl = result.ChecksumsAssetUrl;

            UpdateStatusText = result.StatusMessage;
            StatusMessage = result.StatusMessage;
            OnPropertyChanged(nameof(CanDownloadUpdate));
            AddLog($"[GÜNCELLEME] {result.StatusMessage}");

            if (result.IsUpdateAvailable)
            {
                HasPendingUpdate = true;
                TrayNotificationRequested?.Invoke(
                    LocalizationService.GetText(
                        "FreeCleaner Pro — Güncelleme Mevcut",
                        "FreeCleaner Pro — Update Available",
                        _selectedLanguage),
                    result.StatusMessage,
                    false);
                if (!string.IsNullOrWhiteSpace(_latestDownloadAssetUrl))
                {
                    var ask = MessageBox.Show(
                        LocalizationService.GetText(
                            $"{result.StatusMessage}\n\nDosya: {_latestDownloadAssetName}\nDoğrudan uygulama içinden SHA-256 doğrulamalı indirme ve güncelleme başlatılsın mı?",
                            $"{result.StatusMessage}\n\nFile: {_latestDownloadAssetName}\nStart SHA-256 verified in-app download and update now?",
                            _selectedLanguage),
                        LocalizationService.GetText("Yeni Sürüm Mevcut", "New Release Available", _selectedLanguage),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    if (ask == MessageBoxResult.Yes)
                    {
                        IsBusy = false;
                        await DownloadAndInstallUpdateAsync();
                    }
                }
                else
                {
                    var ask = MessageBox.Show(
                        LocalizationService.GetText(
                            $"{result.StatusMessage}\n\nGitHub Releases indirme sayfasını tarayıcıda açmak ister misiniz?",
                            $"{result.StatusMessage}\n\nOpen the GitHub Releases page in your browser?",
                            _selectedLanguage),
                        LocalizationService.GetText("Yeni Sürüm Mevcut", "New Release Available", _selectedLanguage),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    if (ask == MessageBoxResult.Yes)
                        OpenUrlSafe(result.ReleaseUrl);
                }
            }
            else
            {
                HasPendingUpdate = false;
                ShowToast(
                    LocalizationService.GetText("Güncelleme Kontrolü", "Update Check", _selectedLanguage),
                    result.StatusMessage,
                    result.Success ? ToastSeverity.Success : ToastSeverity.Warning);
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanDownloadUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task DownloadAndInstallUpdateAsync()
    {
        if (IsBusy || IsDownloadingUpdate || string.IsNullOrWhiteSpace(_latestDownloadAssetUrl))
            return;

        string fileName = Path.GetFileName(_latestDownloadAssetName ?? "FreeCleaner-Update.exe");
        string updateDir = Path.Combine(Path.GetTempPath(), "FreeCleanerUpdate");
        string destPath = Path.Combine(updateDir, fileName);

        IsBusy = true;
        IsDownloadingUpdate = true;
        UpdateDownloadPercent = 0;
        ProgressPercent = 0;
        UpdateStatusText = LocalizationService.GetText(
            $"Güncelleme paketi indiriliyor: {fileName}...",
            $"Downloading update package: {fileName}...",
            _selectedLanguage);
        StatusMessage = UpdateStatusText;

        try
        {
            var progress = new Progress<int>(pct =>
            {
                UpdateDownloadPercent = pct;
                ProgressPercent = pct;
            });

            UpdateDownloadResult dl = await AppUpdateService.DownloadAndVerifyUpdateAsync(
                _latestDownloadAssetUrl,
                destPath,
                _latestExpectedSha256,
                progress,
                requireHash: !string.IsNullOrWhiteSpace(_latestChecksumsAssetUrl));

            UpdateStatusText = dl.Message;
            StatusMessage = dl.Message;
            AddLog($"[GÜNCELLEME İNDİRME] {dl.Message}");

            if (!dl.Success || string.IsNullOrEmpty(dl.FilePath))
            {
                MessageBox.Show(
                    dl.Message,
                    LocalizationService.GetText("Güncelleme Hatası", "Update Error", _selectedLanguage),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string? currentExe = Environment.ProcessPath;
            bool canSelfReplace = !string.IsNullOrWhiteSpace(currentExe) &&
                                  File.Exists(currentExe) &&
                                  Path.GetFileName(currentExe).StartsWith("FreeCleaner", StringComparison.OrdinalIgnoreCase);

            if (!canSelfReplace)
            {
                MessageBox.Show(
                    LocalizationService.GetText(
                        $"{dl.Message}\n\nDoğrulanan dosya indirildi:\n{dl.FilePath}\n\nGeliştirme ortamında çalışıldığı için klasör Gezgin'de açılacak.",
                        $"{dl.Message}\n\nVerified package downloaded to:\n{dl.FilePath}\n\nOpening folder in Explorer.",
                        _selectedLanguage),
                    LocalizationService.GetText("Güncelleme Hazır", "Update Ready", _selectedLanguage),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                OpenExplorerPath(dl.FilePath);
                return;
            }

            string verifyNote = dl.HashVerified
                ? LocalizationService.GetText(
                    $"SHA-256 doğrulandı: {dl.ComputedSha256?[..16]}...",
                    $"SHA-256 verified: {dl.ComputedSha256?[..16]}...",
                    _selectedLanguage)
                : LocalizationService.GetText(
                    "Not: Yayında SHA-256 dosyası bulunamadı, kullanıcı onayıyla devam ediliyor.",
                    "Note: No SHA-256 checksum provided in release; continuing with user confirmation.",
                    _selectedLanguage);

            var installAsk = MessageBox.Show(
                LocalizationService.GetText(
                    $"Yeni sürüm hazır!\n{verifyNote}\n\nUygulama kapatılıp yeni sürüm otomatik kurulup yeniden başlatılsın mı?",
                    $"New version ready!\n{verifyNote}\n\nClose app now, install update automatically, and restart?",
                    _selectedLanguage),
                LocalizationService.GetText("Güncellemeyi Uygula", "Apply Update", _selectedLanguage),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (installAsk != MessageBoxResult.Yes)
                return;

            string scriptPath = Path.Combine(updateDir, "apply_update.ps1");
            string scriptBody = AppUpdateService.BuildSafeSelfUpdateScript(Environment.ProcessId, currentExe!, dl.FilePath);
            await File.WriteAllTextAsync(scriptPath, scriptBody);

            ProcessStartInfo updatePsi = SafeProcessRunner.BuildDirectProcessStartInfo(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath]);
            Process.Start(updatePsi);

            Application.Current?.Shutdown();
        }
        finally
        {
            IsDownloadingUpdate = false;
            IsBusy = false;
            OnPropertyChanged(nameof(CanDownloadUpdate));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OpenUrlSafe(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Bağlantı açılamadı: {ex.Message}";
        }
    }

}
