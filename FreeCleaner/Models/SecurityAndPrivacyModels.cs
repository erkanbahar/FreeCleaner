using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FreeCleaner.Models;


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

public class ClipboardAuditEntryItem
{
    public required string CapturedAt { get; init; }
    public required string CategoryBadge { get; init; }
    public required string OriginalPreview { get; init; }
    public required string SanitizedContent { get; init; }
    public required string SecurityAdvice { get; init; }
}


// ==================== 8TH GENERATION AI CACHES, FIREWALL, CRYPTO VAULT, WIN11 TWEAKER & BITLOCKER MODELS ====================

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

public class DefenderSecurityAuditItem
{
    public required string AuditCategory { get; init; }
    public required string SettingOrExclusionName { get; init; }
    public required string StatusBadge { get; init; }
    public required string SecurityNotes { get; init; }
}
