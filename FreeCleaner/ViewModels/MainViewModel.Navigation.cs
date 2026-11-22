using System.Windows.Input;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public partial class MainViewModel
{
    private const int MaxNavHistoryDepth = 50;
    private readonly List<string> _navBackHistory = [];
    private readonly List<string> _navForwardHistory = [];
    private bool _isNavigatingHistory;
    private string _activeSection = "Dashboard";
    private List<string> _expandedNavGroups = ["FavoritesExpander", "NavCatCleaningExpander"];

    public bool CanNavigateBack => _navBackHistory.Count > 0;
    public bool CanNavigateForward => _navForwardHistory.Count > 0;
    public IReadOnlyList<string> ExpandedNavGroups => _expandedNavGroups;

    public string ActiveSection
    {
        get => _activeSection;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _activeSection == value)
                return;

            string previous = _activeSection;
            if (!_isNavigatingHistory && !string.IsNullOrWhiteSpace(previous))
            {
                PushBoundedHistory(_navBackHistory, previous);
                _navForwardHistory.Clear();
            }

            _activeSection = value;
            NotifyActiveSectionChanged();

            FilteredRulesView.Refresh();
            FilteredFilesView.Refresh();

            TriggerSectionLazyLoad(_activeSection);
            UpdateSectionMonitoring(_activeSection);
        }
    }

    public bool NavigateBack() => NavigateHistoryStep(_navBackHistory, _navForwardHistory);

    public bool NavigateForward() => NavigateHistoryStep(_navForwardHistory, _navBackHistory);

    public void RemoveFavoriteSection(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
            return;

        int existingIdx = _favoriteSectionKeys.FindIndex(k => k.Equals(sectionKey, StringComparison.OrdinalIgnoreCase));
        if (existingIdx < 0)
            return;

        _favoriteSectionKeys.RemoveAt(existingIdx);
        RebuildFavoriteNavItems();
        OnPropertyChanged(nameof(IsCurrentSectionFavorite));
        OnPropertyChanged(nameof(FavoriteButtonText));
        SaveCurrentSettings();

        string removedTitle = LocalizationService.GetSectionTitle(sectionKey, _selectedLanguage);
        StatusMessage = LocalizationService.GetText(
            $"'{removedTitle}' favorilerden çıkarıldı.",
            $"Removed '{removedTitle}' from favorites.",
            _selectedLanguage);
    }

    public void SaveExpandedNavGroups(IEnumerable<string>? groupNames)
    {
        _expandedNavGroups = (groupNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        SaveCurrentSettings();
    }

    private bool NavigateHistoryStep(List<string> sourceStack, List<string> targetStack)
    {
        if (sourceStack.Count == 0)
            return false;

        string targetSection = sourceStack[^1];
        sourceStack.RemoveAt(sourceStack.Count - 1);

        if (!string.IsNullOrWhiteSpace(_activeSection))
            PushBoundedHistory(targetStack, _activeSection);

        _isNavigatingHistory = true;
        try
        {
            ActiveSection = targetSection;
        }
        finally
        {
            _isNavigatingHistory = false;
        }

        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        CommandManager.InvalidateRequerySuggested();
        return true;
    }

    private static void PushBoundedHistory(List<string> stack, string sectionKey)
    {
        if (stack.Count >= MaxNavHistoryDepth)
            stack.RemoveAt(0);
        stack.Add(sectionKey);
    }

    private void NotifyActiveSectionChanged()
    {
        OnPropertyChanged(nameof(ActiveSection));
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        OnPropertyChanged(nameof(IsDashboardSection));
        OnPropertyChanged(nameof(IsLiveMonitorSection));
        OnPropertyChanged(nameof(IsAllSection));
        OnPropertyChanged(nameof(IsSystemSection));
        OnPropertyChanged(nameof(IsBrowsersSection));
        OnPropertyChanged(nameof(IsAppsSection));
        OnPropertyChanged(nameof(IsCleanerViewVisible));
        OnPropertyChanged(nameof(IsCurrentSectionFavorite));
        OnPropertyChanged(nameof(FavoriteButtonText));
        OnPropertyChanged(nameof(IsStartupSection));
        OnPropertyChanged(nameof(IsRegistrySection));
        OnPropertyChanged(nameof(IsDiskAnalyzerSection));
        OnPropertyChanged(nameof(IsUninstallerSection));
        OnPropertyChanged(nameof(IsBoosterSection));
        OnPropertyChanged(nameof(IsDevCleanerSection));
        OnPropertyChanged(nameof(IsShredderSection));
        OnPropertyChanged(nameof(IsPrivacySection));
        OnPropertyChanged(nameof(IsNetworkSection));
        OnPropertyChanged(nameof(IsTasksAndTreeSection));
        OnPropertyChanged(nameof(IsDownloadsSection));
        OnPropertyChanged(nameof(IsContextMenuSection));
        OnPropertyChanged(nameof(IsServicesSection));
        OnPropertyChanged(nameof(IsSmartPowerSection));
        OnPropertyChanged(nameof(IsRepairCenterSection));
        OnPropertyChanged(nameof(IsUnlockerSection));
        OnPropertyChanged(nameof(IsBrowserExtensionsSection));
        OnPropertyChanged(nameof(IsDriversUpdatesSection));
        OnPropertyChanged(nameof(IsPathDoctorSection));
        OnPropertyChanged(nameof(IsCrashLogsSection));
        OnPropertyChanged(nameof(IsWifiDnsSection));
        OnPropertyChanged(nameof(IsPackageCachesSection));
        OnPropertyChanged(nameof(IsRestoreGodModeSection));
        OnPropertyChanged(nameof(IsFileAssocSection));
        OnPropertyChanged(nameof(IsPortHunterSection));
        OnPropertyChanged(nameof(IsBloatwareSection));
        OnPropertyChanged(nameof(IsDiskBenchSection));
        OnPropertyChanged(nameof(IsHardwareSpecsSection));
        OnPropertyChanged(nameof(IsUsbForensicsSection));
        OnPropertyChanged(nameof(IsProcessSecuritySection));
        OnPropertyChanged(nameof(IsBatchRenamerSection));
        OnPropertyChanged(nameof(IsNetworkAdapterDoctorSection));
        OnPropertyChanged(nameof(IsFontManagerSection));
        OnPropertyChanged(nameof(IsClipboardShieldSection));
        OnPropertyChanged(nameof(IsAiModelCleanerSection));
        OnPropertyChanged(nameof(IsFirewallBlockerSection));
        OnPropertyChanged(nameof(IsCryptoVaultSection));
        OnPropertyChanged(nameof(IsWin11TweakerSection));
        OnPropertyChanged(nameof(IsBitLockerSection));
        OnPropertyChanged(nameof(IsWingetUpdaterSection));
        OnPropertyChanged(nameof(IsLanScannerSection));
        OnPropertyChanged(nameof(IsSqliteOptimizerSection));
        OnPropertyChanged(nameof(IsCredentialSshSection));
        OnPropertyChanged(nameof(IsProcessPrioritySection));
        OnPropertyChanged(nameof(IsBootPerformanceSection));
        OnPropertyChanged(nameof(IsDockerWslSection));
        OnPropertyChanged(nameof(IsDefenderAuditorSection));
        OnPropertyChanged(nameof(IsMonitorGpuSection));
        OnPropertyChanged(nameof(IsGitRepoCompressorSection));
        OnPropertyChanged(nameof(IsDiskTreemapSection));
        OnPropertyChanged(nameof(IsDeepSpaceRecoverySection));
        OnPropertyChanged(nameof(IsSimilarPhotosSection));
        OnPropertyChanged(nameof(IsExifScrubberSection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(SectionTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    private void TriggerSectionLazyLoad(string activeSection)
    {
        if (activeSection == "Startup" && StartupItems.Count == 0)
            LoadStartupItems();
        else if (activeSection == "Uninstaller" && InstalledApps.Count == 0)
            _ = LoadInstalledAppsAsync();
        else if (activeSection == "Booster")
            LoadTopMemoryProcesses();
        else if (activeSection == "Privacy")
            _ = RunScanPrivacyTracesAsync();
        else if (activeSection == "Network")
        {
            _ = LoadActiveConnectionsAsync();
            LoadHostsEntries();
        }
        else if (activeSection == "TasksAndTree")
        {
            if (ScheduledTasks.Count == 0) _ = LoadScheduledTasksAsync();
            if (FolderSpaceItems.Count == 0) _ = RunAnalyzeFolderSpaceTreeAsync();
        }
        else if (activeSection == "Downloads" && DownloadsAndEmptyItems.Count == 0)
            _ = RunScanOldInstallersAsync();
        else if (activeSection == "ContextMenu" && ContextMenuEntries.Count == 0)
            LoadContextMenuEntries();
        else if (activeSection == "Services" && OptimizableServices.Count == 0)
            _ = LoadOptimizableServicesAsync();
        else if (activeSection == "SmartPower" && PhysicalDisks.Count == 0)
            _ = LoadDiskAndPowerAsync();
        else if (activeSection == "BrowserExtensions" && BrowserExtensions.Count == 0)
            _ = LoadBrowserExtensionsAsync();
        else if (activeSection == "DriversUpdates" && DriversAndHotfixes.Count == 0)
            _ = LoadDriversAndHotfixesAsync();
        else if (activeSection == "PathDoctor" && PathAndShortcutIssues.Count == 0)
            _ = LoadPathAndShortcutIssuesAsync();
        else if (activeSection == "CrashLogs" && CrashAndEventLogs.Count == 0)
            _ = LoadCrashAndEventLogsAsync();
        else if (activeSection == "WifiDns" && WifiAndDnsItems.Count == 0)
            _ = LoadWifiAndDnsAsync();
        else if (activeSection == "PackageCaches" && GlobalPackageCaches.Count == 0)
            _ = LoadGlobalPackageCachesAsync();
        else if (activeSection == "RestoreGodMode" && RestorePoints.Count == 0)
            _ = LoadRestorePointsAsync();
        else if (activeSection == "FileAssoc" && FileAssociations.Count == 0)
            _ = LoadFileAssociationsAsync();
        else if (activeSection == "PortHunter" && ListeningPorts.Count == 0)
            _ = LoadListeningPortsAsync();
        else if (activeSection == "Bloatware" && AppxBloatwareItems.Count == 0)
            _ = LoadAppxBloatwareAsync();
        else if (activeSection == "DiskBench" && AvailableBenchmarkDrives.Count == 0)
            _ = LoadBenchmarkDrivesAsync();
        else if (activeSection == "HardwareSpecs" && HardwareSpecs.Count == 0)
            _ = LoadHardwareSpecsAsync();
        else if (activeSection == "UsbForensics" && UsbHistoryItems.Count == 0)
            _ = LoadUsbHistoryAsync();
        else if (activeSection == "ProcessSecurity" && ProcessSecurityItems.Count == 0)
            _ = LoadProcessSecurityItemsAsync();
        else if (activeSection == "BatchRenamer" && BatchRenameItems.Count == 0)
            GenerateBatchRenamePreview();
        else if (activeSection == "NetworkAdapterDoctor" && NetworkAdapters.Count == 0)
            _ = LoadNetworkAdaptersAsync();
        else if (activeSection == "FontManager" && InstalledFonts.Count == 0)
            _ = LoadInstalledFontsAsync();
        else if (activeSection == "ClipboardShield" && ClipboardAuditItems.Count == 0)
            ExecuteInspectClipboardNow();
        else if (activeSection == "AiModelCleaner" && AiModelCaches.Count == 0)
            _ = LoadAiModelCachesAsync();
        else if (activeSection == "FirewallBlocker" && FirewallBlockRules.Count == 0)
            _ = LoadFirewallBlockRulesAsync();
        else if (activeSection == "Win11Tweaker" && WindowsTweaks.Count == 0)
            LoadWindowsTweaks();
        else if (activeSection == "BitLocker" && VolumeSecurityItems.Count == 0)
            _ = LoadVolumeSecurityAndMountsAsync();
        else if (activeSection == "WingetUpdater" && WingetUpgrades.Count == 0)
            _ = LoadWingetUpgradesAsync();
        else if (activeSection == "LanScanner" && LanDevices.Count == 0)
            _ = LoadLanDevicesAsync();
        else if (activeSection == "SqliteOptimizer" && SqliteDatabases.Count == 0)
            _ = LoadSqliteDatabasesAsync();
        else if (activeSection == "CredentialSsh" && CredentialsAndSshItems.Count == 0)
            _ = LoadCredentialsAndSshAsync();
        else if (activeSection == "ProcessPriority" && ProcessPriorityItems.Count == 0)
            _ = LoadProcessPriorityItemsAsync();
        else if (activeSection == "BootPerformance" && BootPerformanceItems.Count == 0)
            _ = LoadBootPerformanceAsync();
        else if (activeSection == "DockerWsl" && DockerAndWslItems.Count == 0)
            _ = LoadDockerAndWslItemsAsync();
        else if (activeSection == "DefenderAuditor" && DefenderAuditItems.Count == 0)
            _ = LoadDefenderAuditAsync();
        else if (activeSection == "MonitorGpu" && MonitorAndDisplayItems.Count == 0)
            _ = LoadMonitorsAndGpuAsync();
        else if (activeSection == "GitRepoCompressor" && GitRepoItems.Count == 0)
            _ = LoadGitReposAsync();
        else if (activeSection == "DiskTreemap" && TreemapNodes.Count == 0)
            _ = LoadTreemapAsync();
        else if (activeSection == "DeepSpaceRecovery" && DeepSpaceItems.Count == 0)
            _ = LoadDeepSpaceAsync();
    }

    private void UpdateSectionMonitoring(string activeSection)
    {
        if (activeSection == "LiveMonitor")
        {
            _ = TickLiveTelemetryAsync();
            if (!_liveTelemetryTimer.IsEnabled)
                _liveTelemetryTimer.Start();
        }
        else if (_liveTelemetryTimer.IsEnabled)
        {
            _liveTelemetryTimer.Stop();
        }

        if (activeSection == "Dashboard")
        {
            RefreshCleanupHistorySummary();
            UpdateHealthScoreSummary(TotalDetectedBytes, StartupItems.Count);
        }
    }

    public bool IsDashboardSection => ActiveSection == "Dashboard";
    public bool IsLiveMonitorSection => ActiveSection == "LiveMonitor";
    public bool IsDiskTreemapSection => ActiveSection == "DiskTreemap";
    public bool IsDeepSpaceRecoverySection => ActiveSection == "DeepSpaceRecovery";
    public bool IsSimilarPhotosSection => ActiveSection == "SimilarPhotos";
    public bool IsExifScrubberSection => ActiveSection == "ExifScrubber";
    public bool IsAllSection => ActiveSection == "All";
    public bool IsSystemSection => ActiveSection == "System";
    public bool IsBrowsersSection => ActiveSection == "Browsers";
    public bool IsAppsSection => ActiveSection == "Apps";
    public bool IsCleanerViewVisible => ActiveSection is "All" or "System" or "Browsers" or "Apps";

    public bool IsStartupSection => ActiveSection == "Startup";
    public bool IsRegistrySection => ActiveSection == "Registry";
    public bool IsDiskAnalyzerSection => ActiveSection == "DiskAnalyzer";
    public bool IsUninstallerSection => ActiveSection == "Uninstaller";
    public bool IsBoosterSection => ActiveSection == "Booster";
    public bool IsDevCleanerSection => ActiveSection == "DevCleaner";
    public bool IsShredderSection => ActiveSection == "Shredder";
    public bool IsPrivacySection => ActiveSection == "Privacy";
    public bool IsNetworkSection => ActiveSection == "Network";
    public bool IsTasksAndTreeSection => ActiveSection == "TasksAndTree";
    public bool IsDownloadsSection => ActiveSection == "Downloads";
    public bool IsContextMenuSection => ActiveSection == "ContextMenu";
    public bool IsServicesSection => ActiveSection == "Services";
    public bool IsSmartPowerSection => ActiveSection == "SmartPower";
    public bool IsRepairCenterSection => ActiveSection == "RepairCenter";
    public bool IsUnlockerSection => ActiveSection == "Unlocker";
    public bool IsBrowserExtensionsSection => ActiveSection == "BrowserExtensions";
    public bool IsDriversUpdatesSection => ActiveSection == "DriversUpdates";
    public bool IsPathDoctorSection => ActiveSection == "PathDoctor";
    public bool IsCrashLogsSection => ActiveSection == "CrashLogs";
    public bool IsWifiDnsSection => ActiveSection == "WifiDns";
    public bool IsPackageCachesSection => ActiveSection == "PackageCaches";
    public bool IsRestoreGodModeSection => ActiveSection == "RestoreGodMode";
    public bool IsFileAssocSection => ActiveSection == "FileAssoc";
    public bool IsPortHunterSection => ActiveSection == "PortHunter";
    public bool IsBloatwareSection => ActiveSection == "Bloatware";
    public bool IsDiskBenchSection => ActiveSection == "DiskBench";
    public bool IsHardwareSpecsSection => ActiveSection == "HardwareSpecs";
    public bool IsUsbForensicsSection => ActiveSection == "UsbForensics";
    public bool IsProcessSecuritySection => ActiveSection == "ProcessSecurity";
    public bool IsBatchRenamerSection => ActiveSection == "BatchRenamer";
    public bool IsNetworkAdapterDoctorSection => ActiveSection == "NetworkAdapterDoctor";
    public bool IsFontManagerSection => ActiveSection == "FontManager";
    public bool IsClipboardShieldSection => ActiveSection == "ClipboardShield";
    public bool IsAiModelCleanerSection => ActiveSection == "AiModelCleaner";
    public bool IsFirewallBlockerSection => ActiveSection == "FirewallBlocker";
    public bool IsCryptoVaultSection => ActiveSection == "CryptoVault";
    public bool IsWin11TweakerSection => ActiveSection == "Win11Tweaker";
    public bool IsBitLockerSection => ActiveSection == "BitLocker";
    public bool IsWingetUpdaterSection => ActiveSection == "WingetUpdater";
    public bool IsLanScannerSection => ActiveSection == "LanScanner";
    public bool IsSqliteOptimizerSection => ActiveSection == "SqliteOptimizer";
    public bool IsCredentialSshSection => ActiveSection == "CredentialSsh";
    public bool IsProcessPrioritySection => ActiveSection == "ProcessPriority";
    public bool IsBootPerformanceSection => ActiveSection == "BootPerformance";
    public bool IsDockerWslSection => ActiveSection == "DockerWsl";
    public bool IsDefenderAuditorSection => ActiveSection == "DefenderAuditor";
    public bool IsMonitorGpuSection => ActiveSection == "MonitorGpu";
    public bool IsGitRepoCompressorSection => ActiveSection == "GitRepoCompressor";
    public bool IsSettingsSection => ActiveSection == "Settings";

    public string SectionTitle => LocalizationService.GetSectionTitle(ActiveSection, _selectedLanguage);
}
