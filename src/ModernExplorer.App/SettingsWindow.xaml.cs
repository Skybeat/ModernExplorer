using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Settings;
using ModernExplorer.Core.Update;
using ModernExplorer.Core.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace ModernExplorer.App;

public sealed partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private bool _suppressLanguageEvent;

    public SettingsViewModel Settings { get; }

    public SettingsWindow(MainWindow owner)
    {
        _owner = owner;
        Settings = owner.ViewModel.Settings;

        InitializeComponent();
        ConfigureWindowAndCenterOverOwner();

        PopulateLanguageComboBox();
        InstalledVersionLabel.Text = $"v{GitHubUpdateService.GetCurrentVersionString()}";

        ApplyTheme(Settings.Theme);
        ApplyLocalization();

        Settings.ThemeChanged += OnThemeChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        RootGrid.Loaded += (_, _) =>
        {
            ApplyTheme(Settings.Theme);
            ApplyLocalization();
            Bindings.Update();
        };

        Closed += (_, _) =>
        {
            Settings.ThemeChanged -= OnThemeChanged;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            SaveCurrentBounds();
        };
    }

    public string FormatActiveTheme(string themeName) => $"Theme: {themeName}";

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme(Settings.Theme);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        PopulateLanguageComboBox();
        ApplyLocalization();
    }

    private void PopulateLanguageComboBox()
    {
        if (LanguageComboBox is null) return;
        _suppressLanguageEvent = true;
        try
        {
            var options = LocalizationService.Instance.GetLocalizedLanguageOptions();
            LanguageComboBox.ItemsSource = options;
            var selected = options.FirstOrDefault(o => string.Equals(o.Code, Settings.Language, StringComparison.OrdinalIgnoreCase))
                        ?? options[0];
            LanguageComboBox.SelectedItem = selected;
        }
        finally
        {
            _suppressLanguageEvent = false;
        }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        Title = loc["Settings.Title"];
        if (WindowTitleLabel is not null) WindowTitleLabel.Text = loc["Settings.Title"];
        if (CategoriesHeaderLabel is not null) CategoriesHeaderLabel.Text = loc["Settings.Categories"];
        if (LanguageCardTitle is not null) LanguageCardTitle.Text = "🌐 " + loc["Settings.Language.Title"];
        if (LanguageCardDesc is not null) LanguageCardDesc.Text = loc["Settings.Language.Desc"];
        if (UpdateCardTitle is not null) UpdateCardTitle.Text = "🚀 " + loc["Settings.Update.Title"];
        if (CheckUpdatesNowButton is not null) CheckUpdatesNowButton.Content = loc["Settings.Update.CheckNow"];
        if (CloseSettingsButton is not null) CloseSettingsButton.Content = $"{loc["Settings.Close"]} (Esc)";
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvent) return;
        if (LanguageComboBox.SelectedItem is LanguageOption opt)
        {
            Settings.SetLanguage(opt.Code);
        }
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        await _owner.CheckForGitHubUpdatesAsync(manualTrigger: true);
    }

    private void ConfigureWindowAndCenterOverOwner()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(SettingsTitleBarDragRegion);
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            if (AppWindow is { } aw)
            {
                if (aw.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = true;
                    presenter.IsMaximizable = true;
                    presenter.IsMinimizable = false;
                }

                int w = Math.Clamp(Settings.SettingsWindowWidth <= 720 ? AppSettings.DefaultSettingsWindowWidth : Settings.SettingsWindowWidth, 860, 3840);
                int h = Math.Clamp(Settings.SettingsWindowHeight, 560, 2160);

                if (_owner.AppWindow is { } ownerAw)
                {
                    var pos = ownerAw.Position;
                    var size = ownerAw.Size;
                    int x = pos.X + (size.Width - w) / 2;
                    int y = pos.Y + (size.Height - h) / 2;
                    aw.MoveAndResize(new RectInt32(x, y, w, h));
                }
                else
                {
                    aw.Resize(new SizeInt32(w, h));
                }

                if (Settings.SettingsWindowIsMaximized && aw.Presenter is OverlappedPresenter maxPresenter)
                    maxPresenter.Maximize();

                aw.Changed += (_, args) =>
                {
                    if (args.DidSizeChange || args.DidPresenterChange)
                        SaveCurrentBounds();
                };
            }
        }
        catch { }
    }

    private void SaveCurrentBounds()
    {
        try
        {
            if (AppWindow is { } aw)
            {
                if (aw.Presenter is OverlappedPresenter pMin && pMin.State == OverlappedPresenterState.Minimized)
                    return;

                bool isMax = aw.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Maximized;
                Settings.UpdateSettingsWindowBounds(aw.Size.Width, aw.Size.Height, isMax);
            }
        }
        catch { }
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryAppearancePanel is null) return;

        string tag = (CategoryListView.SelectedItem as ListViewItem)?.Tag as string ?? "Appearance";

        CategoryAppearancePanel.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        CategoryColorsPanel.Visibility = tag == "Colors" ? Visibility.Visible : Visibility.Collapsed;
        CategoryBehaviorPanel.Visibility = tag == "Behavior" ? Visibility.Visible : Visibility.Collapsed;
        CategoryPreviewPanel.Visibility = tag == "Preview" ? Visibility.Visible : Visibility.Collapsed;
        CategoryFontsPanel.Visibility = tag == "Fonts" ? Visibility.Visible : Visibility.Collapsed;
        CategorySessionPanel.Visibility = tag == "Session" ? Visibility.Visible : Visibility.Collapsed;

        ContentScrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
    }

    public void ApplyTheme(AppTheme theme)
    {
        bool isLight = theme == AppTheme.Light;
        WindowChromeHelper.ApplyCustomTitleBarAndTheme(this, RootGrid, SettingsTitleBarDragRegion, theme);

        if (DarkThemeButton is not null && LightThemeButton is not null)
        {
            DarkThemeButton.BorderThickness = new Thickness(isLight ? 1 : 2);
            LightThemeButton.BorderThickness = new Thickness(isLight ? 2 : 1);
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnSelectDarkThemeClick(object sender, RoutedEventArgs e) => Settings.SetTheme(AppTheme.Dark);
    private void OnSelectLightThemeClick(object sender, RoutedEventArgs e) => Settings.SetTheme(AppTheme.Light);

    // 1. Active explorer panel border color
    private void OnActivePanelColorPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            Settings.SetActivePanelBorderColor(hex);
            ActivePanelBorderHexBox.Text = Settings.ActivePanelBorderColor;
            Bindings.Update();
        }
    }

    private void OnApplyActivePanelColorClick(object sender, RoutedEventArgs e)
    {
        Settings.SetActivePanelBorderColor(ActivePanelBorderHexBox.Text);
        ActivePanelBorderHexBox.Text = Settings.ActivePanelBorderColor;
        Bindings.Update();
    }

    // 2. Selection border color in file list
    private void OnColorPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            Settings.SetSelectionBorderColor(hex);
            BorderColorHexBox.Text = Settings.SelectionBorderColor;
            Bindings.Update();
        }
    }

    private void OnApplyCustomColorClick(object sender, RoutedEventArgs e)
    {
        Settings.SetSelectionBorderColor(BorderColorHexBox.Text);
        BorderColorHexBox.Text = Settings.SelectionBorderColor;
        Bindings.Update();
    }

    // 3. Marked item text color
    private void OnMarkedTextColorPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            Settings.SetMarkedItemTextColor(hex);
            MarkedTextColorHexBox.Text = Settings.MarkedItemTextColor;
            Bindings.Update();
        }
    }

    private void OnApplyMarkedTextColorClick(object sender, RoutedEventArgs e)
    {
        Settings.SetMarkedItemTextColor(MarkedTextColorHexBox.Text);
        MarkedTextColorHexBox.Text = Settings.MarkedItemTextColor;
        Bindings.Update();
    }

    private void OnSaveSessionNowClick(object sender, RoutedEventArgs e)
    {
        _owner.ViewModel.SaveSessionImmediately();
        _owner.ViewModel.StatusNotification = Settings.SaveSession
            ? "Session saved"
            : "Session saving is disabled in settings";
    }

    private void OnResetWindowBoundsClick(object sender, RoutedEventArgs e)
    {
        Settings.ResetWindowBoundsToDefaults();
        try
        {
            if (AppWindow is { } aw)
            {
                if (aw.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Maximized)
                    p.Restore();
                aw.Resize(new SizeInt32(AppSettings.DefaultSettingsWindowWidth, AppSettings.DefaultSettingsWindowHeight));
            }
        }
        catch { }
        Bindings.Update();
        _owner.ViewModel.StatusNotification = "Window bounds reset to defaults";
    }

    private void OnResetFontSizesClick(object sender, RoutedEventArgs e)
    {
        Settings.ResetFontSizesToDefaults();
        _owner.ViewModel.StatusNotification = "Font sizes reset to defaults";
    }

    private void OnResetAllSettingsClick(object sender, RoutedEventArgs e)
    {
        Settings.ResetAllToDefaults();
        ActivePanelBorderHexBox.Text = Settings.ActivePanelBorderColor;
        BorderColorHexBox.Text = Settings.SelectionBorderColor;
        MarkedTextColorHexBox.Text = Settings.MarkedItemTextColor;
        PopulateLanguageComboBox();
        Bindings.Update();
        _owner.ViewModel.StatusNotification = "All settings reset to factory defaults";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
