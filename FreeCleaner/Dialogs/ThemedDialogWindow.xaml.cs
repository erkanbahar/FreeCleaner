using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using FreeCleaner.Services;

namespace FreeCleaner.Dialogs;

public partial class ThemedDialogWindow : Window
{
    private readonly AppDialogButtons _buttons;

    public AppDialogResult Result { get; private set; } = AppDialogResult.None;

    public ThemedDialogWindow(
        string message,
        string title,
        AppDialogButtons buttons = AppDialogButtons.OK,
        AppDialogIcon icon = AppDialogIcon.Information)
    {
        InitializeComponent();
        _buttons = buttons;

        string safeTitle = string.IsNullOrWhiteSpace(title) ? "FreeCleaner Pro" : title;
        Title = safeTitle;
        DialogTitleText.Text = safeTitle;
        DialogMessageText.Text = message ?? string.Empty;

        string lang = LocalizationService.CurrentLanguage;
        string okText = LocalizationService.Get("Loc_Dialog_OK", lang);
        string yesText = LocalizationService.Get("Loc_Dialog_Yes", lang);
        string noText = LocalizationService.Get("Loc_Dialog_No", lang);
        string cancelText = LocalizationService.Get("Loc_Dialog_Cancel", lang);

        AutomationProperties.SetName(this, safeTitle);
        AutomationProperties.SetName(HeaderCloseButton, cancelText);

        ConfigureIconAppearance(icon);
        ConfigureButtons(buttons, okText, yesText, noText, cancelText);

        Loaded += (_, _) => PrimaryActionButton.Focus();
    }

    private void ConfigureIconAppearance(AppDialogIcon icon)
    {
        (string glyph, string brushKey) = icon switch
        {
            AppDialogIcon.Question => ("\uE897", "AccentBlueBrush"),
            AppDialogIcon.Warning => ("\uE7BA", "WarningAmberBrush"),
            AppDialogIcon.Error => ("\uEA39", "DangerRedBrush"),
            _ => ("\uE946", "AccentEmeraldBrush")
        };

        IconGlyphText.Text = glyph;
        object? resource = TryFindResource(brushKey);
        if (resource is Brush accentBrush)
        {
            IconGlyphText.Foreground = accentBrush;
            IconBadgeBorder.BorderBrush = accentBrush;
        }
    }

    private void ConfigureButtons(
        AppDialogButtons buttons,
        string okText,
        string yesText,
        string noText,
        string cancelText)
    {
        switch (buttons)
        {
            case AppDialogButtons.YesNo:
                PrimaryActionButton.Content = yesText;
                SecondaryActionButton.Content = noText;
                SecondaryActionButton.Visibility = Visibility.Visible;
                AutomationProperties.SetName(PrimaryActionButton, yesText);
                AutomationProperties.SetName(SecondaryActionButton, noText);
                break;

            case AppDialogButtons.YesNoCancel:
                PrimaryActionButton.Content = yesText;
                SecondaryActionButton.Content = noText;
                CancelActionButton.Content = cancelText;
                SecondaryActionButton.Visibility = Visibility.Visible;
                CancelActionButton.Visibility = Visibility.Visible;
                AutomationProperties.SetName(PrimaryActionButton, yesText);
                AutomationProperties.SetName(SecondaryActionButton, noText);
                AutomationProperties.SetName(CancelActionButton, cancelText);
                break;

            default:
                PrimaryActionButton.Content = okText;
                AutomationProperties.SetName(PrimaryActionButton, okText);
                break;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons == AppDialogButtons.OK ? AppDialogResult.OK : AppDialogResult.Yes;
        Close();
    }

    private void SecondaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.No;
        Close();
    }

    private void CancelActionButton_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Cancel;
        Close();
    }

    private void CloseCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons switch
        {
            AppDialogButtons.YesNo => AppDialogResult.No,
            AppDialogButtons.YesNoCancel => AppDialogResult.Cancel,
            _ => AppDialogResult.OK
        };
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PrimaryActionButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseCancel_Click(sender, e);
            e.Handled = true;
        }
    }
}
