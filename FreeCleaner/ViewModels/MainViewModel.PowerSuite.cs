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
    // ==================== 19. SMART TREEMAP DELETE, LIFETIME HISTORY, DEEP SPACE RECOVERY & DHASH PHOTO FINDER ====================

    public string LifetimeFreedFormatted
    {
        get => _lifetimeFreedFormatted;
        set { if (_lifetimeFreedFormatted != value) { _lifetimeFreedFormatted = value; OnPropertyChanged(); } }
    }

    public string LifetimeSessionsSummary
    {
        get => _lifetimeSessionsSummary;
        set { if (_lifetimeSessionsSummary != value) { _lifetimeSessionsSummary = value; OnPropertyChanged(); } }
    }

    public string DeepSpaceSummaryText
    {
        get => _deepSpaceSummaryText;
        set { if (_deepSpaceSummaryText != value) { _deepSpaceSummaryText = value; OnPropertyChanged(); } }
    }

    public string SimilarPhotosFolderPath
    {
        get => _similarPhotosFolderPath;
        set { if (_similarPhotosFolderPath != value) { _similarPhotosFolderPath = value; OnPropertyChanged(); } }
    }

    public bool SimilarPhotosIncludeSubfolders
    {
        get => _similarPhotosIncludeSubfolders;
        set { if (_similarPhotosIncludeSubfolders != value) { _similarPhotosIncludeSubfolders = value; OnPropertyChanged(); } }
    }

    public int SimilarPhotosMaxDistance
    {
        get => _similarPhotosMaxDistance;
        set { if (_similarPhotosMaxDistance != value) { _similarPhotosMaxDistance = value; OnPropertyChanged(); } }
    }

    public string SimilarPhotosSummaryText
    {
        get => _similarPhotosSummaryText;
        set { if (_similarPhotosSummaryText != value) { _similarPhotosSummaryText = value; OnPropertyChanged(); } }
    }

    public SimilarPhotoItem? SelectedSimilarPhoto
    {
        get => _selectedSimilarPhoto;
        set { if (!ReferenceEquals(_selectedSimilarPhoto, value)) { _selectedSimilarPhoto = value; OnPropertyChanged(); } }
    }

    private void RefreshCleanupHistorySummary()
    {
        List<CleanupHistoryEntry> entries = CleanupHistoryService.LoadHistory();
        long totalBytes = CleanupHistoryService.GetTotalLifetimeFreedBytes(entries);
        LifetimeFreedFormatted = CleanerRule.FormatBytes(totalBytes);
        LifetimeSessionsSummary = entries.Count > 0
            ? $"{entries.Count:N0} oturum • {entries.Sum(e => e.ItemsProcessed):N0} öğe"
            : "Henüz kayıtlı temizlik oturumu yok";

        List<CleanupHistoryBarItem> bars = CleanupHistoryService.BuildRecentDailyBars(entries, days: 14, maxBarHeight: 54.0);
        RecentCleanupBars.Clear();
        foreach (CleanupHistoryBarItem bar in bars)
            RecentCleanupBars.Add(bar);
    }

    private void RecordCleanupHistory(string moduleName, long freedBytes, int itemsProcessed)
    {
        if (freedBytes <= 0 && itemsProcessed <= 0)
            return;

        CleanupHistoryService.RecordSession(moduleName, freedBytes, itemsProcessed);
        RefreshCleanupHistorySummary();
    }

    private async Task MoveTreemapNodeToRecycleBinAsync(TreemapNodeItem node)
    {
        if (IsBusy || !node.CanSafeDelete)
            return;

        if (ExclusionAndSettingsService.IsPathExcluded(node.FullPath, ExcludedPaths.ToList()))
        {
            MessageBox.Show(
                $"'{node.FullPath}' Beyaz Liste (korunan klasörler) içinde yer aldığı için silinemez.",
                "Beyaz Liste Koruması",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (SmartDiskAdvisorService.IsProtectedSystemPath(node.FullPath))
        {
            MessageBox.Show(
                $"'{node.FullPath}' kritik bir Windows sistem yoludur ve doğrudan silmeye karşı korunmaktadır.",
                "Sistem Koruması",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"'{node.Name}' ({node.FormattedSize}) Geri Dönüşüm Kutusuna taşınacak.\n\n" +
            $"Danışman Notu: {node.AdvisorSummary}\n" +
            $"Yol: {node.FullPath}\n\nDevam edilsin mi?",
            "Geri Dönüşüm Kutusuna Taşı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        StatusMessage = $"Geri Dönüşüm Kutusuna taşınıyor: {node.Name}...";
        try
        {
            bool moved = await Task.Run(() => RecycleBinHelper.MovePathToRecycleBin(node.FullPath));
            if (moved)
            {
                LastFreedBytes += node.SizeBytes;
                LastDeletedFiles += 1;
                RecordCleanupHistory("Treemap Güvenli Silme", node.SizeBytes, 1);
                VisualDiskAndMediaService.ClearTreemapCache();
                RefreshSystemMetrics();
                AddLog($"[TREEMAP SİLME] '{node.FullPath}' ({node.FormattedSize}) Geri Dönüşüm Kutusuna taşındı.");
                IsBusy = false;
                await LoadTreemapAsync(forceRefresh: true);
            }
            else
            {
                string lockMsg = $"'{node.Name}' şu anda açık bir uygulama tarafından kullanılıyor olabilir (örneğin Chrome veya Claude açıkken kilitli olabilir).";
                StatusMessage = lockMsg;
                AddLog($"[TREEMAP UYARI] {lockMsg}");
                MessageBox.Show(lockMsg, "Dosya Kullanımda / Taşınamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task LoadDeepSpaceAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        DeepSpaceItems.Clear();
        ProgressPercent = 20;
        StatusMessage = "Dev Windows sistem alanı kaynakları (hiberfil.sys, WinSxS, CompactOS, GPU kurulumları) analiz ediliyor...";

        try
        {
            List<DeepSpaceItem> items = await DeepSpaceRecoveryService.ScanDeepSpaceOpportunitiesAsync();
            foreach (DeepSpaceItem item in items)
                DeepSpaceItems.Add(item);

            long knownBytes = items.Where(x => x.SizeBytes > 0 && x.ItemId != "HIBERFIL_REDUCED").Sum(x => x.SizeBytes);
            DeepSpaceSummaryText = $"{items.Count} sistem modülü • Doğrudan {CleanerRule.FormatBytes(knownBytes)} tespit (+WinSxS/CompactOS)";
            ProgressPercent = 100;
            StatusMessage = $"Dev Sistem Alanı Analizi tamamlandı ({CleanerRule.FormatBytes(knownBytes)} doğrudan ölçülebilir alan).";
            AddLog($"[DEV ALAN KURTARICI] Tarama tamamlandı: {items.Count} öğe.");
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ExecuteDeepSpaceItemAsync(DeepSpaceItem item)
    {
        if (IsBusy || !item.CanExecuteAction) return;

        var confirm = MessageBox.Show(
            $"'{item.Title}' çalıştırılacak.\n\nAçıklama: {item.Description}\n\nDevam edilsin mi?",
            "Dev Sistem Alanı Kurtarıcısı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        ProgressPercent = 30;
        StatusMessage = $"Uygulanıyor: {item.Title}...";

        try
        {
            var (success, freed, message) = await DeepSpaceRecoveryService.ExecuteDeepSpaceActionAsync(item.ItemId);
            if (success && freed > 0)
            {
                LastFreedBytes += freed;
                RecordCleanupHistory("Dev Sistem Kurtarma", freed, 1);
            }

            RefreshSystemMetrics();
            ProgressPercent = 100;
            StatusMessage = message;
            AddLog($"[DEV ALAN KURTARICI] {message}");

            if (!success)
            {
                MessageBox.Show(message, "Sistem Alanı Kurtarma Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                IsBusy = false;
                await LoadDeepSpaceAsync();
            }
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void BrowseSimilarPhotosFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Benzer Fotoğraf Taraması İçin Klasör Seçin",
            InitialDirectory = Directory.Exists(SimilarPhotosFolderPath)
                ? SimilarPhotosFolderPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            SimilarPhotosFolderPath = dlg.FolderName;
            _ = LoadSimilarPhotosAsync();
        }
    }

    private async Task LoadSimilarPhotosAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(SimilarPhotosFolderPath) || !Directory.Exists(SimilarPhotosFolderPath))
        {
            StatusMessage = "Geçerli bir fotoğraf klasörü seçin.";
            return;
        }

        IsBusy = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        SimilarPhotoItems.Clear();
        ProgressPercent = 20;
        StatusMessage = $"Perceptual dHash ile benzer fotoğraflar aranıyor: {SimilarPhotosFolderPath}";

        try
        {
            List<SimilarPhotoItem> found = await SimilarPhotoFinderService.FindSimilarPhotosAsync(
                SimilarPhotosFolderPath,
                SimilarPhotosMaxDistance,
                SimilarPhotosIncludeSubfolders,
                _cts.Token);

            foreach (SimilarPhotoItem item in found)
                SimilarPhotoItems.Add(item);

            int groups = found.Select(x => x.GroupId).Distinct().Count();
            long reclaimable = found.Where(x => x.IsSelected).Sum(x => x.SizeBytes);
            SimilarPhotosSummaryText = $"{groups} grup • {found.Count} fotoğraf • {CleanerRule.FormatBytes(reclaimable)} seçili kopya";
            SelectedSimilarPhoto = SimilarPhotoItems.FirstOrDefault();
            ProgressPercent = 100;
            StatusMessage = $"Benzer fotoğraf taraması bitti: {groups} grup, {CleanerRule.FormatBytes(reclaimable)} kazanılabilir alan.";
            AddLog($"[BENZER FOTOĞRAF] {groups} grupta {found.Count} fotoğraf bulundu ({CleanerRule.FormatBytes(reclaimable)} kopya).");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Benzer fotoğraf taraması iptal edildi.";
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task CleanSelectedSimilarPhotosAsync()
    {
        var selected = SimilarPhotoItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0 || IsBusy)
            return;

        long totalBytes = selected.Sum(x => x.SizeBytes);
        string modeText = UseRecycleBinSafety ? "Geri Dönüşüm Kutusuna taşınacak" : "KALICI OLARAK SİLİNECEK";
        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} benzer kopya fotoğraf ({CleanerRule.FormatBytes(totalBytes)}) {modeText}.\n\nEn yüksek çözünürlüklü asıl kopyalar korunur. Devam edilsin mi?",
            "Benzer Fotoğraf Temizliği",
            MessageBoxButton.YesNo,
            UseRecycleBinSafety ? MessageBoxImage.Question : MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var (deleted, freed) = await SimilarPhotoFinderService.CleanSelectedSimilarPhotosAsync(selected, UseRecycleBinSafety);
            if (freed > 0 || deleted > 0)
            {
                LastFreedBytes += freed;
                LastDeletedFiles += deleted;
                RecordCleanupHistory("Benzer Fotoğraf Temizliği", freed, deleted);
                RefreshSystemMetrics();
            }

            string summary = $"{deleted}/{selected.Count} benzer fotoğraf temizlendi ({CleanerRule.FormatBytes(freed)} yer açıldı).";
            SimilarPhotosSummaryText = summary;
            StatusMessage = summary;
            AddLog($"[BENZER FOTOĞRAF] {summary}");
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // ==================== 20. EXPLORER CONTEXT MENU, CLEANING PROFILES, JSON BACKUP, NTFS HARDLINK & TRAY TELEMETRY ====================

    public string ExplorerContextMenuStatusText
    {
        get => _explorerContextMenuStatusText;
        private set
        {
            if (_explorerContextMenuStatusText != value)
            {
                _explorerContextMenuStatusText = value;
                OnPropertyChanged();
            }
        }
    }

    private void RefreshExplorerContextMenuStatus()
    {
        ExplorerContextMenuStatusText = ShellIntegrationAndProfilesService.IsExplorerContextMenuInstalled()
            ? LocalizationService.GetText(
                "✅ Durum: Windows Gezgini sağ tık menüsü AKTİF (HKCU)",
                "✅ Status: Windows Explorer context menu ACTIVE (HKCU)",
                _selectedLanguage)
            : LocalizationService.GetText(
                "ℹ️ Durum: Windows Gezgini sağ tık menüsü henüz eklenmedi",
                "ℹ️ Status: Windows Explorer context menu not installed yet",
                _selectedLanguage);
    }

    private CleanerConfigData BuildCurrentConfigSnapshot()
    {
        return new CleanerConfigData
        {
            ExcludedPaths = ExcludedPaths.ToList(),
            AutoMonitorEnabled = AutoMonitorEnabled,
            AutoMonitorThresholdMb = AutoMonitorThresholdMb,
            MinimizeToTrayOnClose = MinimizeToTrayOnClose,
            UseRecycleBinSafety = UseRecycleBinSafety,
            RiskFilter = RiskFilter,
            ScheduledMaintenanceEnabled = ScheduledMaintenanceEnabled,
            ScheduledMaintenanceIntervalHours = ScheduledMaintenanceIntervalHours,
            SelectedRuleIds = AllRules.Where(rule => rule.IsSelected).Select(rule => rule.Id).ToList(),
            FavoriteSections = _favoriteSectionKeys.ToList(),
            SelectedTheme = SelectedTheme,
            SelectedLanguage = SelectedLanguage,
            ProactiveStorageAlertsEnabled = ProactiveStorageAlertsEnabled,
            StorageAlertThresholdDays = StorageAlertThresholdDays,
            LastStorageForecastAlertDate = _lastStorageForecastAlertDate,
            WindowLeft = _windowLeft,
            WindowTop = _windowTop,
            WindowWidth = _windowWidth,
            WindowHeight = _windowHeight,
            WindowMaximized = _windowMaximized,
            HasCompletedOnboarding = _hasCompletedOnboarding,
            CustomProfiles = CustomProfiles.ToList(),
            AutoCheckUpdatesOnStartup = _autoCheckUpdatesOnStartup,
            LastUpdateCheckDate = _lastUpdateCheckDate
        };
    }

    private void ExecuteApplyCleaningProfile(CleaningProfileKind profile)
    {
        _suppressRuleWarningPrompt = true;
        try
        {
            var (_, desc) = ShellIntegrationAndProfilesService.ApplyProfileToRules(AllRules, profile);
            var selectedIds = AllRules
                .Where(r => r.IsSelected)
                .Select(r => r.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in DiscoveredFiles)
                file.IsSelected = selectedIds.Contains(file.RuleId);

            RecalculateSelectedTotals();
            SaveCurrentSettings();
            StatusMessage = desc;
            AddLog($"[PROFİL] {desc}");
        }
        finally
        {
            _suppressRuleWarningPrompt = false;
        }
    }

    private void ExecuteInstallExplorerContextMenu()
    {
        var (success, message) = ShellIntegrationAndProfilesService.InstallExplorerContextMenu();
        RefreshExplorerContextMenuStatus();
        StatusMessage = message;
        AddLog($"[SAĞ TIK MENÜSÜ] {message}");
        if (!success)
            MessageBox.Show(message, "Gezgin Sağ Tık Menüsü", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ExecuteRemoveExplorerContextMenu()
    {
        var (_, message) = ShellIntegrationAndProfilesService.RemoveExplorerContextMenu();
        RefreshExplorerContextMenuStatus();
        StatusMessage = message;
        AddLog($"[SAĞ TIK MENÜSÜ] {message}");
    }

    private void ExecuteExportSettingsConfig()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "FreeCleaner Ayar Profilini Dışa Aktar",
            Filter = "JSON Ayar Dosyası (*.json)|*.json",
            FileName = $"FreeCleaner-Ayarlar-{DateTime.Now:yyyyMMdd}.json",
            DefaultExt = ".json"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            ShellIntegrationAndProfilesService.ExportSettingsToFile(BuildCurrentConfigSnapshot(), dlg.FileName);
            StatusMessage = LocalizationService.GetText(
                $"Ayar profili dışa aktarıldı: {dlg.FileName}",
                $"Settings exported: {dlg.FileName}",
                _selectedLanguage);
            AddLog($"[AYAR DIŞA AKTAR] Konfigürasyon kaydedildi -> {dlg.FileName}");
            ShowToast(
                LocalizationService.GetText("Ayarlar Dışa Aktarıldı", "Settings Exported", _selectedLanguage),
                dlg.FileName,
                ToastSeverity.Success,
                actionLabel: LocalizationService.Get("Loc_ToastOpenFolderBtn", _selectedLanguage),
                actionKey: $"OPEN_PATH:{dlg.FileName}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ayar dışa aktarılamadı: {ex.Message}";
            AddLog($"[HATA] Ayar dışa aktarma hatası: {ex.Message}");
        }
    }

    private void ExecuteImportSettingsConfig()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "FreeCleaner Ayar Profilini İçe Aktar",
            Filter = "JSON Ayar Dosyası (*.json)|*.json|Tüm Dosyalar (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            CleanerConfigData imported = ShellIntegrationAndProfilesService.ImportSettingsFromFile(dlg.FileName);

            ExcludedPaths.Clear();
            foreach (string path in imported.ExcludedPaths)
                ExcludedPaths.Add(path);

            _favoriteSectionKeys.Clear();
            foreach (string fav in imported.FavoriteSections)
            {
                if (!string.IsNullOrWhiteSpace(fav) && !_favoriteSectionKeys.Contains(fav, StringComparer.OrdinalIgnoreCase))
                    _favoriteSectionKeys.Add(fav);
            }
            RebuildFavoriteNavItems();
            OnPropertyChanged(nameof(IsCurrentSectionFavorite));
            OnPropertyChanged(nameof(FavoriteButtonText));

            AutoMonitorThresholdMb = imported.AutoMonitorThresholdMb;
            AutoMonitorEnabled = imported.AutoMonitorEnabled;
            MinimizeToTrayOnClose = imported.MinimizeToTrayOnClose;
            UseRecycleBinSafety = imported.UseRecycleBinSafety;
            RiskFilter = imported.RiskFilter;
            ScheduledMaintenanceIntervalHours = imported.ScheduledMaintenanceIntervalHours;
            ScheduledMaintenanceEnabled = imported.ScheduledMaintenanceEnabled;
            SelectedLanguage = imported.SelectedLanguage;
            SelectedTheme = imported.SelectedTheme;
            _proactiveStorageAlertsEnabled = imported.ProactiveStorageAlertsEnabled;
            _storageAlertThresholdDays = imported.StorageAlertThresholdDays is 7 or 15 or 30
                ? imported.StorageAlertThresholdDays
                : 15;
            _lastStorageForecastAlertDate = imported.LastStorageForecastAlertDate ?? string.Empty;
            _autoCheckUpdatesOnStartup = imported.AutoCheckUpdatesOnStartup;
            _lastUpdateCheckDate = imported.LastUpdateCheckDate ?? string.Empty;
            OnPropertyChanged(nameof(ProactiveStorageAlertsEnabled));
            OnPropertyChanged(nameof(StorageAlertThresholdDays));
            OnPropertyChanged(nameof(AutoCheckUpdatesOnStartup));

            CustomProfiles.Clear();
            foreach (CustomCleaningProfile cp in imported.CustomProfiles ?? [])
            {
                if (!string.IsNullOrWhiteSpace(cp.Name))
                    CustomProfiles.Add(cp);
            }
            SelectedCustomProfile = CustomProfiles.FirstOrDefault();
            PopulateModuleCatalog();
            RebuildFavoriteNavItems();

            if (imported.SelectedRuleIds.Count > 0)
            {
                var selectedSet = imported.SelectedRuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                _suppressRuleWarningPrompt = true;
                try
                {
                    foreach (var rule in AllRules)
                        rule.IsSelected = selectedSet.Contains(rule.Id);

                    foreach (var file in DiscoveredFiles)
                        file.IsSelected = selectedSet.Contains(file.RuleId);

                    RecalculateSelectedTotals();
                }
                finally
                {
                    _suppressRuleWarningPrompt = false;
                }
            }

            SaveCurrentSettings();
            StatusMessage = $"Ayar profili başarıyla yüklendi ({dlg.FileName}).";
            AddLog($"[AYAR İÇE AKTAR] Profil geri yüklendi <- {dlg.FileName}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ayar içe aktarılamadı: {ex.Message}";
            MessageBox.Show($"Ayar dosyası okunamadı:\n{ex.Message}", "İçe Aktarma Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task MergeDuplicatesWithHardlinksAsync()
    {
        var selectedDuplicates = DiskAnalyzerItems.Where(x => x.IsDuplicate && x.IsSelected).ToList();
        if (selectedDuplicates.Count == 0)
        {
            StatusMessage = "Hardlink birleştirme için önce 'Çift (Kopya) Dosyaları Bul' taraması çalıştırın.";
            return;
        }

        long potentialBytes = selectedDuplicates.Sum(x => x.SizeBytes);
        var confirm = MessageBox.Show(
            $"Seçili {selectedDuplicates.Count} kopya dosya ({CleanerRule.FormatBytes(potentialBytes)}) NTFS Hardlink ile orijinallerine bağlanacak.\n\n" +
            "• İki dosya yolu da diskte yerinde kalır (0 veri kaybı).\n" +
            "• Yalnızca aynı NTFS sürücüdeki SHA-256 birebir eşleşen dosyalar bağlanır.\n\nDevam edilsin mi?",
            "NTFS Hardlink ile Birleştir",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        StatusMessage = "SHA-256 doğrulanıyor ve NTFS hardlink bağlantıları oluşturuluyor...";
        try
        {
            var result = await NtfsHardlinkService.DeduplicateWithHardlinksAsync(DiskAnalyzerItems);
            if (result.BytesReclaimed > 0 || result.LinkedCount > 0)
            {
                LastFreedBytes += result.BytesReclaimed;
                LastDeletedFiles += result.LinkedCount;
                RecordCleanupHistory("NTFS Hardlink Birleştirme", result.BytesReclaimed, result.LinkedCount);
                RefreshSystemMetrics();
            }

            foreach (string warning in result.Warnings.Take(8))
                AddLog($"[HARDLINK UYARI] {warning}");

            string summary = result.LinkedCount > 0
                ? $"🔗 {result.LinkedCount} kopya NTFS hardlink ile birleştirildi — {CleanerRule.FormatBytes(result.BytesReclaimed)} gerçek disk alanı kurtarıldı (atlanan: {result.SkippedCount})."
                : $"Yeni hardlink oluşturulamadı (atlanan: {result.SkippedCount}). Farklı sürücü, kilitli veya zaten bağlı olabilir.";

            StatusMessage = summary;
            AddLog($"[HARDLINK] {summary}");
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public void TickTrayHealthWatcher()
    {
        var (cpu, ram, _, _) = _telemetrySampler.SampleNext();
        var metrics = SystemInfoService.GetMetrics();
        double freeGb = metrics.DriveFreeBytes / (1024.0 * 1024.0 * 1024.0);

        TrayTooltipUpdateRequested?.Invoke(cpu, ram, freeGb, metrics.DriveUsedPercent);

        var alert = FlagshipTelemetryAndThemeService.EvaluateCriticalAlerts(
            ram,
            metrics.DriveUsedPercent,
            freeGb,
            DateTime.UtcNow,
            ref _lastDiskAlertUtc,
            ref _lastRamAlertUtc);

        if ((alert.TriggerDiskAlert || alert.TriggerRamAlert) &&
            !string.IsNullOrEmpty(alert.Title) &&
            !string.IsNullOrEmpty(alert.Message))
        {
            TrayNotificationRequested?.Invoke(alert.Title, alert.Message, true);
            AddLog($"[CANLI TRAY UYARISI] {alert.Message}");
        }

        string todayKey = DateTime.Now.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (_lastStorageForecastSummary is null || !string.Equals(_lastForecastSampleDateKey, todayKey, StringComparison.Ordinal))
        {
            RefreshStorageForecast(metrics);
        }
        else
        {
            EvaluateProactiveStorageForecastAlert(_lastStorageForecastSummary, metrics.DriveUsedPercent);
        }
    }

    public async Task ApplyCliLaunchActionAsync(CliLaunchAction action)
    {
        if (action.Profile is CleaningProfileKind profile)
            ExecuteApplyCleaningProfile(profile);
        else if (!string.IsNullOrWhiteSpace(action.CustomProfileName))
        {
            CustomCleaningProfile? match = CustomProfiles.FirstOrDefault(p =>
                p.Name.Equals(action.CustomProfileName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                ExecuteApplyCustomProfile(match);
        }

        switch (action.Kind)
        {
            case CliLaunchActionKind.ScheduledMaintenance when action.DryRun:
            case CliLaunchActionKind.DryRunReport:
                await RunCliDryRunReportAsync(action);
                return;

            case CliLaunchActionKind.ScheduledMaintenance:
                await RunScheduledMaintenanceAsync(closeWhenFinished: true);
                break;

            case CliLaunchActionKind.OpenTreemap:
                ActiveSection = "DiskTreemap";
                if (!string.IsNullOrWhiteSpace(action.TargetPath) && Directory.Exists(action.TargetPath))
                    TreemapCurrentPath = action.TargetPath;
                await LoadTreemapAsync(forceRefresh: true);
                break;

            case CliLaunchActionKind.OpenExifScrubber:
                ActiveSection = "ExifScrubber";
                if (!string.IsNullOrWhiteSpace(action.TargetPath) && Directory.Exists(action.TargetPath))
                    ExifScanFolderPath = action.TargetPath;
                await LoadPhotoMetadataAsync();
                break;

            case CliLaunchActionKind.OpenSimilarPhotos:
                ActiveSection = "SimilarPhotos";
                if (!string.IsNullOrWhiteSpace(action.TargetPath) && Directory.Exists(action.TargetPath))
                    SimilarPhotosFolderPath = action.TargetPath;
                await LoadSimilarPhotosAsync();
                break;

            case CliLaunchActionKind.OpenDeepSpace:
                ActiveSection = "DeepSpaceRecovery";
                await LoadDeepSpaceAsync();
                break;

            case CliLaunchActionKind.QuickHealthCheck:
                ActiveSection = "Dashboard";
                await RunOneClickHealthCheckAsync();
                break;
        }

        if (action.ExitWhenFinished && action.Kind != CliLaunchActionKind.ScheduledMaintenance)
            Application.Current?.Shutdown(0);
    }

    private async Task RunCliDryRunReportAsync(CliLaunchAction action)
    {
        var sw = Stopwatch.StartNew();
        IsBusy = true;
        try
        {
            List<CleanerRule> rulesToScan = AllRules.Where(r => r.IsSelected).ToList();
            if (rulesToScan.Count == 0)
                rulesToScan = AllRules.Where(r => r.RiskLevel == RiskLevel.Low).ToList();

            List<ScannedFileItem> scanned = await _engine.AnalyzeAsync(rulesToScan, ExcludedPaths.ToList());
            sw.Stop();

            long totalBytes = scanned.Sum(f => f.SizeBytes);
            List<CliCategoryBreakdownItem> breakdown = scanned
                .GroupBy(f => string.IsNullOrWhiteSpace(f.Category) ? "Other" : f.Category)
                .Select(g => new CliCategoryBreakdownItem(g.Key, g.Count(), g.Sum(x => x.SizeBytes)))
                .OrderByDescending(c => c.Bytes)
                .ToList();

            string targetJson = string.IsNullOrWhiteSpace(action.ReportJsonPath)
                ? Path.Combine(AppStoragePathsService.GetDataDirectory(), "cli-dryrun-report.json")
                : action.ReportJsonPath;

            var report = new CliAutomationReport(
                GeneratedUtc: DateTime.UtcNow,
                AppVersion: AppUpdateService.CurrentVersion,
                Profile: action.Profile?.ToString() ?? action.CustomProfileName ?? "ActiveSelection",
                DryRun: true,
                TotalFiles: scanned.Count,
                TotalBytes: totalBytes,
                TotalSizeFormatted: CleanerRule.FormatBytes(totalBytes),
                ElapsedMilliseconds: sw.ElapsedMilliseconds,
                Categories: breakdown);

            string writtenPath = ShellIntegrationAndProfilesService.WriteCliAutomationReportJson(targetJson, report);
            StatusMessage = $"[CLI DRY-RUN] {scanned.Count:N0} dosya ({report.TotalSizeFormatted}) tespit edildi (silinmedi). Rapor: {writtenPath}";
            AddLog($"[CLI DRY-RUN] {scanned.Count:N0} öğe ({report.TotalSizeFormatted}) -> {writtenPath}");
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("CLI DryRun", ex);
            StatusMessage = $"[CLI DRY-RUN HATA] {ex.Message}";
            AddLog($"[CLI DRY-RUN][HATA] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
            if (action.ExitWhenFinished)
                Application.Current?.Shutdown(0);
        }
    }

    // ==================== 21. DISK SNAPSHOT GROWTH DIFF ====================

    public bool HasTreemapDiffResults
    {
        get => _hasTreemapDiffResults;
        private set
        {
            if (_hasTreemapDiffResults != value)
            {
                _hasTreemapDiffResults = value;
                OnPropertyChanged();
            }
        }
    }

    public string TreemapDiffSummaryText
    {
        get => _treemapDiffSummaryText;
        private set
        {
            if (_treemapDiffSummaryText != value)
            {
                _treemapDiffSummaryText = value;
                OnPropertyChanged();
            }
        }
    }

    private void ExecuteSaveTreemapSnapshot()
    {
        if (_lastTreemapRawEntries.Count == 0 || string.IsNullOrWhiteSpace(TreemapActiveScanPath))
        {
            StatusMessage = "Anlık görüntü kaydetmek için önce 'Haritayı Oluştur' ile klasörü tarayın.";
            return;
        }

        long totalBytes = _lastTreemapRawEntries.Sum(x => x.SizeBytes);
        DiskFolderSnapshot saved = DiskSnapshotDiffService.SaveSnapshot(
            TreemapActiveScanPath,
            totalBytes,
            _lastTreemapRawEntries);

        string msg = $"📸 Anlık görüntü kaydedildi: {saved.RootPath} ({CleanerRule.FormatBytes(saved.TotalBytes)}, {saved.Entries.Count} öğe).";
        StatusMessage = msg;
        AddLog($"[SNAPSHOT] {msg}");
    }

    private async Task ExecuteCompareTreemapSnapshotAsync()
    {
        string targetPath = string.IsNullOrWhiteSpace(TreemapCurrentPath)
            ? TreemapActiveScanPath
            : TreemapCurrentPath;

        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
        {
            StatusMessage = "Karşılaştırma için geçerli bir hedef klasör seçin.";
            return;
        }

        DiskFolderSnapshot? baseline = DiskSnapshotDiffService.LoadSnapshot(targetPath);
        if (baseline is null)
        {
            StatusMessage = "Bu klasör için kayıtlı anlık görüntü yok; şimdi taranıp ilk temel görüntü oluşturuluyor...";
            await LoadTreemapAsync(forceRefresh: true);
            if (_lastTreemapRawEntries.Count > 0)
            {
                long total = _lastTreemapRawEntries.Sum(x => x.SizeBytes);
                DiskSnapshotDiffService.SaveSnapshot(targetPath, total, _lastTreemapRawEntries);
                StatusMessage = $"İlk anlık görüntü oluşturuldu ({CleanerRule.FormatBytes(total)}). Daha sonra büyüme farkı için tekrar tıklayın.";
            }
            return;
        }

        if (_lastTreemapRawEntries.Count == 0 ||
            !string.Equals(TreemapActiveScanPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            await LoadTreemapAsync(forceRefresh: true);
        }

        List<DiskGrowthDiffItem> diffs = DiskSnapshotDiffService.ComputeGrowthDiff(
            baseline,
            _lastTreemapRawEntries,
            minAbsDeltaBytes: 1024);

        TreemapDiffItems.Clear();
        foreach (DiskGrowthDiffItem item in diffs.Take(80))
            TreemapDiffItems.Add(item);

        HasTreemapDiffResults = TreemapDiffItems.Count > 0;

        long currentTotal = _lastTreemapRawEntries.Sum(x => x.SizeBytes);
        long netDelta = currentTotal - baseline.TotalBytes;
        string netText = netDelta >= 0
            ? $"+{CleanerRule.FormatBytes(netDelta)}"
            : $"-{CleanerRule.FormatBytes(Math.Abs(netDelta))}";

        TreemapDiffSummaryText = TreemapDiffItems.Count > 0
            ? $"📈 Büyüme Farkı ({baseline.CapturedUtc.ToLocalTime():dd.MM HH:mm} → Şimdi): Net {netText} • {TreemapDiffItems.Count} değişen öğe"
            : $"✅ {baseline.CapturedUtc.ToLocalTime():dd.MM HH:mm} anlık görüntüsüne göre anlamlı boyut farkı bulunmadı.";

        StatusMessage = TreemapDiffSummaryText;
        AddLog($"[SNAPSHOT DIFF] {TreemapDiffSummaryText}");
    }

    private void ExecuteClearTreemapDiff()
    {
        TreemapDiffItems.Clear();
        HasTreemapDiffResults = false;
        TreemapDiffSummaryText = string.Empty;
    }

    // ==================== 22. SILENT AUTO-START TO TRAY & ACTIVITY LOG TOOLS ====================

    public bool StartMinimizedWithWindows
    {
        get => _startMinimizedWithWindows;
        set
        {
            if (_startMinimizedWithWindows != value)
            {
                var (success, message) = ShellIntegrationAndProfilesService.SetAutoStartWithWindows(value);
                if (success)
                {
                    _startMinimizedWithWindows = value;
                    OnPropertyChanged();
                    StatusMessage = message;
                    AddLog($"[OTOMATİK BAŞLATMA] {message}");
                }
                else
                {
                    StatusMessage = message;
                    MessageBox.Show(message, "Windows Otomatik Başlatma", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    public string ActivityLogFilter
    {
        get => _activityLogFilter;
        set
        {
            string normalized = value ?? string.Empty;
            if (_activityLogFilter != normalized)
            {
                _activityLogFilter = normalized;
                OnPropertyChanged();
                FilteredActivityLogsView.Refresh();
            }
        }
    }

    private bool FilterActivityLogPredicate(object obj)
    {
        if (string.IsNullOrWhiteSpace(ActivityLogFilter))
            return true;

        return obj is string line &&
               line.Contains(ActivityLogFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void ExecuteCopyActivityLogs()
    {
        var visible = FilteredActivityLogsView.Cast<string>().ToList();
        if (visible.Count == 0)
        {
            StatusMessage = "Kopyalanacak günlük satırı bulunamadı.";
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, visible));
            StatusMessage = $"{visible.Count} işlem günlüğü satırı panoya kopyalandı.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Panoya kopyalanamadı: {ex.Message}";
        }
    }

    private void ExecuteExportActivityLogs()
    {
        var visible = FilteredActivityLogsView.Cast<string>().ToList();
        if (visible.Count == 0)
        {
            StatusMessage = "Dışa aktarılacak günlük satırı bulunamadı.";
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "İşlem Günlüğünü Dışa Aktar",
            Filter = "Log Dosyası (*.log)|*.log|Metin Dosyası (*.txt)|*.txt",
            FileName = $"FreeCleaner-Gunluk-{DateTime.Now:yyyyMMdd_HHmm}.log",
            DefaultExt = ".log"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllLines(dlg.FileName, visible);
            StatusMessage = $"Günlük kaydedildi: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Günlük kaydedilemedi: {ex.Message}";
        }
    }

    private void ExecuteClearActivityLogs()
    {
        ActivityLogs.Clear();
        FilteredActivityLogsView.Refresh();
        StatusMessage = "Ekrandaki işlem günlüğü temizlendi.";
    }

    // ==================== 23. PORTABLE MODE, SUPPORT BUNDLE, STORAGE FORECAST & FOLDER ORGANIZER ====================

    public bool IsPortableMode
    {
        get => _isPortableMode;
        set
        {
            if (_isPortableMode != value)
            {
                var (success, message) = AppStoragePathsService.SetPortableMode(value);
                if (success)
                {
                    _isPortableMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActiveDataDirectoryDisplay));
                    SaveCurrentSettings();
                    RefreshCleanupHistorySummary();
                    RefreshStorageForecast();
                    StatusMessage = message;
                    AddLog($"[TAŞINABİLİR MOD] {message}");
                }
                else
                {
                    StatusMessage = message;
                    MessageBox.Show(message, "Taşınabilir Mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    public string ActiveDataDirectoryDisplay =>
        LocalizationService.GetText(
            $"Veri Klasörü: {AppStoragePathsService.GetDataDirectory()}",
            $"Data Directory: {AppStoragePathsService.GetDataDirectory()}",
            _selectedLanguage);

    public bool ProactiveStorageAlertsEnabled
    {
        get => _proactiveStorageAlertsEnabled;
        set
        {
            if (_proactiveStorageAlertsEnabled != value)
            {
                _proactiveStorageAlertsEnabled = value;
                OnPropertyChanged();
                SaveCurrentSettings();
                if (_proactiveStorageAlertsEnabled && _lastStorageForecastSummary is not null)
                    EvaluateProactiveStorageForecastAlert(_lastStorageForecastSummary, SystemMetrics.DriveUsedPercent);
            }
        }
    }

    public int StorageAlertThresholdDays
    {
        get => _storageAlertThresholdDays;
        set
        {
            int normalized = value is 7 or 15 or 30 ? value : 15;
            if (_storageAlertThresholdDays != normalized)
            {
                _storageAlertThresholdDays = normalized;
                OnPropertyChanged();
                SaveCurrentSettings();
                if (_proactiveStorageAlertsEnabled && _lastStorageForecastSummary is not null)
                    EvaluateProactiveStorageForecastAlert(_lastStorageForecastSummary, SystemMetrics.DriveUsedPercent);
            }
        }
    }

    public string StorageForecastHeadline
    {
        get => _storageForecastHeadline;
        private set
        {
            if (_storageForecastHeadline != value)
            {
                _storageForecastHeadline = value;
                OnPropertyChanged();
            }
        }
    }

    public string StorageForecastDetail
    {
        get => _storageForecastDetail;
        private set
        {
            if (_storageForecastDetail != value)
            {
                _storageForecastDetail = value;
                OnPropertyChanged();
            }
        }
    }

    private void RefreshStorageForecast(SystemMetricsSnapshot? metricsOverride = null)
    {
        try
        {
            SystemMetricsSnapshot snapshot = metricsOverride ?? SystemMetrics;
            List<DailyDriveSample> samples = StorageForecastService.RecordDailySample(snapshot);
            StorageForecastSummary summary = StorageForecastService.ComputeForecast(samples);
            _lastStorageForecastSummary = summary;
            _lastForecastSampleDateKey = DateTime.Now.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            StorageForecastHeadline = summary.HeadlineText;
            StorageForecastDetail = summary.DetailText;
            EvaluateProactiveStorageForecastAlert(summary, snapshot.DriveUsedPercent);
        }
        catch
        {
            StorageForecastHeadline = "⏳ Veri bekleniyor";
            StorageForecastDetail = "Disk doluluk trendi hesaplanıyor.";
        }
    }

    private void EvaluateProactiveStorageForecastAlert(StorageForecastSummary summary, double usedPercent)
    {
        try
        {
            StorageAlertEvaluation eval = StorageForecastService.EvaluateProactiveAlert(
                summary,
                usedPercent,
                ProactiveStorageAlertsEnabled,
                StorageAlertThresholdDays,
                _lastStorageForecastAlertDate,
                languageCode: SelectedLanguage);

            if (!eval.ShouldNotify)
                return;

            _lastStorageForecastAlertDate = eval.TodayDateKey;
            SaveCurrentSettings();
            TrayNotificationRequested?.Invoke(eval.Title, eval.Message, true);
            AddLog($"[PROAKTİF DEPOLAMA UYARISI] {eval.Message}");
        }
        catch
        {
            // Proactive alert checks should never interrupt dashboard or timer updates.
        }
    }

    private void ExecuteExportSupportBundle()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Teşhis Destek Paketi (.zip) Oluştur",
            Filter = "ZIP Arşivi (*.zip)|*.zip",
            FileName = $"FreeCleaner-SupportBundle-{DateTime.Now:yyyyMMdd_HHmm}.zip",
            DefaultExt = ".zip"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            long lifetimeFreed = CleanupHistoryService.GetTotalLifetimeFreedBytes(CleanupHistoryService.LoadHistory());
            string zipPath = AppStoragePathsService.ExportSupportBundleZip(
                dlg.FileName,
                SystemMetrics,
                AllRules,
                LastFreedBytes,
                LastDeletedFiles,
                ActivityLogs,
                HealthScore,
                HealthGradeBadge,
                lifetimeFreed,
                RecentCleanupBars.ToList());

            StatusMessage = LocalizationService.GetText(
                $"📦 Teşhis destek paketi oluşturuldu: {zipPath}",
                $"📦 Diagnostic support bundle created: {zipPath}",
                _selectedLanguage);
            AddLog($"[TEŞHİS PAKETİ] Hassas verilerden arındırılmış ZIP paketi: {zipPath}");
            ShowToast(
                LocalizationService.GetText("Destek Paketi Oluşturuldu", "Support Bundle Created", _selectedLanguage),
                zipPath,
                ToastSeverity.Success,
                actionLabel: LocalizationService.Get("Loc_ToastOpenFolderBtn", _selectedLanguage),
                actionKey: $"OPEN_PATH:{zipPath}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Destek paketi oluşturulamadı: {ex.Message}";
            AddLog($"[HATA] Destek paketi oluşturulamadı: {ex.Message}");
        }
    }

    public string OrganizerTargetFolder
    {
        get => _organizerTargetFolder;
        set
        {
            if (_organizerTargetFolder != value)
            {
                _organizerTargetFolder = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public string OrganizerSummaryText
    {
        get => _organizerSummaryText;
        private set
        {
            if (_organizerSummaryText != value)
            {
                _organizerSummaryText = value;
                OnPropertyChanged();
            }
        }
    }

    private void SelectOrganizerPreset(string preset)
    {
        OrganizerTargetFolder = preset.Equals("Desktop", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        ExecutePreviewFolderOrganize();
    }

    private void BrowseOrganizerFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Düzenlenecek Klasörü Seçin",
            InitialDirectory = Directory.Exists(OrganizerTargetFolder)
                ? OrganizerTargetFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dlg.ShowDialog() == true)
        {
            OrganizerTargetFolder = dlg.FolderName;
            ExecutePreviewFolderOrganize();
        }
    }

    private void ExecutePreviewFolderOrganize()
    {
        if (string.IsNullOrWhiteSpace(OrganizerTargetFolder) || !Directory.Exists(OrganizerTargetFolder))
        {
            OrganizerSummaryText = "Geçerli bir klasör seçin.";
            return;
        }

        List<OrganizePlanItem> plan = SmartFolderOrganizerService.BuildOrganizePlan(OrganizerTargetFolder);
        OrganizerPlanItems.Clear();
        foreach (OrganizePlanItem item in plan)
            OrganizerPlanItems.Add(item);

        long totalBytes = plan.Sum(x => x.SizeBytes);
        OrganizerSummaryText = plan.Count > 0
            ? $"👁️ Önizleme: {plan.Count} dosya ({CleanerRule.FormatBytes(totalBytes)}) kategorilere ayrılacak."
            : "✅ Bu klasörde taşınacak dağınık dosya yok.";

        StatusMessage = OrganizerSummaryText;
        CommandManager.InvalidateRequerySuggested();
    }

    private void ExecuteApplyFolderOrganize()
    {
        if (OrganizerPlanItems.Count == 0)
            ExecutePreviewFolderOrganize();

        if (OrganizerPlanItems.Count == 0)
            return;

        if (MessageBox.Show(
                $"'{OrganizerTargetFolder}' içindeki {OrganizerPlanItems.Count} dosya uzantı kategorilerine (Kurulumlar, Belgeler, Görseller, Medya, Arşivler, Kod) taşınacak.\n\nİsterseniz sonradan 'Son Düzenlemeyi Geri Al' ile tamamını geri alabilirsiniz. Devam edilsin mi?",
                "Akıllı Klasör Düzenleyici",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var (moved, failed, notes) = SmartFolderOrganizerService.ExecutePlan(OrganizerPlanItems, OrganizerTargetFolder);
        foreach (string note in notes.Take(5))
            AddLog($"[DÜZENLEYİCİ NOT] {note}");

        OrganizerPlanItems.Clear();
        OrganizerSummaryText = $"🗂️ {moved} dosya düzenlendi (hata: {failed}).";
        StatusMessage = OrganizerSummaryText;
        AddLog($"[KLASÖR DÜZENLEYİCİ] '{OrganizerTargetFolder}': {OrganizerSummaryText}");
        CommandManager.InvalidateRequerySuggested();
    }

    private void ExecuteUndoFolderOrganize()
    {
        var (restored, failed, message) = SmartFolderOrganizerService.UndoLastOrganize();
        OrganizerSummaryText = message;
        StatusMessage = message;
        AddLog($"[KLASÖR GERİ AL] {restored} dosya iade edildi (hata: {failed}).");
        CommandManager.InvalidateRequerySuggested();
    }

}
