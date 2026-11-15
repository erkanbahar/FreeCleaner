using System.ComponentModel;
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

public class LargeOrDuplicateFileItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime LastModified { get; init; }
    public string DuplicateGroupHash { get; init; } = string.Empty;
    public bool IsDuplicate { get; init; }

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
    public string FormattedDate => LastModified.ToString("dd.MM.yyyy HH:mm");
    public string TypeBadge => IsDuplicate ? $"Kopya ({DuplicateGroupHash[..8]})" : Extension.ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

public class DevArtifactFolderItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string ProjectName { get; init; }
    public required string ArtifactType { get; init; } // node_modules, bin, obj, .vs, __pycache__, .venv, target
    public required string FullPath { get; init; }
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
    public string FormattedDate => LastModified.ToString("dd.MM.yyyy HH:mm");

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class ShredTargetItem : INotifyPropertyChanged
{
    private string _status = "Hazır";

    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
    public required bool IsDirectory { get; init; }

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

    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class PrivacyTraceItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private int _detectedCount;
    private long _detectedBytes;
    private string _statusText = "Hazır";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }

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

    public int DetectedCount
    {
        get => _detectedCount;
        set
        {
            if (_detectedCount != value)
            {
                _detectedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public long DetectedBytes
    {
        get => _detectedBytes;
        set
        {
            if (_detectedBytes != value)
            {
                _detectedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedSize));
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

    public string FormattedSize => DetectedBytes > 0 ? CleanerRule.FormatBytes(DetectedBytes) : $"{DetectedCount} kayıt";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class ActiveConnectionItem
{
    public required string Protocol { get; init; }
    public required string LocalAddress { get; init; }
    public required string RemoteAddress { get; init; }
    public required string State { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
}

public class HostsEntryItem : INotifyPropertyChanged
{
    private bool _isEnabled = true;

    public required string IpAddress { get; init; }
    public required string Hostname { get; init; }
    public required string Comment { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

public class FolderSpaceBarItem
{
    public required string FolderName { get; init; }
    public required string FullPath { get; init; }
    public required long SizeBytes { get; init; }
    public required double PercentageOfParent { get; init; }
    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);
    public string PercentText => $"{PercentageOfParent:F1}%";
}

public class DownloadOrEmptyItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string ItemType { get; init; } // Kurulum (.exe/.msi), Arşiv (.zip/.iso), Boş Klasör
    public required long SizeBytes { get; init; }
    public required DateTime LastModified { get; init; }
    public required int AgeDays { get; init; }
    public required bool IsEmptyFolder { get; init; }

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

    public string FormattedSize => IsEmptyFolder ? "0 B (Boş)" : CleanerRule.FormatBytes(SizeBytes);
    public string FormattedAge => $"{AgeDays} gün önce ({LastModified:dd.MM.yyyy})";

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

public class LockingProcessItem
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string ApplicationType { get; init; }
    public required string LockedTargetPath { get; init; }
}

public class BrowserExtensionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string BrowserName { get; init; }
    public required string ProfileName { get; init; }
    public required string ExtensionId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public required string FullPath { get; init; }
    public required long SizeBytes { get; init; }

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

public class DriverOrHotfixItem
{
    public required string Category { get; init; } // Windows Hotfix (KB) veya DriverStore Paketi
    public required string Identifier { get; init; } // KB503... veya oem12.inf
    public required string ProviderOrType { get; init; }
    public required string DateOrVersion { get; init; }
    public required string Details { get; init; }
}

public class PathOrShortcutIssueItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string IssueType { get; init; } // Geçersiz PATH Klasörü, Tekrarlanan PATH, Kırık Kısayol (.lnk)
    public required string Scope { get; init; } // Kullanıcı PATH, Sistem PATH, Masaüstü, Başlat Menüsü
    public required string PathOrShortcutFile { get; init; }
    public required string MissingTarget { get; init; }
    public required bool IsShortcutFile { get; init; }
    public required bool IsUserEnvironment { get; init; }

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




public class CrashOrEventLogItem
{
    public required string Timestamp { get; init; }
    public required string EventType { get; init; } // Uygulama Çökmesi, Sistem Hatası, Mavi Ekran (BSOD), Çökme Dökümü (.dmp)
    public required string SourceOrApp { get; init; }
    public required string EventIdOrSize { get; init; }
    public required string MessageSummary { get; init; }
    public string DumpFilePath { get; init; } = string.Empty;
    public long DumpSizeBytes { get; init; }
}

public class WifiProfileAndDnsItem
{
    public required string ItemCategory { get; init; } // Kayıtlı Wi-Fi Ağı veya DNS Ping Kıyaslaması
    public required string NameOrSsid { get; init; }
    public required string SecurityOrIp { get; init; }
    public required string PasswordOrLatency { get; init; }
    public required string StatusBadge { get; init; }
}

public class GlobalPackageCacheItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string Ecosystem { get; init; } // NuGet, NPM, Pip, Yarn/PNPM, Gradle/Maven, WSL2 / Docker VHDX
    public required string CacheName { get; init; }
    public required string FolderPath { get; init; }
    public required long SizeBytes { get; init; }
    public required bool CanSafeDelete { get; init; }

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
    public string SafetyLabel => CanSafeDelete ? "Güvenle Temizlenebilir Önbellek" : "Sanal Disk (Salt Okunur Bilgi)";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class RestorePointItem
{
    public required string SequenceNumber { get; init; }
    public required string Description { get; init; }
    public required string CreationTime { get; init; }
    public required string RestorePointType { get; init; }
}

public class FileAssociationItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Extension { get; init; }
    public required string CurrentProgId { get; init; }
    public required string HandlerDescription { get; init; }
    public required string StatusText { get; init; }
    public required string RegistryKeyPath { get; init; }

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

public class ListeningPortItem
{
    public required string Protocol { get; init; }
    public required string LocalAddress { get; init; }
    public required int PortNumber { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string PortBadge { get; init; }
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

public class DiskBenchmarkResultItem
{
    public required string DriveName { get; init; }
    public required int TestBlockSizeMb { get; init; }
    public required double WriteSpeedMbps { get; init; }
    public required double ReadSpeedMbps { get; init; }
    public required string PerformanceGrade { get; init; }
    public required string TestedAt { get; init; }

    public string WriteFormatted => $"{WriteSpeedMbps:F1} MB/sn";
    public string ReadFormatted => $"{ReadSpeedMbps:F1} MB/sn";
}

public class HardwareSpecItem
{
    public required string ComponentCategory { get; init; }
    public required string ComponentName { get; init; }
    public required string PrimaryValue { get; init; }
    public required string SecondaryDetails { get; init; }
}

public class UsbHistoryItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string FriendlyName { get; init; }
    public required string VendorAndProduct { get; init; }
    public required string SerialNumber { get; init; }
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

public class BenchmarkDriveOption
{
    public required string DriveRoot { get; init; }
    public required string DisplayTitle { get; init; }
    public override string ToString() => DisplayTitle;
}


// ==================== 7TH GENERATION SYSTEM, SECURITY & POWER UTILITIES MODELS ====================

public class ProcessSecurityAuditItem
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string ExecutablePath { get; init; }
    public required string SignatureStatus { get; init; }
    public required string PublisherName { get; init; }
    public required string MemoryFormatted { get; init; }
    public required string Sha256Short { get; init; }
    public required bool IsUnsignedOrSuspicious { get; init; }
}

public class BatchRenamePreviewItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string OriginalName { get; init; }
    public required string NewName { get; init; }
    public required string Extension { get; init; }
    public required string OriginalFullPath { get; init; }
    public required string NewFullPath { get; init; }
    public required string ChangeStatus { get; init; }

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

public class NetworkAdapterDetailItem
{
    public required string AdapterName { get; init; }
    public required string Description { get; init; }
    public required string StatusBadge { get; init; }
    public required string MacAddress { get; init; }
    public required string IpAndGateway { get; init; }
    public required string DnsAndSpeed { get; init; }
}

public class SystemFontItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string FontFileName { get; init; }
    public required string FontFamilyGuess { get; init; }
    public required string SourceCategory { get; init; }
    public required string FullFilePath { get; init; }
    public required long SizeBytes { get; init; }
    public required bool IsUserFont { get; init; }
    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);
    public string SamplePreviewText => "Pijamalı hasta yağız şoföre çabucak güvendi • 0123456789";

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

public class ClipboardAuditEntryItem
{
    public required string CapturedAt { get; init; }
    public required string CategoryBadge { get; init; }
    public required string OriginalPreview { get; init; }
    public required string SanitizedContent { get; init; }
    public required string SecurityAdvice { get; init; }
}


// ==================== 8TH GENERATION AI CACHES, FIREWALL, CRYPTO VAULT, WIN11 TWEAKER & BITLOCKER MODELS ====================

public class AiModelCacheItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string AiFramework { get; init; }
    public required string ModelOrCacheName { get; init; }
    public required string FullPath { get; init; }
    public required long SizeBytes { get; init; }
    public required bool IsDirectory { get; init; }
    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);

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

public class FirewallBlockRuleItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string RuleName { get; init; }
    public required string Direction { get; init; }
    public required string ActionBadge { get; init; }
    public required string ProgramPath { get; init; }
    public required string ProfileText { get; init; }

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

public class FileHashAndVaultResultItem
{
    public required string AlgorithmOrOperation { get; init; }
    public required string FileName { get; init; }
    public required string HashOrOutputValue { get; init; }
    public required string VerificationStatus { get; init; }
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

public class VolumeSecurityAndMountItem
{
    public required string DriveRoot { get; init; }
    public required string VolumeLabel { get; init; }
    public required string FileSystemAndType { get; init; }
    public required string BitLockerStatus { get; init; }
    public required string CapacitySummary { get; init; }
    public required string BackingImagePath { get; init; }
    public required bool CanDismount { get; init; }
}


// ==================== 9TH GENERATION WINGET UPDATER, LAN RADAR, SQLITE OPTIMIZER, CREDENTIAL/SSH & CPU PRIORITY MODELS ====================

public class WingetUpgradeItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string PackageName { get; init; }
    public required string PackageId { get; init; }
    public required string InstalledVersion { get; init; }
    public required string AvailableVersion { get; init; }
    public required string SourceName { get; init; }

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

public class LanDeviceItem
{
    public required string IpAddress { get; init; }
    public required string MacAddress { get; init; }
    public required string Hostname { get; init; }
    public required string VendorName { get; init; }
    public required string PingStatus { get; init; }
    public required string DeviceRoleBadge { get; init; }
}

public class SqliteDatabaseItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string AppName { get; init; }
    public required string DatabaseName { get; init; }
    public required string FullFilePath { get; init; }
    public required long TotalSizeBytes { get; init; }
    public required long ReclaimableBytes { get; init; }
    public required string HealthDetails { get; init; }
    public string FormattedTotalSize => CleanerRule.FormatBytes(TotalSizeBytes);
    public string FormattedReclaimable => CleanerRule.FormatBytes(ReclaimableBytes);

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

public class CredentialOrSshItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Category { get; init; }
    public required string TargetOrKeyName { get; init; }
    public required string UserOrType { get; init; }
    public required string DetailsOrPublicKey { get; init; }
    public required bool CanDeleteFromVault { get; init; }

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

public class ProcessPriorityItem
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string PriorityBadge { get; init; }
    public required int ThreadCount { get; init; }
    public required string RamFormatted { get; init; }
    public required string CpuTimeFormatted { get; init; }
}
