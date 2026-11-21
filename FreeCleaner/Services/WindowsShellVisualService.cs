using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FreeCleaner.Services;

/// <summary>
/// Applies native Windows 11 DWM rounded corner and dark/light frame attributes cleanly.
/// </summary>
public static class WindowsShellVisualService
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    public static void ApplyWindowChromePolish(Window window, string? themeName)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int cornerPref = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            int useDarkMode = !string.Equals(themeName, "FluentLight", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
        }
        catch
        {
            // Older Windows builds (Win10 < 19044) ignore DWMWA_WINDOW_CORNER_PREFERENCE safely.
        }
    }
}
