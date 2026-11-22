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
    private string _name = string.Empty;
    private string _category = string.Empty;
    private string _description = string.Empty;
    private string? _warningMessage;
    private string? _languageCodeOverride;
    private bool _isSelected = true;
    private long _detectedSizeBytes;
    private int _detectedFileCount;
    private string _statusText = "Hazır";

    public required string Id { get; init; }

    public required string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public required string Category
    {
        get => _category;
        set
        {
            if (_category != value)
            {
                _category = value;
                OnPropertyChanged();
            }
        }
    }

    public required string Description
    {
        get => _description;
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged();
            }
        }
    }

    public required string IconGlyph { get; init; }
    public required RuleGroup Group { get; init; }
    public RiskLevel RiskLevel { get => Risk; init => Risk = value; }
    public bool IsRecycleBin { get; init; }
    public bool RequiresWarning { get; init; }

    public string? WarningMessage
    {
        get => _warningMessage;
        set
        {
            if (_warningMessage != value)
            {
                _warningMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public RiskLevel Risk { get; init; } = RiskLevel.Medium;

    public string RiskLabel
    {
        get
        {
            bool isEn = Services.LocalizationService.NormalizeLanguageCode(
                _languageCodeOverride ?? Services.LocalizationService.CurrentLanguage) == "en-US";
            return Risk switch
            {
                RiskLevel.Low => isEn ? "Low" : "Düşük",
                RiskLevel.High => isEn ? "High" : "Yüksek",
                _ => isEn ? "Medium" : "Orta"
            };
        }
    }

    public void ApplyLocalizedMetadata(
        string name,
        string category,
        string description,
        string? warningMessage,
        string languageCode)
    {
        Name = name;
        Category = category;
        Description = description;
        WarningMessage = warningMessage;
        _languageCodeOverride = Services.LocalizationService.NormalizeLanguageCode(languageCode);

        bool isEn = _languageCodeOverride == "en-US";
        StatusText = StatusText switch
        {
            "Hazır" or "Ready" => isEn ? "Ready" : "Hazır",
            "Bekliyor..." or "Pending..." => isEn ? "Pending..." : "Bekliyor...",
            "Atlandı" or "Skipped" => isEn ? "Skipped" : "Atlandı",
            _ => StatusText
        };

        OnPropertyChanged(nameof(RiskLabel));
    }

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
