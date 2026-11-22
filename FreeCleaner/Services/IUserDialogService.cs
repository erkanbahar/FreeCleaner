using System.Windows;
using FreeCleaner.Dialogs;

namespace FreeCleaner.Services;

public enum AppDialogIcon
{
    Information,
    Question,
    Warning,
    Error
}

public enum AppDialogButtons
{
    OK,
    YesNo,
    YesNoCancel
}

public enum AppDialogResult
{
    None,
    OK,
    Yes,
    No,
    Cancel
}

/// <summary>
/// Abstracts confirmation and notification dialogs away from concrete Win32 MessageBox calls
/// so themed WPF dialogs can run in production and deterministic stubs can be injected in tests.
/// </summary>
public interface IUserDialogService
{
    AppDialogResult Show(
        string message,
        string title,
        AppDialogButtons buttons = AppDialogButtons.OK,
        AppDialogIcon icon = AppDialogIcon.Information);
}

public sealed class WpfUserDialogService : IUserDialogService
{
    public AppDialogResult Show(
        string message,
        string title,
        AppDialogButtons buttons = AppDialogButtons.OK,
        AppDialogIcon icon = AppDialogIcon.Information)
    {
        Application? app = Application.Current;
        if (app is null)
            return ShowFallbackWin32MessageBox(message, title, buttons, icon);

        if (app.Dispatcher.CheckAccess())
            return ShowThemedDialogOnUiThread(app, message, title, buttons, icon);

        return app.Dispatcher.Invoke(() => ShowThemedDialogOnUiThread(app, message, title, buttons, icon));
    }

    private static AppDialogResult ShowThemedDialogOnUiThread(
        Application app,
        string message,
        string title,
        AppDialogButtons buttons,
        AppDialogIcon icon)
    {
        Window? owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded && w.IsVisible)
            ?? (app.MainWindow is { IsLoaded: true, IsVisible: true } main ? main : null);

        var dialog = new ThemedDialogWindow(message, title, buttons, icon);
        if (owner is not null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _ = dialog.ShowDialog();
        return dialog.Result;
    }

    private static AppDialogResult ShowFallbackWin32MessageBox(
        string message,
        string title,
        AppDialogButtons buttons,
        AppDialogIcon icon)
    {
        MessageBoxButton mbButtons = buttons switch
        {
            AppDialogButtons.YesNo => MessageBoxButton.YesNo,
            AppDialogButtons.YesNoCancel => MessageBoxButton.YesNoCancel,
            _ => MessageBoxButton.OK
        };

        MessageBoxImage mbImage = icon switch
        {
            AppDialogIcon.Question => MessageBoxImage.Question,
            AppDialogIcon.Warning => MessageBoxImage.Warning,
            AppDialogIcon.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Information
        };

        return MessageBox.Show(message, title, mbButtons, mbImage) switch
        {
            MessageBoxResult.OK => AppDialogResult.OK,
            MessageBoxResult.Yes => AppDialogResult.Yes,
            MessageBoxResult.No => AppDialogResult.No,
            MessageBoxResult.Cancel => AppDialogResult.Cancel,
            _ => AppDialogResult.None
        };
    }
}
