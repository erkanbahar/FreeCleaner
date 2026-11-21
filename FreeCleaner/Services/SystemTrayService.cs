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
    private bool _disposed;

    public SystemTrayService(
        Action showWindowAction,
        Action optimizeRamAction,
        Action runHealthCheckAction,
        Action analyzeJunkAction,
        Action exitAppAction)
    {
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("🖥️ FreeCleaner Pro'yu Göster", null, (_, _) => InvokeOnUi(showWindowAction));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("🧠 Hızlı RAM Boşalt", null, (_, _) => InvokeOnUi(optimizeRamAction));
        _menu.Items.Add("🩺 Tek Tıkla Sağlık Kontrolü", null, (_, _) => InvokeOnUi(runHealthCheckAction));
        _menu.Items.Add("🔍 Akıllı Temizlik Analizi", null, (_, _) => InvokeOnUi(analyzeJunkAction));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("🚪 Çıkış", null, (_, _) => InvokeOnUi(exitAppAction));

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "FreeCleaner Pro (.NET 10) — Sistem Bakım & Güvenlik",
            Icon = LoadTrayIcon(),
            ContextMenuStrip = _menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => InvokeOnUi(showWindowAction);
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

    public static string FormatTrayTooltip(double cpuPercent, double ramPercent, double freeDiskGb)
    {
        double clampedCpu = Math.Clamp(cpuPercent, 0, 100);
        double clampedRam = Math.Clamp(ramPercent, 0, 100);
        double safeFreeGb = Math.Max(0, freeDiskGb);

        string formatted = $"FreeCleaner | CPU %{clampedCpu:F0} | RAM %{clampedRam:F0} | Boş {safeFreeGb:F0}GB";
        if (formatted.Length > 63)
            formatted = formatted[..63];

        return formatted;
    }

    public void UpdateStatusTooltip(double cpuPercent, double ramPercent, double freeDiskGb)
    {
        if (_disposed)
            return;

        string tooltip = FormatTrayTooltip(cpuPercent, ramPercent, freeDiskGb);
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
