using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace FreeCleaner.Services;

/// <summary>
/// Manages the Windows System Tray (NotifyIcon) context menu, double-click restore, and balloon notifications.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripItem _showItem;
    private readonly Forms.ToolStripItem _ramItem;
    private readonly Forms.ToolStripItem _healthItem;
    private readonly Forms.ToolStripItem _analyzeItem;
    private readonly Forms.ToolStripItem _exitItem;
    private string _languageCode = LocalizationService.CurrentLanguage;
    private bool _disposed;

    public SystemTrayService(
        Action showWindowAction,
        Action optimizeRamAction,
        Action runHealthCheckAction,
        Action analyzeJunkAction,
        Action exitAppAction,
        Action? balloonClickedAction = null)
    {
        _menu = new Forms.ContextMenuStrip();
        _showItem = _menu.Items.Add(LocalizationService.Get("Loc_TrayShow", _languageCode), null, (_, _) => InvokeOnUi(showWindowAction));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _ramItem = _menu.Items.Add(LocalizationService.Get("Loc_TrayOptimizeRam", _languageCode), null, (_, _) => InvokeOnUi(optimizeRamAction));
        _healthItem = _menu.Items.Add(LocalizationService.Get("Loc_TrayHealthCheck", _languageCode), null, (_, _) => InvokeOnUi(runHealthCheckAction));
        _analyzeItem = _menu.Items.Add(LocalizationService.Get("Loc_TrayAnalyzeJunk", _languageCode), null, (_, _) => InvokeOnUi(analyzeJunkAction));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _exitItem = _menu.Items.Add(LocalizationService.Get("Loc_TrayExit", _languageCode), null, (_, _) => InvokeOnUi(exitAppAction));

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = LocalizationService.Get("Loc_TrayDefaultTip", _languageCode),
            Icon = LoadTrayIcon(),
            ContextMenuStrip = _menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => InvokeOnUi(showWindowAction);
        if (balloonClickedAction is not null)
            _notifyIcon.BalloonTipClicked += (_, _) => InvokeOnUi(balloonClickedAction);
    }

    public void RefreshLanguage(string? languageCode = null)
    {
        if (_disposed)
            return;

        _languageCode = LocalizationService.NormalizeLanguageCode(languageCode ?? LocalizationService.CurrentLanguage);
        InvokeOnUi(() =>
        {
            if (_disposed)
                return;

            _showItem.Text = LocalizationService.Get("Loc_TrayShow", _languageCode);
            _ramItem.Text = LocalizationService.Get("Loc_TrayOptimizeRam", _languageCode);
            _healthItem.Text = LocalizationService.Get("Loc_TrayHealthCheck", _languageCode);
            _analyzeItem.Text = LocalizationService.Get("Loc_TrayAnalyzeJunk", _languageCode);
            _exitItem.Text = LocalizationService.Get("Loc_TrayExit", _languageCode);
            try
            {
                string defaultTip = LocalizationService.Get("Loc_TrayDefaultTip", _languageCode);
                _notifyIcon.Text = defaultTip.Length > 63 ? defaultTip[..63] : defaultTip;
            }
            catch { }
        });
    }

    public void ShowNotification(string title, string message, bool isWarning = false)
    {
        if (_disposed)
            return;

        try
        {
            var icon = isWarning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(4500, title, message, icon);
        }
        catch { }
    }

    public static string FormatTrayTooltip(double cpuPercent, double ramPercent, double freeDiskGb, string? languageCode = null)
    {
        double clampedCpu = Math.Clamp(cpuPercent, 0, 100);
        double clampedRam = Math.Clamp(ramPercent, 0, 100);
        double safeFreeGb = Math.Max(0, freeDiskGb);

        bool isEn = LocalizationService.NormalizeLanguageCode(languageCode ?? LocalizationService.CurrentLanguage) == "en-US";
        string formatted = isEn
            ? $"FreeCleaner | CPU {clampedCpu:F0}% | RAM {clampedRam:F0}% | Free {safeFreeGb:F0}GB"
            : $"FreeCleaner | CPU %{clampedCpu:F0} | RAM %{clampedRam:F0} | Boş {safeFreeGb:F0}GB";

        if (formatted.Length > 63)
            formatted = formatted[..63];

        return formatted;
    }

    public void UpdateStatusTooltip(double cpuPercent, double ramPercent, double freeDiskGb)
    {
        if (_disposed)
            return;

        string tooltip = FormatTrayTooltip(cpuPercent, ramPercent, freeDiskGb, _languageCode);
        InvokeOnUi(() =>
        {
            if (!_disposed)
            {
                try
                {
                    _notifyIcon.Text = tooltip;
                }
                catch { }
            }
        });
    }

    private static void InvokeOnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
            dispatcher.Invoke(action);
        else
            action();
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (streamInfo?.Stream is { } s)
            {
                using (s)
                {
                    return new Drawing.Icon(s);
                }
            }
        }
        catch { }

        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
            {
                var associated = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (associated is not null)
                    return associated;
            }
        }
        catch { }

        return Drawing.SystemIcons.Shield;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch { }

        try
        {
            _menu.Dispose();
        }
        catch { }
    }
}
