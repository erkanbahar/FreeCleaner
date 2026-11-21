using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FreeCleaner.Models;


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

    public required string IpAddress { get; set; }
    public required string Hostname { get; set; }
    public required string Comment { get; set; }
    public string OriginalIpAddress { get; set; } = string.Empty;
    public string OriginalHostname { get; set; } = string.Empty;

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

    public void MarkSaved()
    {
        OriginalIpAddress = IpAddress;
        OriginalHostname = Hostname;
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

public class WifiProfileAndDnsItem
{
    public required string ItemCategory { get; init; } // Kayıtlı Wi-Fi Ağı veya DNS Ping Kıyaslaması
    public required string NameOrSsid { get; init; }
    public required string SecurityOrIp { get; init; }
    public required string PasswordOrLatency { get; init; }
    public required string StatusBadge { get; init; }
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

public class BenchmarkDriveOption
{
    public required string DriveRoot { get; init; }
    public required string DisplayTitle { get; init; }
    public override string ToString() => DisplayTitle;
}


// ==================== 7TH GENERATION SYSTEM, SECURITY & POWER UTILITIES MODELS ====================

public class NetworkAdapterDetailItem
{
    public required string AdapterName { get; init; }
    public required string Description { get; init; }
    public required string StatusBadge { get; init; }
    public required string MacAddress { get; init; }
    public required string IpAndGateway { get; init; }
    public required string DnsAndSpeed { get; init; }
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

public class TreemapNodeItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required long SizeBytes { get; init; }
    public required bool IsDirectory { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required string FillHex { get; init; }
    public required double SharePercent { get; init; }
    public string AdvisorBadge { get; init; } = string.Empty;
    public string AdvisorSummary { get; init; } = string.Empty;
    public bool CanSafeDelete { get; init; } = true;

    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);
    public string PercentText => $"{SharePercent:F1}%";
    public string TypeBadge => IsDirectory ? "📁 Klasör" : "📄 Dosya";
    public bool ShowDetailedLabel => Width >= 88 && Height >= 40;
    public bool ShowCompactLabel => Width >= 48 && Height >= 22;
    public string ToolTipText => string.IsNullOrWhiteSpace(AdvisorSummary)
        ? $"{TypeBadge}: {Name}\nBoyut: {FormattedSize} ({PercentText})\nYol: {FullPath}"
        : $"{TypeBadge}: {Name}\nBoyut: {FormattedSize} ({PercentText})\nDanışman: {AdvisorBadge}\n{AdvisorSummary}\nYol: {FullPath}";
}

public class DeepSpaceItem : INotifyPropertyChanged
{
    private string _statusBadge = string.Empty;

    public required string ItemId { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required long SizeBytes { get; init; }
    public required string ActionLabel { get; init; }
    public required string Description { get; init; }
    public required string TargetPath { get; init; }
    public required bool CanExecuteAction { get; init; }

    public string FormattedSize => SizeBytes > 0 ? CleanerRule.FormatBytes(SizeBytes) : "-";

    public string StatusBadge
    {
        get => _statusBadge;
        set
        {
            if (_statusBadge != value)
            {
                _statusBadge = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record CleanupHistoryEntry(
    DateTime TimestampUtc,
    string ModuleName,
    long FreedBytes,
    int ItemsProcessed);

public sealed record CleanupHistoryBarItem(
    string DayLabel,
    long FreedBytes,
    double BarHeight,
    string FormattedSize,
    string ToolTipText);
