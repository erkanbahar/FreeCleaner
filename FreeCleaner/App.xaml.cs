using System.Windows;

namespace FreeCleaner;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Owned resources are disposed in WPF OnExit/Closed lifecycle handlers.")]
public partial class App : Application
{
    private Services.SingleInstanceBridgeService? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services.CrashGuardService.InstallGlobalHandlers(this);

        var smokeOptions = Services.SmokeTestOptions.Parse(e.Args);
        if (smokeOptions.Enabled)
        {
            StartSmokeTest(smokeOptions);
            return;
        }

        Services.BindingErrorListener.Install();
        Services.AppUpdateService.TryCleanupStaleUpdateBackup();

        _singleInstance = new Services.SingleInstanceBridgeService();
        if (!_singleInstance.TryAcquirePrimaryInstance())
        {
            try
            {
                Services.SingleInstanceBridgeService
                    .SendArgsToPrimaryInstanceAsync(e.Args)
                    .GetAwaiter()
                    .GetResult();
            }
            catch { }

            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        var startupConfig = Services.ExclusionAndSettingsService.Load();
        Services.LocalizationService.ApplyLanguage(startupConfig.SelectedLanguage);
        Services.ThemeManagerService.ApplyTheme(startupConfig.SelectedTheme);

        var cliAction = Services.ShellIntegrationAndProfilesService.ParseStartupArguments(e.Args);

        var window = new MainWindow();
        MainWindow = window;
        if (cliAction.StartMinimized)
            window.ConfigureStartMinimizedToTray();
        window.Show();

        _singleInstance.StartListening(forwardedArgs =>
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window.BringToFrontFromExternalTrigger();
                    var forwardedAction = Services.ShellIntegrationAndProfilesService.ParseStartupArguments(forwardedArgs);
                    if (forwardedAction.Kind != Services.CliLaunchActionKind.None &&
                        window.DataContext is ViewModels.MainViewModel vm)
                    {
                        await vm.ApplyCliLaunchActionAsync(forwardedAction);
                    }
                }
                catch (Exception ex)
                {
                    Services.ErrorLogService.Log("IPC Başlatma", ex);
                }
            });
        });

        if (cliAction.Kind != Services.CliLaunchActionKind.None)
        {
            window.Loaded += async (_, _) =>
            {
                try
                {
                    if (window.DataContext is ViewModels.MainViewModel viewModel)
                        await viewModel.ApplyCliLaunchActionAsync(cliAction);
                }
                catch (Exception ex)
                {
                    Services.ErrorLogService.Log("CLI Başlatma", ex);
                }
            };
        }
    }

    /// <summary>
    /// <c>--smoke-test</c>: no single-instance check, no CLI actions, dialogs answered automatically,
    /// settings and logs redirected to a temporary folder; exits with the test result.
    /// </summary>
    private void StartSmokeTest(Services.SmokeTestOptions options)
    {
        var dialogs = new Services.UnattendedDialogService();
        Services.WpfUserDialogService.UnattendedOverride = dialogs;
        Services.CrashGuardService.UnattendedCrashSink = SmokeTestRunner.RecordUnhandled;
        SmokeTestRunner.PrepareIsolatedDataDirectory(options);
        Services.BindingErrorListener.Install();

        var startupConfig = Services.ExclusionAndSettingsService.Load();
        Services.LocalizationService.ApplyLanguage(startupConfig.SelectedLanguage);
        Services.ThemeManagerService.ApplyTheme(startupConfig.SelectedTheme);

        var window = new MainWindow();
        MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            int exitCode = 1;
            try
            {
                if (window.DataContext is ViewModels.MainViewModel vm)
                    exitCode = await SmokeTestRunner.RunAsync(window, vm, options, dialogs);
            }
            catch (Exception ex)
            {
                SmokeTestRunner.RecordUnhandled("SmokeTest", ex);
            }
            finally
            {
                window.CloseForSmokeTest();
                Shutdown(exitCode);
            }
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }
}
