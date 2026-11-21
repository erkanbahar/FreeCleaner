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

    private async Task RunAnalyzeAsync()
    {
        if (!AllRules.Any(r => r.IsSelected))
        {
            MessageBox.Show(
                "Lütfen analiz etmek için sol panelden en az bir temizlik kategorisi seçin.",
                "Kategori Seçilmedi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

        StatusMessage = "Sistem analiz ediliyor (Simülasyon Modu)...";
        AddLog("Analiz başlatıldı (Hiçbir dosya silinmiyor, Beyaz Liste korunuyor)...");

        _cts = new CancellationTokenSource();
        var progress = new Progress<(int Percent, string CurrentRuleName, long TotalFoundBytes, int TotalFoundCount)>(report =>
        {
            ProgressPercent = report.Percent;
            CurrentOperationDetail = $"Taranıyor: {report.CurrentRuleName}";
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

            StatusMessage = $"Analiz tamamlandı: {TotalDetectedFiles:N0} öğede toplam {TotalDetectedFormatted} gereksiz veri bulundu.";
            CurrentOperationDetail = "Sonuçları inceleyebilir ve 'Temizliği Başlat' ile silebilirsiniz.";
            AddLog($"Analiz tamamlandı: {TotalDetectedFiles:N0} dosya ({TotalDetectedFormatted}) tespit edildi.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Analiz kullanıcı tarafından iptal edildi.";
            CurrentOperationDetail = "İptal edildi.";
            AddLog("Analiz işlemi iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Analiz sırasında hata: {ex.Message}";
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
                $"Yüksek riskli temizlik seçenekleri seçildi:\n\n{highRiskNames}\n\n" +
                "Bu işlemler oturum verilerini, geçmişleri veya geri alınması zor sistem verilerini etkileyebilir. " +
                "Seçili öğeleri temizlemek istediğinizi açıkça onaylıyor musunuz?",
                "Yüksek Riskli Temizlik Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (highRiskConfirmation != MessageBoxResult.Yes)
            {
                AddLog("Yüksek riskli temizlik kullanıcı tarafından onaylanmadı.");
                return;
            }
        }

        string modeDescription = UseRecycleBinSafety
            ? "Seçili dosyalar Geri Dönüşüm Kutusu'na taşınacaktır."
            : "Seçili dosyalar kalıcı olarak silinecektir (açık/kullanımda olan sistem dosyaları güvenle atlanır).";

        var confirm = MessageBox.Show(
            $"Toplam {selectedCount:N0} adet gereksiz öğe ({selectedSize}) temizlenecek.\n\n" +
            $"Mod: {modeDescription}\n\n" +
            "Temizleme işlemini onaylıyor musunuz?",
            "FreeCleaner - Temizleme Onayı",
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
                "Bu işlem Geri Dönüşüm Kutusu'nu tamamen boşaltacaktır ve geri alınamaz.\n\n" +
                "Devam etmek istediğinizden emin misiniz?",
                "Geri Dönüşüm Kutusu - Son Onay",
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
            string lockList = string.Join("\n", lockConflicts.Select(g => $"• {g.FriendlyName} ({g.RunningInstanceCount} işlem)"));
            var lockPrompt = MessageBox.Show(
                $"Temizlenecek önbellekleri şu anda kullanan açık uygulamalar tespit edildi:\n\n{lockList}\n\n" +
                "• Evet: Bu uygulamaları güvenle kapatıp tüm kilitli dosyaları temizle\n" +
                "• Hayır: Uygulamaları kapatmadan devam et (kilitli dosyalar atlanır)\n" +
                "• İptal: Temizliği durdur",
                "Akıllı Süreç Koruması (Process Lock Guard)",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (lockPrompt == MessageBoxResult.Cancel)
            {
                AddLog("Temizlik kilitli süreç uyarısı üzerine kullanıcı tarafından iptal edildi.");
                return;
            }

            if (lockPrompt == MessageBoxResult.Yes)
            {
                StatusMessage = "Kilitli tarayıcı / uygulamalar güvenle kapatılıyor...";
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
        StatusMessage = "Temizlik işlemi yürütülüyor...";
        AddLog($"Temizlik başlatıldı ({selectedCount:N0} öğe, {selectedSize})...");

        _cts = new CancellationTokenSource();
        var progress = new Progress<(int Percent, string CurrentFile, long FreedBytes, int DeletedCount)>(report =>
        {
            ProgressPercent = report.Percent;
            CurrentOperationDetail = $"Temizleniyor: {report.CurrentFile}";
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

            StatusMessage = $"Temizlik tamamlandı! {summary.DeletedFilesCount:N0} dosya silindi, {CleanerRule.FormatBytes(summary.FreedBytes)} alan açıldı.";
            CurrentOperationDetail = summary.SkippedFilesCount > 0
                ? $"Süre: {summary.Elapsed.TotalSeconds:F1} sn • {summary.SkippedFilesCount:N0} kilitli/kullanımda olan dosya güvenle atlandı."
                : $"Süre: {summary.Elapsed.TotalSeconds:F1} sn • Tüm seçili öğeler başarıyla temizlendi.";

            AddLog($"[TAMAMLANDI] {CleanerRule.FormatBytes(summary.FreedBytes)} boş alan kazanıldı ({summary.DeletedFilesCount:N0} silindi, {summary.SkippedFilesCount:N0} kilitli dosya atlandı).");
            foreach (var detail in summary.Logs.Where(log => log.StartsWith("[ATLANDI]", StringComparison.Ordinal)).Take(20))
                AddLog(detail);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Temizleme işlemi durduruldu.";
            AddLog("Temizleme işlemi kullanıcı tarafından durduruldu.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Temizleme hatası: {ex.Message}";
            AddLog($"[HATA] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshSystemMetrics();
        }
    }

    // ==================== 6. SYSTEM BOOSTER (RAM & DNS) & REPORTS ====================

    private void LoadTopMemoryProcesses()
    {
        TopMemoryProcesses.Clear();
        foreach (var proc in SystemBoosterAndReportService.GetTopMemoryProcesses(15))
        {
            TopMemoryProcesses.Add(proc);
        }
    }

    private async Task RunOptimizeRamAsync()
    {
        IsBusy = true;
        StatusMessage = "Arka plan işlemlerinin kullanılmayan bellek (Working Set) blokları serbest bırakılıyor...";
        try
        {
            var (freedBytes, count) = await SystemBoosterAndReportService.OptimizeRamWorkingSetAsync();
            RefreshSystemMetrics();
            LoadTopMemoryProcesses();

            string freedText = CleanerRule.FormatBytes(freedBytes);
            StatusMessage = $"RAM Optimizasyonu Tamamlandı: {count} süreç optimize edildi, {freedText} bellek serbest bırakıldı.";
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
            var (ok, msg) = await SystemBoosterAndReportService.FlushDnsCacheAsync();
            StatusMessage = $"DNS Önbelleği Temizlendi: {msg}";
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
            StatusMessage = $"Ağ Hedef Önbelleği Sıfırlandı: {msg}";
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

            StatusMessage = $"HTML ve JSON raporları Masaüstü\\FreeCleaner_Raporlar klasörüne kaydedildi.";
            AddLog($"[RAPOR] HTML Raporu: {htmlPath}");
            AddLog($"[RAPOR] JSON Raporu: {jsonPath}");

            var openRes = MessageBox.Show(
                $"Raporlar başarıyla oluşturuldu:\n\n• HTML: {htmlPath}\n• JSON: {jsonPath}\n\nHTML raporunu şimdi tarayıcıda açmak ister misiniz?",
                "Rapor Dışa Aktarıldı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (openRes == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = htmlPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Rapor oluşturulamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 7. EXCLUSIONS & AUTO MONITOR ====================

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
            Title = "Beyaz Listeye (Koruma Altına) Eklenecek Klasörü Seçin"
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
                string msg = $"Sistemde {CleanerRule.FormatBytes(totalBytes)} gereksiz dosya birikti!";
                StatusMessage = $"[OTOMATİK İZLEME] {msg}";
                AddLog($"[OTOMATİK UYARI] Eşik ({AutoMonitorThresholdMb} MB) aşıldı: {CleanerRule.FormatBytes(totalBytes)} çöp tespit edildi.");
                TrayNotificationRequested?.Invoke("FreeCleaner Pro — Çöp Dosya Uyarısı", msg, true);
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
                StatusMessage = "Zamanlanmış güvenli bakım için düşük riskli öğeler taranıyor...";
                var results = await _engine.AnalyzeAsync(safeRules, ExcludedPaths.ToList());
                if (results.Count == 0)
                {
                    AddLog("[ZAMANLI BAKIM] Düşük riskli temizlenecek öğe bulunamadı.");
                    return;
                }

                var confirm = MessageBox.Show(
                    $"Zamanlanmış güvenli bakımda {results.Count:N0} düşük riskli öğe " +
                    $"({CleanerRule.FormatBytes(results.Sum(item => item.SizeBytes))}) bulundu.\n\n" +
                    "Öğeler Geri Dönüşüm Kutusu'na taşınacak. Devam edilsin mi?",
                    "Zamanlanmış Güvenli Bakım",
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
                StatusMessage = $"Zamanlanmış bakım hatası: {ex.Message}";
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
        if (!result.Success)
            MessageBox.Show(result.Message, "Zamanlanmış Bakım", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RemoveScheduledMaintenance()
    {
        var result = ScheduledMaintenanceService.Remove();
        StatusMessage = result.Message;
        AddLog($"[ZAMANLI BAKIM] {result.Message}");
        if (!result.Success)
            MessageBox.Show(result.Message, "Zamanlanmış Bakım", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

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
        _cts = new CancellationTokenSource();

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
            var (updated, failed, msg) = await NinthGenWingetAndNetworkSuiteService.UpgradeWingetPackagesWithProgressAsync(targets, progress, _cts.Token);
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

    private string _gitWorkspacePath = @"C:\CURSOR";
    public string GitWorkspacePath
    {
        get => _gitWorkspacePath;
        set
        {
            if (_gitWorkspacePath != value)
            {
                _gitWorkspacePath = value;
                OnPropertyChanged();
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

    // ==================== 17. FLAGSHIP DASHBOARD, FAVORITES, PALETTE, LIVE TELEMETRY & THEMES ====================

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme != value && !string.IsNullOrWhiteSpace(value))
            {
                _selectedTheme = value;
                ThemeManagerService.ApplyTheme(_selectedTheme);
                OnPropertyChanged();
                SaveCurrentSettings();
                StatusMessage = $"Tema değiştirildi: {_selectedTheme}";
                AddLog($"[TEMA] Uygulama görünümü '{_selectedTheme}' olarak güncellendi.");
            }
        }
    }

    public int HealthScore
    {
        get => _healthScore;
        set { if (_healthScore != value) { _healthScore = value; OnPropertyChanged(); } }
    }

    public string HealthGradeBadge
    {
        get => _healthGradeBadge;
        set { if (_healthGradeBadge != value) { _healthGradeBadge = value; OnPropertyChanged(); } }
    }

    public string HealthAdviceText
    {
        get => _healthAdviceText;
        set { if (_healthAdviceText != value) { _healthAdviceText = value; OnPropertyChanged(); } }
    }

    public double LiveCpuPercent
    {
        get => _liveCpuPercent;
        set { _liveCpuPercent = value; OnPropertyChanged(); }
    }

    public double LiveRamPercent
    {
        get => _liveRamPercent;
        set { _liveRamPercent = value; OnPropertyChanged(); }
    }

    public string LiveNetDownText
    {
        get => _liveNetDownText;
        set { _liveNetDownText = value; OnPropertyChanged(); }
    }

    public string LiveNetUpText
    {
        get => _liveNetUpText;
        set { _liveNetUpText = value; OnPropertyChanged(); }
    }

    public PointCollection CpuSparklinePoints
    {
        get => _cpuSparklinePoints;
        set { _cpuSparklinePoints = value; OnPropertyChanged(); }
    }

    public PointCollection RamSparklinePoints
    {
        get => _ramSparklinePoints;
        set { _ramSparklinePoints = value; OnPropertyChanged(); }
    }

    public PointCollection NetDownSparklinePoints
    {
        get => _netDownSparklinePoints;
        set { _netDownSparklinePoints = value; OnPropertyChanged(); }
    }

    public PointCollection NetUpSparklinePoints
    {
        get => _netUpSparklinePoints;
        set { _netUpSparklinePoints = value; OnPropertyChanged(); }
    }

    public bool IsCurrentSectionFavorite => _favoriteSectionKeys.Contains(ActiveSection, StringComparer.OrdinalIgnoreCase);

    public string FavoriteButtonText => LocalizationService.NormalizeLanguageCode(_selectedLanguage) == "en-US"
        ? (IsCurrentSectionFavorite ? "★ Pinned" : "☆ Pin Favorite")
        : (IsCurrentSectionFavorite ? "★ Favorilerde" : "☆ Favoriye Ekle");

    public void ExecuteCommandPaletteShortcut(NavShortcutItem item)
    {
        switch (item.SectionKey)
        {
            case "CMD:HealthCheck":
                ActiveSection = "Dashboard";
                if (RunOneClickHealthCheckCommand.CanExecute(null))
                    RunOneClickHealthCheckCommand.Execute(null);
                break;
            case "CMD:Analyze":
                ActiveSection = "All";
                if (AnalyzeCommand.CanExecute(null))
                    AnalyzeCommand.Execute(null);
                break;
            case "CMD:OptimizeRam":
                if (OptimizeRamCommand.CanExecute(null))
                    OptimizeRamCommand.Execute(null);
                break;
            case "CMD:FlushDns":
                if (FlushDnsCommand.CanExecute(null))
                    FlushDnsCommand.Execute(null);
                break;
            case "CMD:CheckUpdate":
                ActiveSection = "Settings";
                if (CheckForUpdatesCommand.CanExecute(null))
                    CheckForUpdatesCommand.Execute(null);
                break;
            case "CMD:ProfileSafe":
                ActiveSection = "All";
                ExecuteApplyCleaningProfile(CleaningProfileKind.SafeDaily);
                break;
            case "CMD:ProfileDev":
                ActiveSection = "All";
                ExecuteApplyCleaningProfile(CleaningProfileKind.DeveloperDeep);
                break;
            case "CMD:ProfileGamer":
                ActiveSection = "All";
                ExecuteApplyCleaningProfile(CleaningProfileKind.GamerBoost);
                break;
            case "CMD:ProfilePrivacy":
                ActiveSection = "All";
                ExecuteApplyCleaningProfile(CleaningProfileKind.MaxPrivacy);
                break;
            case "CMD:ExportSettings":
                ExecuteExportSettingsConfig();
                break;
            case "CMD:ExportSupportBundle":
                ExecuteExportSupportBundle();
                break;
            case "CMD:OrganizeDownloads":
                ActiveSection = "Downloads";
                SelectOrganizerPreset("Downloads");
                break;
            default:
                ActiveSection = item.SectionKey;
                break;
        }
    }

    private void ToggleCurrentSectionFavorite()
    {
        int existingIdx = _favoriteSectionKeys.FindIndex(k => k.Equals(ActiveSection, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0)
        {
            _favoriteSectionKeys.RemoveAt(existingIdx);
            StatusMessage = $"'{SectionTitle}' favorilerden çıkarıldı.";
        }
        else
        {
            _favoriteSectionKeys.Add(ActiveSection);
            StatusMessage = $"'{SectionTitle}' favorilere sabitlendi!";
        }

        RebuildFavoriteNavItems();
        OnPropertyChanged(nameof(IsCurrentSectionFavorite));
        OnPropertyChanged(nameof(FavoriteButtonText));
        SaveCurrentSettings();
    }

    private void RebuildFavoriteNavItems()
    {
        FavoriteNavItems.Clear();
        foreach (string key in _favoriteSectionKeys)
        {
            var match = AllModuleCatalog.FirstOrDefault(m => m.SectionKey.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                FavoriteNavItems.Add(match);
        }
    }

    private void UpdateHealthScoreSummary(long junkBytes, int startupCount)
    {
        RefreshSystemMetrics();
        var (score, badge, advice) = FlagshipTelemetryAndThemeService.ComputeHealthScore(SystemMetrics, junkBytes, startupCount);
        HealthScore = score;
        HealthGradeBadge = badge;
        HealthAdviceText = advice;
    }

    public async Task RunOneClickHealthCheckAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        ActiveSection = "Dashboard";
        HealthCheckResults.Clear();
        ProgressPercent = 10;
        StatusMessage = "Tek Tıkla 100 Puanlık PC Sağlık Kontrolü başlatıldı: Sistem analiz ediliyor...";
        AddLog("[SAĞLIK KONTROLÜ] Kapsamlı sistem taraması ve hızlı optimizasyon başlatıldı.");

        try
        {
            // 1. System Disk & RAM check
            RefreshSystemMetrics();
            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Depolama",
                CheckName = $"Sistem Diski ({SystemMetrics.DriveName})",
                StatusBadge = SystemMetrics.DriveUsedPercent > 85 ? "⚠️ Dolu" : "✅ Sağlıklı",
                Details = SystemMetrics.DriveSummaryText
            });
            ProgressPercent = 30;

            // 2. Junk Files Analysis (Simulation)
            var junkResults = await _engine.AnalyzeAsync(AllRules, ExcludedPaths.ToList());
            long junkBytes = junkResults.Sum(x => x.SizeBytes);
            TotalDetectedFiles = junkResults.Count;
            TotalDetectedBytes = junkBytes;
            HasAnalyzed = true;

            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Gereksiz Dosyalar",
                CheckName = "Sistem & Tarayıcı Çöp Taraması",
                StatusBadge = junkBytes > 512L * 1024 * 1024 ? "🧹 Temizlik Önerilir" : "✅ Temiz",
                Details = $"{junkResults.Count:N0} geçici dosya ({CleanerRule.FormatBytes(junkBytes)}) tespit edildi."
            });
            ProgressPercent = 55;

            // 3. Startup items audit
            int startupCount = await Task.Run(() => StartupManagerService.GetStartupItems().Count);
            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Açılış Hızı",
                CheckName = "Başlangıç (Startup) Uygulamaları",
                StatusBadge = startupCount > 15 ? "⚠️ Yüksek" : "✅ Dengeli",
                Details = $"{startupCount} adet başlangıç kaydı bulundu."
            });
            ProgressPercent = 75;

            // 4. RAM Working Set Boost & DNS Cache Flush
            var (freedRamBytes, trimmedProcCount) = await SystemBoosterAndReportService.OptimizeRamWorkingSetAsync();
            await SystemBoosterAndReportService.FlushDnsCacheAsync();
            RefreshSystemMetrics();
            string reclaimedFormatted = CleanerRule.FormatBytes(freedRamBytes);

            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Bellek (RAM)",
                CheckName = "Çalışma Kümesi (Working Set) Optimizasyonu",
                StatusBadge = "⚡ Hızlandırıldı",
                Details = $"{trimmedProcCount} işlemden {reclaimedFormatted} önbellek belleği serbest bırakıldı. Güncel kullanım: {SystemMetrics.RamUsedPercent:F0}%"
            });

            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Ağ & DNS",
                CheckName = "Windows DNS Çözümleyici Önbelleği",
                StatusBadge = "✅ Yenilendi",
                Details = "DNS çözümleyici önbelleği (ipconfig /flushdns) tazelendi."
            });

            UpdateHealthScoreSummary(junkBytes, startupCount);
            ProgressPercent = 100;

            string summary = $"Sağlık Kontrolü Tamamlandı: Skor {HealthScore}/100 ({HealthGradeBadge}) • {reclaimedFormatted} RAM açıldı.";
            StatusMessage = summary;
            AddLog($"[SAĞLIK KONTROLÜ] {summary}");
            TrayNotificationRequested?.Invoke("FreeCleaner Pro — Sağlık Kontrolü", summary, HealthScore < 60);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void TickLiveTelemetry()
    {
        var (cpu, ram, downKbps, upKbps) = _telemetrySampler.SampleNext();
        LiveCpuPercent = cpu;
        LiveRamPercent = ram;
        LiveNetDownText = FormatNetRate(downKbps);
        LiveNetUpText = FormatNetRate(upKbps);

        _cpuHistory.Add(cpu);
        _ramHistory.Add(ram);
        _netDownHistory.Add(downKbps);
        _netUpHistory.Add(upKbps);

        TrimHistory(_cpuHistory);
        TrimHistory(_ramHistory);
        TrimHistory(_netDownHistory);
        TrimHistory(_netUpHistory);

        CpuSparklinePoints = FlagshipTelemetryAndThemeService.BuildSparklinePoints(_cpuHistory, 100.0, 260, 74);
        RamSparklinePoints = FlagshipTelemetryAndThemeService.BuildSparklinePoints(_ramHistory, 100.0, 260, 74);
        double maxNet = Math.Max(50.0, Math.Max(_netDownHistory.Max(), _netUpHistory.Max()));
        NetDownSparklinePoints = FlagshipTelemetryAndThemeService.BuildSparklinePoints(_netDownHistory, maxNet, 260, 74);
        NetUpSparklinePoints = FlagshipTelemetryAndThemeService.BuildSparklinePoints(_netUpHistory, maxNet, 260, 74);

        var topProcs = FlagshipTelemetryAndThemeService.GetTopProcessesSnapshot(10);
        LiveTopProcesses.Clear();
        foreach (var p in topProcs)
            LiveTopProcesses.Add(p);
    }

    private static void TrimHistory(List<double> list, int maxPoints = 40)
    {
        if (list.Count > maxPoints)
            list.RemoveRange(0, list.Count - maxPoints);
    }

    private static string FormatNetRate(double kbps)
        => kbps >= 1024.0 ? $"{kbps / 1024.0:F2} MB/s" : $"{kbps:F1} KB/s";

    private void PopulateModuleCatalog()
    {
        bool en = LocalizationService.NormalizeLanguageCode(_selectedLanguage) == "en-US";
        AllModuleCatalog.Clear();
        void AddEntry(string key, string trTitle, string enTitle, string glyph, string trCat, string enCat, string keywords)
            => AllModuleCatalog.Add(new NavShortcutItem
            {
                SectionKey = key,
                Title = en ? enTitle : trTitle,
                IconGlyph = glyph,
                CategoryName = en ? enCat : trCat,
                Keywords = keywords
            });

        const string TrCmd = "Hızlı Komut";
        const string EnCmd = "Quick Action";
        const string TrClean = "Temizlik & Disk";
        const string EnClean = "Clean & Disk";
        const string TrPerf = "Performans & Donanım";
        const string EnPerf = "Performance & Hardware";
        const string TrSys = "Sistem & Windows 11";
        const string EnSys = "System & Windows 11";
        const string TrNet = "Ağ & Firewall";
        const string EnNet = "Network & Firewall";
        const string TrSec = "Güvenlik & Gizlilik";
        const string EnSec = "Security & Privacy";
        const string TrDev = "Geliştirici & AI";
        const string EnDev = "Developer & AI";
        const string TrSet = "Ayarlar & Tema";
        const string EnSet = "Settings & Theme";

        // Quick Actions (Ctrl+K)
        AddEntry("CMD:HealthCheck", "⚡ Hızlı Eylem: Tek Tıkla PC Sağlık Kontrolü Başlat", "⚡ Quick Action: Run 1-Click PC Health Check", "\uE95E", TrCmd, EnCmd, "saglik skor health check tarama hizlandir");
        AddEntry("CMD:Analyze", "🔍 Hızlı Eylem: Akıllı Gereksiz Dosya Analizini Başlat", "🔍 Quick Action: Start Smart Junk Analysis", "\uE721", TrCmd, EnCmd, "analiz tara temizlik cop analyze scan junk");
        AddEntry("CMD:OptimizeRam", "🧠 Hızlı Eylem: RAM Belleğini Şimdi Boşalt", "🧠 Quick Action: Optimize RAM Now", "\uE945", TrCmd, EnCmd, "ram bellek bosalt memory boost optimize");
        AddEntry("CMD:FlushDns", "🌐 Hızlı Eylem: DNS Önbelleğini Temizle (flushdns)", "🌐 Quick Action: Flush DNS Cache (flushdns)", "\uE701", TrCmd, EnCmd, "dns flush ip yenile");
        AddEntry("CMD:CheckUpdate", "🔄 Hızlı Eylem: Yeni Sürüm Kontrol Et (GitHub Releases)", "🔄 Quick Action: Check for Updates (GitHub Releases)", "\uE895", TrCmd, EnCmd, "guncelleme update version release github");
        AddEntry("CMD:ProfileSafe", "🛡️ Profil Uygula: Güvenli Günlük Temizlik Kural Seti", "🛡️ Apply Profile: Safe Daily Rule Preset", "\uEA18", TrCmd, EnCmd, "profil safe gunluk daily temizlik kural");
        AddEntry("CMD:ProfileDev", "🛠️ Profil Uygula: Geliştirici Derin (SDK / Cache) Kural Seti", "🛠️ Apply Profile: Developer Deep Rule Preset", "\uE943", TrCmd, EnCmd, "profil developer gelistirici sdk cache nuget npm");
        AddEntry("CMD:ProfileGamer", "🎮 Profil Uygula: Oyuncu Boost (GPU / Shader / Temp)", "🎮 Apply Profile: Gamer Boost Rule Preset", "\uE7FC", TrCmd, EnCmd, "profil gamer oyuncu shader directx steam temp");
        AddEntry("CMD:ProfilePrivacy", "🕵️ Profil Uygula: Maksimum Gizlilik (Tarayıcı / Telemetri)", "🕵️ Apply Profile: Max Privacy Rule Preset", "\uE72E", TrCmd, EnCmd, "profil gizlilik privacy cookie gecmis history");
        AddEntry("CMD:ExportSettings", "📤 Hızlı Eylem: Ayar Profilini JSON Olarak Dışa Aktar", "📤 Quick Action: Export Settings Profile (JSON)", "\uE74E", TrCmd, EnCmd, "ayar yedek export import json profil settings");
        AddEntry("CMD:ExportSupportBundle", "📦 Hızlı Eylem: Teşhis Destek Paketi (.zip) Oluştur", "📦 Quick Action: Export Diagnostic Support Bundle (.zip)", "\uE7B8", TrCmd, EnCmd, "destek teshis zip support bundle diagnostic report");
        AddEntry("CMD:OrganizeDownloads", "🗂️ Hızlı Eylem: İndirilenler Klasörünü Düzenle (Önizleme)", "🗂️ Quick Action: Organize Downloads Folder (Preview)", "\uE8F4", TrCmd, EnCmd, "indirilenler downloads duzenle organize sort undo");

        // All 57 Modules
        AddEntry("Dashboard", "Genel Bakış (Dashboard) & Sağlık Skoru", "Overview (Dashboard) & Health Score", "\uE80F", TrClean, EnClean, "dashboard ana sayfa overview saglik skoru gecmis lifetime forecast");
        AddEntry("DiskTreemap", "🗺️ Görsel Disk Haritası (Treemap + Danışman)", "🗺️ Visual Disk Treemap & Smart Advisor", "\uE8A9", TrClean, EnClean, "treemap harita disk gorsel boyut klasor danisman snapshot diff");
        AddEntry("DeepSpaceRecovery", "💾 Dev Windows Sistem Alanı Kurtarıcısı (15–40 GB)", "💾 Deep Windows Space Recovery (15–40 GB)", "\uEDA2", TrClean, EnClean, "hiberfil winsxs compactos nvidia amd installer deep space");
        AddEntry("SimilarPhotos", "🖼️ Benzer / Tekrarlayan Fotoğraf Bulucu (dHash)", "🖼️ Similar Photo Finder (Perceptual dHash)", "\uEB9F", TrClean, EnClean, "benzer fotograf similar duplicate dhash resim galeri");
        AddEntry("LiveMonitor", "Canlı CPU, RAM & Ağ Grafikleri", "Live CPU, RAM & Network Graphs", "\uE9D9", TrPerf, EnPerf, "canli live grafik cpu ram ag hizi monitor sparkline");
        AddEntry("All", "Akıllı Temizlik (Tümü)", "Smart Cleaner (All Rules)", "\uE770", TrClean, EnClean, "temizlik cop temp gecici smart clean");
        AddEntry("System", "Windows & Sistem Temizliği", "Windows & System Cleanup", "\uE7F8", TrClean, EnClean, "windows temp log recycle bin system");
        AddEntry("Browsers", "Web Tarayıcıları Temizliği", "Web Browsers & AI Cache Cleanup", "\uE774", TrClean, EnClean, "chrome edge firefox brave cache cookie gemini nano optguideondevicemodel weights.bin");
        AddEntry("Apps", "Uygulama & Geliştirici Önbelleği", "Apps & Developer Caches", "\uE943", TrClean, EnClean, "app cache discord steam vscode");
        AddEntry("DiskAnalyzer", "Büyük & Kopya Dosya Bulucu", "Large & Duplicate File Finder (Hardlink)", "\uEDA2", TrClean, EnClean, "buyuk large dosya duplicate sha256 kopya hardlink");
        AddEntry("Downloads", "İndirilenler, Boş Klasör & Düzenleyici", "Downloads, Empty Folders & Organizer", "\uE896", TrClean, EnClean, "downloads kurulum setup iso bos klasor organize sort undo");
        AddEntry("Bloatware", "Bloatware (UWP Appx) Kaldırıcı", "Appx Bloatware Remover", "\uE74D", TrClean, EnClean, "bloatware appx uwp xbox windows 11");
        AddEntry("Shredder", "Dosya Öğütücü (DoD 5220.22-M)", "File Shredder (DoD 5220.22-M)", "\uE74D", TrClean, EnClean, "shredder kalici silme bos alan wipe");
        AddEntry("Startup", "Başlangıç (Startup) Yöneticisi", "Startup Applications Manager", "\uE7E8", TrPerf, EnPerf, "startup acilis baslangic");
        AddEntry("BootPerformance", "Açılış Süresi (Boot Time) Analizi", "Boot Time Bottleneck Analyzer", "\uE916", TrPerf, EnPerf, "boot acilis suresi darbogaz startup");
        AddEntry("Booster", "RAM & Ağ Hızlandırıcı", "RAM & Network Booster", "\uE945", TrPerf, EnPerf, "ram booster hizlandirici dns");
        AddEntry("ProcessPriority", "Canlı CPU Önceliği & Eco Mod", "Live CPU Priority & EcoQoS", "\uEC4A", TrPerf, EnPerf, "cpu oncelik priority eco game boost");
        AddEntry("DiskBench", "SSD / NVMe Disk Hız Testi", "SSD / NVMe Read & Write Benchmark", "\uEC4A", TrPerf, EnPerf, "ssd nvme benchmark okuma yazma hiz");
        AddEntry("SmartPower", "Disk Sağlığı (S.M.A.R.T.) & Güç Planı", "Disk S.M.A.R.T., Battery & Power Plan", "\uE945", TrPerf, EnPerf, "smart disk saglik pil guc oyun modu battery power");
        AddEntry("HardwareSpecs", "Donanım, BIOS & OEM Lisans", "Hardware Inventory, BIOS & OEM Key", "\uE950", TrPerf, EnPerf, "cpu gpu ram bios anakart lisans product key");
        AddEntry("MonitorGpu", "Monitör (Hz) & GPU Doktoru", "Monitor EDID (Hz) & GPU Reset Doctor", "\uE7F4", TrPerf, EnPerf, "monitor edid hz yenileme gpu reset");
        AddEntry("WingetUpdater", "Winget Program Güncelleyici", "Winget Bulk App Updater", "\uE896", TrSys, EnSys, "winget guncelle update yazilim apps");
        AddEntry("Win11Tweaker", "Windows 11 İnce Ayar (Tweaker)", "Windows 11 Ultimate Tweaker", "\uE90F", TrSys, EnSys, "win11 sag tik explorer bing gemini nano chrome ai tweak");
        AddEntry("Registry", "Kayıt Defteri (Registry) Temizleyici", "Registry Scanner & Backup", "\uE90F", TrSys, EnSys, "registry regedit yedek backup");
        AddEntry("Uninstaller", "Program Kaldırıcı & Yetim Kalıntılar", "Program Uninstaller & Orphan Cleaner", "\uE7B8", TrSys, EnSys, "uninstall program kaldir appdata orphan");
        AddEntry("Services", "Windows Servisleri Yöneticisi", "Windows Services Optimizer", "\uE912", TrSys, EnSys, "services hizmetler telemetri");
        AddEntry("RepairCenter", "Sistem Onarım Merkezi (SFC/DISM)", "System Repair Center (SFC / DISM)", "\uE90F", TrSys, EnSys, "sfc dism onarim repair windows.old fontcache");
        AddEntry("RestoreGodMode", "Geri Yükleme Noktası & God Mode", "Restore Points & Windows God Mode", "\uE81C", TrSys, EnSys, "restore point tanri modu god mode");
        AddEntry("ContextMenu", "Sağ Tık Bağlam Menüsü (Shell)", "Shell Context Menu Manager", "\uE700", TrSys, EnSys, "context menu shell sag tik");
        AddEntry("FileAssoc", "Dosya Uzantısı Doktoru (UserChoice)", "File Association Doctor (UserChoice)", "\uE8A5", TrSys, EnSys, "uzanti file association progid default");
        AddEntry("FontManager", "Font (Yazı Tipi) Yöneticisi", "System & User Font Manager", "\uE8D2", TrSys, EnSys, "font yazi tipi ttf otf");
        AddEntry("LanScanner", "Yerel Ağ (LAN / Wi-Fi) Radarı", "Local Network (LAN / Wi-Fi) Radar", "\uE701", TrNet, EnNet, "lan arp ping modem wifi cihaz device");
        AddEntry("WifiDns", "Wi-Fi Şifreleri & DNS Ping Testi", "Saved Wi-Fi Passwords & DNS Ping", "\uE701", TrNet, EnNet, "wifi sifre password dns ping");
        AddEntry("Network", "Canlı Ağ Bağlantıları & Hosts", "Live TCP/UDP Connections & Hosts", "\uE968", TrNet, EnNet, "tcp udp netstat hosts");
        AddEntry("NetworkAdapterDoctor", "Ağ Adaptörü, MAC & Winsock Doktoru", "Network Adapter, MAC & Winsock Doctor", "\uE839", TrNet, EnNet, "mac ip gateway dhcp winsock arp");
        AddEntry("FirewallBlocker", "Firewall & Uygulama İnternet Kesici", "Windows Firewall & 1-Click Blocker", "\uE72E", TrNet, EnNet, "firewall guvenlik duvari engel block");
        AddEntry("PortHunter", "Geliştirici Port Avcısı (Port Killer)", "Developer Port Hunter (Port Killer)", "\uE968", TrNet, EnNet, "port 3000 8080 5000 pid kill");
        AddEntry("DefenderAuditor", "Windows Defender & İstisna Denetçisi", "Defender Shield & Exclusions Auditor", "\uE83D", TrSec, EnSec, "defender antivirus tarama exclusion");
        AddEntry("ExifScrubber", "📸 Fotoğraf EXIF / GPS Gizlilik Temizleyici", "📸 Photo EXIF / GPS Privacy Scrubber", "\uE722", TrSec, EnSec, "exif gps fotograf resim photo gizlilik metadata scrubber");
        AddEntry("ProcessSecurity", "Süreç & Dijital İmza Denetçisi", "Process & X.509 Signature Auditor", "\uEA18", TrSec, EnSec, "imza x509 sha256 unsigned process signature");
        AddEntry("CryptoVault", "AES-256 Dosya Kasası & Çoklu Hash", "AES-256 Vault (.fcvault) & Multi-Hash", "\uE8D7", TrSec, EnSec, "aes256 sifrele encrypt fcvault md5 sha256 sha512");
        AddEntry("Privacy", "Windows Gizlilik & Telemetri Kalkanı", "Windows Privacy & Telemetry Shield", "\uE72E", TrSec, EnSec, "gizlilik privacy mru jumplist telemetri");
        AddEntry("ClipboardShield", "Akıllı Pano & Takip URL Temizleyici", "Smart Clipboard Privacy Shield", "\uE77F", TrSec, EnSec, "clipboard pano utm api key iban");
        AddEntry("BitLocker", "BitLocker & ISO/VHD Sanal Disk", "BitLocker & Mounted ISO/VHD", "\uEDA2", TrSec, EnSec, "bitlocker iso vhd vhdx mount");
        AddEntry("CredentialSsh", "Kimlik Kasası (cmdkey) & SSH", "Credential Vault (cmdkey) & SSH", "\uE8D7", TrSec, EnSec, "ssh credential cmdkey git key");
        AddEntry("UsbForensics", "USB Depolama Geçmişi (USBSTOR)", "USB Storage Forensics (USBSTOR)", "\uE88E", TrSec, EnSec, "usb usbstor adli analiz history");
        AddEntry("CrashLogs", "Mavi Ekran (BSOD) & Olay Günlüğü", "BSOD Crash Dumps & Event Logs", "\uE7BA", TrSec, EnSec, "bsod dmp eventlog cokme crash");
        AddEntry("AiModelCleaner", "Yapay Zeka (AI / LLM) Model Avcısı", "Local AI / LLM Model & Cache Hunter", "\uE99A", TrDev, EnDev, "ollama huggingface lmstudio gguf safetensors ai llm chrome gemini nano optguideondevicemodel weights.bin claude rootfs vhdx");
        AddEntry("DockerWsl", "WSL2 & Docker Yöneticisi", "WSL2 & Docker Cleaner", "\uE756", TrDev, EnDev, "wsl wsl2 docker container prune");
        AddEntry("GitRepoCompressor", "Git Depo (.git) Sıkıştırıcı (git gc)", "Git Repository (.git) Compressor (git gc)", "\uE8B7", TrDev, EnDev, "git gc branch repo");
        AddEntry("DevCleaner", "VS Debug/Retail & DevCleaner", "DevCleaner (VS Debug/Retail, node_modules)", "\uE943", TrDev, EnDev, "visual studio debug release obj node_modules");
        AddEntry("PackageCaches", "NuGet / NPM & WSL2 / Claude VHDX Yöneticisi", "Package Caches & WSL2 / Claude VHDX", "\uE7B8", TrDev, EnDev, "nuget npm pip cargo gradle maven vhdx claude rootfs compact sikistir");
        AddEntry("SqliteOptimizer", "Tarayıcı & IDE SQLite Sıkıştırıcı", "Browser & IDE SQLite Vacuum Optimizer", "\uEE94", TrDev, EnDev, "sqlite veritabani wal chrome vscode cursor vacuum");
        AddEntry("BatchRenamer", "Toplu Dosya Adlandırıcı (PowerRename)", "Batch File Renamer (PowerRename)", "\uE8AC", TrDev, EnDev, "rename toplu adlandirma prefix suffix");
        AddEntry("Unlocker", "Kilitli Dosya Çözücü (File Unlocker)", "Locked File Unlocker (Restart Manager)", "\uE785", TrDev, EnDev, "kilitli dosya locked unlocker restart manager");
        AddEntry("PathDoctor", "PATH Doktoru & Kırık Kısayollar", "Environment PATH & Shortcut Doctor", "\uE950", TrDev, EnDev, "path ortam degiskeni lnk kisayol shortcut");
        AddEntry("TasksAndTree", "Zamanlanmış Görevler & Klasör Ağacı", "Task Scheduler & Folder Size Tree", "\uE823", TrDev, EnDev, "task scheduler klasor boyutu folder size");
        AddEntry("BrowserExtensions", "Tarayıcı Eklentileri Denetçisi", "Browser Extensions Auditor", "\uEA86", TrDev, EnDev, "extension eklenti chrome edge browser");
        AddEntry("DriversUpdates", "KB Güncellemeler & Sürücü Deposu", "Windows Updates (KB) & DriverStore", "\uE777", TrDev, EnDev, "hotfix kb driverstore pnputil drivers");
        AddEntry("Settings", "Tema, Dil, Güncelleme, Beyaz Liste & Raporlar", "Settings, Themes, Language, Portable & Bundles", "\uE713", TrSet, EnSet, "tema theme dil language tr en guncelle update tray portable support zip whitelist ayarlar rapor settings");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
