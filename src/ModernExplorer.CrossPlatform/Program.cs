using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using ModernExplorer.Core;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Input;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.Session;
using ModernExplorer.Core.Settings;
using ModernExplorer.Core.Update;
using ModernExplorer.Core.Viewer;
using ModernExplorer.Core.ViewModels;

namespace ModernExplorer.CrossPlatform;

/// <summary>
/// Cross-platform entry point for Linux (x64, ARM64), macOS (x64, Apple Silicon ARM64), and Windows.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<CrossPlatformApp>()
            .UsePlatformDetect()
            .LogToTrace();
}

/// <summary>
/// Avalonia UI application host sharing the full ModernExplorer.Core domain and view models.
/// </summary>
public sealed class CrossPlatformApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new CrossPlatformMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// Avalonia UI thread dispatcher implementing <see cref="IUiDispatcher"/>.
/// </summary>
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool HasThreadAccess => Dispatcher.UIThread.CheckAccess();
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

/// <summary>
/// Cross-platform Dual-Pane Explorer window powered by Avalonia UI and ModernExplorer.Core.
/// </summary>
public sealed class CrossPlatformMainWindow : Window, IShellUi, IConflictResolver
{
    private readonly MainViewModel _viewModel;
    private readonly GitHubUpdateService _updateService = new();
    private readonly Border _leftBorder;
    private readonly Border _rightBorder;
    private readonly Grid _panesGrid;
    private TextBox? _leftAddressBox;
    private TextBox? _rightAddressBox;
    private TextBox? _leftFilterBox;
    private TextBox? _rightFilterBox;
    private ListBox? _leftFileList;
    private ListBox? _rightFileList;

    public CrossPlatformMainWindow()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();
        var keyBindings = KeyBindingStore.LoadDefaultsWithUserOverride(paths.KeyBindingsFile);
        var watcherHub = new WatcherHub();
        var queue = new FileOperationQueue(this);
        var templates = new TemplateStore(paths.TemplatesDir);
        templates.Load(paths.TemplatesFile);
        var creator = new FileCreationService(templates);
        var sessionStore = new SessionStore(paths.SessionFile);
        var settingsStore = new SettingsStore(paths.SettingsFile);

        _viewModel = new MainViewModel(
            keyBindings,
            watcherHub,
            queue,
            creator,
            sessionStore,
            new AvaloniaUiDispatcher(),
            this,
            settingsStore);

        DataContext = _viewModel;
        Title = "Modern Explorer";
        Width = Math.Max(900, _viewModel.Settings.MainWindowWidth);
        Height = Math.Max(600, _viewModel.Settings.MainWindowHeight);
        MinWidth = 860;
        MinHeight = 520;
        Background = SolidColorBrush.Parse("#0F131A");

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            try { Icon = new WindowIcon(iconPath); } catch { }
        }

        _leftBorder = BuildPanelView(_viewModel.Left, isLeft: true);
        _rightBorder = BuildPanelView(_viewModel.Right, isLeft: false);

        _panesGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,6,*"),
            Margin = new Thickness(8, 4, 8, 4),
        };
        Grid.SetColumn(_leftBorder, 0);
        var splitter = new GridSplitter
        {
            Background = SolidColorBrush.Parse("#1F2633"),
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(_rightBorder, 2);
        _panesGrid.Children.Add(_leftBorder);
        _panesGrid.Children.Add(splitter);
        _panesGrid.Children.Add(_rightBorder);

        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
        };

        var topBar = BuildTopToolbar();
        Grid.SetRow(topBar, 0);
        Grid.SetRow(_panesGrid, 1);

        var statusBar = BuildStatusBar();
        Grid.SetRow(statusBar, 2);

        var functionBar = BuildFunctionKeyBar();
        Grid.SetRow(functionBar, 3);

        rootGrid.Children.Add(topBar);
        rootGrid.Children.Add(_panesGrid);
        rootGrid.Children.Add(statusBar);
        rootGrid.Children.Add(functionBar);

        Content = rootGrid;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
        AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Opened += async (_, _) =>
        {
            ApplyTheme(_viewModel.Settings.Theme);
            UpdateActivePanelBorders();
            await _viewModel.InitializeAsync();
            if (_viewModel.Settings.CheckForUpdatesOnStartup)
                _ = CheckForUpdatesOnStartupAsync();
        };

        Closing += async (_, _) =>
        {
            _viewModel.SaveSessionImmediately();
            await _viewModel.DisposeAsync();
        };
    }

    private Control BuildTopToolbar()
    {
        var bar = new Border
        {
            Background = SolidColorBrush.Parse("#161B24"),
            BorderBrush = SolidColorBrush.Parse("#252D3D"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8),
        };

        var dock = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };

        var brandStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        brandStack.Children.Add(new TextBlock
        {
            Text = "Modern Explorer",
            FontWeight = FontWeight.SemiBold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var actionsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        Button MakeToolbarBtn(string locKey, Func<Task> action)
        {
            var btn = new Button
            {
                Content = LocalizationService.T(locKey),
                Padding = new Thickness(10, 5),
            };
            LocalizationService.Instance.LanguageChanged += (_, _) => btn.Content = LocalizationService.T(locKey);
            btn.Click += async (_, _) => await action();
            return btn;
        }

        actionsStack.Children.Add(MakeToolbarBtn("Toolbar.NewFile", () => _viewModel.CreateNewFileAsync()));
        actionsStack.Children.Add(MakeToolbarBtn("Toolbar.NewFolder", () => _viewModel.CreateNewFolderAsync()));
        actionsStack.Children.Add(MakeToolbarBtn("Toolbar.Copy", () => _viewModel.CopyOrMoveAsync(OpKind.Copy, showDialog: true)));
        actionsStack.Children.Add(MakeToolbarBtn("Toolbar.Move", () => _viewModel.CopyOrMoveAsync(OpKind.Move, showDialog: true)));
        actionsStack.Children.Add(MakeToolbarBtn("Toolbar.Delete", () => _viewModel.DeleteTargetsAsync(recycle: true)));
        actionsStack.Children.Add(MakeToolbarBtn("Toolbar.SplitView", () =>
        {
            _viewModel.IsDualMode = !_viewModel.IsDualMode;
            UpdateDualLayout();
            return Task.CompletedTask;
        }));
        actionsStack.Children.Add(MakeToolbarBtn("Toolbar.Settings", async () => await ShowSettingsDialogAsync()));
        actionsStack.Children.Add(MakeToolbarBtn("Fn.F1", () => { ShowHelp(); return Task.CompletedTask; }));
        actionsStack.Children.Add(MakeToolbarBtn("Toolbar.About", async () => await ShowAboutDialogAsync()));

        Grid.SetColumn(brandStack, 0);
        Grid.SetColumn(actionsStack, 2);
        dock.Children.Add(brandStack);
        dock.Children.Add(actionsStack);
        bar.Child = dock;
        return bar;
    }

    private Border BuildPanelView(PanelViewModel panel, bool isLeft)
    {
        var border = new Border
        {
            Background = SolidColorBrush.Parse("#141922"),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(6),
            DataContext = panel,
        };

        border.PointerPressed += (_, _) => _viewModel.ActiveSide = panel.Side;

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
        };

        // 1. Drive bar
        var drivesControl = new ItemsControl
        {
            ItemsSource = panel.Drives,
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }),
            Margin = new Thickness(0, 0, 0, 4),
            ItemTemplate = new FuncDataTemplate<DriveItemViewModel>((drive, _) =>
            {
                var btn = new Button
                {
                    Content = drive.DisplayText,
                    Margin = new Thickness(0, 0, 4, 4),
                    Padding = new Thickness(8, 3),
                };
                btn.Click += async (_, _) =>
                {
                    _viewModel.ActiveSide = panel.Side;
                    await panel.NavigateToDriveAsync(drive.RootPath);
                };
                return btn;
            }),
        };
        Grid.SetRow(drivesControl, 0);

        // 2. Tab bar
        var tabRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var tabList = new ListBox
        {
            ItemsSource = panel.Tabs,
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(PanelViewModel.ActiveTab), BindingMode.TwoWay),
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }),
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<TabViewModel>((tab, _) =>
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var title = new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding(nameof(TabViewModel.Title)),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var closeBtn = new Button
                {
                    Content = "✕",
                    Padding = new Thickness(4, 0),
                    Background = Brushes.Transparent,
                };
                closeBtn.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await panel.CloseTabAsync(tab);
                };
                sp.Children.Add(title);
                sp.Children.Add(closeBtn);
                return sp;
            }),
        };
        var newTabBtn = new Button
        {
            Content = "+",
            Padding = new Thickness(10, 2),
            Margin = new Thickness(4, 0, 0, 0),
        };
        newTabBtn.Click += async (_, _) =>
        {
            _viewModel.ActiveSide = panel.Side;
            await panel.CreateTabAsync();
        };
        Grid.SetColumn(tabList, 0);
        Grid.SetColumn(newTabBtn, 1);
        tabRow.Children.Add(tabList);
        tabRow.Children.Add(newTabBtn);
        Grid.SetRow(tabRow, 1);

        // 3. Navigation, address, and filter bar
        var navGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,180"),
            Margin = new Thickness(0, 0, 0, 6),
        };
        var backBtn = new Button { Content = "◀", Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 2, 0) };
        backBtn.Click += async (_, _) => { if (panel.ActiveTab is { } t) await t.GoBackAsync(); };
        var fwdBtn = new Button { Content = "▶", Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 2, 0) };
        fwdBtn.Click += async (_, _) => { if (panel.ActiveTab is { } t) await t.GoForwardAsync(); };
        var upBtn = new Button { Content = "▲", Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 6, 0) };
        upBtn.Click += async (_, _) => { if (panel.ActiveTab is { } t) await t.GoUpAsync(); };

        var addrBox = new TextBox
        {
            Watermark = LocalizationService.T("Panel.AddressPlaceholder"),
            Margin = new Thickness(0, 0, 6, 0),
        };
        LocalizationService.Instance.LanguageChanged += (_, _) => addrBox.Watermark = LocalizationService.T("Panel.AddressPlaceholder");
        addrBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && panel.ActiveTab is { } t && !string.IsNullOrWhiteSpace(addrBox.Text))
            {
                await t.NavigateAsync(addrBox.Text);
                e.Handled = true;
            }
        };

        var filterBox = new TextBox
        {
            Watermark = LocalizationService.T("Panel.FilterPlaceholder"),
        };
        LocalizationService.Instance.LanguageChanged += (_, _) => filterBox.Watermark = LocalizationService.T("Panel.FilterPlaceholder");
        filterBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && panel.ActiveTab is { } t)
                t.Filter = filterBox.Text ?? "";
        };

        if (isLeft) { _leftAddressBox = addrBox; _leftFilterBox = filterBox; }
        else { _rightAddressBox = addrBox; _rightFilterBox = filterBox; }

        Grid.SetColumn(backBtn, 0);
        Grid.SetColumn(fwdBtn, 1);
        Grid.SetColumn(upBtn, 2);
        Grid.SetColumn(addrBox, 3);
        Grid.SetColumn(filterBox, 4);
        navGrid.Children.Add(backBtn);
        navGrid.Children.Add(fwdBtn);
        navGrid.Children.Add(upBtn);
        navGrid.Children.Add(addrBox);
        navGrid.Children.Add(filterBox);
        Grid.SetRow(navGrid, 2);

        // 4. File list
        var fileList = new ListBox
        {
            Background = SolidColorBrush.Parse("#0F131A"),
            ItemTemplate = new FuncDataTemplate<FileItemViewModel>((item, _) =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("28,*,90,110,140"),
                    Margin = new Thickness(4, 1),
                };
                var iconTxt = new TextBlock
                {
                    Text = item.IsDirectory ? "📁" : "📄",
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var nameTxt = new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding(nameof(FileItemViewModel.DisplayName)),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var extTxt = new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding(nameof(FileItemViewModel.TypeText)),
                    Foreground = SolidColorBrush.Parse("#8B95A7"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var sizeTxt = new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding(nameof(FileItemViewModel.SizeText)),
                    Foreground = SolidColorBrush.Parse("#8B95A7"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var dateTxt = new TextBlock
                {
                    Text = item.ModifiedText,
                    Foreground = SolidColorBrush.Parse("#8B95A7"),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                Grid.SetColumn(iconTxt, 0);
                Grid.SetColumn(nameTxt, 1);
                Grid.SetColumn(extTxt, 2);
                Grid.SetColumn(sizeTxt, 3);
                Grid.SetColumn(dateTxt, 4);
                row.Children.Add(iconTxt);
                row.Children.Add(nameTxt);
                row.Children.Add(extTxt);
                row.Children.Add(sizeTxt);
                row.Children.Add(dateTxt);
                return row;
            }),
        };

        fileList.DoubleTapped += async (_, _) =>
        {
            _viewModel.ActiveSide = panel.Side;
            await _viewModel.OpenFocusedEntryAsync();
        };

        if (isLeft) _leftFileList = fileList;
        else _rightFileList = fileList;

        panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelViewModel.ActiveTab))
                BindActiveTabToPanelControls(panel.ActiveTab, addrBox, filterBox, fileList);
        };

        Grid.SetRow(fileList, 3);
        layout.Children.Add(drivesControl);
        layout.Children.Add(tabRow);
        layout.Children.Add(navGrid);
        layout.Children.Add(fileList);

        border.Child = layout;
        return border;
    }

    private static void BindActiveTabToPanelControls(TabViewModel? tab, TextBox addrBox, TextBox filterBox, ListBox fileList)
    {
        if (tab is null)
        {
            fileList.ItemsSource = null;
            addrBox.Text = "";
            return;
        }

        addrBox.Text = tab.CurrentPath;
        filterBox.Text = tab.Filter;
        fileList.ItemsSource = tab.Items;
        fileList.SelectedItem = tab.FocusedItem;

        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabViewModel.CurrentPath))
                Dispatcher.UIThread.Post(() => addrBox.Text = tab.CurrentPath);
            else if (e.PropertyName == nameof(TabViewModel.FocusedItem))
                Dispatcher.UIThread.Post(() => fileList.SelectedItem = tab.FocusedItem);
        };

        fileList.SelectionChanged += (_, _) =>
        {
            if (fileList.SelectedItem is FileItemViewModel f)
                tab.FocusedItem = f;
        };
    }

    private Control BuildStatusBar()
    {
        var border = new Border
        {
            Background = SolidColorBrush.Parse("#141922"),
            BorderBrush = SolidColorBrush.Parse("#252D3D"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 4),
        };
        var txt = new TextBlock
        {
            [!TextBlock.TextProperty] = new Binding("ActivePanel.ActiveTab.StatusSummary"),
            FontSize = 12,
            Foreground = SolidColorBrush.Parse("#9AA4B5"),
        };
        border.Child = txt;
        return border;
    }

    private Control BuildFunctionKeyBar()
    {
        var grid = new UniformGrid
        {
            Columns = 9,
            Margin = new Thickness(8, 4, 8, 8),
        };

        void AddFnButton(string badge, string locKey, Func<Task> onClick)
        {
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(2, 0),
                Padding = new Thickness(6, 5),
                Content = $"{badge} {LocalizationService.T(locKey)}",
            };
            LocalizationService.Instance.LanguageChanged += (_, _) =>
                btn.Content = $"{badge} {LocalizationService.T(locKey)}";
            btn.Click += async (_, _) => await onClick();
            grid.Children.Add(btn);
        }

        AddFnButton("F2", "Fn.F2", () => _viewModel.RenameFocusedAsync());
        AddFnButton("F3", "Fn.F3", () =>
        {
            if (_viewModel.ActivePanel.ActiveTab?.FocusedItem is { IsParentLink: false } f)
                OpenViewer(f.FullPath);
            return Task.CompletedTask;
        });
        AddFnButton("F4", "Fn.F4", () =>
        {
            if (_viewModel.ActivePanel.ActiveTab?.FocusedItem is { IsDirectory: false } f)
                OpenEditor(f.FullPath);
            return Task.CompletedTask;
        });
        AddFnButton("F5", "Fn.F5", () => _viewModel.CopyOrMoveAsync(OpKind.Copy, showDialog: true));
        AddFnButton("F6", "Fn.F6", () => _viewModel.CopyOrMoveAsync(OpKind.Move, showDialog: true));
        AddFnButton("F7", "Fn.F7", () => _viewModel.CreateNewFolderAsync());
        AddFnButton("F8", "Fn.F8", () => _viewModel.DeleteTargetsAsync(recycle: true));
        AddFnButton("F9", "Fn.F9", () =>
        {
            _viewModel.Commands.Execute("terminal.open", null, FocusContext.For(ContextFlags.FileList));
            return Task.CompletedTask;
        });
        AddFnButton("F10", "Fn.F10", () =>
        {
            _viewModel.IsDualMode = !_viewModel.IsDualMode;
            UpdateDualLayout();
            return Task.CompletedTask;
        });

        return grid;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            if (_viewModel.IsDualMode)
                _viewModel.ActiveSide = _viewModel.ActiveSide == PanelSide.Left ? PanelSide.Right : PanelSide.Left;
            FocusFileList(_viewModel.ActiveSide);
            e.Handled = true;
        }
        else if (e.Key == Key.F2) { _ = _viewModel.RenameFocusedAsync(); e.Handled = true; }
        else if (e.Key == Key.F3 && _viewModel.ActivePanel.ActiveTab?.FocusedItem is { IsParentLink: false } f3)
        {
            OpenViewer(f3.FullPath);
            e.Handled = true;
        }
        else if (e.Key == Key.F4 && _viewModel.ActivePanel.ActiveTab?.FocusedItem is { IsDirectory: false } f4)
        {
            OpenEditor(f4.FullPath);
            e.Handled = true;
        }
        else if (e.Key == Key.F5) { _ = _viewModel.CopyOrMoveAsync(OpKind.Copy, showDialog: true); e.Handled = true; }
        else if (e.Key == Key.F6) { _ = _viewModel.CopyOrMoveAsync(OpKind.Move, showDialog: true); e.Handled = true; }
        else if (e.Key == Key.F7) { _ = _viewModel.CreateNewFolderAsync(); e.Handled = true; }
        else if (e.Key == Key.F8 || e.Key == Key.Delete)
        {
            bool permanent = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);
            _ = _viewModel.DeleteTargetsAsync(recycle: !permanent);
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveSide))
            UpdateActivePanelBorders();
        else if (e.PropertyName == nameof(MainViewModel.IsDualMode))
            UpdateDualLayout();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Theme))
            ApplyTheme(_viewModel.Settings.Theme);
        else if (e.PropertyName == nameof(SettingsViewModel.ActivePanelBorderColor))
            UpdateActivePanelBorders();
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    private void UpdateActivePanelBorders()
    {
        var activeBrush = SolidColorBrush.Parse(AppSettings.NormalizeHexColor(_viewModel.Settings.ActivePanelBorderColor, "#0078D4"));
        var inactiveBrush = SolidColorBrush.Parse("#252D3D");
        _leftBorder.BorderBrush = _viewModel.ActiveSide == PanelSide.Left ? activeBrush : inactiveBrush;
        _rightBorder.BorderBrush = _viewModel.ActiveSide == PanelSide.Right ? activeBrush : inactiveBrush;
    }

    private void UpdateDualLayout()
    {
        _rightBorder.IsVisible = _viewModel.IsDualMode;
        _panesGrid.ColumnDefinitions[1].Width = _viewModel.IsDualMode ? new GridLength(6) : new GridLength(0);
        _panesGrid.ColumnDefinitions[2].Width = _viewModel.IsDualMode ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await Task.Delay(1200);
            var check = await _updateService.CheckForUpdateAsync();
            if (check.IsUpdateAvailable && check.LatestRelease is not null)
            {
                var install = await ConfirmAsync(
                    LocalizationService.T("Update.AvailableTitle"),
                    $"{LocalizationService.Format("Update.AvailableHeader", check.LatestRelease.ParsedVersion, check.CurrentVersion)}\n\n{check.LatestRelease.Body}",
                    LocalizationService.T("Update.InstallNowButton"));
                if (install)
                {
                    bool shouldExit = await _updateService.DownloadAndInstallUpdateAsync(check.LatestRelease);
                    if (shouldExit) Close();
                }
            }
        }
        catch { }
    }

    private async Task ShowSettingsDialogAsync()
    {
        var win = new Window
        {
            Title = LocalizationService.T("Settings.WindowTitle"),
            Width = 640,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = SolidColorBrush.Parse("#141922"),
        };

        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("Settings.Language.Title"),
            FontWeight = FontWeight.SemiBold,
            FontSize = 15,
        });

        var langCombo = new ComboBox
        {
            ItemsSource = LocalizationService.Instance.GetLocalizedLanguageOptions(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<LanguageOption>((opt, _) => new TextBlock { Text = opt.DisplayName }),
        };
        langCombo.SelectedItem = LocalizationService.Instance.GetLocalizedLanguageOptions()
            .FirstOrDefault(o => string.Equals(o.Code, _viewModel.Settings.Language, StringComparison.OrdinalIgnoreCase));
        langCombo.SelectionChanged += (_, _) =>
        {
            if (langCombo.SelectedItem is LanguageOption selected)
                _viewModel.Settings.Language = selected.Code;
        };
        stack.Children.Add(langCombo);

        var checkUpdatesToggle = new CheckBox
        {
            Content = LocalizationService.T("Settings.Updates.CheckOnStartup"),
            IsChecked = _viewModel.Settings.CheckForUpdatesOnStartup,
        };
        checkUpdatesToggle.IsCheckedChanged += (_, _) =>
            _viewModel.Settings.CheckForUpdatesOnStartup = checkUpdatesToggle.IsChecked == true;
        stack.Children.Add(checkUpdatesToggle);

        var checkNowBtn = new Button
        {
            Content = LocalizationService.T("Settings.Updates.CheckNowButton"),
            Padding = new Thickness(12, 6),
        };
        checkNowBtn.Click += async (_, _) =>
        {
            var res = await _updateService.CheckForUpdateAsync();
            if (res.IsUpdateAvailable && res.LatestRelease is not null)
            {
                var install = await ConfirmAsync(
                    LocalizationService.T("Update.AvailableTitle"),
                    LocalizationService.Format("Update.AvailableHeader", res.LatestRelease.ParsedVersion, res.CurrentVersion),
                    LocalizationService.T("Update.InstallNowButton"));
                if (install && await _updateService.DownloadAndInstallUpdateAsync(res.LatestRelease))
                    Close();
            }
            else
            {
                ShowMessage(LocalizationService.T("Settings.Updates.Title"), LocalizationService.Format("Update.UpToDate", res.CurrentVersion));
            }
        };
        stack.Children.Add(checkNowBtn);

        win.Content = stack;
        await win.ShowDialog(this);
    }

    private async Task ShowAboutDialogAsync()
    {
        var aboutVm = new AboutViewModel(_updateService);
        var win = new Window
        {
            Title = LocalizationService.T("About.WindowTitle"),
            Width = 560,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = SolidColorBrush.Parse("#141922"),
            DataContext = aboutVm,
        };

        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        stack.Children.Add(new TextBlock
        {
            Text = "ModernExplorer",
            FontWeight = FontWeight.Bold,
            FontSize = 20,
        });
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("About.Subtitle"),
            Opacity = 0.75,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("About.Description"),
            Opacity = 0.9,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });

        var repoBtn = new Button
        {
            Content = $"{aboutVm.RepositoryLabel}: {aboutVm.RepositoryUrl}",
            Padding = new Thickness(10, 6),
        };
        repoBtn.Click += (_, _) => aboutVm.OpenRepositoryInBrowser();
        stack.Children.Add(repoBtn);

        var installedRow = new TextBlock
        {
            Text = $"{aboutVm.InstalledVersionLabel} {aboutVm.InstalledVersion}",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
        };
        stack.Children.Add(installedRow);

        var latestRow = new TextBlock
        {
            [!TextBlock.TextProperty] = new Binding(nameof(AboutViewModel.LatestVersionText)),
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
        };
        stack.Children.Add(latestRow);

        var statusRow = new TextBlock
        {
            [!TextBlock.TextProperty] = new Binding(nameof(AboutViewModel.StatusMessage)),
            Opacity = 0.85,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(statusRow);

        var updateBtn = new Button
        {
            Content = aboutVm.UpdateNowButtonText,
            [!Visual.IsVisibleProperty] = new Binding(nameof(AboutViewModel.ShowUpdateButton)),
            Padding = new Thickness(14, 7),
        };
        updateBtn.Click += async (_, _) =>
        {
            bool shouldExit = await aboutVm.InstallFoundUpdateAsync();
            if (shouldExit)
            {
                win.Close();
                Close();
            }
        };
        stack.Children.Add(updateBtn);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var checkAgainBtn = new Button
        {
            Content = aboutVm.CheckAgainButtonText,
            Padding = new Thickness(12, 6),
        };
        checkAgainBtn.Click += async (_, _) => await aboutVm.CheckForUpdateAsync();
        var closeBtn = new Button
        {
            Content = aboutVm.CloseButtonText,
            Padding = new Thickness(12, 6),
        };
        closeBtn.Click += (_, _) => win.Close();
        btnRow.Children.Add(checkAgainBtn);
        btnRow.Children.Add(closeBtn);
        stack.Children.Add(btnRow);

        win.Opened += async (_, _) => await aboutVm.CheckForUpdateAsync();
        win.Closed += (_, _) => aboutVm.Dispose();
        win.Content = stack;
        await win.ShowDialog(this);
    }

    // ---- IShellUi & IConflictResolver Implementation ----

    public Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct) =>
        Task.FromResult(new ConflictDecision(ConflictChoice.Rename, ApplyToAll: false));

    public async Task<NewFileRequest?> ShowNewFileDialogAsync(
        string directory,
        IReadOnlyList<FileTemplate> templates,
        FileTemplate selected,
        Func<FileTemplate, string> suggestName,
        Func<string, string?> validate)
    {
        var name = await PromptAsync(LocalizationService.T("Toolbar.NewFile"), "File name:", suggestName(selected), 0, validate);
        return string.IsNullOrWhiteSpace(name) ? null : new NewFileRequest(name, selected, OpenAfterCreate: false);
    }

    public Task<string?> ShowNewFolderDialogAsync(string directory, Func<string, string?> validate) =>
        PromptAsync(LocalizationService.T("Toolbar.NewFolder"), "Folder name:", "New Folder", 10, validate);

    public async Task<string?> PromptAsync(string title, string label, string initial, int selectLength, Func<string, string?>? validate = null)
    {
        var tcs = new TaskCompletionSource<string?>();
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = SolidColorBrush.Parse("#161B24"),
        };

        var tb = new TextBox { Text = initial };
        var okBtn = new Button { Content = "OK", Width = 90 };
        var cancelBtn = new Button { Content = LocalizationService.T("Dialog.Cancel"), Width = 90 };

        okBtn.Click += (_, _) => { tcs.TrySetResult(tb.Text); dlg.Close(); };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(null); dlg.Close(); };
        dlg.Closed += (_, _) => tcs.TrySetResult(null);

        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        sp.Children.Add(new TextBlock { Text = label });
        sp.Children.Add(tb);
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        sp.Children.Add(btnRow);
        dlg.Content = sp;

        await dlg.ShowDialog(this);
        return await tcs.Task;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, bool destructive = false)
    {
        var tcs = new TaskCompletionSource<bool>();
        var dlg = new Window
        {
            Title = title,
            Width = 480,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = SolidColorBrush.Parse("#161B24"),
        };

        var okBtn = new Button { Content = primaryText, Padding = new Thickness(12, 6) };
        var cancelBtn = new Button { Content = LocalizationService.T("Dialog.Cancel"), Padding = new Thickness(12, 6) };

        okBtn.Click += (_, _) => { tcs.TrySetResult(true); dlg.Close(); };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(false); dlg.Close(); };
        dlg.Closed += (_, _) => tcs.TrySetResult(false);

        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        sp.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        sp.Children.Add(btnRow);
        dlg.Content = sp;

        await dlg.ShowDialog(this);
        return await tcs.Task;
    }

    public void ShowMessage(string title, string message)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await ConfirmAsync(title, message, LocalizationService.T("Dialog.Close"));
        });
    }

    public void FocusFileList(PanelSide side)
    {
        var target = side == PanelSide.Left ? _leftFileList : _rightFileList;
        target?.Focus();
    }

    public void FocusAddressBar()
    {
        var target = _viewModel.ActiveSide == PanelSide.Left ? _leftAddressBox : _rightAddressBox;
        target?.Focus();
    }

    public void FocusFilter()
    {
        var target = _viewModel.ActiveSide == PanelSide.Left ? _leftFilterBox : _rightFilterBox;
        target?.Focus();
    }

    public void ToggleFullScreen() =>
        WindowState = WindowState == Avalonia.Controls.WindowState.FullScreen
            ? Avalonia.Controls.WindowState.Normal
            : Avalonia.Controls.WindowState.FullScreen;

    public void ShowCommandPalette() { }

    public void ShowHelp() =>
        ShowMessage("Shortcuts", "F2 Rename · F3 View · F4 Edit · F5 Copy · F6 Move · F7 New Folder · F8 Delete · F9 Terminal · F10 Split · Tab Switch Panel");

    public void OpenNewWindow(TabState tab) { }

    public async void OpenViewer(string path)
    {
        try
        {
            var doc = await FileContentService.LoadPreviewAsync(path);
            var win = new Window
            {
                Title = $"Quick Viewer (F3) – {Path.GetFileName(path)} [{doc.FileTypeDescription}]",
                Width = 860,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = SolidColorBrush.Parse("#0F131A"),
                Content = new TextBox
                {
                    Text = doc.Content,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    FontFamily = new FontFamily("Consolas, Monospace"),
                    Margin = new Thickness(12),
                },
            };
            win.Show(this);
        }
        catch (Exception ex)
        {
            ShowMessage("Quick Viewer (F3)", ex.Message);
        }
    }

    public async void OpenEditor(string path)
    {
        try
        {
            var doc = await FileContentService.LoadPreviewAsync(path);
            var editorBox = new TextBox
            {
                Text = doc.Content,
                AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas, Monospace"),
            };
            var saveBtn = new Button { Content = "Save (Ctrl+S)", Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 0, 8) };
            saveBtn.Click += async (_, _) =>
            {
                await FileContentService.SaveAtomicAsync(path, editorBox.Text ?? "", doc.Encoding, doc.NewLine);
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(12) };
            Grid.SetRow(saveBtn, 0);
            Grid.SetRow(editorBox, 1);
            grid.Children.Add(saveBtn);
            grid.Children.Add(editorBox);

            var win = new Window
            {
                Title = $"Editor (F4) – {Path.GetFileName(path)}",
                Width = 860,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = SolidColorBrush.Parse("#0F131A"),
                Content = grid,
            };
            win.Show(this);
        }
        catch (Exception ex)
        {
            ShowMessage("Editor (F4)", ex.Message);
        }
    }

    public void SetClipboardText(string text) => Clipboard?.SetTextAsync(text);

    public Task SetClipboardFilesAsync(IReadOnlyList<string> paths, bool cut) => Task.CompletedTask;

    public Task<(IReadOnlyList<string> Paths, bool Cut)?> GetClipboardFilesAsync() =>
        Task.FromResult<(IReadOnlyList<string> Paths, bool Cut)?>(null);
}
