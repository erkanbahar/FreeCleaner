using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
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

public class MainViewModel : INotifyPropertyChanged
{
    private readonly CleanerEngine _engine = new();
    private readonly DispatcherTimer _autoMonitorTimer;
    private static readonly string ActivityLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FreeCleaner",
        "activity.log");
    private CancellationTokenSource? _cts;

    private string _activeSection = "All";
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
    public ObservableCollection<string> ExcludedPaths { get; } = [];
    public ObservableCollection<string> ActivityLogs { get; } = [];

    public ICollectionView FilteredRulesView { get; }
    public ICollectionView FilteredFilesView { get; }
    public ICollectionView FilteredInstalledAppsView { get; }

    public int[] AvailableSizeThresholdsMb { get; } = [10, 25, 50, 100, 250, 500, 1024];
    public int[] AvailableShredPasses { get; } = [1, 3, 7];
    public int[] AvailableInstallerAgeDays { get; } = [0, 7, 14, 30, 60, 90];
    public int[] AvailableMaintenanceIntervalsHours { get; } = [24, 48, 168];
    public int[] AvailableBenchmarkBlockSizesMb { get; } = [64, 128, 256, 512, 1024];
    public string[] AvailableRenameCaseModes { get; } = ["Değiştirme (Olduğu Gibi)", "küçük harf (lowercase)", "BÜYÜK HARF (UPPERCASE)", "Baş Harfler Büyük (Title Case)"];
    public string[] AvailableRiskFilters { get; } = ["Tümü", "Düşük", "Orta", "Yüksek"];

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

    public bool IsAdmin => UltimateSystemSuiteService.IsRunningAsAdministrator();
    public bool IsNotAdmin => !IsAdmin;
    public string AdminStatusBadge => IsAdmin ? "Yönetici Modu (Admin)" : "Standart Kullanıcı Modu";

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
        _lastScheduledMaintenanceUtc = DateTime.UtcNow;
        LoadActivityLogs();

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

        AddLog("FreeCleaner Pro Flagship (.NET 10) başlatıldı: Tüm 51 uzman modül kullanıma hazır.");
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
                OnPropertyChanged(nameof(IsAllSection));
                OnPropertyChanged(nameof(IsSystemSection));
                OnPropertyChanged(nameof(IsBrowsersSection));
                OnPropertyChanged(nameof(IsAppsSection));
                OnPropertyChanged(nameof(IsCleanerViewVisible));
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
            }
        }
    }

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

    public string SectionTitle => ActiveSection switch
    {
        "System" => "Sistem Temizleyici (Temp, Loglar & Geri Dönüşüm)",
        "Browsers" => "Web Tarayıcıları (Chrome, Edge, Brave, Firefox)",
        "Apps" => "Uygulama & Geliştirici Önbellekleri",
        "Startup" => "Başlangıç (Startup) Yöneticisi",
        "Registry" => "Kayıt Defteri (Registry) Taraması & Yedekleme",
        "DiskAnalyzer" => "Büyük Dosya & Kopya (Duplicate) Bulucu",
        "Uninstaller" => "Yüklü Programlar & Yetim Kalıntı Klasör Temizleyici",
        "Booster" => "RAM Boşaltıcı, Ağ (DNS) & Sistem Hızlandırıcı",
        "DevCleaner" => "DevCleaner & Gamer (VS Debug/Retail, node_modules, Shader)",
        "Shredder" => "Güvenli Dosya Öğütücü (DoD 5220.22-M) & Boş Alan Silici",
        "Privacy" => "Windows Gizlilik & Telemetri Kalkanı (MRU, JumpList, Pano)",
        "Network" => "Canlı Ağ Bağlantıları (TCP/UDP) & Hosts Yöneticisi",
        "TasksAndTree" => "Zamanlanmış Görevler (Task Scheduler) & Klasör Ağacı",
        "Downloads" => "İndirilenler (Downloads) Asistanı & Boş Klasör Temizleyici",
        "ContextMenu" => "Sağ Tık Bağlam Menüsü (Context Menu) Yöneticisi",
        "Services" => "Windows Servisleri & Telemetri Optimizasyon Yöneticisi",
        "SmartPower" => "Disk Sağlığı (S.M.A.R.T.), Pil Raporu & Oyun Modu",
        "RepairCenter" => "Sistem Onarım Merkezi (SFC, DISM, FontCache & Windows.old)",
        "Unlocker" => "Kilitli Dosya Çözücü (File Unlocker - Win32 Restart Manager)",
        "BrowserExtensions" => "Tarayıcı Eklentileri (Extensions) Denetçisi",
        "DriversUpdates" => "Windows Güncelleme Geçmişi (KB) & Sürücü Deposu (DriverStore)",
        "PathDoctor" => "Ortam Değişkenleri (PATH) Doktoru & Kırık Kısayol (.lnk) Onarıcı",
        "CrashLogs" => "Mavi Ekran (BSOD), Çökme Dökümleri (.dmp) & Olay Günlüğü Analizcisi",
        "WifiDns" => "Kayıtlı Wi-Fi Şifre Görüntüleyici & Canlı DNS Ping Kıyaslayıcı",
        "PackageCaches" => "WSL2 / Docker VHDX & Küresel Paket Önbelleği (NuGet, NPM, Pip, Gradle) Kurtarıcısı",
        "RestoreGodMode" => "Sistem Geri Yükleme Noktası (Restore Point) & Windows God Mode Merkezi",
        "FileAssoc" => "Dosya Uzantısı & Varsayılan Program (UserChoice) Doktoru",
        "PortHunter" => "Geliştirici Port Avcısı (Port Killer - Hangi Portu Kim Kullanıyor?)",
        "Bloatware" => "Windows 10/11 Önyüklü Bloatware (UWP Appx) Kaldırıcı",
        "DiskBench" => "Canlı SSD / NVMe Disk Okuma & Yazma Hız Testi (Benchmark)",
        "HardwareSpecs" => "Derin Donanım Envanteri, BIOS & OEM Windows Lisans Anahtarı",
        "UsbForensics" => "USB Depolama Geçmişi (USBSTOR Adli Analiz) & İz Temizleyici",
        "ProcessSecurity" => "Süreç & Dijital İmza Denetçisi (Unsigned Binary & SHA-256 Hunter)",
        "BatchRenamer" => "Toplu Dosya Yeniden Adlandırıcı (PowerRename & Geri Alma Merkezi)",
        "NetworkAdapterDoctor" => "Ağ Adaptörü, IP/MAC & TCP/IP Winsock Onarım Doktoru",
        "FontManager" => "Sistem & Kullanıcı Font (Yazı Tipi) Yöneticisi ve Canlı Önizleme",
        "ClipboardShield" => "Akıllı Pano (Clipboard) Kalkanı & Takip URL Temizleyici",
        "AiModelCleaner" => "Yerel Yapay Zeka (AI / LLM) Model & Önbellek Avcısı (Ollama, HuggingFace, LM Studio)",
        "FirewallBlocker" => "Windows Güvenlik Duvarı (Firewall) Denetçisi & Tek Tıkla Uygulama İnternet Kesici",
        "CryptoVault" => "AES-256-GCM Dosya Şifreleme Kasası (.fcvault) & Çoklu Hash (MD5/SHA256/SHA512) Doğrulayıcı",
        "Win11Tweaker" => "Windows 11 İnce Ayar (Ultimate Tweaker) & Explorer Hızlandırıcı",
        "BitLocker" => "BitLocker Durum Denetçisi & Bağlı ISO / VHD Sanal Disk Yöneticisi",
        "WingetUpdater" => "Winget Toplu Program Güncelleyici (One-Click Software Updater)",
        "LanScanner" => "Yerel Ağ (LAN / Wi-Fi) Cihaz Radarı & ARP/Ping Tarayıcı",
        "SqliteOptimizer" => "Tarayıcı & IDE SQLite Veritabanı Sıkıştırıcı (Zero-Data-Loss Optimizer)",
        "CredentialSsh" => "Windows Kimlik Kasası (cmdkey) & SSH / Git Anahtar Denetçisi",
        "ProcessPriority" => "Canlı CPU Önceliği (Process Priority) & Verimlilik Modu (EcoQoS) Yöneticisi",
        "BootPerformance" => "Windows Açılış Süresi (Boot Time) & Önyükleme Darboğaz Analizcisi",
        "DockerWsl" => "WSL2 Linux Dağıtım Yöneticisi & Docker Konteyner / İmaj Temizleyici",
        "DefenderAuditor" => "Windows Defender Güvenlik Kalkanı & Gizli İstisna (ExclusionPath) Denetçisi",
        "MonitorGpu" => "Monitör EDID, Yenileme Hızı (Hz) & Ekran Kartı (GPU) Sıfırlama Doktoru",
        "GitRepoCompressor" => "Git Depoları (.git) Sıkıştırıcı (git gc) & Proje Durum Radarı",
        "Settings" => "Beyaz Liste (İstisnalar), Otomasyon & Raporlama",
        _ => "Akıllı Temizlik & Tüm Kategoriler"
    };

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
            if (changedRule.IsSelected && changedRule.RequiresWarning && !string.IsNullOrEmpty(changedRule.WarningMessage))
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
            if (selected && rule.RequiresWarning)
                continue;
            rule.IsSelected = selected;
        }
    }

    public void RefreshSystemMetrics()
    {
        SystemMetrics = SystemInfoService.GetMetrics();
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

        _cts = new CancellationTokenSource();
        try
        {
            var found = await RegistryCleanerService.ScanRegistryIssuesAsync(_cts.Token);
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
        StatusMessage = $"{BackupFiles.Count} yedek dosyası listelendi.";
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

        _cts = new CancellationTokenSource();
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
                _cts.Token);

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

        _cts = new CancellationTokenSource();
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
                _cts.Token);

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

        _cts = new CancellationTokenSource();
        try
        {
            if (InstalledApps.Count == 0)
            {
                var apps = await UninstallerAndOrphanService.GetInstalledAppsAsync();
                foreach (var a in apps) InstalledApps.Add(a);
            }

            var orphans = await UninstallerAndOrphanService.ScanOrphanFoldersAsync(InstalledApps, _cts.Token);
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
            var (htmlPath, jsonPath) = SystemBoosterAndReportService.ExportDiagnosticReports(
                SystemMetrics,
                AllRules,
                LastFreedBytes,
                LastDeletedFiles,
                ActivityLogs);

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
        ExclusionAndSettingsService.Save(new CleanerConfigData
        {
            ExcludedPaths = ExcludedPaths.ToList(),
            AutoMonitorEnabled = AutoMonitorEnabled,
            AutoMonitorThresholdMb = AutoMonitorThresholdMb,
            MinimizeToTrayOnClose = MinimizeToTrayOnClose,
            UseRecycleBinSafety = UseRecycleBinSafety,
            RiskFilter = RiskFilter,
            ScheduledMaintenanceEnabled = ScheduledMaintenanceEnabled,
            ScheduledMaintenanceIntervalHours = ScheduledMaintenanceIntervalHours,
            SelectedRuleIds = AllRules.Where(rule => rule.IsSelected).Select(rule => rule.Id).ToList()
        });
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
                StatusMessage = $"[OTOMATİK İZLEME] Sistemde {CleanerRule.FormatBytes(totalBytes)} gereksiz dosya birikti!";
                AddLog($"[OTOMATİK UYARI] Eşik ({AutoMonitorThresholdMb} MB) aşıldı: {CleanerRule.FormatBytes(totalBytes)} çöp tespit edildi.");
            }
        }
        catch
        {
            // Ignore background check error
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

    // ==================== 8. DEVCLEANER & GAMER CACHES ====================

    public string DevWorkspacePath
    {
        get => _devWorkspacePath;
        set
        {
            if (_devWorkspacePath != value)
            {
                _devWorkspacePath = value;
                OnPropertyChanged();
            }
        }
    }

    public bool VsIncludeDebug
    {
        get => _vsIncludeDebug;
        set
        {
            if (_vsIncludeDebug != value)
            {
                _vsIncludeDebug = value;
                OnPropertyChanged();
            }
        }
    }

    public bool VsIncludeRetailAndRelease
    {
        get => _vsIncludeRetailAndRelease;
        set
        {
            if (_vsIncludeRetailAndRelease != value)
            {
                _vsIncludeRetailAndRelease = value;
                OnPropertyChanged();
            }
        }
    }

    public bool VsIncludeObjAndVs
    {
        get => _vsIncludeObjAndVs;
        set
        {
            if (_vsIncludeObjAndVs != value)
            {
                _vsIncludeObjAndVs = value;
                OnPropertyChanged();
            }
        }
    }

    private async Task RunScanVsDebugRetailAsync(bool autoDeleteAfterScan)
    {
        IsBusy = true;
        ProgressPercent = 20;
        DevArtifactItems.Clear();

        StatusMessage = $"'{DevWorkspacePath}' içindeki Visual Studio projelerinin Debug / Retail / Release klasörleri taranıyor...";
        AddLog($"[VS TEMİZLEYİCİ] Visual Studio Debug & Retail klasörleri taranıyor: {DevWorkspacePath}");

        _cts = new CancellationTokenSource();
        var progress = new Progress<(string CurrentDir, int FoundCount)>(rep =>
        {
            CurrentOperationDetail = $"Bulunan VS Klasörü: {rep.FoundCount} • {rep.CurrentDir}";
        });

        try
        {
            var found = await DevAndGamerCleanerService.ScanVisualStudioDebugAndRetailAsync(
                DevWorkspacePath,
                VsIncludeDebug,
                VsIncludeRetailAndRelease,
                VsIncludeObjAndVs,
                progress,
                _cts.Token);

            foreach (var item in found)
                DevArtifactItems.Add(item);

            ProgressPercent = 100;
            long totalSize = found.Sum(x => x.SizeBytes);

            if (found.Count == 0)
            {
                StatusMessage = "Visual Studio Debug / Retail klasörü bulunamadı (Projeler zaten temiz).";
                AddLog("[VS TEMİZLEYİCİ] Silinecek Debug/Retail klasörü bulunamadı.");
                return;
            }

            StatusMessage = $"VS Temizleyici: {found.Count} adet Debug / Retail / obj klasörü bulundu ({CleanerRule.FormatBytes(totalSize)}).";
            AddLog($"[VS TEMİZLEYİCİ] {found.Count} VS çıktı klasörü ({CleanerRule.FormatBytes(totalSize)}) tespit edildi.");

            if (autoDeleteAfterScan)
            {
                int deleted = 0;
                long freed = 0;

                await Task.Run(() =>
                {
                    foreach (var item in found)
                    {
                        try
                        {
                            if (Directory.Exists(item.FullPath))
                            {
                                Directory.Delete(item.FullPath, recursive: true);
                                deleted++;
                                freed += item.SizeBytes;
                            }
                        }
                        catch
                        {
                            // Skip locked active build folder
                        }
                    }
                });

                DevArtifactItems.Clear();
                LastFreedBytes += freed;
                LastDeletedFiles += deleted;
                RefreshSystemMetrics();

                StatusMessage = $"VS Otomatik Temizlik Tamamlandı! {deleted} adet Debug/Retail klasörü silindi, {CleanerRule.FormatBytes(freed)} yer açıldı.";
                CurrentOperationDetail = $"Temizlenen kök dizin: {DevWorkspacePath}";
                AddLog($"[VS OTOMATİK SİLME] {deleted} adet Visual Studio Debug/Retail klasörü otomatik silindi ({CleanerRule.FormatBytes(freed)} kazanıldı).");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Visual Studio Debug/Retail taraması durduruldu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public int ShredPassesCount
    {
        get => _shredPassesCount;
        set
        {
            if (_shredPassesCount != value)
            {
                _shredPassesCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int FreeSpaceWipeMb
    {
        get => _freeSpaceWipeMb;
        set
        {
            if (_freeSpaceWipeMb != value)
            {
                _freeSpaceWipeMb = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewHostIp
    {
        get => _newHostIp;
        set
        {
            if (_newHostIp != value)
            {
                _newHostIp = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewHostDomain
    {
        get => _newHostDomain;
        set
        {
            if (_newHostDomain != value)
            {
                _newHostDomain = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewHostComment
    {
        get => _newHostComment;
        set
        {
            if (_newHostComment != value)
            {
                _newHostComment = value;
                OnPropertyChanged();
            }
        }
    }

    public string SpaceTreeTargetFolder
    {
        get => _spaceTreeTargetFolder;
        set
        {
            if (_spaceTreeTargetFolder != value)
            {
                _spaceTreeTargetFolder = value;
                OnPropertyChanged();
            }
        }
    }

    private void BrowseDevWorkspace()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Yazılım Projelerinin Bulunduğu Klasörü Seçin (node_modules, bin/obj, .venv taraması)",
            InitialDirectory = Directory.Exists(DevWorkspacePath) ? DevWorkspacePath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dlg.ShowDialog() == true)
        {
            DevWorkspacePath = dlg.FolderName;
        }
    }

    private async Task RunScanDevProjectsAsync()
    {
        IsBusy = true;
        ProgressPercent = 20;
        DevArtifactItems.Clear();
        StatusMessage = $"'{DevWorkspacePath}' içindeki yazılım projelerinin derleme/paket klasörleri taranıyor...";
        AddLog($"[DEVCLEANER] Proje klasörü taranıyor: {DevWorkspacePath}");

        _cts = new CancellationTokenSource();
        var progress = new Progress<(string CurrentDir, int FoundCount)>(rep =>
        {
            CurrentOperationDetail = $"Bulunan: {rep.FoundCount} • {rep.CurrentDir}";
        });

        try
        {
            var found = await DevAndGamerCleanerService.ScanWorkspaceArtifactsAsync(DevWorkspacePath, progress, _cts.Token);
            foreach (var item in found)
                DevArtifactItems.Add(item);

            ProgressPercent = 100;
            long totalSize = found.Sum(x => x.SizeBytes);
            StatusMessage = $"DevCleaner: {found.Count} derleme/paket klasörü bulundu (Toplam {CleanerRule.FormatBytes(totalSize)}).";
            AddLog($"[DEVCLEANER] {found.Count} klasör ({CleanerRule.FormatBytes(totalSize)}) tespit edildi.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "DevCleaner taraması iptal edildi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunScanGamerCachesAsync()
    {
        IsBusy = true;
        ProgressPercent = 30;
        DevArtifactItems.Clear();
        StatusMessage = "GPU Shader (NVIDIA/AMD), Steam, Epic, Pip, Yarn, Gradle ve Cargo önbellekleri taranıyor...";
        AddLog("[GAMER & GLOBAL CACHE] Oyun ve paket yöneticisi önbellekleri taranıyor...");

        _cts = new CancellationTokenSource();
        try
        {
            var found = await DevAndGamerCleanerService.ScanGlobalDevAndGamerCachesAsync(_cts.Token);
            foreach (var item in found)
                DevArtifactItems.Add(item);

            ProgressPercent = 100;
            long totalSize = found.Sum(x => x.SizeBytes);
            StatusMessage = $"Oyun & Paket Önbellekleri: {found.Count} hedef bulundu ({CleanerRule.FormatBytes(totalSize)}).";
            AddLog($"[GAMER & CACHE] {found.Count} önbellek hedefi ({CleanerRule.FormatBytes(totalSize)}) bulundu.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CleanSelectedDevArtifacts()
    {
        var selected = DevArtifactItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        long totalBytes = selected.Sum(x => x.SizeBytes);
        var confirm = MessageBox.Show(
            $"Seçili {selected.Count} adet geliştirici/oyun önbellek klasörü ({CleanerRule.FormatBytes(totalBytes)}) temizlenecek.\n\nOnaylıyor musunuz?",
            "DevCleaner & Gamer Temizlik Onayı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        long freed = 0;
        foreach (var item in selected)
        {
            try
            {
                if (Directory.Exists(item.FullPath))
                {
                    Directory.Delete(item.FullPath, recursive: true);
                    deleted++;
                    freed += item.SizeBytes;
                    DevArtifactItems.Remove(item);
                }
            }
            catch
            {
                // Ignore locked files inside active shader cache
            }
        }

        LastFreedBytes += freed;
        LastDeletedFiles += deleted;
        RefreshSystemMetrics();
        StatusMessage = $"{deleted} geliştirici/oyun klasörü silindi ({CleanerRule.FormatBytes(freed)} alan açıldı).";
        AddLog($"[DEVCLEANER] {deleted} klasör temizlendi ({CleanerRule.FormatBytes(freed)}).");
    }

    // ==================== 9. FILE SHREDDER & FREE SPACE WIPER ====================

    private void AddFilesToShredQueue()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Kurtarılamaz Şekilde İmha Edilecek (Öğütülecek) Dosyaları Seçin",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
        {
            foreach (string file in dlg.FileNames)
            {
                if (ShredQueueItems.Any(x => x.FullPath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var fi = new FileInfo(file);
                ShredQueueItems.Add(new ShredTargetItem
                {
                    FullPath = file,
                    Name = fi.Name,
                    SizeBytes = fi.Exists ? fi.Length : 0,
                    IsDirectory = false
                });
            }
        }
    }

    private void AddFolderToShredQueue()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kurtarılamaz Şekilde İmha Edilecek Klasörü Seçin"
        };
        if (dlg.ShowDialog() == true && Directory.Exists(dlg.FolderName))
        {
            var di = new DirectoryInfo(dlg.FolderName);
            long size = di.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            ShredQueueItems.Add(new ShredTargetItem
            {
                FullPath = di.FullName,
                Name = $"[KLASÖR] {di.Name}",
                SizeBytes = size,
                IsDirectory = true
            });
        }
    }

    private async Task RunFileShredderAsync()
    {
        if (ShredQueueItems.Count == 0) return;

        var confirm = MessageBox.Show(
            $"DİKKAT! Listede bulunan {ShredQueueItems.Count} öğe ({ShredPassesCount} geçişli DoD 5220.22-M algoritmasıyla) üzerine yazılarak KALICI OLARAK İMHA EDİLECEKTİR!\n\n" +
            "Bu işlem sonrasında dosyalar hiçbir veri kurtarma yazılımıyla geri getirilemez.\n\nDevam etmek istiyor musunuz?",
            "Güvenli Dosya Öğütücü - Kalıcı İmha Onayı",
            MessageBoxButton.YesNo,
            MessageBoxImage.Stop);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();

        var progress = new Progress<(int Percent, string CurrentItem)>(rep =>
        {
            ProgressPercent = rep.Percent;
            CurrentOperationDetail = $"Öğütülüyor ({ShredPassesCount} geçiş): {rep.CurrentItem}";
        });

        try
        {
            var (shreddedCount, bytes) = await FileShredderAndWiperService.ShredItemsAsync(
                ShredQueueItems,
                ShredPassesCount,
                progress,
                _cts.Token);

            LastFreedBytes += bytes;
            LastDeletedFiles += shreddedCount;
            StatusMessage = $"{shreddedCount} dosya kurtarılamaz şekilde imha edildi ({CleanerRule.FormatBytes(bytes)}).";
            AddLog($"[DOSYA ÖĞÜTÜCÜ] {shreddedCount} dosya ({ShredPassesCount} geçişli DoD) imha edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunFreeSpaceWiperAsync()
    {
        var confirm = MessageBox.Show(
            $"Boş disk sektörlerindeki eski silinmiş dosya kalıntılarını temizlemek için {FreeSpaceWipeMb} MB kriptografik sıfırlama işlemi uygulanacak.\n\nBaşlatılsın mı?",
            "Boş Disk Alanı Sıfırlayıcı (Free Space Wiper)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();

        var progress = new Progress<(int Percent, string Status)>(rep =>
        {
            ProgressPercent = rep.Percent;
            CurrentOperationDetail = rep.Status;
        });

        try
        {
            long wiped = await FileShredderAndWiperService.WipeDriveFreeSpaceSampleAsync(
                "C:\\",
                FreeSpaceWipeMb,
                progress,
                _cts.Token);

            StatusMessage = $"Boş alan sıfırlama tamamlandı ({CleanerRule.FormatBytes(wiped)} sektör temizlendi).";
            AddLog($"[BOŞ ALAN SİLİCİ] {CleanerRule.FormatBytes(wiped)} boş disk alanı kriptografik olarak sıfırlandı.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== 10. PRIVACY SHIELD ====================

    private async Task RunScanPrivacyTracesAsync()
    {
        IsBusy = true;
        StatusMessage = "Windows gizlilik ve etkinlik izleri (RunMRU, JumpList, Timeline, Pano) taranıyor...";
        try
        {
            await PrivacyAndNetworkShieldService.ScanPrivacyTracesAsync(PrivacyTraces);
            int totalTraces = PrivacyTraces.Sum(x => x.DetectedCount);
            StatusMessage = $"Gizlilik Taraması: {totalTraces} etkinlik ve geçmiş izi bulundu.";
            AddLog($"[GİZLİLİK KALKANI] {totalTraces} Windows kullanım izi tespit edildi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCleanPrivacyTracesAsync()
    {
        IsBusy = true;
        try
        {
            var (cleaned, bytes) = await PrivacyAndNetworkShieldService.CleanPrivacyTracesAsync(PrivacyTraces);
            LastFreedBytes += bytes;
            LastDeletedFiles += cleaned;
            StatusMessage = $"Gizlilik Kalkanı: {cleaned} etkinlik geçmişi/iz başarıyla temizlendi.";
            AddLog($"[GİZLİLİK KALKANI] {cleaned} Windows izi ve pano geçmişi temizlendi.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== 11. NETWORK CONNECTIONS & HOSTS ====================

    private async Task LoadActiveConnectionsAsync()
    {
        IsBusy = true;
        ActiveConnections.Clear();
        StatusMessage = "Canlı TCP/UDP ağ bağlantıları ve portlar listeleniyor...";
        try
        {
            var conns = await PrivacyAndNetworkShieldService.GetActiveNetworkConnectionsAsync();
            foreach (var c in conns)
                ActiveConnections.Add(c);
            StatusMessage = $"{ActiveConnections.Count} aktif TCP/UDP bağlantısı listelendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadHostsEntries()
    {
        HostsEntries.Clear();
        foreach (var h in PrivacyAndNetworkShieldService.LoadHostsFileEntries())
        {
            h.MarkSaved();
            HostsEntries.Add(h);
        }
    }

    private void AddNewHostEntry()
    {
        if (string.IsNullOrWhiteSpace(NewHostDomain)) return;
        if (PrivacyAndNetworkShieldService.AddHostsEntry(NewHostIp.Trim(), NewHostDomain.Trim(), NewHostComment.Trim(), out string msg))
        {
            NewHostDomain = string.Empty;
            LoadHostsEntries();
            StatusMessage = msg;
            AddLog($"[HOSTS] {msg}");
        }
        else
        {
            MessageBox.Show(msg, "Hosts Dosyası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveHostEntry(HostsEntryItem? item)
    {
        if (item is null) return;
        if (PrivacyAndNetworkShieldService.UpdateHostsEntry(item, out string msg))
        {
            StatusMessage = msg;
            AddLog($"[HOSTS] {msg}");
        }
        else
            MessageBox.Show(msg, "Hosts Dosyası", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DeleteHostEntry(HostsEntryItem? item)
    {
        if (item is null ||
            MessageBox.Show($"'{item.Hostname}' hosts kaydı silinecek. Devam edilsin mi?",
                "Hosts Kaydı Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (PrivacyAndNetworkShieldService.RemoveHostsEntry(item, out string msg))
        {
            HostsEntries.Remove(item);
            StatusMessage = msg;
            AddLog($"[HOSTS] {msg}");
        }
        else
            MessageBox.Show(msg, "Hosts Dosyası", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RestoreLatestHostsBackup()
    {
        string? backupPath = PrivacyAndNetworkShieldService.GetLatestHostsBackup();
        if (backupPath is null) return;

        if (MessageBox.Show(
                $"Son hosts yedeği geri yüklenecek:\n\n{backupPath}\n\nDevam edilsin mi?",
                "Hosts Yedeğini Geri Yükle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var result = PrivacyAndNetworkShieldService.RestoreHostsBackup(backupPath);
        StatusMessage = result.Message;
        AddLog($"[GERİ YÜKLEME] {result.Message}");
        if (result.Success)
            LoadHostsEntries();
        else
            MessageBox.Show(result.Message, "Hosts Geri Yükleme", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
