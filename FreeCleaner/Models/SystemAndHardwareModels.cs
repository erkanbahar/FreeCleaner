using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FreeCleaner.Models;


public class StartupItemModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _statusText = "Aktif";

    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; } // HKCU Run, HKLM Run, Startup Folder
    public required string RegistryKeyPath { get; init; }
    public required bool IsCurrentUser { get; init; }
    public required bool IsFolderShortcut { get; init; }
    public required string ImpactLevel { get; init; } // Yüksek, Orta, Düşük

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                StatusText = _isEnabled ? "Etkin" : "Devre Dışı";
                OnPropertyChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class RegistryIssueItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string Category { get; init; }
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public required string ProblemDescription { get; init; }
    public required string MissingTarget { get; init; }
    public required bool IsCurrentUser { get; init; }
    public required string SubKeyPath { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class BackupFileItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string FilePath { get; init; }
    public required string BackupType { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required long SizeBytes { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);
    public string FormattedDate => CreatedAt.ToString("dd.MM.yyyy HH:mm:ss");

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class InstalledAppModel
{
    public required string DisplayName { get; init; }
    public required string Publisher { get; init; }
    public required string DisplayVersion { get; init; }
    public required string InstallDate { get; init; }
    public required long EstimatedSizeBytes { get; init; }
    public required string UninstallString { get; init; }
    public required string InstallLocation { get; init; }

    public string FormattedSize => EstimatedSizeBytes > 0 ? CleanerRule.FormatBytes(EstimatedSizeBytes) : "-";
}

public class OrphanAppFolderItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string FolderName { get; init; }
    public required string FullPath { get; init; }
    public required string LocationType { get; init; } // AppData\Roaming, AppData\Local, ProgramData
    public required long SizeBytes { get; init; }
    public required DateTime LastModified { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class ProcessMemoryItem
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required long WorkingSetBytes { get; init; }
    public string FormattedMemory => CleanerRule.FormatBytes(WorkingSetBytes);
}

public class ScheduledTaskItem : INotifyPropertyChanged
{
    private string _status;

    public required string TaskName { get; init; }
    public required string TaskPath { get; init; }
    public required string NextRunTime { get; init; }
    public required string Author { get; init; }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public ScheduledTaskItem(string status = "Ready")
    {
        _status = status;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class ContextMenuEntryItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Name { get; init; }
    public required string TargetScope { get; init; } // Tüm Dosyalar (*), Klasör, Klasör Arka Planı
    public required string HandlerValue { get; init; }
    public required string RegistrySubKeyPath { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class WindowsServiceOptItem : INotifyPropertyChanged
{
    private string _status = "Bilinmiyor";

    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string StartType { get; init; }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class PhysicalDiskHealthItem
{
    public required string FriendlyName { get; init; }
    public required string MediaType { get; init; } // NVMe, SSD, HDD
    public required string HealthStatus { get; init; } // Healthy, Warning, Unhealthy
    public required string OperationalStatus { get; init; }
    public required string SizeFormatted { get; init; }
    public required string BusType { get; init; }
}

public class PowerPlanItem : INotifyPropertyChanged
{
    private bool _isActive;

    public required string Guid { get; init; }
    public required string Name { get; init; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBadge));
            }
        }
    }

    public string StatusBadge => IsActive ? "AKTİF PLAN" : "Hazır";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class DriverOrHotfixItem
{
    public required string Category { get; init; } // Windows Hotfix (KB) veya DriverStore Paketi
    public required string Identifier { get; init; } // KB503... veya oem12.inf
    public required string ProviderOrType { get; init; }
    public required string DateOrVersion { get; init; }
    public required string Details { get; init; }
}

public class RestorePointItem
{
    public required string SequenceNumber { get; init; }
    public required string Description { get; init; }
    public required string CreationTime { get; init; }
    public required string RestorePointType { get; init; }
}

public class AppxBloatwareItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Name { get; init; }
    public required string PackageFullName { get; init; }
    public required string Version { get; init; }
    public required string CategoryBadge { get; init; }
    public required string InstallLocation { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class HardwareSpecItem
{
    public required string ComponentCategory { get; init; }
    public required string ComponentName { get; init; }
    public required string PrimaryValue { get; init; }
    public required string SecondaryDetails { get; init; }
}

public class WindowsTweakItem
{
    public required string TweakId { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required bool IsEnabled { get; init; }
    public required string CurrentStateText { get; init; }
}

public class ProcessPriorityItem
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string PriorityBadge { get; init; }
    public required int ThreadCount { get; init; }
    public required string RamFormatted { get; init; }
    public required string CpuTimeFormatted { get; init; }
}


// ==================== 10TH GENERATION BOOT TIME, DOCKER/WSL2, DEFENDER AUDITOR, MONITOR/HZ & GIT GC MODELS ====================

public class BootPerformanceMetricItem
{
    public required string MetricCategory { get; init; }
    public required string ComponentOrAppName { get; init; }
    public required string DurationOrValue { get; init; }
    public required string ImpactAssessment { get; init; }
}

public class MonitorAndDisplayItem
{
    public required string DisplayName { get; init; }
    public required string ResolutionAndDepth { get; init; }
    public required string RefreshRateInfo { get; init; }
    public required string EdidAndDriverInfo { get; init; }
    public required string OptimizationBadge { get; init; }
}

public class HealthCheckStepItem
{
    public required string Category { get; init; }
    public required string CheckName { get; init; }
    public required string StatusBadge { get; init; }
    public required string Details { get; init; }
}

public class NavShortcutItem
{
    public required string SectionKey { get; init; }
    public required string Title { get; init; }
    public required string IconGlyph { get; init; }
    public required string CategoryName { get; init; }
    public required string Keywords { get; init; }
}

public class LiveProcessMetricItem
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string RamFormatted { get; init; }
    public required int Threads { get; init; }
    public required string PriorityText { get; init; }
}


// ==================== 11TH GENERATION INTERACTIVE DISK TREEMAP & EXIF PRIVACY SCRUBBER MODELS ====================
