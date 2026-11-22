using System.Diagnostics;
using System.Windows;
using FreeCleaner.Models;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public partial class MainViewModel
{
    // ==================== 5-7. CORE CLEANER, BOOSTER, REPORTS, EXCLUSIONS & SCHEDULED MAINTENANCE ====================

    private async Task RunAnalyzeAsync()
    {
        if (!AllRules.Any(r => r.IsSelected))
        {
            string title = LocalizationService.GetText("Kategori Seçilmedi", "No Category Selected", _selectedLanguage);
            string body = LocalizationService.GetText(
                "Lütfen analiz etmek için en az bir temizlik kuralı seçin.",
                "Please select at least one cleanup rule before analyzing.",
                _selectedLanguage);
            StatusMessage = body;
            ShowToast(title, body, ToastSeverity.Warning);
            return;
        }

        IsBusy = true;
        HasAnalyzed = false;
        ProgressPercent = 0;

        foreach (var oldItem in DiscoveredFiles)
            oldItem.PropertyChanged -= OnFileItemPropertyChanged;
        DiscoveredFiles.Clear();

        TotalDetectedBytes = 0;
        TotalDetectedFiles = 0;

        var runningBrowsers = SystemInfoService.GetRunningBrowsers();
        if (runningBrowsers.Count > 0 && AllRules.Any(r => r.Group == RuleGroup.Browsers && r.IsSelected))
        {
            AddLog($"[BİLGİ] Açık tarayıcılar algılandı ({string.Join(", ", runningBrowsers)}). Bazı önbellek dosyaları kilitli olabilir.");
        }

        StatusMessage = LocalizationService.GetText(
            "Sistem analiz ediliyor (Simülasyon Modu)...",
            "Analyzing system (Safe Simulation Mode)...",
            _selectedLanguage);
        AddLog("Analiz başlatıldı (Hiçbir dosya silinmiyor, Beyaz Liste korunuyor)...");

        _cts = new CancellationTokenSource();
        var progress = new Progress<(int Percent, string CurrentRuleName, long TotalFoundBytes, int TotalFoundCount)>(report =>
        {
            ProgressPercent = report.Percent;
            CurrentOperationDetail = LocalizationService.GetText(
                $"Taranıyor: {report.CurrentRuleName}",
                $"Scanning: {report.CurrentRuleName}",
                _selectedLanguage);
            TotalDetectedBytes = report.TotalFoundBytes;
            TotalDetectedFiles = report.TotalFoundCount;
        });

        try
        {
            var results = await _engine.AnalyzeAsync(AllRules, ExcludedPaths.ToList(), progress, _cts.Token);

            foreach (var item in results)
            {
                item.PropertyChanged += OnFileItemPropertyChanged;
                DiscoveredFiles.Add(item);
            }

            RecalculateSelectedTotals();
            HasAnalyzed = true;
            ProgressPercent = 100;

            StatusMessage = LocalizationService.GetText(
                $"Analiz tamamlandı: {TotalDetectedFiles:N0} öğede toplam {TotalDetectedFormatted} gereksiz veri bulundu.",
                $"Analysis complete: found {TotalDetectedFormatted} across {TotalDetectedFiles:N0} items.",
                _selectedLanguage);
            CurrentOperationDetail = LocalizationService.GetText(
                "Sonuçları inceleyebilir ve 'Temizliği Başlat' ile silebilirsiniz.",
                "Review detected items and click 'Clean Now' to reclaim space.",
                _selectedLanguage);
            AddLog($"Analiz tamamlandı: {TotalDetectedFiles:N0} dosya ({TotalDetectedFormatted}) tespit edildi.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = LocalizationService.GetText("Analiz kullanıcı tarafından iptal edildi.", "Analysis cancelled by user.", _selectedLanguage);
            CurrentOperationDetail = LocalizationService.GetText("İptal edildi.", "Cancelled.", _selectedLanguage);
            AddLog("Analiz işlemi iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.GetText($"Analiz sırasında hata: {ex.Message}", $"Analysis error: {ex.Message}", _selectedLanguage);
            AddLog($"[HATA] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshSystemMetrics();
        }
    }

    private async Task RunCleanAsync()
    {
        int selectedCount = TotalSelectedFilesCount;
        string selectedSize = TotalSelectedSizeFormatted;

        if (selectedCount == 0)
            return;

        var selectedHighRiskRules = AllRules
            .Where(rule => rule.RiskLevel == RiskLevel.High &&
                           DiscoveredFiles.Any(file => file.IsSelected && file.RuleId == rule.Id))
            .ToList();

        if (selectedHighRiskRules.Count > 0)
        {
            string highRiskNames = string.Join(", ", selectedHighRiskRules.Select(rule => rule.Name));
            var highRiskConfirmation = MessageBox.Show(
                LocalizationService.GetText(
                    $"Yüksek riskli temizlik seçenekleri seçildi:\n\n{highRiskNames}\n\n" +
                    "Bu işlemler oturum verilerini, geçmişleri veya geri alınması zor sistem verilerini etkileyebilir. " +
                    "Seçili öğeleri temizlemek istediğinizi açıkça onaylıyor musunuz?",
                    $"High-risk cleanup categories are selected:\n\n{highRiskNames}\n\n" +
                    "These may affect active browser sessions, history, or non-recoverable data. Continue?",
                    _selectedLanguage),
                LocalizationService.GetText("Yüksek Riskli Temizlik Onayı", "High-Risk Cleanup Confirmation", _selectedLanguage),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (highRiskConfirmation != MessageBoxResult.Yes)
            {
                AddLog("Yüksek riskli temizlik kullanıcı tarafından onaylanmadı.");
                return;
            }
        }

        string modeDescription = UseRecycleBinSafety
            ? LocalizationService.GetText(
                "Seçili dosyalar Geri Dönüşüm Kutusu'na taşınacaktır.",
                "Selected files will be moved to the Recycle Bin.",
                _selectedLanguage)
            : LocalizationService.GetText(
                "Seçili dosyalar kalıcı olarak silinecektir (açık/kullanımda olan sistem dosyaları güvenle atlanır).",
                "Selected files will be permanently deleted (locked in-use files are safely skipped).",
                _selectedLanguage);

        var confirm = MessageBox.Show(
            LocalizationService.GetText(
                $"Toplam {selectedCount:N0} adet gereksiz öğe ({selectedSize}) temizlenecek.\n\nMod: {modeDescription}\n\nTemizleme işlemini onaylıyor musunuz?",
                $"Clean {selectedCount:N0} selected items ({selectedSize})?\n\nMode: {modeDescription}",
                _selectedLanguage),
            LocalizationService.GetText("FreeCleaner - Temizleme Onayı", "FreeCleaner - Cleanup Confirmation", _selectedLanguage),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            AddLog("Temizleme işlemi kullanıcı tarafından vazgeçildi.");
            return;
        }

        if (DiscoveredFiles.Any(x => x.IsSelected && x.IsRecycleBinItem))
        {
            var recycleBinConfirm = MessageBox.Show(
                LocalizationService.GetText(
                    "Bu işlem Geri Dönüşüm Kutusu'nu tamamen boşaltacaktır ve geri alınamaz.\n\nDevam etmek istediğinizden emin misiniz?",
                    "This action will permanently empty the Recycle Bin and cannot be undone.\n\nAre you sure you want to continue?",
                    _selectedLanguage),
                LocalizationService.GetText("Geri Dönüşüm Kutusu - Son Onay", "Recycle Bin - Final Confirmation", _selectedLanguage),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (recycleBinConfirm != MessageBoxResult.Yes)
            {
                AddLog("Geri Dönüşüm Kutusu boşaltma işlemi kullanıcı tarafından vazgeçildi.");
                return;
            }
        }

        var activeRulesForLockCheck = AllRules
            .Where(rule => DiscoveredFiles.Any(file => file.IsSelected && file.RuleId == rule.Id))
            .ToList();
        var lockSpecs = ProcessLockGuardService.ResolveTargetProcessSpecsForCleanerRules(activeRulesForLockCheck);
        var lockConflicts = ProcessLockGuardService.DetectRunningConflicts(lockSpecs);
        if (lockConflicts.Count > 0)
        {
            string lockList = string.Join("\n", lockConflicts.Select(g => $"• {g.FriendlyName} ({g.RunningInstanceCount})"));
            var lockPrompt = MessageBox.Show(
                LocalizationService.GetText(
                    $"Temizlenecek önbellekleri şu anda kullanan açık uygulamalar tespit edildi:\n\n{lockList}\n\n" +
                    "• Evet: Bu uygulamaları güvenle kapatıp tüm kilitli dosyaları temizle\n" +
                    "• Hayır: Uygulamaları kapatmadan devam et (kilitli dosyalar atlanır)\n" +
                    "• İptal: Temizliği durdur",
                    $"Running applications are locking target caches:\n\n{lockList}\n\n" +
                    "• Yes: Safely close these apps before cleaning\n" +
                    "• No: Continue without closing (locked files skipped)\n" +
                    "• Cancel: Abort cleanup",
                    _selectedLanguage),
                "Process Lock Guard",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (lockPrompt == MessageBoxResult.Cancel)
            {
                AddLog("Temizlik kilitli süreç uyarısı üzerine kullanıcı tarafından iptal edildi.");
                return;
            }

            if (lockPrompt == MessageBoxResult.Yes)
            {
                StatusMessage = LocalizationService.GetText(
                    "Kilitli tarayıcı / uygulamalar güvenle kapatılıyor...",
                    "Safely closing locking browsers/applications...",
                    _selectedLanguage);
                var (closedCount, notes) = await ProcessLockGuardService.TryCloseProcessGroupsAsync(lockConflicts);
                AddLog($"[SÜREÇ KORUMASI] {closedCount} çalışan süreç kapatıldı.");
                foreach (string note in notes)
                    AddLog($"[SÜREÇ KORUMASI] {note}");
            }
        }

        IsBusy = true;
        ProgressPercent = 0;
        LastFreedBytes = 0;
        LastDeletedFiles = 0;
        StatusMessage = LocalizationService.GetText("Temizlik işlemi yürütülüyor...", "Cleaning in progress...", _selectedLanguage);
        AddLog($"Temizlik başlatıldı ({selectedCount:N0} öğe, {selectedSize})...");

        _cts = new CancellationTokenSource();
        var progress = new Progress<(int Percent, string CurrentFile, long FreedBytes, int DeletedCount)>(report =>
        {
            ProgressPercent = report.Percent;
            CurrentOperationDetail = LocalizationService.GetText(
                $"Temizleniyor: {report.CurrentFile}",
                $"Cleaning: {report.CurrentFile}",
                _selectedLanguage);
            LastFreedBytes = report.FreedBytes;
            LastDeletedFiles = report.DeletedCount;
        });

        try
        {
            var summary = await _engine.CleanAsync(
                DiscoveredFiles,
                AllRules,
                UseRecycleBinSafety,
                progress,
                _cts.Token);

            LastFreedBytes = summary.FreedBytes;
            LastDeletedFiles = summary.DeletedFilesCount;
            RecordCleanupHistory("Akıllı Temizlik", summary.FreedBytes, summary.DeletedFilesCount);

            var remaining = DiscoveredFiles
                .Where(x => x.Status is not ("Silindi" or "Boşaltıldı" or "Zaten Silinmiş" or "Geri Dönüşüm'e Taşındı"))
                .ToList();

            DiscoveredFiles.Clear();
            foreach (var rem in remaining)
                DiscoveredFiles.Add(rem);

            RecalculateSelectedTotals();
            ProgressPercent = 100;

            StatusMessage = LocalizationService.GetText(
                $"Temizlik tamamlandı! {summary.DeletedFilesCount:N0} dosya silindi, {CleanerRule.FormatBytes(summary.FreedBytes)} alan açıldı.",
                $"Cleanup complete! Removed {summary.DeletedFilesCount:N0} files, reclaimed {CleanerRule.FormatBytes(summary.FreedBytes)}.",
                _selectedLanguage);
            CurrentOperationDetail = summary.SkippedFilesCount > 0
                ? LocalizationService.GetText(
                    $"Süre: {summary.Elapsed.TotalSeconds:F1} sn • {summary.SkippedFilesCount:N0} kilitli dosya atlandı.",
                    $"Elapsed: {summary.Elapsed.TotalSeconds:F1}s • {summary.SkippedFilesCount:N0} locked files skipped.",
                    _selectedLanguage)
                : LocalizationService.GetText(
                    $"Süre: {summary.Elapsed.TotalSeconds:F1} sn • Tüm seçili öğeler temizlendi.",
                    $"Elapsed: {summary.Elapsed.TotalSeconds:F1}s • All selected items cleaned.",
                    _selectedLanguage);

            ShowToast(
                LocalizationService.GetText("Temizlik Tamamlandı", "Cleanup Completed", _selectedLanguage),
                StatusMessage,
                ToastSeverity.Success);

            AddLog($"[TAMAMLANDI] {CleanerRule.FormatBytes(summary.FreedBytes)} boş alan kazanıldı ({summary.DeletedFilesCount:N0} silindi, {summary.SkippedFilesCount:N0} kilitli dosya atlandı).");
            foreach (var detail in summary.Logs.Where(log => log.StartsWith("[ATLANDI]", StringComparison.Ordinal)).Take(20))
                AddLog(detail);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = LocalizationService.GetText("Temizleme işlemi durduruldu.", "Cleanup stopped.", _selectedLanguage);
            AddLog("Temizleme işlemi kullanıcı tarafından durduruldu.");
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.GetText($"Temizleme hatası: {ex.Message}", $"Cleanup error: {ex.Message}", _selectedLanguage);
            AddLog($"[HATA] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshSystemMetrics();
        }
    }

    private void LoadTopMemoryProcesses()
    {
        TopMemoryProcesses.Clear();
        foreach (var proc in SystemBoosterAndReportService.GetTopMemoryProcesses(15))
            TopMemoryProcesses.Add(proc);
    }

    private async Task RunOptimizeRamAsync()
    {
        IsBusy = true;
        StatusMessage = LocalizationService.GetText(
            "Arka plan işlemlerinin kullanılmayan bellek blokları serbest bırakılıyor...",
            "Trimming unused background process working sets...",
            _selectedLanguage);
        try
        {
            var (freedBytes, count) = await SystemBoosterAndReportService.OptimizeRamWorkingSetAsync();
            RefreshSystemMetrics();
            LoadTopMemoryProcesses();

            string freedText = CleanerRule.FormatBytes(freedBytes);
            StatusMessage = LocalizationService.GetText(
                $"RAM Optimizasyonu Tamamlandı: {count} süreç optimize edildi, {freedText} bellek serbest bırakıldı.",
                $"RAM Optimization Complete: trimmed {count} processes, freed {freedText}.",
                _selectedLanguage);
            ShowToast("RAM Booster", StatusMessage, ToastSeverity.Success);
            AddLog($"[RAM HIZLANDIRICI] {count} işlem optimize edildi, {freedText} RAM boşaltıldı.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunFlushDnsAsync()
    {
        IsBusy = true;
        try
        {
            var (_, msg) = await SystemBoosterAndReportService.FlushDnsCacheAsync();
            StatusMessage = LocalizationService.GetText($"DNS Önbelleği Temizlendi: {msg}", $"DNS Cache Flushed: {msg}", _selectedLanguage);
            ShowToast("DNS Booster", StatusMessage, ToastSeverity.Success);
            AddLog($"[AĞ OPTİMİZASYONU] ipconfig /flushdns -> {msg}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunResetNetCacheAsync()
    {
        IsBusy = true;
        try
        {
            var (_, msg) = await SystemBoosterAndReportService.ShowArpAndNetStatsAsync();
            StatusMessage = LocalizationService.GetText($"Ağ Hedef Önbelleği Sıfırlandı: {msg}", $"Network Cache Reset: {msg}", _selectedLanguage);
            AddLog($"[AĞ OPTİMİZASYONU] {msg}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExportHtmlAndJsonReports()
    {
        try
        {
            long lifetimeFreed = CleanupHistoryService.GetTotalLifetimeFreedBytes(CleanupHistoryService.LoadHistory());
            var (htmlPath, jsonPath) = SystemBoosterAndReportService.ExportDiagnosticReports(
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
                "HTML ve JSON raporları Masaüstü\\FreeCleaner_Raporlar klasörüne kaydedildi.",
                "HTML and JSON diagnostic reports exported to Desktop\\FreeCleaner_Raporlar.",
                _selectedLanguage);
            AddLog($"[RAPOR] HTML Raporu: {htmlPath}");
            AddLog($"[RAPOR] JSON Raporu: {jsonPath}");
            ShowToast(
                LocalizationService.GetText("Rapor Dışa Aktarıldı", "Report Exported", _selectedLanguage),
                htmlPath,
                ToastSeverity.Success,
                actionLabel: LocalizationService.Get("Loc_ToastOpenFolderBtn", _selectedLanguage),
                actionKey: $"OPEN_PATH:{htmlPath}");
        }
        catch (Exception ex)
        {
            ShowToast(
                LocalizationService.GetText("Hata", "Error", _selectedLanguage),
                LocalizationService.GetText($"Rapor oluşturulamadı: {ex.Message}", $"Report export failed: {ex.Message}", _selectedLanguage),
                ToastSeverity.Error);
        }
    }

    private void AddNewExclusion()
    {
        string trimmed = NewExclusionInput.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        if (!ExcludedPaths.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            ExcludedPaths.Add(trimmed);
            NewExclusionInput = string.Empty;
            SaveCurrentSettings();
            AddLog($"[BEYAZ LİSTE] Yeni istisna eklendi: {trimmed}");
        }
    }

    private void BrowseAndAddExclusionFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationService.GetText(
                "Beyaz Listeye (Koruma Altına) Eklenecek Klasörü Seçin",
                "Select Folder to Protect in Whitelist",
                _selectedLanguage)
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            if (!ExcludedPaths.Contains(dlg.FolderName, StringComparer.OrdinalIgnoreCase))
            {
                ExcludedPaths.Add(dlg.FolderName);
                SaveCurrentSettings();
                AddLog($"[BEYAZ LİSTE] Klasör korumaya alındı: {dlg.FolderName}");
            }
        }
    }

    private void SaveCurrentSettings()
    {
        ExclusionAndSettingsService.Save(BuildCurrentConfigSnapshot());
    }

    private async Task CheckAutoMonitorThresholdAsync()
    {
        if (IsBusy || !AutoMonitorEnabled) return;
        try
        {
            var results = await _engine.AnalyzeAsync(AllRules, ExcludedPaths.ToList());
            long totalBytes = results.Sum(x => x.SizeBytes);
            long thresholdBytes = AutoMonitorThresholdMb * 1024L * 1024L;
            if (totalBytes >= thresholdBytes)
            {
                string msg = LocalizationService.GetText(
                    $"Sistemde {CleanerRule.FormatBytes(totalBytes)} gereksiz dosya birikti!",
                    $"{CleanerRule.FormatBytes(totalBytes)} of junk files accumulated on disk!",
                    _selectedLanguage);
                StatusMessage = $"[OTOMATİK İZLEME] {msg}";
                AddLog($"[OTOMATİK UYARI] Eşik ({AutoMonitorThresholdMb} MB) aşıldı: {CleanerRule.FormatBytes(totalBytes)} çöp tespit edildi.");
                TrayNotificationRequested?.Invoke("FreeCleaner Pro", msg, true);
            }
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Otomatik izleme", ex);
        }
    }

    public async Task RunScheduledMaintenanceAsync(bool closeWhenFinished = false)
    {
        var safeRules = AllRules
            .Where(rule => rule.IsSelected && rule.Risk == RiskLevel.Low)
            .ToList();
        if (safeRules.Count == 0)
        {
            if (closeWhenFinished)
                Application.Current.Shutdown();
            return;
        }

        _lastScheduledMaintenanceUtc = DateTime.UtcNow;
        try
        {
            StatusMessage = LocalizationService.GetText(
                "Zamanlanmış güvenli bakım için düşük riskli öğeler taranıyor...",
                "Scanning low-risk items for scheduled safe maintenance...",
                _selectedLanguage);
            var results = await _engine.AnalyzeAsync(safeRules, ExcludedPaths.ToList());
            if (results.Count == 0)
            {
                AddLog("[ZAMANLI BAKIM] Düşük riskli temizlenecek öğe bulunamadı.");
                return;
            }

            var confirm = MessageBox.Show(
                LocalizationService.GetText(
                    $"Zamanlanmış güvenli bakımda {results.Count:N0} düşük riskli öğe " +
                    $"({CleanerRule.FormatBytes(results.Sum(item => item.SizeBytes))}) bulundu.\n\n" +
                    "Öğeler Geri Dönüşüm Kutusu'na taşınacak. Devam edilsin mi?",
                    $"Scheduled maintenance found {results.Count:N0} low-risk items " +
                    $"({CleanerRule.FormatBytes(results.Sum(item => item.SizeBytes))}).\n\n" +
                    "Items will be moved to the Recycle Bin. Continue?",
                    _selectedLanguage),
                LocalizationService.GetText("Zamanlanmış Güvenli Bakım", "Scheduled Safe Maintenance", _selectedLanguage),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                AddLog("[ZAMANLI BAKIM] Kullanıcı temizliği onaylamadı.");
                return;
            }

            var summary = await _engine.CleanAsync(results, safeRules, moveToRecycleBinInsteadOfPermanentDelete: true);
            AddLog($"[ZAMANLI BAKIM] {summary.DeletedFilesCount:N0} öğe Geri Dönüşüm Kutusu'na taşındı, " +
                   $"{CleanerRule.FormatBytes(summary.FreedBytes)} alan işlendi.");
        }
        catch (OperationCanceledException)
        {
            AddLog("[ZAMANLI BAKIM] İşlem iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.GetText($"Zamanlanmış bakım hatası: {ex.Message}", $"Scheduled maintenance error: {ex.Message}", _selectedLanguage);
            AddLog($"[ZAMANLI BAKIM][HATA] {ex.Message}");
        }
        finally
        {
            if (closeWhenFinished)
                Application.Current.Shutdown();
        }
    }

    private async Task CheckScheduledMaintenanceAsync()
    {
        if (IsBusy || !ScheduledMaintenanceEnabled)
            return;

        if (DateTime.UtcNow - _lastScheduledMaintenanceUtc < TimeSpan.FromHours(ScheduledMaintenanceIntervalHours))
            return;

        await RunScheduledMaintenanceAsync();
    }

    private void InstallScheduledMaintenance()
    {
        var result = ScheduledMaintenanceService.Install(ScheduledMaintenanceIntervalHours);
        StatusMessage = result.Message;
        AddLog($"[ZAMANLI BAKIM] {result.Message}");
        ShowToast(
            LocalizationService.GetText("Zamanlanmış Bakım", "Scheduled Maintenance", _selectedLanguage),
            result.Message,
            result.Success ? ToastSeverity.Success : ToastSeverity.Warning);
    }

    private void RemoveScheduledMaintenance()
    {
        var result = ScheduledMaintenanceService.Remove();
        StatusMessage = result.Message;
        AddLog($"[ZAMANLI BAKIM] {result.Message}");
        ShowToast(
            LocalizationService.GetText("Zamanlanmış Bakım", "Scheduled Maintenance", _selectedLanguage),
            result.Message,
            result.Success ? ToastSeverity.Info : ToastSeverity.Warning);
    }
}
