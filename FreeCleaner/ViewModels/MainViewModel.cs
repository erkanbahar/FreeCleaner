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
    private CancellationTokenSource? _cts;

    private string _activeSection = "All";
    private string _searchFilter = string.Empty;
    private string _statusMessage = "Sistem analizi için 'Analiz Et (Simülasyon)' butonuna tıklayın.";
    private string _currentOperationDetail = "Güvenli Mod Aktif: Analiz işlemi hiçbir dosyayı silmez.";
    private int _progressPercent;
    private bool _isBusy;
    private bool _hasAnalyzed;
    private bool _useRecycleBinSafety;
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

    // Collections
    public ObservableCollection<CleanerRule> AllRules { get; } = [];
    public ObservableCollection<ScannedFileItem> DiscoveredFiles { get; } = [];
    public ObservableCollection<StartupItemModel> StartupItems { get; } = [];
    public ObservableCollection<RegistryIssueItem> RegistryIssues { get; } = [];
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
    public ObservableCollection<string> ExcludedPaths { get; } = [];
    public ObservableCollection<string> ActivityLogs { get; } = [];

    public ICollectionView FilteredRulesView { get; }
    public ICollectionView FilteredFilesView { get; }
    public ICollectionView FilteredInstalledAppsView { get; }

    public int[] AvailableSizeThresholdsMb { get; } = [10, 25, 50, 100, 250, 500, 1024];
    public int[] AvailableShredPasses { get; } = [1, 3, 7];
    public int[] AvailableInstallerAgeDays { get; } = [0, 7, 14, 30, 60, 90];

    // Cleaner Commands
    public ICommand AnalyzeCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectSectionCommand { get; }
    public ICommand SelectAllRulesCommand { get; }
    public ICommand DeselectAllRulesCommand { get; }
    public ICommand OpenFileLocationCommand { get; }
    public ICommand RefreshMetricsCommand { get; }

    // Startup Manager Commands
    public ICommand RefreshStartupCommand { get; }
    public ICommand ToggleStartupItemCommand { get; }
    public ICommand DeleteStartupItemCommand { get; }

    // Registry Cleaner Commands
    public ICommand ScanRegistryCommand { get; }
    public ICommand BackupRegistryCommand { get; }
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
        _minimizeToTrayOnClose = savedConfig.MinimizeToTrayOnClose;

        foreach (var rule in CleanerEngine.GetDefaultRules())
        {
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

        // Background Auto-Monitor Timer (checks every 5 minutes if enabled)
        _autoMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _autoMonitorTimer.Tick += async (_, _) => await CheckAutoMonitorThresholdAsync();
        if (_autoMonitorEnabled)
            _autoMonitorTimer.Start();

        AddLog("FreeCleaner Enterprise (.NET 10) başlatıldı: Tüm 20 modül kullanıma hazır.");
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

        if (string.IsNullOrWhiteSpace(SearchFilter))
            return true;

        return item.FullPath.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
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

        IsBusy = true;
        ProgressPercent = 0;
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

            LastFreedBytes += summary.FreedBytes;
            LastDeletedFiles += summary.DeletedFilesCount;

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
            MinimizeToTrayOnClose = MinimizeToTrayOnClose
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
            HostsEntries.Add(h);
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
