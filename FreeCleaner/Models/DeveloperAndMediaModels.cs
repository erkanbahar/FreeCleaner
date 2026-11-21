using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FreeCleaner.Models;


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
    public bool IsVhdxVirtualDisk => FolderPath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);
    public string SafetyLabel => CanSafeDelete
        ? "Güvenle Temizlenebilir Önbellek"
        : IsVhdxVirtualDisk
            ? "Sanal Disk (Sıkıştırılabilir .vhdx)"
            : "Sanal Disk (Salt Okunur Bilgi)";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

public class WingetUpgradeItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _upgradeStatus = "⏳ Güncellemeye Hazır";

    public required string PackageName { get; init; }
    public required string PackageId { get; init; }
    public required string InstalledVersion { get; init; }
    public required string AvailableVersion { get; init; }
    public required string SourceName { get; init; }

    public string UpgradeStatus
    {
        get => _upgradeStatus;
        set
        {
            if (_upgradeStatus != value)
            {
                _upgradeStatus = value;
                OnPropertyChanged();
            }
        }
    }

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

public class DockerOrWslItem
{
    public required string PlatformType { get; init; }
    public required string NameOrTag { get; init; }
    public required string StateOrStatus { get; init; }
    public required string VersionOrSize { get; init; }
    public required string Details { get; init; }
}

public class GitRepoCompressionItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string RepoName { get; init; }
    public required string RepoRootPath { get; init; }
    public required string ActiveBranch { get; init; }
    public required string WorkingTreeStatus { get; init; }
    public required long GitDirSizeBytes { get; init; }
    public string FormattedGitSize => CleanerRule.FormatBytes(GitDirSizeBytes);

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


// ==================== FLAGSHIP DASHBOARD, TELEMETRY SPARKLINE & COMMAND PALETTE MODELS ====================

public class PhotoMetadataItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _statusText = "⏳ Taranıyor / Hazır";

    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required long SizeBytes { get; init; }
    public required bool HasGpsData { get; init; }
    public required string CameraInfo { get; init; }
    public required string DateTaken { get; init; }
    public required string MetadataSummary { get; init; }
    public required int TagCount { get; init; }

    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);

    public string GpsBadge => HasGpsData
        ? "📍 GPS VAR (Risk)"
        : TagCount > 0
            ? $"📷 EXIF Var ({TagCount})"
            : "✅ Temiz";

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


// ==================== 12TH GENERATION SMART DISK ADVISOR, LIFETIME HISTORY, DEEP SPACE RECOVERY & DHASH PHOTO FINDER ====================

public class SimilarPhotoItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _statusText = "Hazır";

    public required int GroupId { get; init; }
    public required string GroupLabel { get; init; }
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required long SizeBytes { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string SimilarityText { get; init; }
    public required string RoleBadge { get; init; }
    public required int HammingDistance { get; init; }

    public string ResolutionText => $"{Width}x{Height}";
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
