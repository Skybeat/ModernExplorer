using System.IO;
using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ModernExplorer.App.Dialogs;
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
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;

namespace ModernExplorer.App;

public sealed partial class MainWindow : Window
{
    private const string TabDragFormatId = "ModernExplorer.TabState";

    private readonly AppPaths _paths;
    private readonly ShellDialogService _dialogService;
    private readonly FileOperationQueue _operationQueue;
    private readonly GitHubUpdateService _updateService = new();
    private readonly List<Window> _childWindows = new();
    private FileSystemWatcher? _keyBindingWatcher;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private bool _isWindowReady;
    private string _quickSearchBuffer = "";
    private DateTime _lastQuickSearchKeyUtc;
    private string? _hoveredImagePath;
    private int _hoverLoadVersion;
    private TabViewModel? _subscribedLeftTab;
    private TabViewModel? _subscribedRightTab;
    private FileViewMode? _currentLeftViewMode;
    private FileViewMode? _currentRightViewMode;

    public MainViewModel ViewModel { get; }
    public FrameworkElement RootElement => RootGrid;

    public MainWindow(TabState? tearOutTab = null)
    {
        InitializeComponent();
        TryConfigureWindow();

        _paths = new AppPaths();
        _paths.EnsureCreated();

        var dispatcher = new WinUiDispatcher(DispatcherQueue);
        _dialogService = new ShellDialogService(this);

        var keyBindings = KeyBindingStore.LoadDefaultsWithUserOverride(_paths.KeyBindingsFile);
        StartKeyBindingHotReload(keyBindings, dispatcher);

        var templates = new TemplateStore(_paths.TemplatesDir);
        templates.Load(_paths.TemplatesFile);

        var creationService = new FileCreationService(templates);
        var watcherHub = new WatcherHub();
        _operationQueue = new FileOperationQueue(_dialogService);
        var sessionStore = new SessionStore(_paths.SessionFile);
        var settingsStore = new SettingsStore(_paths.SettingsFile);

        ViewModel = new MainViewModel(
            keyBindings,
            watcherHub,
            _operationQueue,
            creationService,
            sessionStore,
            dispatcher,
            _dialogService,
            settingsStore);

        RestoreAndTrackMainWindowBounds();

        ApplyTheme(ViewModel.Settings.Theme);
        ApplyLocalization();

        ViewModel.Settings.ThemeChanged += (_, _) => ApplyTheme(ViewModel.Settings.Theme);
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            ApplyLocalization();
            Bindings.Update();
        };
        ViewModel.Settings.ActivePanelBorderColorChanged += (_, _) => UpdateActivePanelVisuals();
        ViewModel.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.EnableImageHoverPreview) &&
                !ViewModel.Settings.EnableImageHoverPreview)
            {
                HideImageHoverPreview();
            }
            else if (e.PropertyName == nameof(SettingsViewModel.ImageHoverPreviewSize))
            {
                _hoveredImagePath = null;
            }
        };

        ViewModel.Left.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelViewModel.ActiveTab))
            {
                AttachPanelTabSubscription(PanelSide.Left);
                UpdatePanelViewMode(PanelSide.Left);
            }
        };

        ViewModel.Right.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelViewModel.ActiveTab))
            {
                AttachPanelTabSubscription(PanelSide.Right);
                UpdatePanelViewMode(PanelSide.Right);
            }
        };

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDualMode))
                UpdateDualLayout();
            else if (e.PropertyName == nameof(MainViewModel.ActiveSide))
            {
                UpdateActivePanelVisuals();
                Bindings.Update();
                if (ViewModel.IsQuickPreviewPanelOpen)
                    _ = RefreshQuickPreviewPanelAsync();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsQuickPreviewPanelOpen))
            {
                Bindings.Update();
                if (ViewModel.IsQuickPreviewPanelOpen)
                    _ = RefreshQuickPreviewPanelAsync();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen))
            {
                if (ViewModel.IsSettingsOpen)
                    OpenSettingsWindow();
                else
                    CloseSettingsWindow();
            }
        };

        LeftPanelBorder.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLeftPanelPointerPressed), handledEventsToo: true);
        RightPanelBorder.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRightPanelPointerPressed), handledEventsToo: true);

        RootGrid.PreviewKeyDown += OnRootPreviewKeyDown;
        RootGrid.PreviewKeyUp += OnRootPreviewKeyUp;

        RootGrid.Loaded += async (_, _) =>
        {
            ApplyTheme(ViewModel.Settings.Theme);
            ApplyLocalization();
            await ViewModel.InitializeAsync(tearOutTab);
            AttachPanelTabSubscription(PanelSide.Left);
            AttachPanelTabSubscription(PanelSide.Right);
            UpdatePanelViewMode(PanelSide.Left);
            UpdatePanelViewMode(PanelSide.Right);
            UpdateDualLayout();
            UpdateActivePanelVisuals();
            Bindings.Update();
            FocusFileList(ViewModel.ActiveSide);

            if (tearOutTab is null && ViewModel.Settings.CheckForUpdatesOnStartup)
            {
                _ = CheckForGitHubUpdatesAsync(manualTrigger: false);
            }
        };

        Closed += async (_, _) =>
        {
            SaveCurrentMainWindowBounds();
            CloseSettingsWindow();
            CloseAboutWindow();
            CloseChildWindows();
            ViewModel.SaveSessionImmediately();
            _keyBindingWatcher?.Dispose();
            await ViewModel.DisposeAsync();
        };
    }

    public void PrepareForUpdateAndExit()
    {
        try
        {
            SaveCurrentMainWindowBounds();
            CloseSettingsWindow();
            CloseAboutWindow();
            CloseChildWindows();
            ViewModel.SaveSessionImmediately();
            _keyBindingWatcher?.Dispose();
        }
        catch { }

        try { Application.Current.Exit(); } catch { }
        Environment.Exit(0);
    }

    public Visibility HasText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public string FormatFKey(string key, string label) => $"{key} {label}";

    public string FormatActiveTheme(string themeName) => LocalizationService.Format("Settings.ActiveThemePrefix", themeName);

    public void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        if (OperationsTitleButton is not null)
        {
            OperationsTitleButton.Content = loc["Toolbar.Operations"];
            ToolTipService.SetToolTip(OperationsTitleButton, loc["Toolbar.Operations.Tip"]);
        }
        if (DualToggleTitleButton is not null)
        {
            DualToggleTitleButton.Content = loc["Toolbar.DualToggle"];
            ToolTipService.SetToolTip(DualToggleTitleButton, loc["Toolbar.DualToggle.Tip"]);
        }
        if (CommandPaletteTitleButton is not null)
        {
            CommandPaletteTitleButton.Content = loc["Toolbar.Palette"];
            ToolTipService.SetToolTip(CommandPaletteTitleButton, loc["Toolbar.Palette.Tip"]);
        }
        if (SettingsTitleButton is not null)
        {
            SettingsTitleButton.Content = $"⚙ {loc["Toolbar.Settings"]}";
            ToolTipService.SetToolTip(SettingsTitleButton, loc["Toolbar.Settings.Tip"]);
        }
        if (HelpTitleButton is not null)
        {
            HelpTitleButton.Content = $"{loc["Fn.F1"]} (F1)";
            ToolTipService.SetToolTip(HelpTitleButton, loc["Toolbar.Shortcuts.Tip"]);
        }
        if (AboutTitleButton is not null)
        {
            AboutTitleButton.Content = loc["About.Button"];
            ToolTipService.SetToolTip(AboutTitleButton, loc["Toolbar.About.Tip"]);
        }

        if (LeftNewTabButton is not null) ToolTipService.SetToolTip(LeftNewTabButton, loc["Panel.NewTab.Tip"]);
        if (LeftCloseTabButton is not null) ToolTipService.SetToolTip(LeftCloseTabButton, loc["Panel.CloseTab.Tip"]);
        if (LeftBackButton is not null) ToolTipService.SetToolTip(LeftBackButton, loc["Panel.Back.Tip"]);
        if (LeftForwardButton is not null) ToolTipService.SetToolTip(LeftForwardButton, loc["Panel.Forward.Tip"]);
        if (LeftUpButton is not null) ToolTipService.SetToolTip(LeftUpButton, loc["Panel.Up.Tip"]);
        if (LeftRefreshButton is not null) ToolTipService.SetToolTip(LeftRefreshButton, loc["Panel.Refresh.Tip"]);
        if (LeftViewModeButton is not null) ToolTipService.SetToolTip(LeftViewModeButton, loc["Panel.View.Tip"]);
        if (LeftAddressBox is not null) LeftAddressBox.PlaceholderText = loc["Panel.AddressPlaceholder"];
        if (LeftFilterBox is not null) LeftFilterBox.PlaceholderText = loc["Panel.FilterPlaceholder"];

        if (RightNewTabButton is not null) ToolTipService.SetToolTip(RightNewTabButton, loc["Panel.NewTab.Tip"]);
        if (RightCloseTabButton is not null) ToolTipService.SetToolTip(RightCloseTabButton, loc["Panel.CloseTab.Tip"]);
        if (RightBackButton is not null) ToolTipService.SetToolTip(RightBackButton, loc["Panel.Back.Tip"]);
        if (RightForwardButton is not null) ToolTipService.SetToolTip(RightForwardButton, loc["Panel.Forward.Tip"]);
        if (RightUpButton is not null) ToolTipService.SetToolTip(RightUpButton, loc["Panel.Up.Tip"]);
        if (RightRefreshButton is not null) ToolTipService.SetToolTip(RightRefreshButton, loc["Panel.Refresh.Tip"]);
        if (RightViewModeButton is not null) ToolTipService.SetToolTip(RightViewModeButton, loc["Panel.View.Tip"]);
        if (RightAddressBox is not null) RightAddressBox.PlaceholderText = loc["Panel.AddressPlaceholder"];
        if (RightFilterBox is not null) RightFilterBox.PlaceholderText = loc["Panel.FilterPlaceholder"];

        if (LeftColNameText is not null) LeftColNameText.Text = loc["Col.Name"];
        if (LeftColSizeText is not null) LeftColSizeText.Text = loc["Col.Size"];
        if (LeftColModifiedText is not null) LeftColModifiedText.Text = loc["Col.Modified"];
        if (LeftColTypeText is not null) LeftColTypeText.Text = loc["Col.Extension"];

        if (RightColNameText is not null) RightColNameText.Text = loc["Col.Name"];
        if (RightColSizeText is not null) RightColSizeText.Text = loc["Col.Size"];
        if (RightColModifiedText is not null) RightColModifiedText.Text = loc["Col.Modified"];
        if (RightColTypeText is not null) RightColTypeText.Text = loc["Col.Extension"];

        if (OperationsPanelTitleText is not null) OperationsPanelTitleText.Text = loc["Ops.Title"];
        if (OperationsHideButton is not null) OperationsHideButton.Content = loc["Ops.Hide"];
    }

    public async Task CheckForGitHubUpdatesAsync(bool manualTrigger = false)
    {
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (result.IsUpdateAvailable && result.LatestRelease is not null)
            {
                await _dialogService.ShowUpdateAvailableDialogAsync(result, _updateService);
            }
            else if (manualTrigger)
            {
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    _dialogService.ShowMessage(LocalizationService.T("Update.ErrorTitle"), result.ErrorMessage);
                }
                else
                {
                    _dialogService.ShowMessage(
                        LocalizationService.T("Update.UpToDateTitle"),
                        LocalizationService.Format("Update.UpToDateMsg", $"v{GitHubUpdateService.GetCurrentVersionString()}"));
                }
            }
        }
        catch (Exception ex)
        {
            if (manualTrigger)
                _dialogService.ShowMessage(LocalizationService.T("Update.ErrorTitle"), ex.Message);
        }
    }

    public void ApplyTheme(AppTheme theme)
    {
        WindowChromeHelper.ApplyCustomTitleBarAndTheme(this, RootGrid, TitleBarDragRegion, theme);

        if (ViewModel is not null)
            UpdateActivePanelVisuals();
    }

    private void TryConfigureWindow()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarDragRegion);

            if (AppWindowTitleBar.IsCustomizationSupported() && AppWindow is { TitleBar: { } tb })
            {
                tb.ExtendsContentIntoTitleBar = true;
                tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            }

            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            AppWindow?.Resize(new Windows.Graphics.SizeInt32(1480, 900));
            if (AppWindow is not null)
            {
                AppWindow.Closing += (_, _) =>
                {
                    SaveCurrentMainWindowBounds();
                    ViewModel?.SaveSessionImmediately();
                };
            }
        }
        catch { }
    }

    private void RestoreAndTrackMainWindowBounds()
    {
        try
        {
            if (AppWindow is null) return;

            int savedWidth = Math.Clamp(ViewModel.Settings.MainWindowWidth, 720, 7680);
            int savedHeight = Math.Clamp(ViewModel.Settings.MainWindowHeight, 480, 4320);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(savedWidth, savedHeight));

            if (ViewModel.Settings.MainWindowIsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            _isWindowReady = true;

            AppWindow.Changed += (_, args) =>
            {
                if (!_isWindowReady) return;
                if (args.DidSizeChange || args.DidPresenterChange)
                {
                    SaveCurrentMainWindowBounds();
                }
            };
        }
        catch { }
    }

    private void SaveCurrentMainWindowBounds()
    {
        try
        {
            if (!_isWindowReady || AppWindow is null || ViewModel is null) return;
            if (AppWindow.Presenter is OverlappedPresenter pMin &&
                pMin.State == OverlappedPresenterState.Minimized)
            {
                return;
            }

            bool isMaximized = AppWindow.Presenter is OverlappedPresenter p &&
                               p.State == OverlappedPresenterState.Maximized;
            int width = AppWindow.Size.Width;
            int height = AppWindow.Size.Height;
            ViewModel.Settings.UpdateMainWindowBounds(width, height, isMaximized);
        }
        catch { }
    }

    public void RegisterChildWindow(Window child)
    {
        _childWindows.Add(child);
        child.Closed += (_, _) => _childWindows.Remove(child);
    }

    private void CloseChildWindows()
    {
        foreach (var win in _childWindows.ToArray())
        {
            try { win.Close(); } catch { }
        }
        _childWindows.Clear();
    }

    public void FocusFileList(PanelSide side)
    {
        ViewModel.ActiveSide = side;
        var panel = side == PanelSide.Left ? ViewModel.Left : ViewModel.Right;
        var list = side == PanelSide.Left ? LeftFileList : RightFileList;
        var savedFocus = panel.ActiveTab?.FocusedItem ?? panel.ActiveTab?.Items.FirstOrDefault();

        if (savedFocus is not null && panel.ActiveTab is not null)
        {
            panel.ActiveTab.FocusedItem = savedFocus;
            if (!ReferenceEquals(list.SelectedItem, savedFocus))
                list.SelectedItem = savedFocus;
            list.ScrollIntoView(savedFocus);
            list.UpdateLayout();

            if (list.ContainerFromItem(savedFocus) is Control container)
            {
                container.Focus(FocusState.Programmatic);
                panel.ActiveTab.FocusedItem = savedFocus;
                return;
            }
        }

        list.Focus(FocusState.Programmatic);
        if (savedFocus is not null && panel.ActiveTab is not null)
        {
            panel.ActiveTab.FocusedItem = savedFocus;
            if (!ReferenceEquals(list.SelectedItem, savedFocus))
                list.SelectedItem = savedFocus;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.ActiveSide != side || panel.ActiveTab is null) return;
                var currentFocus = panel.ActiveTab.FocusedItem ?? savedFocus;
                panel.ActiveTab.FocusedItem = currentFocus;
                list.ScrollIntoView(currentFocus);
                if (list.ContainerFromItem(currentFocus) is Control delayedContainer)
                    delayedContainer.Focus(FocusState.Programmatic);
                else
                    list.Focus(FocusState.Programmatic);
            });
        }
    }

    public void FocusActiveAddressBar()
    {
        var box = ViewModel.ActiveSide == PanelSide.Left ? LeftAddressBox : RightAddressBox;
        box.Focus(FocusState.Programmatic);
    }

    public void FocusActiveFilterBox()
    {
        var box = ViewModel.ActiveSide == PanelSide.Left ? LeftFilterBox : RightFilterBox;
        box.Focus(FocusState.Programmatic);
    }

    public void ToggleFullScreen()
    {
        if (AppWindow is null) return;
        AppWindow.SetPresenter(AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Overlapped
            : AppWindowPresenterKind.FullScreen);
    }

    public void OpenTearOutWindow(TabState tab)
    {
        var win = new MainWindow(tab);
        win.Activate();
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (ViewModel.IsSettingsOpen)
                ViewModel.IsSettingsOpen = false;
        };
        if (!ViewModel.IsSettingsOpen)
            ViewModel.IsSettingsOpen = true;
        _settingsWindow.Activate();
    }

    private void CloseSettingsWindow()
    {
        if (_settingsWindow is null) return;
        var win = _settingsWindow;
        _settingsWindow = null;
        try { win.Close(); } catch { }
    }

    private void OpenAboutWindow()
    {
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow(this, _updateService);
        _aboutWindow.Closed += (_, _) =>
        {
            _aboutWindow = null;
        };
        _aboutWindow.Activate();
    }

    private void CloseAboutWindow()
    {
        if (_aboutWindow is null) return;
        var win = _aboutWindow;
        _aboutWindow = null;
        try { win.Close(); } catch { }
    }

    private void UpdateDualLayout()
    {
        if (ViewModel.IsDualMode)
        {
            SplitterColumn.Width = new GridLength(6);
            RightColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitterBar.Visibility = Visibility.Visible;
            RightPanelBorder.Visibility = Visibility.Visible;
        }
        else
        {
            SplitterColumn.Width = new GridLength(0);
            RightColumn.Width = new GridLength(0);
            SplitterBar.Visibility = Visibility.Collapsed;
            RightPanelBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateActivePanelVisuals()
    {
        bool leftActive = ViewModel.ActiveSide == PanelSide.Left;
        bool isLight = ViewModel.Settings.Theme == AppTheme.Light;

        Color accentColor = ShellIconConverter.ParseHexColor(
            ViewModel.Settings.ActivePanelBorderColor,
            AppSettings.DefaultActivePanelBorderColor);

        var activeBrush = new SolidColorBrush(accentColor);
        var inactiveBrush = new SolidColorBrush(isLight
            ? Color.FromArgb(255, 204, 210, 220)
            : Color.FromArgb(255, 58, 58, 70));

        LeftPanelBorder.BorderBrush = leftActive ? activeBrush : inactiveBrush;
        RightPanelBorder.BorderBrush = leftActive ? inactiveBrush : activeBrush;
        LeftPanelBorder.BorderThickness = new Thickness(leftActive ? 2 : 1);
        RightPanelBorder.BorderThickness = new Thickness(leftActive ? 1 : 2);
        LeftPanelBorder.Opacity = leftActive ? 1.0 : 0.94;
        RightPanelBorder.Opacity = leftActive ? 0.94 : 1.0;
    }

    // ====================================================================
    // GLOBAL KEYBOARD ROUTING (PreviewKeyDown on window root)
    // ====================================================================

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e) { }
    private void OnRootKeyUp(object sender, KeyRoutedEventArgs e) { }

    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift)
        {
            ViewModel.IsShiftPressed = true;
            return;
        }

        // If a modal ContentDialog is open, let the dialog handle keyboard input
        if (VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot).Any(p => p.Child is ContentDialog))
            return;

        // Tab (and Shift+Tab) without Ctrl/Alt ALWAYS switches between the left and right explorer panels,
        // never cycling through title bar or toolbar buttons.
        if (e.Key == VirtualKey.Tab)
        {
            var mods = VirtualKeyMapper.GetCurrentModifiers();
            if (!mods.HasFlag(KeyModifiers.Ctrl) && !mods.HasFlag(KeyModifiers.Alt))
            {
                e.Handled = true;
                var targetSide = ViewModel.IsDualMode
                    ? (ViewModel.ActiveSide == PanelSide.Left ? PanelSide.Right : PanelSide.Left)
                    : PanelSide.Left;
                FocusFileList(targetSide);
                return;
            }
        }

        if (!VirtualKeyMapper.TryMap(e.Key, out var chord))
            return;

        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
        bool isTextInput = IsTextInputElement(focused);
        bool isAddressBar = IsDescendantOf(focused, LeftAddressBox) || IsDescendantOf(focused, RightAddressBox);
        bool isFileList = !isTextInput;

        // Windows Explorer shortcuts Ctrl+Shift+1..8 to switch view modes directly
        if (chord.Modifiers == (KeyModifiers.Ctrl | KeyModifiers.Shift) &&
            chord.Key is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8")
        {
            var activeTab = ViewModel.ActivePanel.ActiveTab;
            if (activeTab is not null)
            {
                var mode = chord.Key switch
                {
                    "1" => FileViewMode.ExtraLargeIcons,
                    "2" => FileViewMode.LargeIcons,
                    "3" => FileViewMode.MediumIcons,
                    "4" => FileViewMode.SmallIcons,
                    "5" => FileViewMode.List,
                    "6" => FileViewMode.Details,
                    "7" => FileViewMode.Tiles,
                    "8" => FileViewMode.Content,
                    _ => FileViewMode.Details
                };
                activeTab.SetViewMode(mode);
                UpdatePanelViewMode(ViewModel.ActiveSide);
                ViewModel.RequestSessionSave();
                Bindings.Update();
                e.Handled = true;
                return;
            }
        }

        // Move Up/Down/Left/Right/Home/End/PageUp/PageDown deterministically relative to FocusedItem
        if (isFileList && chord.Modifiers == KeyModifiers.None &&
            (chord.Key is "Up" or "Down" or "Home" or "End" or "PageUp" or "PageDown" ||
             (chord.Key is "Left" or "Right" && ViewModel.ActivePanel.ActiveTab is { ViewMode: not FileViewMode.Details and not FileViewMode.Content })))
        {
            var activeTab = ViewModel.ActivePanel.ActiveTab;
            var activeList = ViewModel.ActiveSide == PanelSide.Left ? LeftFileList : RightFileList;
            if (activeTab is not null && activeTab.Items.Count > 0)
            {
                switch (chord.Key)
                {
                    case "Up":
                    case "Left":
                        activeTab.MoveFocus(-1);
                        break;
                    case "Down":
                    case "Right":
                        activeTab.MoveFocus(1);
                        break;
                    case "Home":
                        activeTab.MoveFocusToFirst();
                        break;
                    case "End":
                        activeTab.MoveFocusToLast();
                        break;
                    case "PageUp":
                        activeTab.MoveFocus(-12);
                        break;
                    case "PageDown":
                        activeTab.MoveFocus(12);
                        break;
                }

                if (activeTab.FocusedItem is { } target)
                {
                    activeList.ScrollIntoView(target);
                    if (activeList.ContainerFromItem(target) is Control itemContainer)
                        itemContainer.Focus(FocusState.Programmatic);
                }

                e.Handled = true;
            }
            return;
        }

        var ctx = ViewModel.BuildFocusContext(isFileList, isAddressBar, isTextInput);
        var result = ViewModel.Router.Handle(chord, ctx);
        if (result != KeyRouteResult.NotHandled)
        {
            e.Handled = true;
        }
    }

    private void OnRootPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift)
            ViewModel.IsShiftPressed = false;
    }

    private static bool IsTextInputElement(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBox or AutoSuggestBox or PasswordBox or RichEditBox)
                return true;
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject target)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, target)) return true;
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void StartKeyBindingHotReload(KeyBindingStore store, WinUiDispatcher dispatcher)
    {
        try
        {
            var dir = Path.GetDirectoryName(_paths.KeyBindingsFile)!;
            var file = Path.GetFileName(_paths.KeyBindingsFile);
            _keyBindingWatcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _keyBindingWatcher.Changed += (_, _) => dispatcher.Post(() =>
            {
                store.Reload(_paths.KeyBindingsFile);
                ViewModel.StatusNotification = LocalizationService.Instance.EffectiveLanguage == "de"
                    ? "Tastenbelegung (keybindings.json) neu geladen"
                    : "Key bindings (keybindings.json) reloaded";
            });
        }
        catch { }
    }

    // ====================================================================
    // TOP BAR & F-KEY BAR HANDLERS
    // ====================================================================

    private void OnToggleOperationsClick(object sender, RoutedEventArgs e) =>
        ViewModel.IsOperationsPanelOpen = !ViewModel.IsOperationsPanelOpen;

    private void OnToggleDualClick(object sender, RoutedEventArgs e) => ExecuteCommand("layout.toggleDual");
    private void OnCommandPaletteClick(object sender, RoutedEventArgs e) => ExecuteCommand("palette.open");
    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettingsWindow();
    private void OnHelpClick(object sender, RoutedEventArgs e) => ExecuteCommand("app.help");
    private void OnAboutClick(object sender, RoutedEventArgs e) => OpenAboutWindow();

    private void OnF1Click(object sender, RoutedEventArgs e) => ExecuteCommand("app.help");
    private void OnF2Click(object sender, RoutedEventArgs e) => ExecuteCommand("file.rename");
    private void OnF3Click(object sender, RoutedEventArgs e) => ExecuteCommand("file.view");
    private void OnF4Click(object sender, RoutedEventArgs e) => ExecuteCommand("file.edit");
    private void OnF5Click(object sender, RoutedEventArgs e) => _ = ViewModel.CopyOrMoveAsync(OpKind.Copy, ViewModel.IsShiftPressed);
    private void OnF6Click(object sender, RoutedEventArgs e) => _ = ViewModel.CopyOrMoveAsync(OpKind.Move, ViewModel.IsShiftPressed);
    private void OnF7Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsShiftPressed) _ = ViewModel.CreateNewFolderAsync();
        else _ = ViewModel.CreateNewFileAsync();
    }
    private void OnF8Click(object sender, RoutedEventArgs e) => _ = ViewModel.DeleteTargetsAsync(recycle: !ViewModel.IsShiftPressed);
    private void OnF9Click(object sender, RoutedEventArgs e) => ExecuteCommand("terminal.open");
    private void OnF10Click(object sender, RoutedEventArgs e) => ExecuteCommand("palette.open");
    private void OnF11Click(object sender, RoutedEventArgs e) => ExecuteCommand("view.fullscreen");
    private void OnF12Click(object sender, RoutedEventArgs e) => ExecuteCommand(ViewModel.IsShiftPressed ? "layout.swapPanels" : "layout.toggleDual");

    private void ExecuteCommand(string id) =>
        ViewModel.Commands.Execute(id, null, FocusContext.For(ContextFlags.FileList, ContextFlags.Panel, ContextFlags.HasFocusedItem));

    // ====================================================================
    // IMAGE HOVER PREVIEW HANDLERS
    // ====================================================================

    private async void OnFileItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.Settings.EnableImageHoverPreview ||
            sender is not FrameworkElement { DataContext: FileItemViewModel item } ||
            !item.IsImageFile)
        {
            HideImageHoverPreview();
            return;
        }

        UpdateImageHoverPosition(e.GetCurrentPoint(RootGrid).Position);
        await ShowImageHoverPreviewAsync(item);
    }

    private void OnFileItemPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.Settings.EnableImageHoverPreview ||
            sender is not FrameworkElement { DataContext: FileItemViewModel item } ||
            !item.IsImageFile)
        {
            HideImageHoverPreview();
            return;
        }

        UpdateImageHoverPosition(e.GetCurrentPoint(RootGrid).Position);
    }

    private void OnFileItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        HideImageHoverPreview();
    }

    private void UpdateImageHoverPosition(Windows.Foundation.Point pointerPos)
    {
        const double offsetX = 22;
        const double offsetY = 18;
        double configuredWidth = ViewModel.Settings.ImageHoverCardMaxWidth;
        double configuredHeight = ViewModel.Settings.ImageHoverPreviewHeight + 60;
        double cardWidth = ImageHoverCard.ActualWidth > 0 ? ImageHoverCard.ActualWidth : configuredWidth;
        double cardHeight = ImageHoverCard.ActualHeight > 0 ? ImageHoverCard.ActualHeight : configuredHeight;
        double maxWidth = Math.Max(360, RootGrid.ActualWidth);
        double maxHeight = Math.Max(320, RootGrid.ActualHeight);

        double left = pointerPos.X + offsetX;
        double top = pointerPos.Y + offsetY;

        if (left + cardWidth + 16 > maxWidth)
            left = Math.Max(12, pointerPos.X - cardWidth - offsetX);

        if (top + cardHeight + 16 > maxHeight)
            top = Math.Max(12, maxHeight - cardHeight - 16);

        Canvas.SetLeft(ImageHoverCard, left);
        Canvas.SetTop(ImageHoverCard, top);
    }

    private async Task ShowImageHoverPreviewAsync(FileItemViewModel item)
    {
        string path = item.FullPath;
        if (string.Equals(_hoveredImagePath, path, StringComparison.OrdinalIgnoreCase) &&
            ImageHoverCanvas.Visibility == Visibility.Visible)
        {
            return;
        }

        _hoveredImagePath = path;
        int version = ++_hoverLoadVersion;

        try
        {
            if (!File.Exists(path))
            {
                HideImageHoverPreview();
                return;
            }

            byte[] bytes = await Task.Run(() =>
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > 40 * 1024 * 1024) // 40 MB safety limit for hover preview
                    return Array.Empty<byte>();
                var buffer = new byte[fs.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = fs.Read(buffer, read, buffer.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                return buffer;
            });

            if (version != _hoverLoadVersion || bytes.Length == 0 || !ViewModel.Settings.EnableImageHoverPreview)
                return;

            using var ms = new MemoryStream(bytes, writable: false);
            using var ras = ms.AsRandomAccessStream();

            int decodeWidth = (int)Math.Clamp(Math.Round(ViewModel.Settings.ImageHoverPreviewSize), 160, 720);
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = decodeWidth
            };
            await bitmap.SetSourceAsync(ras);

            if (version != _hoverLoadVersion || !ViewModel.Settings.EnableImageHoverPreview)
                return;

            HoverPreviewImage.Source = bitmap;
            HoverPreviewTitle.Text = item.Name;
            HoverPreviewMeta.Text = $"{item.TypeText.ToUpperInvariant()} • {item.SizeText} • {item.ModifiedText}";
            ImageHoverCanvas.Visibility = Visibility.Visible;
        }
        catch
        {
            if (version == _hoverLoadVersion)
                HideImageHoverPreview();
        }
    }

    private void HideImageHoverPreview()
    {
        _hoverLoadVersion++;
        _hoveredImagePath = null;
        ImageHoverCanvas.Visibility = Visibility.Collapsed;
        HoverPreviewImage.Source = null;
    }

    // ====================================================================
    // WINDOWS EXPLORER CONTEXT MENU ON RIGHT-CLICK
    // ====================================================================

    private async void OnFileItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        HideImageHoverPreview();
        if (sender is not FrameworkElement { DataContext: FileItemViewModel item } fe)
            return;

        bool isLeft = IsDescendantOf(fe, LeftFileList);
        var side = isLeft ? PanelSide.Left : PanelSide.Right;
        ViewModel.ActiveSide = side;

        var panel = side == PanelSide.Left ? ViewModel.Left : ViewModel.Right;
        var tab = panel.ActiveTab;
        if (tab is null) return;

        tab.FocusedItem = item;
        FocusFileList(side);
        e.Handled = true;

        if (item.IsParentLink || tab.IsVirtualArchive || tab.IsRemoteConnection)
        {
            ShowFallbackFileContextMenu(fe, e.GetPosition(fe), panel, tab, item);
            return;
        }

        IReadOnlyList<string> targetPaths;
        if (item.IsMarked)
        {
            var marked = tab.GetTargetPaths();
            targetPaths = marked.Count > 0 ? marked : [item.FullPath];
        }
        else
        {
            targetPaths = [item.FullPath];
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var result = ShellContextMenuService.ShowForPaths(hwnd, targetPaths, isShiftPressed: ViewModel.IsShiftPressed);
        if (result.Handled)
        {
            if (string.Equals(result.SelectedVerb, "rename", StringComparison.OrdinalIgnoreCase))
            {
                await ViewModel.RenameFocusedAsync();
            }
            else if (string.Equals(result.SelectedVerb, "open_folder", StringComparison.OrdinalIgnoreCase) && item.IsDirectory)
            {
                await tab.NavigateAsync(item.FullPath);
            }
            else if (!string.IsNullOrEmpty(result.SelectedVerb))
            {
                await Task.Delay(120);
                await tab.ReloadAsync();
            }

            Bindings.Update();
            return;
        }

        ShowFallbackFileContextMenu(fe, e.GetPosition(fe), panel, tab, item);
    }

    private void ShowFallbackFileContextMenu(
        FrameworkElement anchor,
        Windows.Foundation.Point position,
        PanelViewModel panel,
        TabViewModel tab,
        FileItemViewModel item)
    {
        var menu = new MenuFlyout();

        var openItem = new MenuFlyoutItem
        {
            Text = item.IsParentLink
                ? LocalizationService.T("Menu.OpenParent")
                : (item.IsDirectory ? LocalizationService.T("Menu.OpenFolder") : LocalizationService.T("Menu.OpenFile"))
        };
        openItem.Click += async (_, _) => { await ViewModel.OpenFocusedEntryAsync(); Bindings.Update(); };
        menu.Items.Add(openItem);

        if (item.IsDirectory && !item.IsParentLink)
        {
            var openNewTab = new MenuFlyoutItem { Text = LocalizationService.T("Menu.OpenInNewTab") };
            openNewTab.Click += async (_, _) => { await panel.CreateTabAsync(item.FullPath); Bindings.Update(); };
            menu.Items.Add(openNewTab);
        }

        if (!item.IsParentLink)
        {
            menu.Items.Add(new MenuFlyoutSeparator());

            if (!item.IsDirectory)
            {
                var viewItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.QuickView") };
                viewItem.Click += (_, _) => ExecuteCommand("file.view");
                menu.Items.Add(viewItem);

                var editItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.Edit") };
                editItem.Click += (_, _) => ExecuteCommand("file.edit");
                menu.Items.Add(editItem);

                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var copyItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.CopyToOpposite") };
            copyItem.Click += async (_, _) => await ViewModel.CopyOrMoveAsync(OpKind.Copy, false);
            menu.Items.Add(copyItem);

            var moveItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.MoveToOpposite") };
            moveItem.Click += async (_, _) => await ViewModel.CopyOrMoveAsync(OpKind.Move, false);
            menu.Items.Add(moveItem);

            var renameItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.Rename") };
            renameItem.Click += async (_, _) => await ViewModel.RenameFocusedAsync();
            menu.Items.Add(renameItem);

            var batchRenameItem = new MenuFlyoutItem { Text = "✏ Mehrfach-Umbenennen (Strg+M)" };
            batchRenameItem.Click += (_, _) => ExecuteCommand("file.batchRename");
            menu.Items.Add(batchRenameItem);

            var deleteItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.Delete") };
            deleteItem.Click += async (_, _) => await ViewModel.DeleteTargetsAsync(recycle: true);
            menu.Items.Add(deleteItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var packArchiveItem = new MenuFlyoutItem { Text = "📦 In Archiv packen (Alt+F5)" };
            packArchiveItem.Click += (_, _) => ExecuteCommand("archive.pack");
            menu.Items.Add(packArchiveItem);

            if (!item.IsDirectory && ModernExplorer.Core.Archive.ArchiveService.IsSupportedArchive(item.FullPath))
            {
                var unpackArchiveItem = new MenuFlyoutItem { Text = "📂 Archiv entpacken (Alt+F9)" };
                unpackArchiveItem.Click += (_, _) => ExecuteCommand("archive.extract");
                menu.Items.Add(unpackArchiveItem);
            }

            if (tab.IsRemoteConnection)
            {
                var chmodItem = new MenuFlyoutItem { Text = "🔐 Berechtigungen ändern (chmod)" };
                chmodItem.Click += (_, _) => ExecuteCommand("remote.chmod");
                menu.Items.Add(chmodItem);
            }

            menu.Items.Add(new MenuFlyoutSeparator());

            var copyPathItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.CopyPath") };
            copyPathItem.Click += (_, _) => ExecuteCommand("clipboard.copyPath");
            menu.Items.Add(copyPathItem);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var viewSubMenu = new MenuFlyoutSubItem
        {
            Text = LocalizationService.T("Panel.View.Button"),
            Icon = new FontIcon { Glyph = TabViewModel.GetViewModeGlyph(tab.ViewMode) }
        };
        PopulateViewModeMenuItems(viewSubMenu.Items, panel.Side, tab);
        menu.Items.Add(viewSubMenu);

        menu.ShowAt(anchor, position);
    }

    // ====================================================================
    // PANEL INTERACTIONS (LEFT & RIGHT)
    // ====================================================================

    private void OnLeftPanelPointerPressed(object sender, PointerRoutedEventArgs e) =>
        HandlePanelPointerPressed(PanelSide.Left, e.OriginalSource as DependencyObject);

    private void OnRightPanelPointerPressed(object sender, PointerRoutedEventArgs e) =>
        HandlePanelPointerPressed(PanelSide.Right, e.OriginalSource as DependencyObject);

    private void HandlePanelPointerPressed(PanelSide side, DependencyObject? source)
    {
        ViewModel.ActiveSide = side;
        if (!IsInteractiveSubControl(source))
        {
            FocusFileList(side);
        }
    }

    private static bool IsInteractiveSubControl(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBox or AutoSuggestBox or PasswordBox or RichEditBox or Button or ListViewItem)
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void OnLeftPanelGotFocus(object sender, RoutedEventArgs e) => ViewModel.ActiveSide = PanelSide.Left;
    private void OnRightPanelGotFocus(object sender, RoutedEventArgs e) => ViewModel.ActiveSide = PanelSide.Right;

    private void OnLeftListGotFocus(object sender, RoutedEventArgs e) => ViewModel.ActiveSide = PanelSide.Left;
    private void OnRightListGotFocus(object sender, RoutedEventArgs e) => ViewModel.ActiveSide = PanelSide.Right;

    private async void OnLeftListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        HideImageHoverPreview();
        ViewModel.ActiveSide = PanelSide.Left;
        await ViewModel.OpenFocusedEntryAsync();
    }

    private async void OnRightListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        HideImageHoverPreview();
        ViewModel.ActiveSide = PanelSide.Right;
        await ViewModel.OpenFocusedEntryAsync();
    }

    private void OnLeftListCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args) =>
        HandleQuickSearch(ViewModel.Left.ActiveTab, args);

    private void OnRightListCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args) =>
        HandleQuickSearch(ViewModel.Right.ActiveTab, args);

    private void HandleQuickSearch(TabViewModel? tab, CharacterReceivedRoutedEventArgs args)
    {
        if (tab is null || char.IsControl(args.Character) || args.Character == ' ') return;
        if (VirtualKeyMapper.GetCurrentModifiers().HasFlag(KeyModifiers.Ctrl) ||
            VirtualKeyMapper.GetCurrentModifiers().HasFlag(KeyModifiers.Alt))
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastQuickSearchKeyUtc).TotalMilliseconds > 800)
            _quickSearchBuffer = "";
        _lastQuickSearchKeyUtc = now;
        _quickSearchBuffer += args.Character;
        if (tab.JumpToPrefix(_quickSearchBuffer))
            args.Handled = true;
    }

    // Drive buttons (above the tab bar)
    private async void OnLeftDriveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string rootPath })
        {
            ViewModel.ActiveSide = PanelSide.Left;
            await ViewModel.Left.NavigateToDriveAsync(rootPath);
            FocusFileList(PanelSide.Left);
            Bindings.Update();
        }
    }

    private async void OnRightDriveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string rootPath })
        {
            ViewModel.ActiveSide = PanelSide.Right;
            await ViewModel.Right.NavigateToDriveAsync(rootPath);
            FocusFileList(PanelSide.Right);
            Bindings.Update();
        }
    }

    // Navigation buttons
    private async void OnLeftNewTabClick(object sender, RoutedEventArgs e) { await ViewModel.Left.CreateTabAsync(); Bindings.Update(); }
    private async void OnLeftCloseTabClick(object sender, RoutedEventArgs e) { await ViewModel.Left.CloseTabAsync(); Bindings.Update(); }
    private async void OnLeftBackClick(object sender, RoutedEventArgs e) { if (ViewModel.Left.ActiveTab is { } t) await t.GoBackAsync(); Bindings.Update(); }
    private async void OnLeftForwardClick(object sender, RoutedEventArgs e) { if (ViewModel.Left.ActiveTab is { } t) await t.GoForwardAsync(); Bindings.Update(); }
    private async void OnLeftUpClick(object sender, RoutedEventArgs e) { if (ViewModel.Left.ActiveTab is { } t) await t.GoUpAsync(); Bindings.Update(); }
    private async void OnLeftRefreshClick(object sender, RoutedEventArgs e) { if (ViewModel.Left.ActiveTab is { } t) await t.ReloadAsync(); Bindings.Update(); }
    private void OnLeftViewModeButtonClick(object sender, RoutedEventArgs e) => ShowViewModeFlyout(PanelSide.Left, LeftViewModeButton);

    private async void OnRightNewTabClick(object sender, RoutedEventArgs e) { await ViewModel.Right.CreateTabAsync(); Bindings.Update(); }
    private async void OnRightCloseTabClick(object sender, RoutedEventArgs e) { await ViewModel.Right.CloseTabAsync(); Bindings.Update(); }
    private async void OnRightBackClick(object sender, RoutedEventArgs e) { if (ViewModel.Right.ActiveTab is { } t) await t.GoBackAsync(); Bindings.Update(); }
    private async void OnRightForwardClick(object sender, RoutedEventArgs e) { if (ViewModel.Right.ActiveTab is { } t) await t.GoForwardAsync(); Bindings.Update(); }
    private async void OnRightUpClick(object sender, RoutedEventArgs e) { if (ViewModel.Right.ActiveTab is { } t) await t.GoUpAsync(); Bindings.Update(); }
    private async void OnRightRefreshClick(object sender, RoutedEventArgs e) { if (ViewModel.Right.ActiveTab is { } t) await t.ReloadAsync(); Bindings.Update(); }
    private void OnRightViewModeButtonClick(object sender, RoutedEventArgs e) => ShowViewModeFlyout(PanelSide.Right, RightViewModeButton);

    // ====================================================================
    // FAVORITES BAR, POWER-TOOLS TOOLBAR & QUICK-PREVIEW HANDLERS
    // ====================================================================

    private void OnToggleBookmarkClick(object sender, RoutedEventArgs e)
    {
        ExecuteCommand("favorites.toggle");
        Bindings.Update();
    }

    private async void OnFavoriteItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path } && !string.IsNullOrWhiteSpace(path))
        {
            await ViewModel.NavigateToFavoriteAsync(path);
            Bindings.Update();
        }
    }

    private void OnFavoriteItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModernExplorer.Core.Favorites.FavoriteItem fav } fe)
        {
            var menu = new MenuFlyout();
            var openTabItem = new MenuFlyoutItem { Text = "In neuem Tab öffnen" };
            openTabItem.Click += async (_, _) =>
            {
                await ViewModel.ActivePanel.CreateTabAsync(fav.Path);
                Bindings.Update();
            };
            menu.Items.Add(openTabItem);

            var removeItem = new MenuFlyoutItem { Text = "Aus Favoriten entfernen" };
            removeItem.Click += (_, _) =>
            {
                ViewModel.Favorites.RemoveBookmark(fav.Path);
                ViewModel.UpdateTabsBookmarkedState();
                Bindings.Update();
            };
            menu.Items.Add(removeItem);
            menu.ShowAt(fe, e.GetPosition(fe));
            e.Handled = true;
        }
    }

    private void OnFavoritesBarDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "Zu Favoriten hinzufügen";
            e.Handled = true;
        }
    }

    private async void OnFavoritesBarDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.Path) && Directory.Exists(item.Path))
                {
                    ViewModel.Favorites.AddBookmark(item.Path);
                }
            }
            ViewModel.UpdateTabsBookmarkedState();
            Bindings.Update();
        }
        catch
        {
            // Ignore drop errors
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => ExecuteCommand("edit.undo");
    private void OnBatchRenameClick(object sender, RoutedEventArgs e) => ExecuteCommand("file.batchRename");
    private void OnDeepSearchClick(object sender, RoutedEventArgs e) => ExecuteCommand("tools.deepSearch");
    private void OnDirectorySyncClick(object sender, RoutedEventArgs e) => ExecuteCommand("tools.syncDirs");
    private void OnDuplicateFinderClick(object sender, RoutedEventArgs e) => ExecuteCommand("tools.findDuplicates");
    private void OnPackArchiveClick(object sender, RoutedEventArgs e) => ExecuteCommand("archive.pack");
    private void OnRemoteConnectClick(object sender, RoutedEventArgs e) => ExecuteCommand("tools.remoteConnect");
    private void OnToggleQuickPreviewClick(object sender, RoutedEventArgs e) => ExecuteCommand("view.quickPreview");

    private async void OnLeftBranchViewClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveSide = PanelSide.Left;
        if (ViewModel.Left.ActiveTab is { } t)
        {
            await t.ToggleBranchViewAsync();
            Bindings.Update();
        }
    }

    private async void OnLeftTreeSizeClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveSide = PanelSide.Left;
        if (ViewModel.Left.ActiveTab is { } t)
        {
            await t.ToggleTreeSizeAsync();
            Bindings.Update();
        }
    }

    private async void OnRightBranchViewClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveSide = PanelSide.Right;
        if (ViewModel.Right.ActiveTab is { } t)
        {
            await t.ToggleBranchViewAsync();
            Bindings.Update();
        }
    }

    private async void OnRightTreeSizeClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveSide = PanelSide.Right;
        if (ViewModel.Right.ActiveTab is { } t)
        {
            await t.ToggleTreeSizeAsync();
            Bindings.Update();
        }
    }

    private async Task RefreshQuickPreviewPanelAsync()
    {
        if (!ViewModel.IsQuickPreviewPanelOpen) return;

        var item = ViewModel.ActivePanel.ActiveTab?.FocusedItem;
        if (item is null || item.IsParentLink)
        {
            QuickPreviewTitleText.Text = "Schnellvorschau (Strg+Q)";
            QuickPreviewMetaText.Text = "Keine Datei ausgewählt";
            QuickPreviewImageContainer.Visibility = Visibility.Collapsed;
            QuickPreviewTextBox.Visibility = Visibility.Visible;
            QuickPreviewTextBox.Text = "";
            return;
        }

        QuickPreviewTitleText.Text = item.Name;
        QuickPreviewMetaText.Text = item.IsDirectory
            ? $"Ordner • {item.ModifiedText}"
            : $"{item.SizeText} • {item.ModifiedText}";

        if (item.IsDirectory)
        {
            QuickPreviewImageContainer.Visibility = Visibility.Collapsed;
            QuickPreviewTextBox.Visibility = Visibility.Visible;
            try
            {
                if (Directory.Exists(item.FullPath))
                {
                    var entries = Directory.EnumerateFileSystemEntries(item.FullPath).Take(80).Select(Path.GetFileName).ToList();
                    QuickPreviewTextBox.Text = entries.Count == 0
                        ? "(Leerer Ordner)"
                        : $"Ordnerinhalt ({entries.Count} Einträge):\n\n" + string.Join("\n", entries);
                }
                else
                {
                    QuickPreviewTextBox.Text = item.FullPath;
                }
            }
            catch (Exception ex)
            {
                QuickPreviewTextBox.Text = ex.Message;
            }
            return;
        }

        if (!File.Exists(item.FullPath))
        {
            QuickPreviewImageContainer.Visibility = Visibility.Collapsed;
            QuickPreviewTextBox.Visibility = Visibility.Visible;
            QuickPreviewTextBox.Text = $"Virtueller / Remote-Eintrag:\n{item.FullPath}\n\nDrücken Sie F3 für die vollständige Schnellansicht.";
            return;
        }

        if (item.IsImageFile)
        {
            try
            {
                QuickPreviewTextBox.Visibility = Visibility.Collapsed;
                QuickPreviewImageContainer.Visibility = Visibility.Visible;
                using var stream = File.OpenRead(item.FullPath);
                var bmp = new BitmapImage { DecodePixelWidth = 420 };
                await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                QuickPreviewImage.Source = bmp;
                return;
            }
            catch
            {
                // Fall back to text/hex preview
            }
        }

        QuickPreviewImageContainer.Visibility = Visibility.Collapsed;
        QuickPreviewTextBox.Visibility = Visibility.Visible;
        var preview = await FileContentService.LoadPreviewAsync(item.FullPath, maxBytes: 32768);
        QuickPreviewTextBox.Text = preview.Content;
    }

    // ====================================================================
    // VIEW MODES (8 WINDOWS EXPLORER VIEW MODES)
    // ====================================================================

    private void AttachPanelTabSubscription(PanelSide side)
    {
        if (side == PanelSide.Left)
        {
            if (_subscribedLeftTab is not null)
                _subscribedLeftTab.PropertyChanged -= OnLeftActiveTabPropertyChanged;
            _subscribedLeftTab = ViewModel.Left.ActiveTab;
            if (_subscribedLeftTab is not null)
                _subscribedLeftTab.PropertyChanged += OnLeftActiveTabPropertyChanged;
        }
        else
        {
            if (_subscribedRightTab is not null)
                _subscribedRightTab.PropertyChanged -= OnRightActiveTabPropertyChanged;
            _subscribedRightTab = ViewModel.Right.ActiveTab;
            if (_subscribedRightTab is not null)
                _subscribedRightTab.PropertyChanged += OnRightActiveTabPropertyChanged;
        }
    }

    private void OnLeftActiveTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.ViewMode))
        {
            UpdatePanelViewMode(PanelSide.Left);
            ViewModel.RequestSessionSave();
            Bindings.Update();
        }
        else if (e.PropertyName == nameof(TabViewModel.FocusedItem))
        {
            if (ViewModel.IsQuickPreviewPanelOpen && ViewModel.ActiveSide == PanelSide.Left)
                _ = RefreshQuickPreviewPanelAsync();
        }
        else if (e.PropertyName is nameof(TabViewModel.CurrentPath) or nameof(TabViewModel.IsBookmarked)
                 or nameof(TabViewModel.GitSummaryBadge) or nameof(TabViewModel.IsBranchView) or nameof(TabViewModel.IsTreeSizeActive))
        {
            Bindings.Update();
        }
    }

    private void OnRightActiveTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.ViewMode))
        {
            UpdatePanelViewMode(PanelSide.Right);
            ViewModel.RequestSessionSave();
            Bindings.Update();
        }
        else if (e.PropertyName == nameof(TabViewModel.FocusedItem))
        {
            if (ViewModel.IsQuickPreviewPanelOpen && ViewModel.ActiveSide == PanelSide.Right)
                _ = RefreshQuickPreviewPanelAsync();
        }
        else if (e.PropertyName is nameof(TabViewModel.CurrentPath) or nameof(TabViewModel.IsBookmarked)
                 or nameof(TabViewModel.GitSummaryBadge) or nameof(TabViewModel.IsBranchView) or nameof(TabViewModel.IsTreeSizeActive))
        {
            Bindings.Update();
        }
    }

    public void UpdatePanelViewMode(PanelSide side)
    {
        var panel = side == PanelSide.Left ? ViewModel.Left : ViewModel.Right;
        var list = side == PanelSide.Left ? LeftFileList : RightFileList;
        var headerGrid = side == PanelSide.Left ? LeftColumnHeaderGrid : RightColumnHeaderGrid;
        if (list is null || headerGrid is null) return;

        var mode = panel.ActiveTab?.ViewMode ?? ViewModel.Settings.DefaultViewMode;
        headerGrid.Visibility = mode == FileViewMode.Details ? Visibility.Visible : Visibility.Collapsed;

        var currentApplied = side == PanelSide.Left ? _currentLeftViewMode : _currentRightViewMode;
        if (currentApplied == mode)
            return;

        var (panelKey, templateKey) = mode switch
        {
            FileViewMode.ExtraLargeIcons => ("ExtraLargeIconsItemsPanel", "ExtraLargeIconsItemTemplate"),
            FileViewMode.LargeIcons      => ("LargeIconsItemsPanel", "LargeIconsItemTemplate"),
            FileViewMode.MediumIcons     => ("MediumIconsItemsPanel", "MediumIconsItemTemplate"),
            FileViewMode.SmallIcons      => ("SmallIconsItemsPanel", "SmallIconsItemTemplate"),
            FileViewMode.List            => ("ListItemsPanel", "ListItemTemplate"),
            FileViewMode.Details         => ("VerticalStackItemsPanel", "DetailsItemTemplate"),
            FileViewMode.Tiles           => ("TilesItemsPanel", "TilesItemTemplate"),
            FileViewMode.Content         => ("VerticalStackItemsPanel", "ContentItemTemplate"),
            _                            => ("VerticalStackItemsPanel", "DetailsItemTemplate")
        };

        if (RootGrid.Resources.TryGetValue(panelKey, out var panelObj) && panelObj is ItemsPanelTemplate itemsPanel)
            list.ItemsPanel = itemsPanel;

        if (RootGrid.Resources.TryGetValue(templateKey, out var templateObj) && templateObj is DataTemplate itemTemplate)
            list.ItemTemplate = itemTemplate;

        if (side == PanelSide.Left)
            _currentLeftViewMode = mode;
        else
            _currentRightViewMode = mode;

        Bindings.Update();
    }

    private void ShowViewModeFlyout(PanelSide side, FrameworkElement anchor)
    {
        ViewModel.ActiveSide = side;
        var panel = side == PanelSide.Left ? ViewModel.Left : ViewModel.Right;
        var tab = panel.ActiveTab;
        if (tab is null) return;

        var flyout = new MenuFlyout();
        PopulateViewModeMenuItems(flyout.Items, side, tab);
        flyout.ShowAt(anchor);
    }

    private void PopulateViewModeMenuItems(IList<MenuFlyoutItemBase> menuItems, PanelSide side, TabViewModel tab)
    {
        foreach (var mode in TabViewModel.AllViewModes)
        {
            var capturedMode = mode;
            var item = new ToggleMenuFlyoutItem
            {
                Text = TabViewModel.GetViewModeDisplayName(capturedMode),
                IsChecked = tab.ViewMode == capturedMode,
                Icon = new FontIcon { Glyph = TabViewModel.GetViewModeGlyph(capturedMode) },
                KeyboardAcceleratorTextOverride = TabViewModel.GetViewModeShortcutText(capturedMode)
            };
            item.Click += (_, _) =>
            {
                ViewModel.ActiveSide = side;
                tab.SetViewMode(capturedMode);
                UpdatePanelViewMode(side);
                ViewModel.RequestSessionSave();
                Bindings.Update();
                FocusFileList(side);
            };
            menuItems.Add(item);

            if (capturedMode is FileViewMode.SmallIcons or FileViewMode.Details)
            {
                menuItems.Add(new MenuFlyoutSeparator());
            }
        }
    }

    // Breadcrumbs
    private async void OnLeftBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path } && ViewModel.Left.ActiveTab is { } t)
        {
            await t.NavigateAsync(path);
            Bindings.Update();
        }
    }

    private async void OnRightBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path } && ViewModel.Right.ActiveTab is { } t)
        {
            await t.NavigateAsync(path);
            Bindings.Update();
        }
    }

    // Address Bar + AutoComplete
    private void OnLeftAddressTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && ViewModel.Left.ActiveTab is { } t)
            sender.ItemsSource = t.GetAutoCompleteSuggestions(sender.Text);
    }

    private void OnRightAddressTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && ViewModel.Right.ActiveTab is { } t)
            sender.ItemsSource = t.GetAutoCompleteSuggestions(sender.Text);
    }

    private async void OnLeftAddressQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        await SubmitAddressAsync(PanelSide.Left, args.ChosenSuggestion as string ?? args.QueryText);

    private async void OnRightAddressQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        await SubmitAddressAsync(PanelSide.Right, args.ChosenSuggestion as string ?? args.QueryText);

    private async Task SubmitAddressAsync(PanelSide side, string text)
    {
        ViewModel.ActiveSide = side;
        var trimmed = text.Trim();
        if (trimmed.StartsWith('>'))
        {
            _dialogService.ShowCommandPalette();
            return;
        }
        var panel = side == PanelSide.Left ? ViewModel.Left : ViewModel.Right;
        if (panel.ActiveTab is { } tab)
            await tab.NavigateAsync(trimmed);
        FocusFileList(side);
        Bindings.Update();
    }

    // Column Sorting
    private void OnLeftSortNameClick(object sender, RoutedEventArgs e) { ViewModel.Left.ActiveTab?.ApplySort(SortColumn.Name); Bindings.Update(); }
    private void OnLeftSortSizeClick(object sender, RoutedEventArgs e) { ViewModel.Left.ActiveTab?.ApplySort(SortColumn.Size); Bindings.Update(); }
    private void OnLeftSortModifiedClick(object sender, RoutedEventArgs e) { ViewModel.Left.ActiveTab?.ApplySort(SortColumn.Modified); Bindings.Update(); }
    private void OnLeftSortTypeClick(object sender, RoutedEventArgs e) { ViewModel.Left.ActiveTab?.ApplySort(SortColumn.Type); Bindings.Update(); }

    private void OnRightSortNameClick(object sender, RoutedEventArgs e) { ViewModel.Right.ActiveTab?.ApplySort(SortColumn.Name); Bindings.Update(); }
    private void OnRightSortSizeClick(object sender, RoutedEventArgs e) { ViewModel.Right.ActiveTab?.ApplySort(SortColumn.Size); Bindings.Update(); }
    private void OnRightSortModifiedClick(object sender, RoutedEventArgs e) { ViewModel.Right.ActiveTab?.ApplySort(SortColumn.Modified); Bindings.Update(); }
    private void OnRightSortTypeClick(object sender, RoutedEventArgs e) { ViewModel.Right.ActiveTab?.ApplySort(SortColumn.Type); Bindings.Update(); }

    // ====================================================================
    // TAB INTERACTIONS: MIDDLE-CLICK, CONTEXT MENU, DRAG & DROP
    // ====================================================================

    private async void OnTabItemPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint( sender as UIElement).Properties;
        if (props.PointerUpdateKind != PointerUpdateKind.MiddleButtonReleased) return;
        if (sender is not FrameworkElement { DataContext: TabViewModel tab }) return;

        var panel = ViewModel.Left.Tabs.Contains(tab) ? ViewModel.Left : ViewModel.Right;
        await panel.CloseTabAsync(tab);
        Bindings.Update();
    }

    private void OnTabItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel tab } fe) return;
        var panel = ViewModel.Left.Tabs.Contains(tab) ? ViewModel.Left : ViewModel.Right;
        panel.ActiveTab = tab;
        ViewModel.ActiveSide = panel.Side;

        var menu = new MenuFlyout();

        var newItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.Tab.New") };
        newItem.Click += async (_, _) => { await panel.CreateTabAsync(); Bindings.Update(); };
        menu.Items.Add(newItem);

        var dupItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.Tab.Duplicate") };
        dupItem.Click += async (_, _) => { await panel.DuplicateActiveTabAsync(); Bindings.Update(); };
        menu.Items.Add(dupItem);

        var pinItem = new MenuFlyoutItem { Text = tab.IsPinned ? LocalizationService.T("Menu.Tab.Unpin") : LocalizationService.T("Menu.Tab.Pin") };
        pinItem.Click += (_, _) => { panel.TogglePinActiveTab(); Bindings.Update(); };
        menu.Items.Add(pinItem);

        var lockItem = new MenuFlyoutItem { Text = tab.IsLocked ? LocalizationService.T("Menu.Tab.Unlock") : LocalizationService.T("Menu.Tab.Lock") };
        lockItem.Click += (_, _) => { panel.ToggleLockActiveTab(); Bindings.Update(); };
        menu.Items.Add(lockItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var moveOppItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.Tab.MoveOpposite") };
        moveOppItem.Click += async (_, _) => { await ViewModel.MoveActiveTabToOppositePanelAsync(tab); Bindings.Update(); };
        menu.Items.Add(moveOppItem);

        var tearOutItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.Tab.TearOut") };
        tearOutItem.Click += (_, _) =>
        {
            var state = panel.DetachTab(tab);
            if (state is not null) OpenTearOutWindow(state);
            Bindings.Update();
        };
        menu.Items.Add(tearOutItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var closeItem = new MenuFlyoutItem { Text = LocalizationService.T("Menu.Tab.Close"), IsEnabled = !tab.IsPinned };
        closeItem.Click += async (_, _) => { await panel.CloseTabAsync(tab); Bindings.Update(); };
        menu.Items.Add(closeItem);

        menu.ShowAt(fe, e.GetPosition(fe));
    }

    private void OnTabDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is not TabViewModel tab) return;
        var sourceSide = ViewModel.Left.Tabs.Contains(tab) ? PanelSide.Left : PanelSide.Right;
        var payload = new TabDragPayload(sourceSide, tab.Id, tab.ToState());
        e.Data.SetData(TabDragFormatId, JsonSerializer.Serialize(payload, SessionStore.JsonOptions));
        e.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
    }

    private void OnTabListDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(TabDragFormatId))
        {
            bool copy = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control);
            e.AcceptedOperation = copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
        }
    }

    private async void OnLeftTabListDrop(object sender, DragEventArgs e) =>
        await HandleTabListDropAsync(ViewModel.Left, e);

    private async void OnRightTabListDrop(object sender, DragEventArgs e) =>
        await HandleTabListDropAsync(ViewModel.Right, e);

    private async Task HandleTabListDropAsync(PanelViewModel targetPanel, DragEventArgs e)
    {
        if (!e.DataView.Contains(TabDragFormatId)) return;
        var json = await e.DataView.GetDataAsync(TabDragFormatId) as string;
        if (string.IsNullOrEmpty(json)) return;
        var payload = JsonSerializer.Deserialize<TabDragPayload>(json, SessionStore.JsonOptions);
        if (payload is null) return;

        var sourcePanel = payload.SourceSide == PanelSide.Left ? ViewModel.Left : ViewModel.Right;
        if (ReferenceEquals(sourcePanel, targetPanel)) return; // Reordering within the same panel is handled by ListView

        bool duplicate = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control);
        var sourceTab = sourcePanel.Tabs.FirstOrDefault(t => t.Id == payload.TabId);
        if (sourceTab is not null && !duplicate)
            sourcePanel.DetachTab(sourceTab);

        var state = payload.State;
        if (duplicate)
        {
            state.Id = Guid.NewGuid();
            state.Pinned = false;
        }

        await targetPanel.AddTabFromStateAsync(state, activate: true);
        ViewModel.ActiveSide = targetPanel.Side;
        Bindings.Update();
    }

    private void OnTabItemFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            var panel = ViewModel.Left.Tabs.Contains(tab) ? ViewModel.Left : ViewModel.Right;
            panel.ActiveTab = tab;
        }
        bool move = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift);
        e.AcceptedOperation = move ? DataPackageOperation.Move : DataPackageOperation.Copy;
    }

    private async void OnTabItemFileDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        if (sender is not FrameworkElement { DataContext: TabViewModel tab } || string.IsNullOrEmpty(tab.CurrentPath)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (paths.Count == 0) return;

        bool move = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift);
        _operationQueue.Enqueue(new FileOperation(move ? OpKind.Move : OpKind.Copy, paths, tab.CurrentPath, new OperationOptions()));
    }

    private sealed record TabDragPayload(PanelSide SourceSide, Guid TabId, TabState State);
}
