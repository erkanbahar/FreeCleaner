using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeCleaner.Models;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public sealed record SimilarSensitivityOption(string Label, int MaxDistance);

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly CleanerEngine _engine = new();
    private readonly DispatcherTimer _autoMonitorTimer;
    private readonly DispatcherTimer _liveTelemetryTimer;
    private readonly DispatcherTimer _trayWatcherTimer;
    private readonly LiveSystemTelemetrySampler _telemetrySampler = new();
    private readonly List<double> _cpuHistory = [];
    private readonly List<double> _ramHistory = [];
    private readonly List<double> _netDownHistory = [];
    private readonly List<double> _netUpHistory = [];
    private readonly List<string> _favoriteSectionKeys = [];
    private static string ActivityLogPath => Path.Combine(
        AppStoragePathsService.GetDataDirectory(),
        "activity.log");
    private CancellationTokenSource? _cts;
    private DateTime _lastDiskAlertUtc = DateTime.MinValue;
    private DateTime _lastRamAlertUtc = DateTime.MinValue;
    private string _explorerContextMenuStatusText = string.Empty;
    private bool _suppressRuleWarningPrompt;
    private bool _isPortableMode;
    private string _storageForecastHeadline = "⏳ Veri toplanıyor";
    private string _storageForecastDetail = "Trend için en az 2 günlük veri gereklidir.";
    private string _organizerTargetFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");
    private string _organizerSummaryText = "Önizleme için 'Düzenlemeyi Önizle' butonuna tıklayın.";
    private double? _windowLeft;
    private double? _windowTop;
    private double? _windowWidth;
    private double? _windowHeight;
    private bool _windowMaximized;
    private bool _hasCompletedOnboarding = true;
    private string _newCustomProfileName = string.Empty;
    private CustomCleaningProfile? _selectedCustomProfile;
    private bool _autoCheckUpdatesOnStartup = true;
    private string _lastUpdateCheckDate = string.Empty;
    private readonly DispatcherTimer _toastDismissTimer;
    private ToastNotificationItem? _activeToast;
    private bool _hasPendingUpdate;

    private string _activeSection = "Dashboard";
    private string _selectedTheme = "ObsidianNeon";
    private string _selectedLanguage = "tr-TR";
    private string _updateStatusText = $"Mevcut sürüm: v{AppUpdateService.CurrentVersion} (Güncelleme kontrolü hazır)";
    private string _latestReleaseUrl = AppUpdateService.ReleasesPageUrl;
    private string? _latestDownloadAssetUrl;
    private string? _latestDownloadAssetName;
    private string? _latestExpectedSha256;
    private bool _isDownloadingUpdate;
    private int _updateDownloadPercent;
    private bool _hasTreemapDiffResults;
    private string _treemapDiffSummaryText = string.Empty;
    private string _activityLogFilter = string.Empty;
    private bool _startMinimizedWithWindows;
    private string _treemapCurrentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _treemapSummaryText = "Bir klasör seçip 'Haritayı Oluştur' butonuna tıklayın.";
    private bool _isTreemapScanning;
    private int _treemapScanPercent;
    private bool _isTreemapIndeterminate;
    private string _treemapScanStatusDetail = string.Empty;
    private string _treemapActiveScanPath = string.Empty;
    private List<RawTreemapEntry> _lastTreemapRawEntries = [];
    private double _treemapCanvasWidth = 920;
    private double _treemapCanvasHeight = 340;
    private string _exifScanFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    private bool _exifIncludeSubfolders = true;
    private bool _exifCreateBackup = true;
    private bool _exifOptimizeJpeg;
    private string _exifSummaryText = "Fotoğraf klasörü taraması bekleniyor.";
    private string _lifetimeFreedFormatted = "0 B";
    private string _lifetimeSessionsSummary = "Henüz kayıtlı temizlik oturumu yok";
    private string _deepSpaceSummaryText = "Sistem analizi bekleniyor.";
    private string _similarPhotosFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    private bool _similarPhotosIncludeSubfolders = true;
    private int _similarPhotosMaxDistance = 8;
    private string _similarPhotosSummaryText = "Fotoğraf klasörü seçip taramayı başlatın.";
    private SimilarPhotoItem? _selectedSimilarPhoto;
    private int _healthScore = 100;
    private string _healthGradeBadge = "🌟 Hazır";
    private string _healthAdviceText = "Tek tıkla tüm sistemin sağlık kontrolünü, gereksiz dosya taramasını ve bellek optimizasyonunu başlatın.";
    private double _liveCpuPercent;
    private double _liveRamPercent;
    private string _liveNetDownText = "0 KB/s";
    private string _liveNetUpText = "0 KB/s";
    private PointCollection _cpuSparklinePoints = new();
    private PointCollection _ramSparklinePoints = new();
    private PointCollection _netDownSparklinePoints = new();
    private PointCollection _netUpSparklinePoints = new();
    private string _searchFilter = string.Empty;
    private string _riskFilter = "Tümü";
    private string _statusMessage = "Sistem analizi için 'Analiz Et (Simülasyon)' butonuna tıklayın.";
    private string _currentOperationDetail = "Güvenli Mod Aktif: Analiz işlemi hiçbir dosyayı silmez.";
    private int _progressPercent;
    private bool _isBusy;
    private bool _hasAnalyzed;
    private bool _useRecycleBinSafety = true;
    private long _totalDetectedBytes;
    private int _totalDetectedFiles;
    private long _lastFreedBytes;
    private int _lastDeletedFiles;
    private SystemMetricsSnapshot _systemMetrics;

    // Disk Analyzer properties
    private string _diskScanTargetFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private int _selectedMinSizeMb = 50;

    // Uninstaller filter
    private string _appSearchFilter = string.Empty;

    // Settings / Exclusions
    private string _newExclusionInput = string.Empty;
    private bool _autoMonitorEnabled;
    private int _autoMonitorThresholdMb = 1024;
    private bool _scheduledMaintenanceEnabled;
    private int _scheduledMaintenanceIntervalHours = 168;
    private DateTime _lastScheduledMaintenanceUtc;
    private bool _minimizeToTrayOnClose;
    private bool _proactiveStorageAlertsEnabled = true;
    private int _storageAlertThresholdDays = 15;
    private string _lastStorageForecastAlertDate = string.Empty;
    private StorageForecastSummary? _lastStorageForecastSummary;
    private string _lastForecastSampleDateKey = string.Empty;

    // 2nd Generation Advanced Modules state
    private string _devWorkspacePath = @"C:\CURSOR";
    private bool _vsIncludeDebug = true;
    private bool _vsIncludeRetailAndRelease = true;
    private bool _vsIncludeObjAndVs = true;
    private int _shredPassesCount = 3;
    private int _freeSpaceWipeMb = 100;
    private string _newHostIp = "0.0.0.0";
    private string _newHostDomain = string.Empty;
    private string _newHostComment = "Telemetri / Reklam Engelleme";
    private string _spaceTreeTargetFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // 3rd Generation Ultimate Suite state
    private int _installerMinAgeDays = 7;
    private string _emptyFolderScanTarget = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _repairCenterStatus = "Sistem onarım ve derin bakım araçları hazır.";

    // 4th Generation Enterprise & Power-User state
    private string _unlockerTargetPath = string.Empty;

    // 5th Generation Forensics & Rescue state
    private string _restorePointDescription = $"FreeCleaner Güvenlik Noktası ({DateTime.Now:dd.MM.yyyy})";

    // 6th Generation Hardware, Port Hunter, Bloatware & Benchmark state
    private string _portFilterText = string.Empty;
    private string _benchmarkDriveRoot = @"C:\";
    private BenchmarkDriveOption? _selectedBenchmarkDrive;
    private int _benchmarkBlockSizeMb = 128;

    // Collections
    public ObservableCollection<CleanerRule> AllRules { get; } = [];
    public ObservableCollection<ScannedFileItem> DiscoveredFiles { get; } = [];
    public ObservableCollection<StartupItemModel> StartupItems { get; } = [];
    public ObservableCollection<RegistryIssueItem> RegistryIssues { get; } = [];
    public ObservableCollection<BackupFileItem> BackupFiles { get; } = [];
    public ObservableCollection<LargeOrDuplicateFileItem> DiskAnalyzerItems { get; } = [];
    public ObservableCollection<InstalledAppModel> InstalledApps { get; } = [];
    public ObservableCollection<OrphanAppFolderItem> OrphanFolders { get; } = [];
    public ObservableCollection<ProcessMemoryItem> TopMemoryProcesses { get; } = [];
    public ObservableCollection<DevArtifactFolderItem> DevArtifactItems { get; } = [];
    public ObservableCollection<ShredTargetItem> ShredQueueItems { get; } = [];
    public ObservableCollection<PrivacyTraceItem> PrivacyTraces { get; } = [];
    public ObservableCollection<ActiveConnectionItem> ActiveConnections { get; } = [];
    public ObservableCollection<HostsEntryItem> HostsEntries { get; } = [];
    public ObservableCollection<ScheduledTaskItem> ScheduledTasks { get; } = [];
    public ObservableCollection<FolderSpaceBarItem> FolderSpaceItems { get; } = [];
    public ObservableCollection<DownloadOrEmptyItem> DownloadsAndEmptyItems { get; } = [];
    public ObservableCollection<ContextMenuEntryItem> ContextMenuEntries { get; } = [];
    public ObservableCollection<WindowsServiceOptItem> OptimizableServices { get; } = [];
    public ObservableCollection<PhysicalDiskHealthItem> PhysicalDisks { get; } = [];
    public ObservableCollection<PowerPlanItem> PowerPlans { get; } = [];
    public ObservableCollection<LockingProcessItem> LockingProcesses { get; } = [];
    public ObservableCollection<BrowserExtensionItem> BrowserExtensions { get; } = [];
    public ObservableCollection<DriverOrHotfixItem> DriversAndHotfixes { get; } = [];
    public ObservableCollection<PathOrShortcutIssueItem> PathAndShortcutIssues { get; } = [];
    public ObservableCollection<CrashOrEventLogItem> CrashAndEventLogs { get; } = [];
    public ObservableCollection<WifiProfileAndDnsItem> WifiAndDnsItems { get; } = [];
    public ObservableCollection<GlobalPackageCacheItem> GlobalPackageCaches { get; } = [];
    public ObservableCollection<RestorePointItem> RestorePoints { get; } = [];
    public ObservableCollection<FileAssociationItem> FileAssociations { get; } = [];
    public ObservableCollection<ListeningPortItem> ListeningPorts { get; } = [];
    public ObservableCollection<AppxBloatwareItem> AppxBloatwareItems { get; } = [];
    public ObservableCollection<BenchmarkDriveOption> AvailableBenchmarkDrives { get; } = [];
    public ObservableCollection<DiskBenchmarkResultItem> DiskBenchmarkResults { get; } = [];
    public ObservableCollection<HardwareSpecItem> HardwareSpecs { get; } = [];
    public ObservableCollection<UsbHistoryItem> UsbHistoryItems { get; } = [];
    public ObservableCollection<ProcessSecurityAuditItem> ProcessSecurityItems { get; } = [];
    public ObservableCollection<BatchRenamePreviewItem> BatchRenameItems { get; } = [];
    public ObservableCollection<NetworkAdapterDetailItem> NetworkAdapters { get; } = [];
    public ObservableCollection<SystemFontItem> InstalledFonts { get; } = [];
    public ObservableCollection<ClipboardAuditEntryItem> ClipboardAuditItems { get; } = [];
    public ObservableCollection<AiModelCacheItem> AiModelCaches { get; } = [];
    public ObservableCollection<FirewallBlockRuleItem> FirewallBlockRules { get; } = [];
    public ObservableCollection<FileHashAndVaultResultItem> CryptoVaultResults { get; } = [];
    public ObservableCollection<WindowsTweakItem> WindowsTweaks { get; } = [];
    public ObservableCollection<VolumeSecurityAndMountItem> VolumeSecurityItems { get; } = [];
    public ObservableCollection<WingetUpgradeItem> WingetUpgrades { get; } = [];
    public ObservableCollection<LanDeviceItem> LanDevices { get; } = [];
    public ObservableCollection<SqliteDatabaseItem> SqliteDatabases { get; } = [];
    public ObservableCollection<CredentialOrSshItem> CredentialsAndSshItems { get; } = [];
    public ObservableCollection<ProcessPriorityItem> ProcessPriorityItems { get; } = [];
    public ObservableCollection<BootPerformanceMetricItem> BootPerformanceItems { get; } = [];
    public ObservableCollection<DockerOrWslItem> DockerAndWslItems { get; } = [];
    public ObservableCollection<DefenderSecurityAuditItem> DefenderAuditItems { get; } = [];
    public ObservableCollection<MonitorAndDisplayItem> MonitorAndDisplayItems { get; } = [];
    public ObservableCollection<GitRepoCompressionItem> GitRepoItems { get; } = [];
    public ObservableCollection<HealthCheckStepItem> HealthCheckResults { get; } = [];
    public ObservableCollection<NavShortcutItem> FavoriteNavItems { get; } = [];
    public ObservableCollection<LiveProcessMetricItem> LiveTopProcesses { get; } = [];
    public ObservableCollection<NavShortcutItem> AllModuleCatalog { get; } = [];
    public ObservableCollection<TreemapNodeItem> TreemapNodes { get; } = [];
    public ObservableCollection<DiskGrowthDiffItem> TreemapDiffItems { get; } = [];
    public ObservableCollection<OrganizePlanItem> OrganizerPlanItems { get; } = [];
    public ObservableCollection<PhotoMetadataItem> PhotoMetadataItems { get; } = [];
    public ObservableCollection<DeepSpaceItem> DeepSpaceItems { get; } = [];
    public ObservableCollection<SimilarPhotoItem> SimilarPhotoItems { get; } = [];
    public ObservableCollection<CleanupHistoryBarItem> RecentCleanupBars { get; } = [];
    public ObservableCollection<CustomCleaningProfile> CustomProfiles { get; } = [];
    public ObservableCollection<string> ExcludedPaths { get; } = [];
    public ObservableCollection<string> ActivityLogs { get; } = [];

    public ICollectionView FilteredRulesView { get; }
    public ICollectionView FilteredFilesView { get; }
    public ICollectionView FilteredInstalledAppsView { get; }
    public ICollectionView FilteredActivityLogsView { get; }

    public int[] AvailableSizeThresholdsMb { get; } = [10, 25, 50, 100, 250, 500, 1024];
    public int[] AvailableShredPasses { get; } = [1, 3, 7];
    public int[] AvailableInstallerAgeDays { get; } = [0, 7, 14, 30, 60, 90];
    public int[] AvailableMaintenanceIntervalsHours { get; } = [24, 48, 168];
    public int[] AvailableStorageAlertDays { get; } = [7, 15, 30];
    public int[] AvailableBenchmarkBlockSizesMb { get; } = [64, 128, 256, 512, 1024];
    public string[] AvailableRenameCaseModes { get; } = ["Değiştirme (Olduğu Gibi)", "küçük harf (lowercase)", "BÜYÜK HARF (UPPERCASE)", "Baş Harfler Büyük (Title Case)"];
    public string[] AvailableRiskFilters { get; } = ["Tümü", "Düşük", "Orta", "Yüksek"];
    public string[] AvailableThemes => ThemeManagerService.AvailableThemes;
    public IReadOnlyList<LanguageOption> AvailableLanguages => LocalizationService.AvailableLanguages;
    public SimilarSensitivityOption[] AvailableSimilarSensitivities { get; } =
    [
        new("Sıkı (%94+ — Neredeyse Birebir)", 4),
        new("Dengeli (%88+ — Önerilen)", 8),
        new("Geniş (%80+ — Seri Çekim)", 13)
    ];
    public string AppVersionDisplay => $"v{AppUpdateService.CurrentVersion} • .NET 10";

    public event Action<string, string, bool>? TrayNotificationRequested;
    public event Action<double, double, double, double>? TrayTooltipUpdateRequested;

    // Cleaner Commands
    public ICommand AnalyzeCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectSectionCommand { get; }
    public ICommand SelectAllRulesCommand { get; }
    public ICommand DeselectAllRulesCommand { get; }
    public ICommand OpenFileLocationCommand { get; }
    public ICommand OpenRecycleBinCommand { get; }
    public ICommand RefreshMetricsCommand { get; }

    // Startup Manager Commands
    public ICommand RefreshStartupCommand { get; }
    public ICommand ToggleStartupItemCommand { get; }
    public ICommand DeleteStartupItemCommand { get; }

    // Registry Cleaner Commands
    public ICommand ScanRegistryCommand { get; }
    public ICommand BackupRegistryCommand { get; }
    public ICommand RestoreRegistryBackupCommand { get; }
    public ICommand RefreshBackupsCommand { get; }
    public ICommand RestoreSelectedBackupCommand { get; }
    public ICommand FixRegistryCommand { get; }

    // Disk Analyzer Commands
    public ICommand BrowseDiskFolderCommand { get; }
    public ICommand ScanLargeFilesCommand { get; }
    public ICommand ScanDuplicatesCommand { get; }
    public ICommand DeleteSelectedDiskFilesCommand { get; }
    public ICommand OpenDiskFileLocationCommand { get; }

    // Uninstaller & Orphan Commands
    public ICommand RefreshInstalledAppsCommand { get; }
    public ICommand UninstallAppCommand { get; }
    public ICommand ScanOrphanFoldersCommand { get; }
    public ICommand CleanOrphanFoldersCommand { get; }

    // System Booster & Reports Commands
    public ICommand OptimizeRamCommand { get; }
    public ICommand FlushDnsCommand { get; }
    public ICommand ResetNetCacheCommand { get; }
    public ICommand LaunchCleanMgrCommand { get; }
    public ICommand LaunchSystemRestoreCommand { get; }
    public ICommand RefreshTopProcessesCommand { get; }
    public ICommand ExportReportCommand { get; }

    // Settings / Exclusions Commands
    public ICommand AddExclusionCommand { get; }
    public ICommand BrowseExclusionFolderCommand { get; }
    public ICommand RemoveExclusionCommand { get; }

    // DevCleaner & Gamer Commands
    public ICommand BrowseDevWorkspaceCommand { get; }
    public ICommand ScanVsDebugRetailCommand { get; }
    public ICommand AutoCleanVsDebugRetailCommand { get; }
    public ICommand ScanDevProjectsCommand { get; }
    public ICommand ScanGamerCachesCommand { get; }
    public ICommand CleanDevArtifactsCommand { get; }

    // File Shredder & Free Space Wiper Commands
    public ICommand AddShredFilesCommand { get; }
    public ICommand AddShredFolderCommand { get; }
    public ICommand ClearShredQueueCommand { get; }
    public ICommand ExecuteShredderCommand { get; }
    public ICommand WipeFreeSpaceCommand { get; }

    // Privacy Shield Commands
    public ICommand ScanPrivacyTracesCommand { get; }
    public ICommand CleanPrivacyTracesCommand { get; }

    // Network Connections & Hosts Commands
    public ICommand RefreshConnectionsCommand { get; }
    public ICommand RefreshHostsCommand { get; }
    public ICommand AddHostEntryCommand { get; }
    public ICommand RestoreHostsBackupCommand { get; }
    public ICommand SaveHostEntryCommand { get; }
    public ICommand DeleteHostEntryCommand { get; }
    public ICommand InstallScheduledMaintenanceCommand { get; }
    public ICommand RemoveScheduledMaintenanceCommand { get; }

    // Task Scheduler & Folder Space Tree Commands
    public ICommand RefreshScheduledTasksCommand { get; }
    public ICommand BrowseSpaceTreeFolderCommand { get; }
    public ICommand AnalyzeFolderSpaceTreeCommand { get; }

    // 3rd Gen Ultimate Suite Commands
    public ICommand ScanOldInstallersCommand { get; }
    public ICommand BrowseEmptyFolderTargetCommand { get; }
    public ICommand ScanEmptyFoldersCommand { get; }
    public ICommand CleanDownloadsAndEmptyCommand { get; }
    public ICommand RefreshContextMenuCommand { get; }
    public ICommand DeleteContextMenuCommand { get; }
    public ICommand RefreshServicesCommand { get; }
    public ICommand StartServiceCommand { get; }
    public ICommand StopServiceCommand { get; }
    public ICommand RefreshDiskAndPowerCommand { get; }
    public ICommand ActivatePowerPlanCommand { get; }
    public ICommand UnlockUltimatePowerCommand { get; }
    public ICommand RunDismCheckCommand { get; }
    public ICommand LaunchSfcScannowCommand { get; }
    public ICommand CleanFontAndWindowsOldCommand { get; }
    public ICommand RestartAsAdminCommand { get; }

    // 4th Gen Enterprise & Power-User Commands
    public ICommand BrowseUnlockerFileCommand { get; }
    public ICommand BrowseUnlockerFolderCommand { get; }
    public ICommand ScanLockingProcessesCommand { get; }
    public ICommand KillLockingProcessCommand { get; }
    public ICommand ScanBrowserExtensionsCommand { get; }
    public ICommand OpenExtensionFolderCommand { get; }
    public ICommand RemoveSelectedExtensionsCommand { get; }
    public ICommand ScanDriversAndHotfixesCommand { get; }
    public ICommand ScanPathAndShortcutsCommand { get; }
    public ICommand FixPathAndShortcutsCommand { get; }
    public ICommand ActivateGameBoosterCommand { get; }
    public ICommand GenerateBatteryReportCommand { get; }

    // 5th Gen Forensics, Wi-Fi, Package Caches, Restore Point & FileAssoc Commands
    public ICommand ExportReportsCommand => ExportReportCommand;
    public ICommand ScanCrashLogsCommand { get; }
    public ICommand CleanCrashDumpsCommand { get; }
    public ICommand ScanWifiAndDnsCommand { get; }
    public ICommand ScanPackageCachesCommand { get; }
    public ICommand CleanPackageCachesCommand { get; }
    public ICommand CompactVhdxCommand { get; }
    public ICommand OpenPackageCacheFolderCommand { get; }
    public ICommand RefreshRestorePointsCommand { get; }
    public ICommand CreateRestorePointCommand { get; }
    public ICommand OpenGodModeCommand { get; }
    public ICommand LaunchSystemToolCommand { get; }
    public ICommand ScanFileAssocCommand { get; }
    public ICommand ResetFileAssocCommand { get; }

    // 6th Gen Port Hunter, Bloatware, Disk Benchmark, Hardware & USB History Commands
    public ICommand ScanListeningPortsCommand { get; }
    public ICommand KillPortProcessCommand { get; }
    public ICommand ScanAppxBloatwareCommand { get; }
    public ICommand RemoveSelectedAppxCommand { get; }
    public ICommand RefreshBenchmarkDrivesCommand { get; }
    public ICommand RunDiskBenchmarkCommand { get; }
    public ICommand RefreshHardwareInventoryCommand { get; }
    public ICommand ScanUsbHistoryCommand { get; }
    public ICommand CleanUsbHistoryCommand { get; }

    // 7th Gen Process Security, Batch Renamer, Network Adapter Doctor, Font Manager & Clipboard Shield Commands
    public ICommand ScanProcessSecurityCommand { get; }
    public ICommand OpenProcessSecurityFileCommand { get; }
    public ICommand KillProcessSecurityItemCommand { get; }
    public ICommand BrowseRenameFolderCommand { get; }
    public ICommand PreviewBatchRenameCommand { get; }
    public ICommand ApplyBatchRenameCommand { get; }
    public ICommand UndoBatchRenameCommand { get; }
    public ICommand RefreshNetworkAdaptersCommand { get; }
    public ICommand RunNetworkRepairActionCommand { get; }
    public ICommand ScanInstalledFontsCommand { get; }
    public ICommand RemoveSelectedUserFontsCommand { get; }
    public ICommand OpenWindowsFontsFolderCommand { get; }
    public ICommand InspectClipboardNowCommand { get; }
    public ICommand ApplySanitizedClipboardCommand { get; }
    public ICommand ClearClipboardNowCommand { get; }

    // 8th Gen AI Model Cleaner, Firewall Blocker, AES-256 Vault, Win11 Tweaker & BitLocker/VHD Commands
    public ICommand BrowseAiScanFolderCommand { get; }
    public ICommand ScanAiModelCachesCommand { get; }
    public ICommand CleanSelectedAiCachesCommand { get; }
    public ICommand OpenAiCacheLocationCommand { get; }
    public ICommand RefreshFirewallRulesCommand { get; }
    public ICommand BrowseAndBlockExeFirewallCommand { get; }
    public ICommand RemoveSelectedFirewallRulesCommand { get; }
    public ICommand BrowseVaultTargetFileCommand { get; }
    public ICommand ComputeMultiHashesCommand { get; }
    public ICommand EncryptVaultFileCommand { get; }
    public ICommand DecryptVaultFileCommand { get; }
    public ICommand CopyHashValueCommand { get; }
    public ICommand RefreshWindowsTweaksCommand { get; }
    public ICommand ToggleWindowsTweakCommand { get; }
    public ICommand RestartExplorerNowCommand { get; }
    public ICommand RefreshVolumesAndBitLockerCommand { get; }
    public ICommand BrowseAndMountDiskImageCommand { get; }
    public ICommand DismountVolumeItemCommand { get; }

    // 9th Gen Winget Updater, LAN Device Radar, SQLite Optimizer, Credential/SSH & CPU Priority Commands
    public ICommand ScanWingetUpgradesCommand { get; }
    public ICommand UpgradeSelectedWingetCommand { get; }
    public ICommand UpgradeAllWingetCommand { get; }
    public ICommand ScanLanDevicesCommand { get; }
    public ICommand ScanSqliteDatabasesCommand { get; }
    public ICommand OptimizeSqliteDatabasesCommand { get; }
    public ICommand ScanCredentialsAndSshCommand { get; }
    public ICommand DeleteSelectedCredentialsCommand { get; }
    public ICommand CopyCredentialOrSshValueCommand { get; }
    public ICommand RefreshProcessPriorityListCommand { get; }
    public ICommand SetProcessHighPriorityCommand { get; }
    public ICommand SetProcessNormalPriorityCommand { get; }
    public ICommand SetProcessEcoPriorityCommand { get; }

    // 10th Gen Boot Time, Docker/WSL2, Defender Auditor, Monitor/Hz & Git GC Commands
    public ICommand ScanBootPerformanceCommand { get; }
    public ICommand ScanDockerAndWslCommand { get; }
    public ICommand ShutdownWslCommand { get; }
    public ICommand PruneDockerSystemCommand { get; }
    public ICommand ScanDefenderAuditCommand { get; }
    public ICommand UpdateDefenderSignaturesCommand { get; }
    public ICommand RunDefenderQuickScanCommand { get; }
    public ICommand ScanMonitorsAndGpuCommand { get; }
    public ICommand RestartGpuDriverCommand { get; }
    public ICommand BrowseGitWorkspaceCommand { get; }
    public ICommand ScanGitReposCommand { get; }
    public ICommand CompressSelectedGitReposCommand { get; }
    public ICommand OpenGitRepoFolderCommand { get; }

    // Flagship Dashboard, Health Check, Favorites, Live Telemetry & Theme Commands
    public ICommand RunOneClickHealthCheckCommand { get; }
    public ICommand ToggleCurrentFavoriteCommand { get; }
    public ICommand ApplyThemeCommand { get; }

    // 11th Gen Disk Treemap, Photo EXIF/GPS Scrubber & App Update Commands
    public ICommand BrowseTreemapFolderCommand { get; }
    public ICommand AnalyzeTreemapCommand { get; }
    public ICommand NavigateTreemapParentCommand { get; }
    public ICommand QuickSelectTreemapPathCommand { get; }
    public ICommand DrillDownTreemapNodeCommand { get; }
    public ICommand OpenTreemapNodeInExplorerCommand { get; }
    public ICommand OpenTreemapCurrentFolderCommand { get; }
    public ICommand BrowseExifFolderCommand { get; }
    public ICommand ScanPhotosExifCommand { get; }
    public ICommand ScrubSelectedPhotosCommand { get; }
    public ICommand OpenPhotoLocationCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand OpenLatestReleasePageCommand { get; }

    // 12th Gen Treemap Advisor Recycle, Deep Windows Space Recovery & Perceptual Similar Photo Finder Commands
    public ICommand MoveTreemapNodeToRecycleBinCommand { get; }
    public ICommand ScanDeepSpaceCommand { get; }
    public ICommand ExecuteDeepSpaceItemCommand { get; }
    public ICommand BrowseSimilarPhotosFolderCommand { get; }
    public ICommand ScanSimilarPhotosCommand { get; }
    public ICommand CleanSelectedSimilarPhotosCommand { get; }
    public ICommand OpenSimilarPhotoLocationCommand { get; }

    // 13th Gen Explorer Context Menu, Cleaning Profiles, Settings JSON Backup & NTFS Hardlink Commands
    public ICommand ApplyCleaningProfileCommand { get; }
    public ICommand InstallExplorerContextMenuCommand { get; }
    public ICommand RemoveExplorerContextMenuCommand { get; }
    public ICommand ExportSettingsConfigCommand { get; }
    public ICommand ImportSettingsConfigCommand { get; }
    public ICommand MergeDuplicatesWithHardlinksCommand { get; }

    // 14th Gen Snapshot Diff & In-App SHA-256 Updater Commands
    public ICommand SaveTreemapSnapshotCommand { get; }
    public ICommand CompareTreemapSnapshotCommand { get; }
    public ICommand ClearTreemapDiffCommand { get; }
    public ICommand OpenTreemapDiffPathCommand { get; }
    public ICommand DownloadAndInstallUpdateCommand { get; }

    // 15th Gen Activity Console Filter & Export Commands
    public ICommand CopyActivityLogsCommand { get; }
    public ICommand ExportActivityLogsCommand { get; }
    public ICommand ClearActivityLogsCommand { get; }

    // 16th Gen Portable Mode, Support Bundle & Smart Folder Organizer Commands
    public ICommand ExportSupportBundleCommand { get; }
    public ICommand SelectOrganizerPresetCommand { get; }
    public ICommand BrowseOrganizerFolderCommand { get; }
    public ICommand PreviewFolderOrganizeCommand { get; }
    public ICommand ApplyFolderOrganizeCommand { get; }
    public ICommand UndoFolderOrganizeCommand { get; }

    // 17th Gen Custom Cleaning Profiles & Onboarding Commands
    public ICommand SaveCustomProfileCommand { get; }
    public ICommand ApplyCustomProfileCommand { get; }
    public ICommand DeleteCustomProfileCommand { get; }
    public ICommand CompleteOnboardingCommand { get; }
    public ICommand OpenOnboardingCommand { get; }

    // 18th Gen Toast Notifications & Unified Backup Management Commands
    public ICommand DismissToastCommand { get; }
    public ICommand ExecuteToastActionCommand { get; }
    public ICommand DeleteSelectedBackupsCommand { get; }
    public ICommand PruneOldBackupsCommand { get; }

    public bool IsAdmin => UltimateSystemSuiteService.IsRunningAsAdministrator();
    public bool IsNotAdmin => !IsAdmin;
    public string AdminStatusBadge => LocalizationService.NormalizeLanguageCode(_selectedLanguage) == "en-US"
        ? (IsAdmin ? "Administrator Mode" : "Standard User Mode")
        : (IsAdmin ? "Yönetici Modu (Admin)" : "Standart Kullanıcı Modu");

    public MainViewModel()
    {
        _systemMetrics = SystemInfoService.GetMetrics();

        // Load saved settings & exclusions
        var savedConfig = ExclusionAndSettingsService.Load();
        foreach (var ex in savedConfig.ExcludedPaths)
            ExcludedPaths.Add(ex);
        _autoMonitorEnabled = savedConfig.AutoMonitorEnabled;
        _autoMonitorThresholdMb = savedConfig.AutoMonitorThresholdMb;
        _scheduledMaintenanceEnabled = savedConfig.ScheduledMaintenanceEnabled;
        _scheduledMaintenanceIntervalHours = savedConfig.ScheduledMaintenanceIntervalHours is 24 or 48 or 168
            ? savedConfig.ScheduledMaintenanceIntervalHours
            : 168;
        _minimizeToTrayOnClose = savedConfig.MinimizeToTrayOnClose;
        _useRecycleBinSafety = savedConfig.UseRecycleBinSafety;
        _riskFilter = savedConfig.RiskFilter is "Düşük" or "Orta" or "Yüksek"
            ? savedConfig.RiskFilter
            : "Tümü";
        _selectedTheme = ThemeManagerService.AvailableThemes.Contains(savedConfig.SelectedTheme, StringComparer.OrdinalIgnoreCase)
            ? savedConfig.SelectedTheme
            : "ObsidianNeon";
        _selectedLanguage = LocalizationService.NormalizeLanguageCode(savedConfig.SelectedLanguage);
        _proactiveStorageAlertsEnabled = savedConfig.ProactiveStorageAlertsEnabled;
        _storageAlertThresholdDays = savedConfig.StorageAlertThresholdDays is 7 or 15 or 30
            ? savedConfig.StorageAlertThresholdDays
            : 15;
        _lastStorageForecastAlertDate = savedConfig.LastStorageForecastAlertDate ?? string.Empty;
        _windowLeft = savedConfig.WindowLeft;
        _windowTop = savedConfig.WindowTop;
        _windowWidth = savedConfig.WindowWidth;
        _windowHeight = savedConfig.WindowHeight;
        _windowMaximized = savedConfig.WindowMaximized;
        _hasCompletedOnboarding = savedConfig.HasCompletedOnboarding;
        _autoCheckUpdatesOnStartup = savedConfig.AutoCheckUpdatesOnStartup;
        _lastUpdateCheckDate = savedConfig.LastUpdateCheckDate ?? string.Empty;
        foreach (CustomCleaningProfile cp in savedConfig.CustomProfiles ?? [])
        {
            if (!string.IsNullOrWhiteSpace(cp.Name))
                CustomProfiles.Add(cp);
        }
        SelectedCustomProfile = CustomProfiles.FirstOrDefault();
        _startMinimizedWithWindows = ShellIntegrationAndProfilesService.IsAutoStartWithWindowsEnabled();
        _lastScheduledMaintenanceUtc = DateTime.UtcNow;
        LoadActivityLogs();

        PopulateModuleCatalog();
        var initialFavs = savedConfig.FavoriteSections is { Count: > 0 }
            ? savedConfig.FavoriteSections
            : ["Dashboard", "All", "LiveMonitor", "DiskBench", "WingetUpdater"];
        _favoriteSectionKeys.AddRange(initialFavs.Distinct(StringComparer.OrdinalIgnoreCase));
        RebuildFavoriteNavItems();

        foreach (var rule in CleanerEngine.GetDefaultRules())
        {
            if (savedConfig.SelectedRuleIds.Count > 0)
                rule.IsSelected = savedConfig.SelectedRuleIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase);
            rule.PropertyChanged += OnRulePropertyChanged;
            AllRules.Add(rule);
        }
        LocalizationService.ApplyRuleLocalization(AllRules, _selectedLanguage);

        FilteredRulesView = CollectionViewSource.GetDefaultView(AllRules);
        FilteredRulesView.Filter = FilterRulePredicate;

        FilteredFilesView = CollectionViewSource.GetDefaultView(DiscoveredFiles);
        FilteredFilesView.Filter = FilterFilePredicate;

        FilteredInstalledAppsView = CollectionViewSource.GetDefaultView(InstalledApps);
        FilteredInstalledAppsView.Filter = FilterInstalledAppsPredicate;

        FilteredActivityLogsView = CollectionViewSource.GetDefaultView(ActivityLogs);
        FilteredActivityLogsView.Filter = FilterActivityLogPredicate;

        // Cleaner Commands
        AnalyzeCommand = new RelayCommand(async _ => await RunAnalyzeAsync(), _ => !IsBusy);
        CleanCommand = new RelayCommand(async _ => await RunCleanAsync(), _ => !IsBusy && HasAnalyzed && TotalSelectedFilesCount > 0);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
        SelectSectionCommand = new RelayCommand(param =>
        {
            if (param is string section)
                ActiveSection = section;
        });
        SelectAllRulesCommand = new RelayCommand(_ => SetVisibleRulesSelection(true), _ => !IsBusy);
        DeselectAllRulesCommand = new RelayCommand(_ => SetVisibleRulesSelection(false), _ => !IsBusy);
        OpenFileLocationCommand = new RelayCommand(OpenFileLocation);
        OpenRecycleBinCommand = new RelayCommand(_ => OpenRecycleBin());
        RefreshMetricsCommand = new RelayCommand(_ => RefreshSystemMetrics());

        // Startup Commands
        RefreshStartupCommand = new RelayCommand(_ => LoadStartupItems(), _ => !IsBusy);
        ToggleStartupItemCommand = new RelayCommand(param =>
        {
            if (param is StartupItemModel item)
                ToggleStartupItem(item);
        });
        DeleteStartupItemCommand = new RelayCommand(param =>
        {
            if (param is StartupItemModel item)
                DeleteStartupItem(item);
        });

        // Registry Commands
        ScanRegistryCommand = new RelayCommand(async _ => await RunRegistryScanAsync(), _ => !IsBusy);
        BackupRegistryCommand = new RelayCommand(_ => RunRegistryBackupOnly(), _ => !IsBusy && RegistryIssues.Any(x => x.IsSelected));
        RestoreRegistryBackupCommand = new RelayCommand(_ => RestoreLatestRegistryBackup(), _ => !IsBusy && RegistryCleanerService.GetLatestBackupFile() is not null);
        RefreshBackupsCommand = new RelayCommand(_ => LoadBackups(), _ => !IsBusy);
        RestoreSelectedBackupCommand = new RelayCommand(_ => RestoreSelectedBackup(), _ => !IsBusy && BackupFiles.Any(x => x.IsSelected));
        DeleteSelectedBackupsCommand = new RelayCommand(_ => DeleteSelectedBackups(), _ => !IsBusy && BackupFiles.Any(x => x.IsSelected));
        PruneOldBackupsCommand = new RelayCommand(_ => PruneOldBackups(), _ => !IsBusy);
        FixRegistryCommand = new RelayCommand(_ => RunRegistryFix(), _ => !IsBusy && RegistryIssues.Any(x => x.IsSelected));

        // Disk Analyzer Commands
        BrowseDiskFolderCommand = new RelayCommand(_ => BrowseDiskTargetFolder(), _ => !IsBusy);
        ScanLargeFilesCommand = new RelayCommand(async _ => await RunScanLargeFilesAsync(), _ => !IsBusy);
        ScanDuplicatesCommand = new RelayCommand(async _ => await RunScanDuplicatesAsync(), _ => !IsBusy);
        DeleteSelectedDiskFilesCommand = new RelayCommand(_ => DeleteSelectedDiskAnalyzerFiles(), _ => !IsBusy && DiskAnalyzerItems.Any(x => x.IsSelected));
        OpenDiskFileLocationCommand = new RelayCommand(param =>
        {
            if (param is LargeOrDuplicateFileItem item)
                OpenExplorerPath(item.FullPath);
        });

        // Uninstaller & Orphan Commands
        RefreshInstalledAppsCommand = new RelayCommand(async _ => await LoadInstalledAppsAsync(), _ => !IsBusy);
        UninstallAppCommand = new RelayCommand(param =>
        {
            if (param is InstalledAppModel app)
            {
                if (UninstallerAndOrphanService.LaunchUninstaller(app, out string msg))
                    AddLog($"[KALDIRICI] {msg}");
                else
                    MessageBox.Show(msg, "Kaldırıcı Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
        ScanOrphanFoldersCommand = new RelayCommand(async _ => await RunScanOrphanFoldersAsync(), _ => !IsBusy);
        CleanOrphanFoldersCommand = new RelayCommand(_ => CleanSelectedOrphanFolders(), _ => !IsBusy && OrphanFolders.Any(x => x.IsSelected));

        // Booster & Reports Commands
        OptimizeRamCommand = new RelayCommand(async _ => await RunOptimizeRamAsync(), _ => !IsBusy);
        FlushDnsCommand = new RelayCommand(async _ => await RunFlushDnsAsync(), _ => !IsBusy);
        ResetNetCacheCommand = new RelayCommand(async _ => await RunResetNetCacheAsync(), _ => !IsBusy);
        LaunchCleanMgrCommand = new RelayCommand(_ => SystemBoosterAndReportService.LaunchWindowsDiskCleanup());
        LaunchSystemRestoreCommand = new RelayCommand(_ => SystemBoosterAndReportService.LaunchSystemPropertiesRestore());
        RefreshTopProcessesCommand = new RelayCommand(_ => LoadTopMemoryProcesses());
        ExportReportCommand = new RelayCommand(_ => ExportHtmlAndJsonReports());

        // Settings / Exclusion Commands
        AddExclusionCommand = new RelayCommand(_ => AddNewExclusion());
        BrowseExclusionFolderCommand = new RelayCommand(_ => BrowseAndAddExclusionFolder());
        RemoveExclusionCommand = new RelayCommand(param =>
        {
            if (param is string path && ExcludedPaths.Remove(path))
            {
                SaveCurrentSettings();
                AddLog($"[BEYAZ LİSTE] İstisna kaldırıldı: {path}");
            }
        });

        // Initialize Privacy Traces list
        foreach (var p in PrivacyAndNetworkShieldService.GetPrivacyCategories())
            PrivacyTraces.Add(p);

        // DevCleaner & Gamer Commands
        BrowseDevWorkspaceCommand = new RelayCommand(_ => BrowseDevWorkspace(), _ => !IsBusy);
        ScanVsDebugRetailCommand = new RelayCommand(async _ => await RunScanVsDebugRetailAsync(autoDeleteAfterScan: false), _ => !IsBusy);
        AutoCleanVsDebugRetailCommand = new RelayCommand(async _ => await RunScanVsDebugRetailAsync(autoDeleteAfterScan: true), _ => !IsBusy);
        ScanDevProjectsCommand = new RelayCommand(async _ => await RunScanDevProjectsAsync(), _ => !IsBusy);
        ScanGamerCachesCommand = new RelayCommand(async _ => await RunScanGamerCachesAsync(), _ => !IsBusy);
        CleanDevArtifactsCommand = new RelayCommand(_ => CleanSelectedDevArtifacts(), _ => !IsBusy && DevArtifactItems.Any(x => x.IsSelected));

        // File Shredder & Free Space Wiper Commands
        AddShredFilesCommand = new RelayCommand(_ => AddFilesToShredQueue(), _ => !IsBusy);
        AddShredFolderCommand = new RelayCommand(_ => AddFolderToShredQueue(), _ => !IsBusy);
        ClearShredQueueCommand = new RelayCommand(_ => ShredQueueItems.Clear(), _ => !IsBusy && ShredQueueItems.Count > 0);
        ExecuteShredderCommand = new RelayCommand(async _ => await RunFileShredderAsync(), _ => !IsBusy && ShredQueueItems.Count > 0);
        WipeFreeSpaceCommand = new RelayCommand(async _ => await RunFreeSpaceWiperAsync(), _ => !IsBusy);

        // Privacy Shield Commands
        ScanPrivacyTracesCommand = new RelayCommand(async _ => await RunScanPrivacyTracesAsync(), _ => !IsBusy);
        CleanPrivacyTracesCommand = new RelayCommand(async _ => await RunCleanPrivacyTracesAsync(), _ => !IsBusy && PrivacyTraces.Any(x => x.IsSelected));

        // Network Connections & Hosts Commands
        RefreshConnectionsCommand = new RelayCommand(async _ => await LoadActiveConnectionsAsync(), _ => !IsBusy);
        RefreshHostsCommand = new RelayCommand(_ => LoadHostsEntries(), _ => !IsBusy);
        AddHostEntryCommand = new RelayCommand(_ => AddNewHostEntry(), _ => !IsBusy);
        RestoreHostsBackupCommand = new RelayCommand(_ => RestoreLatestHostsBackup(), _ => !IsBusy && PrivacyAndNetworkShieldService.GetLatestHostsBackup() is not null);
        SaveHostEntryCommand = new RelayCommand(param => SaveHostEntry(param as HostsEntryItem), _ => !IsBusy);
        DeleteHostEntryCommand = new RelayCommand(param => DeleteHostEntry(param as HostsEntryItem), _ => !IsBusy);
        InstallScheduledMaintenanceCommand = new RelayCommand(_ => InstallScheduledMaintenance(), _ => !IsBusy);
        RemoveScheduledMaintenanceCommand = new RelayCommand(_ => RemoveScheduledMaintenance(), _ => !IsBusy);

        LoadBackups();

        // Task Scheduler & Folder Space Tree Commands
        RefreshScheduledTasksCommand = new RelayCommand(async _ => await LoadScheduledTasksAsync(), _ => !IsBusy);
        BrowseSpaceTreeFolderCommand = new RelayCommand(_ => BrowseSpaceTreeFolder(), _ => !IsBusy);
        AnalyzeFolderSpaceTreeCommand = new RelayCommand(async _ => await RunAnalyzeFolderSpaceTreeAsync(), _ => !IsBusy);

        // 3rd Gen Ultimate Suite Commands
        ScanOldInstallersCommand = new RelayCommand(async _ => await RunScanOldInstallersAsync(), _ => !IsBusy);
        BrowseEmptyFolderTargetCommand = new RelayCommand(_ => BrowseEmptyFolderTarget(), _ => !IsBusy);
        ScanEmptyFoldersCommand = new RelayCommand(async _ => await RunScanEmptyFoldersAsync(), _ => !IsBusy);
        CleanDownloadsAndEmptyCommand = new RelayCommand(_ => CleanSelectedDownloadsAndEmpty(), _ => !IsBusy && DownloadsAndEmptyItems.Any(x => x.IsSelected));
        RefreshContextMenuCommand = new RelayCommand(_ => LoadContextMenuEntries(), _ => !IsBusy);
        DeleteContextMenuCommand = new RelayCommand(_ => DeleteSelectedContextMenuEntries(), _ => !IsBusy && ContextMenuEntries.Any(x => x.IsSelected));
        RefreshServicesCommand = new RelayCommand(async _ => await LoadOptimizableServicesAsync(), _ => !IsBusy);
        StartServiceCommand = new RelayCommand(async param =>
        {
            if (param is WindowsServiceOptItem svc)
                await ControlServiceAsync(svc, start: true);
        }, _ => !IsBusy);
        StopServiceCommand = new RelayCommand(async param =>
        {
            if (param is WindowsServiceOptItem svc)
                await ControlServiceAsync(svc, start: false);
        }, _ => !IsBusy);
        RefreshDiskAndPowerCommand = new RelayCommand(async _ => await LoadDiskAndPowerAsync(), _ => !IsBusy);
        ActivatePowerPlanCommand = new RelayCommand(async param =>
        {
            if (param is PowerPlanItem plan)
                await ActivatePowerPlanAsync(plan);
        }, _ => !IsBusy);
        UnlockUltimatePowerCommand = new RelayCommand(async _ => await UnlockUltimatePowerPlanAsync(), _ => !IsBusy);
        RunDismCheckCommand = new RelayCommand(async _ => await RunDismHealthCheckAsync(), _ => !IsBusy);
        LaunchSfcScannowCommand = new RelayCommand(_ =>
        {
            UltimateSystemSuiteService.LaunchElevatedSfcScannow();
            AddLog("[ONARIM MERKEZİ] Yönetici CMD üzerinde 'sfc /scannow' başlatıldı.");
        });
        CleanFontAndWindowsOldCommand = new RelayCommand(async _ => await CleanFontCacheAndWindowsOldAsync(), _ => !IsBusy);
        RestartAsAdminCommand = new RelayCommand(_ =>
        {
            if (UltimateSystemSuiteService.RestartAppAsAdministrator())
            {
                Application.Current?.Shutdown();
            }
        });

        // 4th Gen Enterprise & Power-User Commands
        BrowseUnlockerFileCommand = new RelayCommand(_ => BrowseUnlockerFile(), _ => !IsBusy);
        BrowseUnlockerFolderCommand = new RelayCommand(_ => BrowseUnlockerFolder(), _ => !IsBusy);
        ScanLockingProcessesCommand = new RelayCommand(async _ => await RunScanLockingProcessesAsync(), _ => !IsBusy);
        KillLockingProcessCommand = new RelayCommand(async param =>
        {
            if (param is LockingProcessItem proc)
                await KillLockingProcessAndRescanAsync(proc);
        }, _ => !IsBusy);
        ScanBrowserExtensionsCommand = new RelayCommand(async _ => await LoadBrowserExtensionsAsync(), _ => !IsBusy);
        OpenExtensionFolderCommand = new RelayCommand(param =>
        {
            if (param is BrowserExtensionItem ext)
                OpenExplorerPath(ext.FullPath);
        });
        RemoveSelectedExtensionsCommand = new RelayCommand(_ => RemoveSelectedBrowserExtensions(), _ => !IsBusy && BrowserExtensions.Any(x => x.IsSelected));
        ScanDriversAndHotfixesCommand = new RelayCommand(async _ => await LoadDriversAndHotfixesAsync(), _ => !IsBusy);
        ScanPathAndShortcutsCommand = new RelayCommand(async _ => await LoadPathAndShortcutIssuesAsync(), _ => !IsBusy);
        FixPathAndShortcutsCommand = new RelayCommand(_ => FixSelectedPathAndShortcuts(), _ => !IsBusy && PathAndShortcutIssues.Any(x => x.IsSelected));
        ActivateGameBoosterCommand = new RelayCommand(async _ => await RunGameBoosterAsync(), _ => !IsBusy);
        GenerateBatteryReportCommand = new RelayCommand(async _ => await RunGenerateBatteryReportAsync(), _ => !IsBusy);

        // 5th Gen Forensics, Wi-Fi, Package Caches, Restore Point & FileAssoc Commands
        ScanCrashLogsCommand = new RelayCommand(async _ => await LoadCrashAndEventLogsAsync(), _ => !IsBusy);
        CleanCrashDumpsCommand = new RelayCommand(async _ => await RunCleanCrashDumpsAsync(), _ => !IsBusy && CrashAndEventLogs.Any(x => !string.IsNullOrWhiteSpace(x.DumpFilePath)));
        ScanWifiAndDnsCommand = new RelayCommand(async _ => await LoadWifiAndDnsAsync(), _ => !IsBusy);
        ScanPackageCachesCommand = new RelayCommand(async _ => await LoadGlobalPackageCachesAsync(), _ => !IsBusy);
        CleanPackageCachesCommand = new RelayCommand(async _ => await RunCleanPackageCachesAsync(), _ => !IsBusy && GlobalPackageCaches.Any(x => x.IsSelected && x.CanSafeDelete));
        CompactVhdxCommand = new RelayCommand(async param =>
        {
            if (param is GlobalPackageCacheItem item)
                await RunCompactVhdxAsync(item);
        }, param => !IsBusy && param is GlobalPackageCacheItem { IsVhdxVirtualDisk: true });
        OpenPackageCacheFolderCommand = new RelayCommand(param =>
        {
            if (param is GlobalPackageCacheItem item)
                OpenExplorerPath(item.FolderPath);
        });
        RefreshRestorePointsCommand = new RelayCommand(async _ => await LoadRestorePointsAsync(), _ => !IsBusy);
        CreateRestorePointCommand = new RelayCommand(async _ => await RunCreateRestorePointAsync(), _ => !IsBusy);
        OpenGodModeCommand = new RelayCommand(_ => ExecuteOpenGodMode());
        LaunchSystemToolCommand = new RelayCommand(param =>
        {
            if (param is string tool && !string.IsNullOrWhiteSpace(tool))
                NextGenForensicsAndRescueService.LaunchSystemUtility(tool);
        });
        ScanFileAssocCommand = new RelayCommand(async _ => await LoadFileAssociationsAsync(), _ => !IsBusy);
        ResetFileAssocCommand = new RelayCommand(_ => ExecuteResetFileAssociations(), _ => !IsBusy && FileAssociations.Any(x => x.IsSelected));

        // 6th Gen Port Hunter, Bloatware, Disk Benchmark, Hardware & USB History Commands
        ScanListeningPortsCommand = new RelayCommand(async _ => await LoadListeningPortsAsync(), _ => !IsBusy);
        KillPortProcessCommand = new RelayCommand(async param =>
        {
            if (param is ListeningPortItem item)
                await KillPortAndRefreshAsync(item);
        }, _ => !IsBusy);
        ScanAppxBloatwareCommand = new RelayCommand(async _ => await LoadAppxBloatwareAsync(), _ => !IsBusy);
        RemoveSelectedAppxCommand = new RelayCommand(async _ => await RunRemoveSelectedAppxAsync(), _ => !IsBusy && AppxBloatwareItems.Any(x => x.IsSelected));
        RefreshBenchmarkDrivesCommand = new RelayCommand(async _ => await LoadBenchmarkDrivesAsync(), _ => !IsBusy);
        RunDiskBenchmarkCommand = new RelayCommand(async _ => await ExecuteDiskBenchmarkAsync(), _ => !IsBusy);
        RefreshHardwareInventoryCommand = new RelayCommand(async _ => await LoadHardwareSpecsAsync(), _ => !IsBusy);
        ScanUsbHistoryCommand = new RelayCommand(async _ => await LoadUsbHistoryAsync(), _ => !IsBusy);
        CleanUsbHistoryCommand = new RelayCommand(_ => ExecuteCleanUsbHistory(), _ => !IsBusy && UsbHistoryItems.Any(x => x.IsSelected));

        // 7th Gen Commands initialization
        ScanProcessSecurityCommand = new RelayCommand(async _ => await LoadProcessSecurityItemsAsync(), _ => !IsBusy);
        OpenProcessSecurityFileCommand = new RelayCommand(param =>
        {
            if (param is ProcessSecurityAuditItem item)
                OpenExplorerPath(item.ExecutablePath);
        });
        KillProcessSecurityItemCommand = new RelayCommand(async param =>
        {
            if (param is ProcessSecurityAuditItem item)
                await KillSecurityProcessAndRefreshAsync(item);
        }, _ => !IsBusy);
        BrowseRenameFolderCommand = new RelayCommand(_ => BrowseRenameFolder(), _ => !IsBusy);
        PreviewBatchRenameCommand = new RelayCommand(_ => GenerateBatchRenamePreview(), _ => !IsBusy);
        ApplyBatchRenameCommand = new RelayCommand(_ => ExecuteApplyBatchRename(), _ => !IsBusy && BatchRenameItems.Any(x => x.IsSelected));
        UndoBatchRenameCommand = new RelayCommand(_ => ExecuteUndoBatchRename(), _ => !IsBusy);
        RefreshNetworkAdaptersCommand = new RelayCommand(async _ => await LoadNetworkAdaptersAsync(), _ => !IsBusy);
        RunNetworkRepairActionCommand = new RelayCommand(async param =>
        {
            if (param is string actionKey)
                await ExecuteNetworkRepairActionAsync(actionKey);
        }, _ => !IsBusy);
        ScanInstalledFontsCommand = new RelayCommand(async _ => await LoadInstalledFontsAsync(), _ => !IsBusy);
        RemoveSelectedUserFontsCommand = new RelayCommand(_ => ExecuteRemoveSelectedUserFonts(), _ => !IsBusy && InstalledFonts.Any(x => x.IsSelected && x.IsUserFont));
        OpenWindowsFontsFolderCommand = new RelayCommand(_ => OpenExplorerPath(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)));
        InspectClipboardNowCommand = new RelayCommand(_ => ExecuteInspectClipboardNow());
        ApplySanitizedClipboardCommand = new RelayCommand(param =>
        {
            if (param is ClipboardAuditEntryItem entry && !string.IsNullOrEmpty(entry.SanitizedContent))
            {
                Clipboard.SetText(entry.SanitizedContent);
                StatusMessage = "Temizlenmiş/arındırılmış içerik panoya kopyalandı!";
                AddLog($"[PANO KALKANI] Temizlenmiş veri panoya aktarıldı: {entry.SanitizedContent}");
            }
        });
        ClearClipboardNowCommand = new RelayCommand(_ => ExecuteClearClipboardNow());

        // 8th Gen Commands initialization
        BrowseAiScanFolderCommand = new RelayCommand(_ => BrowseAiScanFolder(), _ => !IsBusy);
        ScanAiModelCachesCommand = new RelayCommand(async _ => await LoadAiModelCachesAsync(), _ => !IsBusy);
        CleanSelectedAiCachesCommand = new RelayCommand(async _ => await ExecuteCleanAiCachesAsync(), _ => !IsBusy && AiModelCaches.Any(x => x.IsSelected));
        OpenAiCacheLocationCommand = new RelayCommand(param =>
        {
            if (param is AiModelCacheItem item)
                OpenExplorerPath(item.FullPath);
        });
        RefreshFirewallRulesCommand = new RelayCommand(async _ => await LoadFirewallBlockRulesAsync(), _ => !IsBusy);
        BrowseAndBlockExeFirewallCommand = new RelayCommand(async _ => await BrowseAndBlockExecutableInFirewallAsync(), _ => !IsBusy);
        RemoveSelectedFirewallRulesCommand = new RelayCommand(async _ => await ExecuteRemoveSelectedFirewallRulesAsync(), _ => !IsBusy && FirewallBlockRules.Any(x => x.IsSelected));
        BrowseVaultTargetFileCommand = new RelayCommand(_ => BrowseVaultTargetFile(), _ => !IsBusy);
        ComputeMultiHashesCommand = new RelayCommand(async _ => await ExecuteComputeMultiHashesAsync(), _ => !IsBusy);
        EncryptVaultFileCommand = new RelayCommand(async _ => await ExecuteEncryptVaultFileAsync(), _ => !IsBusy);
        DecryptVaultFileCommand = new RelayCommand(async _ => await ExecuteDecryptVaultFileAsync(), _ => !IsBusy);
        CopyHashValueCommand = new RelayCommand(param =>
        {
            if (param is FileHashAndVaultResultItem row && !string.IsNullOrEmpty(row.HashOrOutputValue))
            {
                Clipboard.SetText(row.HashOrOutputValue);
                StatusMessage = $"{row.AlgorithmOrOperation} değeri panoya kopyalandı!";
            }
        });
        RefreshWindowsTweaksCommand = new RelayCommand(_ => LoadWindowsTweaks(), _ => !IsBusy);
        ToggleWindowsTweakCommand = new RelayCommand(param =>
        {
            if (param is WindowsTweakItem tweak)
                ExecuteToggleWindowsTweak(tweak);
        }, _ => !IsBusy);
        RestartExplorerNowCommand = new RelayCommand(_ => ExecuteRestartExplorerNow(), _ => !IsBusy);
        RefreshVolumesAndBitLockerCommand = new RelayCommand(async _ => await LoadVolumeSecurityAndMountsAsync(), _ => !IsBusy);
        BrowseAndMountDiskImageCommand = new RelayCommand(async _ => await BrowseAndMountIsoOrVhdAsync(), _ => !IsBusy);
        DismountVolumeItemCommand = new RelayCommand(async param =>
        {
            if (param is VolumeSecurityAndMountItem vol)
                await ExecuteDismountVolumeAsync(vol);
        }, _ => !IsBusy);

        // 9th Gen Commands initialization
        ScanWingetUpgradesCommand = new RelayCommand(async _ => await LoadWingetUpgradesAsync(), _ => !IsBusy);
        UpgradeSelectedWingetCommand = new RelayCommand(async _ => await ExecuteWingetUpgradeAsync(upgradeAll: false), _ => !IsBusy && WingetUpgrades.Any(x => x.IsSelected));
        UpgradeAllWingetCommand = new RelayCommand(async _ => await ExecuteWingetUpgradeAsync(upgradeAll: true), _ => !IsBusy);
        ScanLanDevicesCommand = new RelayCommand(async _ => await LoadLanDevicesAsync(), _ => !IsBusy);
        ScanSqliteDatabasesCommand = new RelayCommand(async _ => await LoadSqliteDatabasesAsync(), _ => !IsBusy);
        OptimizeSqliteDatabasesCommand = new RelayCommand(async _ => await ExecuteOptimizeSqliteDatabasesAsync(), _ => !IsBusy && SqliteDatabases.Any(x => x.IsSelected));
        ScanCredentialsAndSshCommand = new RelayCommand(async _ => await LoadCredentialsAndSshAsync(), _ => !IsBusy);
        DeleteSelectedCredentialsCommand = new RelayCommand(_ => ExecuteDeleteSelectedCredentials(), _ => !IsBusy && CredentialsAndSshItems.Any(x => x.IsSelected && x.CanDeleteFromVault));
        CopyCredentialOrSshValueCommand = new RelayCommand(param =>
        {
            if (param is CredentialOrSshItem item && !string.IsNullOrWhiteSpace(item.DetailsOrPublicKey))
            {
                Clipboard.SetText(item.DetailsOrPublicKey);
                StatusMessage = $"'{item.TargetOrKeyName}' detayı / açık anahtarı panoya kopyalandı!";
            }
        });
        RefreshProcessPriorityListCommand = new RelayCommand(async _ => await LoadProcessPriorityItemsAsync(), _ => !IsBusy);
        SetProcessHighPriorityCommand = new RelayCommand(async param =>
        {
            if (param is ProcessPriorityItem item)
                await ChangeProcessPriorityAndRefreshAsync(item, ProcessPriorityClass.High);
        }, _ => !IsBusy);
        SetProcessNormalPriorityCommand = new RelayCommand(async param =>
        {
            if (param is ProcessPriorityItem item)
                await ChangeProcessPriorityAndRefreshAsync(item, ProcessPriorityClass.Normal);
        }, _ => !IsBusy);
        SetProcessEcoPriorityCommand = new RelayCommand(async param =>
        {
            if (param is ProcessPriorityItem item)
                await ChangeProcessPriorityAndRefreshAsync(item, ProcessPriorityClass.Idle);
        }, _ => !IsBusy);

        // 10th Gen Commands initialization
        ScanBootPerformanceCommand = new RelayCommand(async _ => await LoadBootPerformanceAsync(), _ => !IsBusy);
        ScanDockerAndWslCommand = new RelayCommand(async _ => await LoadDockerAndWslItemsAsync(), _ => !IsBusy);
        ShutdownWslCommand = new RelayCommand(async _ => await ExecuteShutdownWslAsync(), _ => !IsBusy);
        PruneDockerSystemCommand = new RelayCommand(async _ => await ExecutePruneDockerSystemAsync(), _ => !IsBusy);
        ScanDefenderAuditCommand = new RelayCommand(async _ => await LoadDefenderAuditAsync(), _ => !IsBusy);
        UpdateDefenderSignaturesCommand = new RelayCommand(async _ => await ExecuteDefenderActionAsync(quickScan: false), _ => !IsBusy);
        RunDefenderQuickScanCommand = new RelayCommand(async _ => await ExecuteDefenderActionAsync(quickScan: true), _ => !IsBusy);
        ScanMonitorsAndGpuCommand = new RelayCommand(async _ => await LoadMonitorsAndGpuAsync(), _ => !IsBusy);
        RestartGpuDriverCommand = new RelayCommand(async _ => await ExecuteRestartGpuDriverAsync(), _ => !IsBusy);
        BrowseGitWorkspaceCommand = new RelayCommand(_ => BrowseGitWorkspace(), _ => !IsBusy);
        ScanGitReposCommand = new RelayCommand(async _ => await LoadGitReposAsync(), _ => !IsBusy);
        CompressSelectedGitReposCommand = new RelayCommand(async _ => await ExecuteCompressSelectedGitReposAsync(), _ => !IsBusy && GitRepoItems.Any(x => x.IsSelected));
        OpenGitRepoFolderCommand = new RelayCommand(param =>
        {
            if (param is GitRepoCompressionItem repo)
                OpenExplorerPath(repo.RepoRootPath);
        });

        // Flagship Dashboard, Favorites, Live Telemetry & Theme Commands
        RunOneClickHealthCheckCommand = new RelayCommand(async _ => await RunOneClickHealthCheckAsync(), _ => !IsBusy);
        ToggleCurrentFavoriteCommand = new RelayCommand(_ => ToggleCurrentSectionFavorite());
        ApplyThemeCommand = new RelayCommand(param =>
        {
            if (param is string themeKey)
                SelectedTheme = themeKey;
        });

        // 11th Gen Disk Treemap, Photo EXIF/GPS Scrubber & App Update Commands
        BrowseTreemapFolderCommand = new RelayCommand(_ => BrowseTreemapFolder(), _ => !IsBusy);
        AnalyzeTreemapCommand = new RelayCommand(async _ => await LoadTreemapAsync(forceRefresh: true), _ => !IsBusy);
        NavigateTreemapParentCommand = new RelayCommand(async _ => await NavigateTreemapParentAsync(), _ => !IsBusy);
        QuickSelectTreemapPathCommand = new RelayCommand(async param =>
        {
            if (param is string preset)
                await SelectTreemapPresetAsync(preset);
        }, _ => !IsBusy);
        DrillDownTreemapNodeCommand = new RelayCommand(async param =>
        {
            if (param is TreemapNodeItem node && node.IsDirectory && System.IO.Directory.Exists(node.FullPath))
            {
                TreemapCurrentPath = node.FullPath;
                await LoadTreemapAsync(forceRefresh: false);
            }
        }, param => !IsBusy && param is TreemapNodeItem { IsDirectory: true });
        OpenTreemapNodeInExplorerCommand = new RelayCommand(param =>
        {
            if (param is TreemapNodeItem node)
                OpenExplorerPath(node.FullPath);
        });
        OpenTreemapCurrentFolderCommand = new RelayCommand(_ => OpenExplorerPath(TreemapCurrentPath));

        BrowseExifFolderCommand = new RelayCommand(_ => BrowseExifFolder(), _ => !IsBusy);
        ScanPhotosExifCommand = new RelayCommand(async _ => await LoadPhotoMetadataAsync(), _ => !IsBusy);
        ScrubSelectedPhotosCommand = new RelayCommand(async _ => await ScrubSelectedPhotosAsync(), _ => !IsBusy && PhotoMetadataItems.Any(p => p.IsSelected));
        OpenPhotoLocationCommand = new RelayCommand(param =>
        {
            if (param is PhotoMetadataItem photo)
                OpenExplorerPath(photo.FullPath);
        });

        CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync(), _ => !IsBusy);
        OpenLatestReleasePageCommand = new RelayCommand(_ => OpenUrlSafe(_latestReleaseUrl));

        // 12th Gen Commands
        MoveTreemapNodeToRecycleBinCommand = new RelayCommand(async param =>
        {
            if (param is TreemapNodeItem node)
                await MoveTreemapNodeToRecycleBinAsync(node);
        }, param => !IsBusy && param is TreemapNodeItem { CanSafeDelete: true });
        ScanDeepSpaceCommand = new RelayCommand(async _ => await LoadDeepSpaceAsync(), _ => !IsBusy);
        ExecuteDeepSpaceItemCommand = new RelayCommand(async param =>
        {
            if (param is DeepSpaceItem item)
                await ExecuteDeepSpaceItemAsync(item);
        }, param => !IsBusy && param is DeepSpaceItem { CanExecuteAction: true });
        BrowseSimilarPhotosFolderCommand = new RelayCommand(_ => BrowseSimilarPhotosFolder(), _ => !IsBusy);
        ScanSimilarPhotosCommand = new RelayCommand(async _ => await LoadSimilarPhotosAsync(), _ => !IsBusy);
        CleanSelectedSimilarPhotosCommand = new RelayCommand(async _ => await CleanSelectedSimilarPhotosAsync(), _ => !IsBusy && SimilarPhotoItems.Any(p => p.IsSelected));
        OpenSimilarPhotoLocationCommand = new RelayCommand(param =>
        {
            if (param is SimilarPhotoItem photo)
                OpenExplorerPath(photo.FullPath);
        });

        // 13th Gen Commands
        ApplyCleaningProfileCommand = new RelayCommand(param =>
        {
            if (param is string profileKey && Enum.TryParse<CleaningProfileKind>(profileKey, ignoreCase: true, out var profile))
                ExecuteApplyCleaningProfile(profile);
            else if (param is CleaningProfileKind directProfile)
                ExecuteApplyCleaningProfile(directProfile);
        }, _ => !IsBusy);
        InstallExplorerContextMenuCommand = new RelayCommand(_ => ExecuteInstallExplorerContextMenu());
        RemoveExplorerContextMenuCommand = new RelayCommand(_ => ExecuteRemoveExplorerContextMenu());
        ExportSettingsConfigCommand = new RelayCommand(_ => ExecuteExportSettingsConfig());
        ImportSettingsConfigCommand = new RelayCommand(_ => ExecuteImportSettingsConfig());
        MergeDuplicatesWithHardlinksCommand = new RelayCommand(
            async _ => await MergeDuplicatesWithHardlinksAsync(),
            _ => !IsBusy && DiskAnalyzerItems.Any(x => x.IsDuplicate && x.IsSelected));

        // 14th Gen Commands
        SaveTreemapSnapshotCommand = new RelayCommand(_ => ExecuteSaveTreemapSnapshot(), _ => !IsBusy && _lastTreemapRawEntries.Count > 0);
        CompareTreemapSnapshotCommand = new RelayCommand(async _ => await ExecuteCompareTreemapSnapshotAsync(), _ => !IsBusy);
        ClearTreemapDiffCommand = new RelayCommand(_ => ExecuteClearTreemapDiff());
        OpenTreemapDiffPathCommand = new RelayCommand(param =>
        {
            if (param is DiskGrowthDiffItem diff)
                OpenExplorerPath(diff.FullPath);
        });
        DownloadAndInstallUpdateCommand = new RelayCommand(
            async _ => await DownloadAndInstallUpdateAsync(),
            _ => CanDownloadUpdate);

        // 15th Gen Activity Console Commands
        CopyActivityLogsCommand = new RelayCommand(_ => ExecuteCopyActivityLogs());
        ExportActivityLogsCommand = new RelayCommand(_ => ExecuteExportActivityLogs());
        ClearActivityLogsCommand = new RelayCommand(_ => ExecuteClearActivityLogs());

        // 16th Gen Portable Mode, Support Bundle & Smart Folder Organizer Commands
        _isPortableMode = AppStoragePathsService.IsPortableModeEnabled();
        ExportSupportBundleCommand = new RelayCommand(_ => ExecuteExportSupportBundle());
        SelectOrganizerPresetCommand = new RelayCommand(param =>
        {
            if (param is string preset)
                SelectOrganizerPreset(preset);
        }, _ => !IsBusy);
        BrowseOrganizerFolderCommand = new RelayCommand(_ => BrowseOrganizerFolder(), _ => !IsBusy);
        PreviewFolderOrganizeCommand = new RelayCommand(_ => ExecutePreviewFolderOrganize(), _ => !IsBusy);
        ApplyFolderOrganizeCommand = new RelayCommand(_ => ExecuteApplyFolderOrganize(), _ => !IsBusy);
        UndoFolderOrganizeCommand = new RelayCommand(_ => ExecuteUndoFolderOrganize(), _ => !IsBusy && SmartFolderOrganizerService.HasUndoJournal());

        // 17th Gen Custom Cleaning Profiles & Onboarding Commands
        SaveCustomProfileCommand = new RelayCommand(_ => ExecuteSaveCustomProfile(), _ => !IsBusy && !string.IsNullOrWhiteSpace(NewCustomProfileName));
        ApplyCustomProfileCommand = new RelayCommand(_ => ExecuteApplySelectedCustomProfile(), _ => !IsBusy && SelectedCustomProfile is not null);
        DeleteCustomProfileCommand = new RelayCommand(_ => ExecuteDeleteSelectedCustomProfile(), _ => !IsBusy && SelectedCustomProfile is not null);
        CompleteOnboardingCommand = new RelayCommand(_ =>
        {
            HasCompletedOnboarding = true;
            SaveCurrentSettings();
        });
        OpenOnboardingCommand = new RelayCommand(_ => HasCompletedOnboarding = false);

        DismissToastCommand = new RelayCommand(_ => DismissToast());
        ExecuteToastActionCommand = new RelayCommand(_ => ExecuteToastAction());

        _toastDismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        _toastDismissTimer.Tick += (_, _) => DismissToast();

        _liveTelemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _liveTelemetryTimer.Tick += (_, _) => TickLiveTelemetry();

        _trayWatcherTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _trayWatcherTimer.Tick += (_, _) => TickTrayHealthWatcher();
        _trayWatcherTimer.Start();

        // Background Auto-Monitor Timer (checks every 5 minutes if enabled)
        _autoMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _autoMonitorTimer.Tick += async (_, _) =>
        {
            await CheckAutoMonitorThresholdAsync();
            await CheckScheduledMaintenanceAsync();
        };
        if (_autoMonitorEnabled)
            _autoMonitorTimer.Start();

        LocalizationService.ApplyLanguage(_selectedLanguage);
        ThemeManagerService.ApplyTheme(_selectedTheme);
        RefreshCleanupHistorySummary();
        RefreshExplorerContextMenuStatus();
        UpdateHealthScoreSummary(0, StartupItems.Count);
        AddLog("FreeCleaner Pro (.NET 10) başlatıldı: Dashboard Geçmişi, Akıllı Treemap Danışmanı, Dev Alan Kurtarıcı, dHash & 57 Modül hazır.");
    }

    public SystemMetricsSnapshot SystemMetrics
    {
        get => _systemMetrics;
        set
        {
            _systemMetrics = value;
            OnPropertyChanged();
        }
    }

    public string ActiveSection
    {
        get => _activeSection;
        set
        {
            if (_activeSection != value)
            {
                _activeSection = value;
                OnPropertyChanged();
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

                FilteredRulesView.Refresh();
                FilteredFilesView.Refresh();

                // Lazy-load data when switching into specialized tabs
                if (_activeSection == "Startup" && StartupItems.Count == 0)
                    LoadStartupItems();
                else if (_activeSection == "Uninstaller" && InstalledApps.Count == 0)
                    _ = LoadInstalledAppsAsync();
                else if (_activeSection == "Booster")
                    LoadTopMemoryProcesses();
                else if (_activeSection == "Privacy")
                    _ = RunScanPrivacyTracesAsync();
                else if (_activeSection == "Network")
                {
                    _ = LoadActiveConnectionsAsync();
                    LoadHostsEntries();
                }
                else if (_activeSection == "TasksAndTree")
                {
                    if (ScheduledTasks.Count == 0) _ = LoadScheduledTasksAsync();
                    if (FolderSpaceItems.Count == 0) _ = RunAnalyzeFolderSpaceTreeAsync();
                }
                else if (_activeSection == "Downloads" && DownloadsAndEmptyItems.Count == 0)
                    _ = RunScanOldInstallersAsync();
                else if (_activeSection == "ContextMenu" && ContextMenuEntries.Count == 0)
                    LoadContextMenuEntries();
                else if (_activeSection == "Services" && OptimizableServices.Count == 0)
                    _ = LoadOptimizableServicesAsync();
                else if (_activeSection == "SmartPower" && PhysicalDisks.Count == 0)
                    _ = LoadDiskAndPowerAsync();
                else if (_activeSection == "BrowserExtensions" && BrowserExtensions.Count == 0)
                    _ = LoadBrowserExtensionsAsync();
                else if (_activeSection == "DriversUpdates" && DriversAndHotfixes.Count == 0)
                    _ = LoadDriversAndHotfixesAsync();
                else if (_activeSection == "PathDoctor" && PathAndShortcutIssues.Count == 0)
                    _ = LoadPathAndShortcutIssuesAsync();
                else if (_activeSection == "CrashLogs" && CrashAndEventLogs.Count == 0)
                    _ = LoadCrashAndEventLogsAsync();
                else if (_activeSection == "WifiDns" && WifiAndDnsItems.Count == 0)
                    _ = LoadWifiAndDnsAsync();
                else if (_activeSection == "PackageCaches" && GlobalPackageCaches.Count == 0)
                    _ = LoadGlobalPackageCachesAsync();
                else if (_activeSection == "RestoreGodMode" && RestorePoints.Count == 0)
                    _ = LoadRestorePointsAsync();
                else if (_activeSection == "FileAssoc" && FileAssociations.Count == 0)
                    _ = LoadFileAssociationsAsync();
                else if (_activeSection == "PortHunter" && ListeningPorts.Count == 0)
                    _ = LoadListeningPortsAsync();
                else if (_activeSection == "Bloatware" && AppxBloatwareItems.Count == 0)
                    _ = LoadAppxBloatwareAsync();
                else if (_activeSection == "DiskBench" && AvailableBenchmarkDrives.Count == 0)
                    _ = LoadBenchmarkDrivesAsync();
                else if (_activeSection == "HardwareSpecs" && HardwareSpecs.Count == 0)
                    _ = LoadHardwareSpecsAsync();
                else if (_activeSection == "UsbForensics" && UsbHistoryItems.Count == 0)
                    _ = LoadUsbHistoryAsync();
                else if (_activeSection == "ProcessSecurity" && ProcessSecurityItems.Count == 0)
                    _ = LoadProcessSecurityItemsAsync();
                else if (_activeSection == "BatchRenamer" && BatchRenameItems.Count == 0)
                    GenerateBatchRenamePreview();
                else if (_activeSection == "NetworkAdapterDoctor" && NetworkAdapters.Count == 0)
                    _ = LoadNetworkAdaptersAsync();
                else if (_activeSection == "FontManager" && InstalledFonts.Count == 0)
                    _ = LoadInstalledFontsAsync();
                else if (_activeSection == "ClipboardShield" && ClipboardAuditItems.Count == 0)
                    ExecuteInspectClipboardNow();
                else if (_activeSection == "AiModelCleaner" && AiModelCaches.Count == 0)
                    _ = LoadAiModelCachesAsync();
                else if (_activeSection == "FirewallBlocker" && FirewallBlockRules.Count == 0)
                    _ = LoadFirewallBlockRulesAsync();
                else if (_activeSection == "Win11Tweaker" && WindowsTweaks.Count == 0)
                    LoadWindowsTweaks();
                else if (_activeSection == "BitLocker" && VolumeSecurityItems.Count == 0)
                    _ = LoadVolumeSecurityAndMountsAsync();
                else if (_activeSection == "WingetUpdater" && WingetUpgrades.Count == 0)
                    _ = LoadWingetUpgradesAsync();
                else if (_activeSection == "LanScanner" && LanDevices.Count == 0)
                    _ = LoadLanDevicesAsync();
                else if (_activeSection == "SqliteOptimizer" && SqliteDatabases.Count == 0)
                    _ = LoadSqliteDatabasesAsync();
                else if (_activeSection == "CredentialSsh" && CredentialsAndSshItems.Count == 0)
                    _ = LoadCredentialsAndSshAsync();
                else if (_activeSection == "ProcessPriority" && ProcessPriorityItems.Count == 0)
                    _ = LoadProcessPriorityItemsAsync();
                else if (_activeSection == "BootPerformance" && BootPerformanceItems.Count == 0)
                    _ = LoadBootPerformanceAsync();
                else if (_activeSection == "DockerWsl" && DockerAndWslItems.Count == 0)
                    _ = LoadDockerAndWslItemsAsync();
                else if (_activeSection == "DefenderAuditor" && DefenderAuditItems.Count == 0)
                    _ = LoadDefenderAuditAsync();
                else if (_activeSection == "MonitorGpu" && MonitorAndDisplayItems.Count == 0)
                    _ = LoadMonitorsAndGpuAsync();
                else if (_activeSection == "GitRepoCompressor" && GitRepoItems.Count == 0)
                    _ = LoadGitReposAsync();
                else if (_activeSection == "DiskTreemap" && TreemapNodes.Count == 0)
                    _ = LoadTreemapAsync();
                else if (_activeSection == "DeepSpaceRecovery" && DeepSpaceItems.Count == 0)
                    _ = LoadDeepSpaceAsync();

                if (_activeSection == "LiveMonitor")
                {
                    TickLiveTelemetry();
                    if (!_liveTelemetryTimer.IsEnabled)
                        _liveTelemetryTimer.Start();
                }
                else if (_liveTelemetryTimer.IsEnabled)
                {
                    _liveTelemetryTimer.Stop();
                }

                if (_activeSection == "Dashboard")
                {
                    RefreshCleanupHistorySummary();
                    UpdateHealthScoreSummary(TotalDetectedBytes, StartupItems.Count);
                }
            }
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

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (_searchFilter != value)
            {
                _searchFilter = value;
                OnPropertyChanged();
                FilteredFilesView.Refresh();
            }
        }
    }

    public string RiskFilter
    {
        get => _riskFilter;
        set
        {
            if (_riskFilter != value)
            {
                _riskFilter = value;
                OnPropertyChanged();
                FilteredFilesView.Refresh();
            }
        }
    }

    public string AppSearchFilter
    {
        get => _appSearchFilter;
        set
        {
            if (_appSearchFilter != value)
            {
                _appSearchFilter = value;
                OnPropertyChanged();
                FilteredInstalledAppsView.Refresh();
            }
        }
    }

    public string DiskScanTargetFolder
    {
        get => _diskScanTargetFolder;
        set
        {
            if (_diskScanTargetFolder != value)
            {
                _diskScanTargetFolder = value;
                OnPropertyChanged();
            }
        }
    }

    public int SelectedMinSizeMb
    {
        get => _selectedMinSizeMb;
        set
        {
            if (_selectedMinSizeMb != value)
            {
                _selectedMinSizeMb = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewExclusionInput
    {
        get => _newExclusionInput;
        set
        {
            if (_newExclusionInput != value)
            {
                _newExclusionInput = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AutoMonitorEnabled
    {
        get => _autoMonitorEnabled;
        set
        {
            if (_autoMonitorEnabled != value)
            {
                _autoMonitorEnabled = value;
                OnPropertyChanged();
                if (_autoMonitorEnabled)
                    _autoMonitorTimer.Start();
                else
                    _autoMonitorTimer.Stop();
                SaveCurrentSettings();
            }
        }
    }

    public int AutoMonitorThresholdMb
    {
        get => _autoMonitorThresholdMb;
        set
        {
            if (_autoMonitorThresholdMb != value)
            {
                _autoMonitorThresholdMb = value;
                OnPropertyChanged();
                SaveCurrentSettings();
            }
        }
    }

    public bool ScheduledMaintenanceEnabled
    {
        get => _scheduledMaintenanceEnabled;
        set
        {
            if (_scheduledMaintenanceEnabled != value)
            {
                _scheduledMaintenanceEnabled = value;
                OnPropertyChanged();
                SaveCurrentSettings();
            }
        }
    }

    public int ScheduledMaintenanceIntervalHours
    {
        get => _scheduledMaintenanceIntervalHours;
        set
        {
            if (_scheduledMaintenanceIntervalHours != value)
            {
                _scheduledMaintenanceIntervalHours = value;
                OnPropertyChanged();
                SaveCurrentSettings();
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (_minimizeToTrayOnClose != value)
            {
                _minimizeToTrayOnClose = value;
                OnPropertyChanged();
                SaveCurrentSettings();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentOperationDetail
    {
        get => _currentOperationDetail;
        set
        {
            if (_currentOperationDetail != value)
            {
                _currentOperationDetail = value;
                OnPropertyChanged();
            }
        }
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (_progressPercent != value)
            {
                _progressPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasAnalyzed
    {
        get => _hasAnalyzed;
        set
        {
            if (_hasAnalyzed != value)
            {
                _hasAnalyzed = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool UseRecycleBinSafety
    {
        get => _useRecycleBinSafety;
        set
        {
            if (_useRecycleBinSafety != value)
            {
                _useRecycleBinSafety = value;
                OnPropertyChanged();
            }
        }
    }

    public long TotalDetectedBytes
    {
        get => _totalDetectedBytes;
        set
        {
            if (_totalDetectedBytes != value)
            {
                _totalDetectedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalDetectedFormatted));
            }
        }
    }

    public int TotalDetectedFiles
    {
        get => _totalDetectedFiles;
        set
        {
            if (_totalDetectedFiles != value)
            {
                _totalDetectedFiles = value;
                OnPropertyChanged();
            }
        }
    }

    public long LastFreedBytes
    {
        get => _lastFreedBytes;
        set
        {
            if (_lastFreedBytes != value)
            {
                _lastFreedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastFreedFormatted));
            }
        }
    }

    public int LastDeletedFiles
    {
        get => _lastDeletedFiles;
        set
        {
            if (_lastDeletedFiles != value)
            {
                _lastDeletedFiles = value;
                OnPropertyChanged();
            }
        }
    }

    public string TotalDetectedFormatted => CleanerRule.FormatBytes(TotalDetectedBytes);
    public string LastFreedFormatted => CleanerRule.FormatBytes(LastFreedBytes);
    public int TotalSelectedFilesCount => DiscoveredFiles.Count(f => f.IsSelected);
    public string TotalSelectedSizeFormatted => CleanerRule.FormatBytes(DiscoveredFiles.Where(f => f.IsSelected).Sum(f => f.SizeBytes));

    // ==================== 1. CLEANER LOGIC ====================

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanerRule.IsSelected) && sender is CleanerRule changedRule)
        {
            if (!_suppressRuleWarningPrompt &&
                changedRule.IsSelected &&
                (changedRule.RequiresWarning || changedRule.RiskLevel == RiskLevel.High) &&
                !string.IsNullOrEmpty(changedRule.WarningMessage))
            {
                var res = MessageBox.Show(
                    $"{changedRule.WarningMessage}\n\nYine de bu seçeneği etkinleştirmek istiyor musunuz?",
                    "Dikkat - Oturum Verisi",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (res != MessageBoxResult.Yes)
                {
                    changedRule.IsSelected = false;
                    return;
                }
            }

            foreach (var file in DiscoveredFiles.Where(f => f.RuleId == changedRule.Id))
            {
                file.IsSelected = changedRule.IsSelected;
            }
            RecalculateSelectedTotals();
        }
    }

    private void OnFileItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScannedFileItem.IsSelected))
        {
            RecalculateSelectedTotals();
        }
    }

    private void RecalculateSelectedTotals()
    {
        TotalDetectedBytes = DiscoveredFiles.Where(x => x.IsSelected).Sum(x => x.SizeBytes);
        TotalDetectedFiles = DiscoveredFiles.Count(x => x.IsSelected);
        OnPropertyChanged(nameof(TotalSelectedFilesCount));
        OnPropertyChanged(nameof(TotalSelectedSizeFormatted));
        CommandManager.InvalidateRequerySuggested();
    }

    private bool FilterRulePredicate(object obj)
    {
        if (obj is not CleanerRule rule) return false;
        return ActiveSection switch
        {
            "System" => rule.Group == RuleGroup.System,
            "Browsers" => rule.Group == RuleGroup.Browsers,
            "Apps" => rule.Group == RuleGroup.DeveloperAndApps,
            _ => true
        };
    }

    private bool FilterFilePredicate(object obj)
    {
        if (obj is not ScannedFileItem item) return false;

        if (ActiveSection is "System" or "Browsers" or "Apps")
        {
            var matchingRule = AllRules.FirstOrDefault(r => r.Id == item.RuleId);
            if (matchingRule is not null && !FilterRulePredicate(matchingRule))
                return false;
        }

        if (RiskFilter is not "Tümü" && item.RiskLabel != RiskFilter)
            return false;

        return string.IsNullOrWhiteSpace(SearchFilter) ||
               item.FullPath.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
               item.RuleName.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
               item.Category.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterInstalledAppsPredicate(object obj)
    {
        if (obj is not InstalledAppModel app) return false;
        if (string.IsNullOrWhiteSpace(AppSearchFilter)) return true;

        return app.DisplayName.Contains(AppSearchFilter, StringComparison.OrdinalIgnoreCase) ||
               app.Publisher.Contains(AppSearchFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void SetVisibleRulesSelection(bool selected)
    {
        foreach (CleanerRule rule in FilteredRulesView)
        {
            if (selected && (rule.RequiresWarning || rule.RiskLevel == RiskLevel.High))
                continue;
            rule.IsSelected = selected;
        }
    }

    public void RefreshSystemMetrics()
    {
        SystemMetrics = SystemInfoService.GetMetrics();
        RefreshStorageForecast();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
