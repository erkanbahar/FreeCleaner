using System.Windows;

namespace FreeCleaner.Services;

public sealed record WindowBoundsState(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);

/// <summary>
/// Saves, clamps to active virtual monitor bounds, and restores main window geometry safely.
/// </summary>
public static class WindowPlacementService
{
    public static WindowBoundsState ClampToVirtualScreen(
        WindowBoundsState saved,
        double virtualLeft,
        double virtualTop,
        double virtualWidth,
        double virtualHeight,
        double minWidth = 1120,
        double minHeight = 700)
    {
        double safeVirtualWidth = Math.Max(400, virtualWidth);
        double safeVirtualHeight = Math.Max(300, virtualHeight);

        double effectiveMinW = Math.Min(minWidth, safeVirtualWidth);
        double effectiveMinH = Math.Min(minHeight, safeVirtualHeight);

        double clampedW = Math.Clamp(
            double.IsNaN(saved.Width) || double.IsInfinity(saved.Width) ? effectiveMinW : saved.Width,
            effectiveMinW,
            safeVirtualWidth);
        double clampedH = Math.Clamp(
            double.IsNaN(saved.Height) || double.IsInfinity(saved.Height) ? effectiveMinH : saved.Height,
            effectiveMinH,
            safeVirtualHeight);

        double maxLeft = virtualLeft + Math.Max(0, safeVirtualWidth - clampedW);
        double maxTop = virtualTop + Math.Max(0, safeVirtualHeight - clampedH);

        double safeLeft = double.IsNaN(saved.Left) || double.IsInfinity(saved.Left)
            ? virtualLeft
            : Math.Clamp(saved.Left, virtualLeft, maxLeft);
        double safeTop = double.IsNaN(saved.Top) || double.IsInfinity(saved.Top)
            ? virtualTop
            : Math.Clamp(saved.Top, virtualTop, maxTop);

        return new WindowBoundsState(safeLeft, safeTop, clampedW, clampedH, saved.IsMaximized);
    }

    public static void ApplySavedBounds(Window window, CleanerConfigData config, bool skipMaximized = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(config);

        if (config.WindowWidth is not double width ||
            config.WindowHeight is not double height ||
            width < 800 ||
            height < 500)
        {
            return;
        }

        double left = config.WindowLeft ?? 0;
        double top = config.WindowTop ?? 0;

        WindowBoundsState clamped = ClampToVirtualScreen(
            new WindowBoundsState(left, top, width, height, config.WindowMaximized),
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight,
            window.MinWidth > 0 ? window.MinWidth : 1120,
            window.MinHeight > 0 ? window.MinHeight : 700);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = clamped.Left;
        window.Top = clamped.Top;
        window.Width = clamped.Width;
        window.Height = clamped.Height;

        if (clamped.IsMaximized && !skipMaximized)
            window.WindowState = WindowState.Maximized;
    }

    public static WindowBoundsState CaptureBounds(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        bool isMaximized = window.WindowState == WindowState.Maximized;
        Rect rect = window.WindowState != WindowState.Normal &&
                    window.RestoreBounds != Rect.Empty &&
                    window.RestoreBounds.Width > 0 &&
                    window.RestoreBounds.Height > 0
            ? window.RestoreBounds
            : new Rect(window.Left, window.Top, window.Width, window.Height);

        double width = double.IsNaN(rect.Width) || rect.Width <= 0 ? window.ActualWidth : rect.Width;
        double height = double.IsNaN(rect.Height) || rect.Height <= 0 ? window.ActualHeight : rect.Height;

        return new WindowBoundsState(rect.Left, rect.Top, width, height, isMaximized);
    }
}
