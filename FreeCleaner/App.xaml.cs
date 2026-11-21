using System.Configuration;
using System.Data;
using System.Windows;

namespace FreeCleaner;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startupConfig = Services.ExclusionAndSettingsService.Load();
        Services.LocalizationService.ApplyLanguage(startupConfig.SelectedLanguage);
        Services.ThemeManagerService.ApplyTheme(startupConfig.SelectedTheme);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var cliAction = Services.ShellIntegrationAndProfilesService.ParseStartupArguments(e.Args);
        if (cliAction.Kind != Services.CliLaunchActionKind.None)
        {
            window.Loaded += async (_, _) =>
            {
                if (window.DataContext is ViewModels.MainViewModel viewModel)
                    await viewModel.ApplyCliLaunchActionAsync(cliAction);
            };
        }
    }
}

