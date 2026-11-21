using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.Services;
using FreeCleaner.ViewModels;
using FreeCleaner.Views;

namespace FreeCleaner;

public partial class MainWindow : Window
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private Dictionary<Expander, bool>? _preSearchExpanderStates;
    private SystemTrayService? _trayService;
    private bool _forceExit;
    private bool _startMinimizedToTray;
    private readonly LazyViewRegistry _lazyViews = new();

    public MainWindow()
    {
        InitializeComponent();
        RegisterLazyViews();
        SourceInitialized += (_, _) => ApplyDwmThemePolish();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    public void ConfigureStartMinimizedToTray()
    {
        _startMinimizedToTray = true;
        ShowInTaskbar = false;
        Opacity = 0;
        WindowState = WindowState.Minimized;
    }

    private void ApplyDwmThemePolish()
    {
        string? theme = (DataContext as MainViewModel)?.SelectedTheme;
        WindowsShellVisualService.ApplyWindowChromePolish(this, theme);
    }

    private void RegisterLazyViews()
    {
        CleanerView? cleanerView = null;
        object GetOrCreateCleanerView() => cleanerView ??= new CleanerView { DataContext = DataContext };

        _lazyViews.Register("All", GetOrCreateCleanerView);
        _lazyViews.Register("System", GetOrCreateCleanerView);
        _lazyViews.Register("Browsers", GetOrCreateCleanerView);
        _lazyViews.Register("Apps", GetOrCreateCleanerView);
        _lazyViews.Register("Startup", () => new StartupView { DataContext = DataContext });
        _lazyViews.Register("Registry", () => new RegistryView { DataContext = DataContext });
        _lazyViews.Register("Uninstaller", () => new UninstallerView { DataContext = DataContext });
        _lazyViews.Register("Booster", () => new BoosterView { DataContext = DataContext });
        _lazyViews.Register("Shredder", () => new ShredderView { DataContext = DataContext });
        _lazyViews.Register("Privacy", () => new PrivacyView { DataContext = DataContext });
        _lazyViews.Register("Network", () => new NetworkView { DataContext = DataContext });
        _lazyViews.Register("Downloads", () => new DownloadsView { DataContext = DataContext });
        _lazyViews.Register("Bloatware", () => new BloatwareView { DataContext = DataContext });
        _lazyViews.Register("Dashboard", () => new DashboardView { DataContext = DataContext });
        _lazyViews.Register("LiveMonitor", () => new LiveMonitorView { DataContext = DataContext });
        _lazyViews.Register("DiskAnalyzer", () => new DiskAnalyzerView { DataContext = DataContext });
        _lazyViews.Register("DevCleaner", () => new DevCleanerView { DataContext = DataContext });
        _lazyViews.Register("PackageCaches", () => new PackageCachesView { DataContext = DataContext });
        _lazyViews.Register("DiskTreemap", () => new DiskTreemapView { DataContext = DataContext });
        _lazyViews.Register("ExifScrubber", () => new ExifScrubberView { DataContext = DataContext });
        _lazyViews.Register("DeepSpaceRecovery", () => new DeepSpaceRecoveryView { DataContext = DataContext });
        _lazyViews.Register("SimilarPhotos", () => new SimilarPhotoFinderView { DataContext = DataContext });
        _lazyViews.Register("Settings", () => new SettingsView { DataContext = DataContext });
        _lazyViews.Register("AiModelCleaner", () => new AiModelCleanerView { DataContext = DataContext });
        _lazyViews.Register("WingetUpdater", () => new WingetUpdaterView { DataContext = DataContext });
    }

    private void SyncLazyModuleHost(string? sectionKey)
    {
        if (_lazyViews.HasView(sectionKey))
        {
            object? view = _lazyViews.GetOrCreate(sectionKey);
            if (view is FrameworkElement fe && !ReferenceEquals(fe.DataContext, DataContext))
                fe.DataContext = DataContext;
            LazyModuleHost.Content = view;
            LazyModuleHost.Visibility = Visibility.Visible;
        }
        else
        {
            LazyModuleHost.Visibility = Visibility.Collapsed;
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_trayService is not null)
            return;

        _trayService = new SystemTrayService(
            showWindowAction: RestoreWindowFromTray,
            optimizeRamAction: () =>
            {
                if (DataContext is MainViewModel vm && vm.OptimizeRamCommand.CanExecute(null))
                    vm.OptimizeRamCommand.Execute(null);
            },
            runHealthCheckAction: () =>
            {
                RestoreWindowFromTray();
                if (DataContext is MainViewModel vm && vm.RunOneClickHealthCheckCommand.CanExecute(null))
                    vm.RunOneClickHealthCheckCommand.Execute(null);
            },
            analyzeJunkAction: () =>
            {
                RestoreWindowFromTray();
                if (DataContext is MainViewModel vm)
                {
                    vm.ActiveSection = "All";
                    if (vm.AnalyzeCommand.CanExecute(null))
                        vm.AnalyzeCommand.Execute(null);
                }
            },
            exitAppAction: ExitApplicationFromTray);

        if (DataContext is MainViewModel viewModel)
        {
            ApplyDwmThemePolish();
            SyncLazyModuleHost(viewModel.ActiveSection);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.ActiveSection))
                    SyncLazyModuleHost(viewModel.ActiveSection);
                else if (args.PropertyName == nameof(MainViewModel.SelectedTheme))
                    ApplyDwmThemePolish();
            };

            viewModel.TrayNotificationRequested += (title, message, isWarning) =>
                _trayService?.ShowNotification(title, message, isWarning);

            viewModel.TrayTooltipUpdateRequested += (cpu, ram, freeGb, _) =>
                _trayService?.UpdateStatusTooltip(cpu, ram, freeGb);

            viewModel.TickTrayHealthWatcher();

            if (_startMinimizedToTray)
            {
                _startMinimizedToTray = false;
                Hide();
                WindowState = WindowState.Normal;
                Opacity = 1;
                ShowInTaskbar = true;
            }
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_forceExit && DataContext is MainViewModel vm && vm.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            _trayService?.ShowNotification(
                "FreeCleaner Pro Arka Planda Çalışıyor",
                "Uygulama sağ alt sistem tepsisine küçültüldü. Açmak için ikona çift tıklayın.");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _trayService?.Dispose();
        _trayService = null;
    }

    public void BringToFrontFromExternalTrigger() => RestoreWindowFromTray();

    private void RestoreWindowFromTray()
    {
        ShowInTaskbar = true;
        Opacity = 1;
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplicationFromTray()
    {
        _forceExit = true;
        _trayService?.Dispose();
        _trayService = null;
        Close();
        Application.Current?.Shutdown();
    }

    // ==================== CTRL+K QUICK COMMAND PALETTE ====================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (CommandPaletteOverlay.Visibility == Visibility.Visible)
                CloseCommandPalette();
            else
                OpenCommandPalette();
            e.Handled = true;
            return;
        }

        if (CommandPaletteOverlay.Visibility != Visibility.Visible)
            return;

        if (e.Key == Key.Escape)
        {
            CloseCommandPalette();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && CommandPaletteList.Items.Count > 0)
        {
            int next = Math.Min(CommandPaletteList.Items.Count - 1, CommandPaletteList.SelectedIndex + 1);
            CommandPaletteList.SelectedIndex = next;
            CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && CommandPaletteList.Items.Count > 0)
        {
            int prev = Math.Max(0, CommandPaletteList.SelectedIndex - 1);
            CommandPaletteList.SelectedIndex = prev;
            CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedPaletteShortcut();
            e.Handled = true;
        }
    }

    private void OpenCommandPalette_Click(object sender, RoutedEventArgs e) => OpenCommandPalette();

    private void OpenCommandPalette()
    {
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandPaletteInput.Text = string.Empty;
        RefreshCommandPaletteList(string.Empty);
        CommandPaletteInput.Focus();
    }

    private void CloseCommandPalette()
    {
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
    }

    private void CommandPaletteBackdrop_MouseDown(object sender, MouseButtonEventArgs e) => CloseCommandPalette();

    private void CommandPaletteCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void CommandPaletteInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshCommandPaletteList(CommandPaletteInput.Text ?? string.Empty);
    }

    private void CommandPaletteList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ExecuteSelectedPaletteShortcut();

    private void RefreshCommandPaletteList(string query)
    {
        if (DataContext is not MainViewModel vm)
            return;

        string q = query.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? vm.AllModuleCatalog.Take(18).ToList()
            : vm.AllModuleCatalog
                .Where(m => ContainsTurkishInsensitive(m.Title, q) ||
                            ContainsTurkishInsensitive(m.CategoryName, q) ||
                            ContainsTurkishInsensitive(m.Keywords, q) ||
                            ContainsTurkishInsensitive(m.SectionKey, q))
                .ToList();

        CommandPaletteList.ItemsSource = filtered;
        if (filtered.Count > 0)
            CommandPaletteList.SelectedIndex = 0;
    }

    private void ExecuteSelectedPaletteShortcut()
    {
        if (CommandPaletteList.SelectedItem is NavShortcutItem selected &&
            DataContext is MainViewModel vm)
        {
            CloseCommandPalette();
            vm.ExecuteCommandPaletteShortcut(selected);
        }
    }

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestoreWindow();
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        MaximizeRestoreWindow();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MaximizeRestoreWindow()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void NavSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (NavMenuStackPanel is null)
            return;

        string query = (NavSearchTextBox.Text ?? string.Empty).Trim();
        ClearNavSearchBtn.Visibility = string.IsNullOrEmpty(NavSearchTextBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (string.IsNullOrEmpty(query))
        {
            foreach (Expander exp in NavMenuStackPanel.Children.OfType<Expander>())
            {
                exp.Visibility = Visibility.Visible;
                foreach (RadioButton rb in EnumerateNavButtons(exp))
                    rb.Visibility = Visibility.Visible;

                if (_preSearchExpanderStates is not null && _preSearchExpanderStates.TryGetValue(exp, out bool wasExpanded))
                    exp.IsExpanded = wasExpanded;
            }
            _preSearchExpanderStates = null;
            return;
        }

        if (_preSearchExpanderStates is null)
        {
            _preSearchExpanderStates = new Dictionary<Expander, bool>();
            foreach (Expander exp in NavMenuStackPanel.Children.OfType<Expander>())
                _preSearchExpanderStates[exp] = exp.IsExpanded;
        }

        foreach (Expander exp in NavMenuStackPanel.Children.OfType<Expander>())
        {
            int visibleMatches = 0;
            foreach (RadioButton rb in EnumerateNavButtons(exp))
            {
                string labelText = CollectReadableText(rb);
                string commandKey = rb.CommandParameter?.ToString() ?? string.Empty;
                bool isMatch = ContainsTurkishInsensitive(labelText, query) ||
                               ContainsTurkishInsensitive(commandKey, query);

                rb.Visibility = isMatch ? Visibility.Visible : Visibility.Collapsed;
                if (isMatch)
                    visibleMatches++;
            }

            if (visibleMatches > 0)
            {
                exp.Visibility = Visibility.Visible;
                exp.IsExpanded = true;
            }
            else
            {
                exp.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ClearNavSearchButton_Click(object sender, RoutedEventArgs e)
    {
        NavSearchTextBox.Clear();
        NavSearchTextBox.Focus();
    }

    private void ExpandAllNavGroups_Click(object sender, RoutedEventArgs e) => SetAllNavGroupsExpanded(true);

    private void CollapseAllNavGroups_Click(object sender, RoutedEventArgs e) => SetAllNavGroupsExpanded(false);

    private void SetAllNavGroupsExpanded(bool expanded)
    {
        _preSearchExpanderStates = null;
        foreach (Expander exp in NavMenuStackPanel.Children.OfType<Expander>())
            exp.IsExpanded = expanded;
    }

    private static IEnumerable<RadioButton> EnumerateNavButtons(Expander expander)
    {
        if (expander.Content is Panel panel)
            return panel.Children.OfType<RadioButton>();
        return Enumerable.Empty<RadioButton>();
    }

    private static string CollectReadableText(RadioButton radioButton)
    {
        var sb = new StringBuilder();
        CollectLogicalText(radioButton.Content, sb);
        return sb.ToString();
    }

    private static void CollectLogicalText(object? node, StringBuilder sb)
    {
        if (node is null)
            return;

        if (node is TextBlock tb)
        {
            if (!string.Equals(tb.FontFamily?.Source, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(tb.Text))
            {
                sb.Append(tb.Text).Append(' ');
            }
            return;
        }

        if (node is ContentControl cc)
        {
            CollectLogicalText(cc.Content, sb);
            return;
        }

        if (node is DependencyObject dep)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(dep))
                CollectLogicalText(child, sb);
        }
    }

    private static bool ContainsTurkishInsensitive(string source, string query)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(query))
            return false;
        return TurkishCulture.CompareInfo.IndexOf(source, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }
}
