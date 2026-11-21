using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FreeCleaner.Models;

public enum RuleGroup
{
    System,
    Browsers,
    DeveloperAndApps
}

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public class CleanerRule : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private long _detectedSizeBytes;
    private int _detectedFileCount;
    private string _statusText = "Hazır";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string IconGlyph { get; init; }
    public required RuleGroup Group { get; init; }
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;
    public bool IsRecycleBin { get; init; }
    public bool RequiresWarning { get; init; }
    public string? WarningMessage { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Medium;
    public string RiskLabel => Risk switch
    {
        RiskLevel.Low => "Düşük",
        RiskLevel.High => "Yüksek",
        _ => "Orta"
    };

    /// <summary>
    /// Directories to scan along with optional search patterns (default "*").
    /// </summary>
    public List<ScanTarget> Targets { get; init; } = [];

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

    public long DetectedSizeBytes
    {
        get => _detectedSizeBytes;
        set
        {
            if (_detectedSizeBytes != value)
            {
                _detectedSizeBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedSize));
            }
        }
    }

    public int DetectedFileCount
    {
        get => _detectedFileCount;
        set
        {
            if (_detectedFileCount != value)
            {
                _detectedFileCount = value;
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

    public string FormattedSize => FormatBytes(DetectedSizeBytes);
    public string RiskLabel => RiskLevel switch
    {
        RiskLevel.Low => "Düşük risk",
        RiskLevel.High => "Yüksek risk",
        _ => "Orta risk"
    };

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public record ScanTarget(
    string BasePath,
    string SearchPattern = "*",
    bool Recursive = true,
    string? SubFolderPattern = null);
