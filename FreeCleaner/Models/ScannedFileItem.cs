using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FreeCleaner.Models;

public class ScannedFileItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _status = "Bekliyor";

    public required string RuleId { get; init; }
    public required string RuleName { get; init; }
    public required string Category { get; init; }
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime LastModified { get; init; }
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;
    public bool IsRecycleBinItem { get; init; }

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
    public string FormattedDate => LastModified.ToString("dd.MM.yyyy HH:mm");
    public string RiskLabel => RiskLevel switch
    {
        RiskLevel.Low => "Düşük",
        RiskLevel.High => "Yüksek",
        _ => "Orta"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class CleanSummaryResult
{
    public int DeletedFilesCount { get; set; }
    public int SkippedFilesCount { get; set; }
    public long FreedBytes { get; set; }
    public TimeSpan Elapsed { get; set; }
    public List<string> Logs { get; set; } = [];
}
