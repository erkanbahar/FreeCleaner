using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public partial class MainViewModel
{
    // ==================== 25. IN-APP TOASTS, SILENT STARTUP UPDATE CHECK & UNIFIED BACKUP RETENTION ====================

    public ToastNotificationItem? ActiveToast
    {
        get => _activeToast;
        private set
        {
            if (!ReferenceEquals(_activeToast, value))
            {
                _activeToast = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsToastVisible));
                OnPropertyChanged(nameof(ToastHasAction));
            }
        }
    }

    public bool IsToastVisible => ActiveToast is not null;

    public bool ToastHasAction => ActiveToast?.HasAction ?? false;

    public bool AutoCheckUpdatesOnStartup
    {
        get => _autoCheckUpdatesOnStartup;
        set
        {
            if (_autoCheckUpdatesOnStartup != value)
            {
                _autoCheckUpdatesOnStartup = value;
                OnPropertyChanged();
                SaveCurrentSettings();
            }
        }
    }

    public bool HasPendingUpdate
    {
        get => _hasPendingUpdate;
        private set
        {
            if (_hasPendingUpdate != value)
            {
                _hasPendingUpdate = value;
                OnPropertyChanged();
            }
        }
    }

    public void ShowToast(
        string title,
        string message,
        ToastSeverity severity = ToastSeverity.Info,
        string? actionLabel = null,
        string? actionKey = null,
        int durationSeconds = 6)
    {
        ActiveToast = ToastNotificationService.BuildToast(title, message, severity, actionLabel, actionKey);
        _toastDismissTimer.Stop();
        _toastDismissTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 2, 15));
        _toastDismissTimer.Start();
    }

    private void DismissToast()
    {
        _toastDismissTimer.Stop();
        ActiveToast = null;
    }

    private void ExecuteToastAction()
    {
        string? actionKey = ActiveToast?.ActionKey;
        DismissToast();
        if (string.IsNullOrWhiteSpace(actionKey))
            return;

        if (actionKey == "OPEN_SETTINGS_UPDATE")
        {
            ActiveSection = "Settings";
            return;
        }

        if (actionKey.StartsWith("OPEN_PATH:", StringComparison.Ordinal))
        {
            string target = actionKey["OPEN_PATH:".Length..];
            if (!string.IsNullOrWhiteSpace(target))
            {
                try
                {
                    if (target.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                    {
                        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                    }
                    else
                    {
                        OpenExplorerPath(target);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = ex.Message;
                }
            }
        }
    }

    public async Task RunSilentStartupUpdateCheckAsync()
    {
        if (!ToastNotificationService.ShouldRunSilentUpdateCheck(_autoCheckUpdatesOnStartup, _lastUpdateCheckDate))
            return;

        _lastUpdateCheckDate = ToastNotificationService.GetTodayDateKey();
        SaveCurrentSettings();

        try
        {
            UpdateCheckResult result = await AppUpdateService.CheckForUpdatesAsync();
            if (!result.Success)
                return;

            _latestReleaseUrl = result.ReleaseUrl;
            _latestDownloadAssetUrl = result.DownloadAssetUrl;
            _latestDownloadAssetName = result.DownloadAssetName;
            _latestExpectedSha256 = result.ExpectedSha256;
            UpdateStatusText = result.StatusMessage;
            OnPropertyChanged(nameof(CanDownloadUpdate));

            if (result.IsUpdateAvailable)
            {
                HasPendingUpdate = true;
                AddLog($"[GÜNCELLEME] {result.StatusMessage}");
                ShowToast(
                    LocalizationService.GetText("Yeni Sürüm Mevcut", "Update Available", _selectedLanguage),
                    result.StatusMessage,
                    ToastSeverity.Info,
                    actionLabel: LocalizationService.Get("Loc_ToastOpenUpdatesBtn", _selectedLanguage),
                    actionKey: "OPEN_SETTINGS_UPDATE",
                    durationSeconds: 9);
            }
        }
        catch
        {
            // Startup update check is strictly silent and best-effort.
        }
    }

    private void DeleteSelectedBackups()
    {
        List<BackupFileItem> selected = BackupFiles.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var confirm = MessageBox.Show(
            LocalizationService.GetText(
                $"{selected.Count} yedek dosyası kalıcı olarak silinecek. Devam edilsin mi?",
                $"Delete {selected.Count} selected backup files permanently?",
                _selectedLanguage),
            LocalizationService.GetText("Yedekleri Sil", "Delete Backups", _selectedLanguage),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        var (deletedCount, _, message) = BackupManagerService.DeleteBackups(selected);
        StatusMessage = message;
        AddLog($"[YEDEKLEME] {message}");
        LoadBackups();
        if (deletedCount > 0)
            ShowToast("FreeCleaner Pro", message, ToastSeverity.Success);
    }

    private void PruneOldBackups()
    {
        var (deletedCount, _, message) = BackupManagerService.PruneOldBackups(keepLatestPerType: 10, maxAgeDays: 30);
        StatusMessage = message;
        AddLog($"[YEDEK BUDAMA] {message}");
        LoadBackups();
        ShowToast(
            "FreeCleaner Pro",
            message,
            deletedCount > 0 ? ToastSeverity.Success : ToastSeverity.Info);
    }

    private AsyncRelayCommand CreateAsyncCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
        => new(executeAsync, canExecute, HandleUnhandledCommandException);

    private void HandleUnhandledCommandException(Exception ex)
    {
        ErrorLogService.Log("Komut Hatası", ex);
        IsBusy = false;
        string msg = LocalizationService.GetText(
            $"İşlem beklenmeyen bir hatayla karşılaştı: {ex.Message}",
            $"Operation encountered an unexpected error: {ex.Message}",
            _selectedLanguage);
        StatusMessage = msg;
        AddLog($"[HATA] {ex.Message}");
        ShowToast(
            LocalizationService.GetText("İşlem Hatası", "Operation Error", _selectedLanguage),
            msg,
            ToastSeverity.Error);
    }

    private CancellationToken ResetCancellationTokenSource()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _autoMonitorTimer.Stop();
        _liveTelemetryTimer.Stop();
        _trayWatcherTimer.Stop();
        _toastDismissTimer.Stop();

        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Ignore cancellation errors during shutdown.
        }

        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}
