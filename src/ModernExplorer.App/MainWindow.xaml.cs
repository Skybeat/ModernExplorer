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
    private bool _isWindowReady;
    private string _quickSearchBuffer = "";
    private DateTime _lastQuickSearchKeyUtc;
    private string? _hoveredImagePath;
    private int _hoverLoadVersion;

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

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDualMode))
                UpdateDualLayout();
            else if (e.PropertyName == nameof(MainViewModel.ActiveSide))
                UpdateActivePanelVisuals();
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
            CloseChildWindows();
            ViewModel.SaveSessionImmediately();
            _keyBindingWatcher?.Dispose();
            await ViewModel.DisposeAsync();
        };
    }

    public Visibility HasText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public string FormatFKey(string key, string label) => $"{key} {label}";

    public string FormatActiveTheme(string themeName) => $"Theme: {themeName}";

    public void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
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
        if (LeftColNameText is not null) LeftColNameText.Text = loc["Col.Name"];
        if (LeftColSizeText is not null) LeftColSizeText.Text = loc["Col.Size"];
        if (LeftColModifiedText is not null) LeftColModifiedText.Text = loc["Col.Modified"];
        if (LeftColTypeText is not null) LeftColTypeText.Text = loc["Col.Extension"];

        if (RightColNameText is not null) RightColNameText.Text = loc["Col.Name"];
        if (RightColSizeText is not null) RightColSizeText.Text = loc["Col.Size"];
        if (RightColModifiedText is not null) RightColModifiedText.Text = loc["Col.Modified"];
        if (RightColTypeText is not null) RightColTypeText.Text = loc["Col.Extension"];

        if (LeftFilterBox is not null) LeftFilterBox.PlaceholderText = loc["Panel.FilterPlaceholder"];
        if (RightFilterBox is not null) RightFilterBox.PlaceholderText = loc["Panel.FilterPlaceholder"];
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

        // Move Up/Down/Home/End/PageUp/PageDown deterministically relative to FocusedItem
        if (isFileList && chord.Modifiers == KeyModifiers.None &&
            chord.Key is "Up" or "Down" or "Home" or "End" or "PageUp" or "PageDown")
        {
            var activeTab = ViewModel.ActivePanel.ActiveTab;
            var activeList = ViewModel.ActiveSide == PanelSide.Left ? LeftFileList : RightFileList;
            if (activeTab is not null && activeTab.Items.Count > 0)
            {
                switch (chord.Key)
                {
                    case "Up":
                        activeTab.MoveFocus(-1);
                        break;
                    case "Down":
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
                ViewModel.StatusNotification = "Tastenbelegung (keybindings.json) neu geladen";
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

        if (item.IsParentLink)
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
                ? "Übergeordneten Ordner öffnen (Backspace)"
                : (item.IsDirectory ? "Ordner öffnen (Enter)" : "Öffnen (Enter)")
        };
        openItem.Click += async (_, _) => { await ViewModel.OpenFocusedEntryAsync(); Bindings.Update(); };
        menu.Items.Add(openItem);

        if (item.IsDirectory && !item.IsParentLink)
        {
            var openNewTab = new MenuFlyoutItem { Text = "In neuem Tab öffnen" };
            openNewTab.Click += async (_, _) => { await panel.CreateTabAsync(item.FullPath); Bindings.Update(); };
            menu.Items.Add(openNewTab);
        }

        if (!item.IsParentLink)
        {
            menu.Items.Add(new MenuFlyoutSeparator());

            if (!item.IsDirectory)
            {
                var viewItem = new MenuFlyoutItem { Text = "Schnellansicht (F3)" };
                viewItem.Click += (_, _) => ExecuteCommand("file.view");
                menu.Items.Add(viewItem);

                var editItem = new MenuFlyoutItem { Text = "Bearbeiten (F4)" };
                editItem.Click += (_, _) => ExecuteCommand("file.edit");
                menu.Items.Add(editItem);

                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var copyItem = new MenuFlyoutItem { Text = "Kopieren ins Gegenpanel (F5)" };
            copyItem.Click += async (_, _) => await ViewModel.CopyOrMoveAsync(OpKind.Copy, false);
            menu.Items.Add(copyItem);

            var moveItem = new MenuFlyoutItem { Text = "Verschieben ins Gegenpanel (F6)" };
            moveItem.Click += async (_, _) => await ViewModel.CopyOrMoveAsync(OpKind.Move, false);
            menu.Items.Add(moveItem);

            var renameItem = new MenuFlyoutItem { Text = "Umbenennen (F2)" };
            renameItem.Click += async (_, _) => await ViewModel.RenameFocusedAsync();
            menu.Items.Add(renameItem);

            var deleteItem = new MenuFlyoutItem { Text = "Löschen (F8)" };
            deleteItem.Click += async (_, _) => await ViewModel.DeleteTargetsAsync(recycle: true);
            menu.Items.Add(deleteItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var copyPathItem = new MenuFlyoutItem { Text = "Pfad kopieren (Strg+Shift+C)" };
            copyPathItem.Click += (_, _) => ExecuteCommand("clip.copyPaths");
            menu.Items.Add(copyPathItem);
        }

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

    private async void OnRightNewTabClick(object sender, RoutedEventArgs e) { await ViewModel.Right.CreateTabAsync(); Bindings.Update(); }
    private async void OnRightCloseTabClick(object sender, RoutedEventArgs e) { await ViewModel.Right.CloseTabAsync(); Bindings.Update(); }
    private async void OnRightBackClick(object sender, RoutedEventArgs e) { if (ViewModel.Right.ActiveTab is { } t) await t.GoBackAsync(); Bindings.Update(); }
    private async void OnRightForwardClick(object sender, RoutedEventArgs e) { if (ViewModel.Right.ActiveTab is { } t) await t.GoForwardAsync(); Bindings.Update(); }
    private async void OnRightUpClick(object sender, RoutedEventArgs e) { if (ViewModel.Right.ActiveTab is { } t) await t.GoUpAsync(); Bindings.Update(); }
    private async void OnRightRefreshClick(object sender, RoutedEventArgs e) { if (ViewModel.Right.ActiveTab is { } t) await t.ReloadAsync(); Bindings.Update(); }

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

        var newItem = new MenuFlyoutItem { Text = "Neuer Tab (Strg+T)" };
        newItem.Click += async (_, _) => { await panel.CreateTabAsync(); Bindings.Update(); };
        menu.Items.Add(newItem);

        var dupItem = new MenuFlyoutItem { Text = "Tab duplizieren (Strg+Shift+D)" };
        dupItem.Click += async (_, _) => { await panel.DuplicateActiveTabAsync(); Bindings.Update(); };
        menu.Items.Add(dupItem);

        var pinItem = new MenuFlyoutItem { Text = tab.IsPinned ? "Tab lösen (Strg+K Strg+P)" : "Tab anheften (Strg+K Strg+P)" };
        pinItem.Click += (_, _) => { panel.TogglePinActiveTab(); Bindings.Update(); };
        menu.Items.Add(pinItem);

        var lockItem = new MenuFlyoutItem { Text = tab.IsLocked ? "Tab entsperren (Strg+K Strg+L)" : "Tab sperren (Strg+K Strg+L)" };
        lockItem.Click += (_, _) => { panel.ToggleLockActiveTab(); Bindings.Update(); };
        menu.Items.Add(lockItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var moveOppItem = new MenuFlyoutItem { Text = "Ins Gegenpanel verschieben (Strg+Shift+←/→)" };
        moveOppItem.Click += async (_, _) => { await ViewModel.MoveActiveTabToOppositePanelAsync(tab); Bindings.Update(); };
        menu.Items.Add(moveOppItem);

        var tearOutItem = new MenuFlyoutItem { Text = "In neues Fenster herauslösen (Strg+Shift+N)" };
        tearOutItem.Click += (_, _) =>
        {
            var state = panel.DetachTab(tab);
            if (state is not null) OpenTearOutWindow(state);
            Bindings.Update();
        };
        menu.Items.Add(tearOutItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var closeItem = new MenuFlyoutItem { Text = "Tab schließen (Strg+W)", IsEnabled = !tab.IsPinned };
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
