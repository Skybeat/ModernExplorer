using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Session;
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
    private bool _suppressViewModeEvent;

    public SettingsViewModel Settings { get; }

    public SettingsWindow(MainWindow owner)
    {
        _owner = owner;
        Settings = owner.ViewModel.Settings;

        InitializeComponent();
        ConfigureWindowAndCenterOverOwner();

        PopulateLanguageComboBox();
        PopulateDefaultViewModeComboBox();
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

    public string FormatActiveTheme(string themeName) =>
        LocalizationService.Format("Settings.ActiveThemePrefix", themeName);

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyTheme(Settings.Theme);
        Bindings.Update();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        PopulateLanguageComboBox();
        PopulateDefaultViewModeComboBox();
        ApplyLocalization();
        Bindings.Update();
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

    private void PopulateDefaultViewModeComboBox()
    {
        if (DefaultViewModeComboBox is null) return;
        _suppressViewModeEvent = true;
        try
        {
            DefaultViewModeComboBox.Items.Clear();
            ComboBoxItem? selectedItem = null;
            foreach (var mode in TabViewModel.AllViewModes)
            {
                var item = new ComboBoxItem
                {
                    Tag = mode,
                    Content = $"{TabViewModel.GetViewModeDisplayName(mode)} ({TabViewModel.GetViewModeShortcutText(mode)})"
                };
                DefaultViewModeComboBox.Items.Add(item);
                if (mode == Settings.DefaultViewMode)
                    selectedItem = item;
            }
            DefaultViewModeComboBox.SelectedItem = selectedItem ?? DefaultViewModeComboBox.Items.FirstOrDefault();
        }
        finally
        {
            _suppressViewModeEvent = false;
        }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        Title = loc["Settings.Title"];
        if (WindowTitleLabel is not null) WindowTitleLabel.Text = loc["Settings.Title"];
        if (CategoriesHeaderLabel is not null) CategoriesHeaderLabel.Text = loc["Settings.Categories"];
        if (ActiveThemeBadgeLabel is not null) ActiveThemeBadgeLabel.Text = FormatActiveTheme(Settings.ThemeDisplayName);

        // Left Category Navigation
        if (CatAppearanceTitle is not null) CatAppearanceTitle.Text = loc["Settings.Nav.Appearance.Title"];
        if (CatAppearanceSub is not null) CatAppearanceSub.Text = loc["Settings.Nav.Appearance.Sub"];
        if (CatColorsTitle is not null) CatColorsTitle.Text = loc["Settings.Nav.Colors.Title"];
        if (CatColorsSub is not null) CatColorsSub.Text = loc["Settings.Nav.Colors.Sub"];
        if (CatBehaviorTitle is not null) CatBehaviorTitle.Text = loc["Settings.Nav.Behavior.Title"];
        if (CatBehaviorSub is not null) CatBehaviorSub.Text = loc["Settings.Nav.Behavior.Sub"];
        if (CatPreviewTitle is not null) CatPreviewTitle.Text = loc["Settings.Nav.Preview.Title"];
        if (CatPreviewSub is not null) CatPreviewSub.Text = loc["Settings.Nav.Preview.Sub"];
        if (CatFontsTitle is not null) CatFontsTitle.Text = loc["Settings.Nav.Fonts.Title"];
        if (CatFontsSub is not null) CatFontsSub.Text = loc["Settings.Nav.Fonts.Sub"];
        if (CatSessionTitle is not null) CatSessionTitle.Text = loc["Settings.Nav.Session.Title"];
        if (CatSessionSub is not null) CatSessionSub.Text = loc["Settings.Nav.Session.Sub"];

        // Category 1: Appearance & Design
        if (AppearanceHeaderTitle is not null) AppearanceHeaderTitle.Text = loc["Settings.Appearance.Header"];
        if (AppearanceHeaderSub is not null) AppearanceHeaderSub.Text = loc["Settings.Appearance.Sub"];
        if (LanguageCardTitle is not null) LanguageCardTitle.Text = "🌐 " + loc["Settings.Language.Title"];
        if (LanguageCardDesc is not null) LanguageCardDesc.Text = loc["Settings.Language.Desc"];
        if (ThemeCardTitle is not null) ThemeCardTitle.Text = loc["Settings.Theme.CardTitle"];
        if (ThemeCardDesc is not null) ThemeCardDesc.Text = loc["Settings.Theme.CardDesc"];
        if (DarkThemeTitle is not null) DarkThemeTitle.Text = loc["Settings.Theme.DarkTitle"];
        if (DarkThemeDesc is not null) DarkThemeDesc.Text = loc["Settings.Theme.DarkDesc"];
        if (LightThemeTitle is not null) LightThemeTitle.Text = loc["Settings.Theme.LightTitle"];
        if (LightThemeDesc is not null) LightThemeDesc.Text = loc["Settings.Theme.LightDesc"];
        if (ToolbarsCardTitle is not null) ToolbarsCardTitle.Text = loc["Settings.Toolbars.CardTitle"];
        if (ToggleDriveBar is not null)
        {
            ToggleDriveBar.Header = loc["Settings.Toolbars.DriveBar.Header"];
            ToggleDriveBar.OnContent = loc["Settings.Toolbars.DriveBar.On"];
            ToggleDriveBar.OffContent = loc["Settings.Toolbars.DriveBar.Off"];
        }
        if (ToggleFunctionKeyBar is not null)
        {
            ToggleFunctionKeyBar.Header = loc["Settings.Toolbars.FnBar.Header"];
            ToggleFunctionKeyBar.OnContent = loc["Settings.Toolbars.FnBar.On"];
            ToggleFunctionKeyBar.OffContent = loc["Settings.Toolbars.FnBar.Off"];
        }
        if (ToggleShowToolTips is not null)
        {
            ToggleShowToolTips.Header = loc["Settings.Toolbars.ToolTips.Header"];
            ToggleShowToolTips.OnContent = loc["Settings.Toolbars.ToolTips.On"];
            ToggleShowToolTips.OffContent = loc["Settings.Toolbars.ToolTips.Off"];
        }
        if (RowHeightLabel is not null) RowHeightLabel.Text = loc["Settings.Toolbars.RowHeight"];

        // Category 2: Colors & Highlighting
        if (ColorsHeaderTitle is not null) ColorsHeaderTitle.Text = loc["Settings.Colors.Header"];
        if (ColorsHeaderSub is not null) ColorsHeaderSub.Text = loc["Settings.Colors.Sub"];
        if (ColorActivePanelTitle is not null) ColorActivePanelTitle.Text = loc["Settings.Colors.ActivePanel.Title"];
        if (ColorActivePanelDesc is not null) ColorActivePanelDesc.Text = loc["Settings.Colors.ActivePanel.Desc"];
        if (ColorActivePanelApplyBtn is not null) ColorActivePanelApplyBtn.Content = loc["Settings.Apply"];
        if (ColorActivePanelPreviewText is not null) ColorActivePanelPreviewText.Text = loc["Settings.Colors.ActivePanel.Preview"];
        if (ColorSelectionTitle is not null) ColorSelectionTitle.Text = loc["Settings.Colors.Selection.Title"];
        if (ColorSelectionDesc is not null) ColorSelectionDesc.Text = loc["Settings.Colors.Selection.Desc"];
        if (ColorSelectionApplyBtn is not null) ColorSelectionApplyBtn.Content = loc["Settings.Apply"];
        if (ColorSelectionPreviewText is not null) ColorSelectionPreviewText.Text = loc["Settings.Colors.Selection.Preview"];
        if (ColorMarkedTextTitle is not null) ColorMarkedTextTitle.Text = loc["Settings.Colors.Marked.Title"];
        if (ColorMarkedTextDesc is not null) ColorMarkedTextDesc.Text = loc["Settings.Colors.Marked.Desc"];
        if (ColorMarkedTextApplyBtn is not null) ColorMarkedTextApplyBtn.Content = loc["Settings.Apply"];
        if (ColorMarkedTextPreviewText is not null) ColorMarkedTextPreviewText.Text = loc["Settings.Colors.Marked.Preview"];
        if (ColorTreemapTitle is not null) ColorTreemapTitle.Text = loc["Settings.Colors.Treemap.Title"];
        if (ColorTreemapDesc is not null) ColorTreemapDesc.Text = loc["Settings.Colors.Treemap.Desc"];
        if (TreemapCatVideoLabel is not null) TreemapCatVideoLabel.Text = loc["Settings.Colors.Treemap.CatVideo"];
        if (TreemapCatArchiveLabel is not null) TreemapCatArchiveLabel.Text = loc["Settings.Colors.Treemap.CatArchive"];
        if (TreemapCatVdiskLabel is not null) TreemapCatVdiskLabel.Text = loc["Settings.Colors.Treemap.CatVdisk"];
        if (TreemapCatCodeLabel is not null) TreemapCatCodeLabel.Text = loc["Settings.Colors.Treemap.CatCode"];
        if (TreemapCatPictureLabel is not null) TreemapCatPictureLabel.Text = loc["Settings.Colors.Treemap.CatPic"];
        if (TreemapCatBinaryLabel is not null) TreemapCatBinaryLabel.Text = loc["Settings.Colors.Treemap.CatBin"];
        if (TreemapCatOtherLabel is not null) TreemapCatOtherLabel.Text = loc["Settings.Colors.Treemap.CatOther"];
        if (TreemapApplyBtn1 is not null) TreemapApplyBtn1.Content = loc["Settings.Apply"];
        if (TreemapApplyBtn2 is not null) TreemapApplyBtn2.Content = loc["Settings.Apply"];
        if (TreemapApplyBtn3 is not null) TreemapApplyBtn3.Content = loc["Settings.Apply"];
        if (TreemapApplyBtn4 is not null) TreemapApplyBtn4.Content = loc["Settings.Apply"];
        if (TreemapApplyBtn5 is not null) TreemapApplyBtn5.Content = loc["Settings.Apply"];
        if (TreemapApplyBtn6 is not null) TreemapApplyBtn6.Content = loc["Settings.Apply"];
        if (TreemapApplyBtn7 is not null) TreemapApplyBtn7.Content = loc["Settings.Apply"];
        if (ResetTreemapColorsButton is not null) ResetTreemapColorsButton.Content = loc["Settings.Colors.Treemap.ResetButton"];

        // Category 3: File List & Behavior
        if (BehaviorHeaderTitle is not null) BehaviorHeaderTitle.Text = loc["Settings.Behavior.Header"];
        if (BehaviorHeaderSub is not null) BehaviorHeaderSub.Text = loc["Settings.Behavior.Sub"];
        if (DefaultViewModeTitle is not null) DefaultViewModeTitle.Text = loc["Settings.Behavior.DefaultViewModeTitle"];
        if (DefaultViewModeDesc is not null) DefaultViewModeDesc.Text = loc["Settings.Behavior.DefaultViewModeDesc"];
        if (ToggleFoldersFirst is not null)
        {
            ToggleFoldersFirst.Header = loc["Settings.Behavior.FoldersFirst.Header"];
            ToggleFoldersFirst.OnContent = loc["Settings.Behavior.FoldersFirst.On"];
            ToggleFoldersFirst.OffContent = loc["Settings.Behavior.FoldersFirst.Off"];
        }
        if (ToggleFileExtensions is not null)
        {
            ToggleFileExtensions.Header = loc["Settings.Behavior.Extensions.Header"];
            ToggleFileExtensions.OnContent = loc["Settings.Behavior.Extensions.On"];
            ToggleFileExtensions.OffContent = loc["Settings.Behavior.Extensions.Off"];
        }
        if (ToggleHiddenFiles is not null)
        {
            ToggleHiddenFiles.Header = loc["Settings.Behavior.Hidden.Header"];
            ToggleHiddenFiles.OnContent = loc["Settings.Behavior.Hidden.On"];
            ToggleHiddenFiles.OffContent = loc["Settings.Behavior.Hidden.Off"];
        }
        if (ToggleFolderSizeOnMark is not null)
        {
            ToggleFolderSizeOnMark.Header = loc["Settings.Behavior.FolderSize.Header"];
            ToggleFolderSizeOnMark.OnContent = loc["Settings.Behavior.FolderSize.On"];
            ToggleFolderSizeOnMark.OffContent = loc["Settings.Behavior.FolderSize.Off"];
        }
        if (ToggleConfirmRecycleBin is not null)
        {
            ToggleConfirmRecycleBin.Header = loc["Settings.Behavior.ConfirmDelete.Header"];
            ToggleConfirmRecycleBin.OnContent = loc["Settings.Behavior.ConfirmDelete.On"];
            ToggleConfirmRecycleBin.OffContent = loc["Settings.Behavior.ConfirmDelete.Off"];
        }

        // Category 4: Image Preview
        if (PreviewHeaderTitle is not null) PreviewHeaderTitle.Text = loc["Settings.Preview.Header"];
        if (PreviewHeaderSub is not null) PreviewHeaderSub.Text = loc["Settings.Preview.Sub"];
        if (ToggleImageHoverPreview is not null)
        {
            ToggleImageHoverPreview.Header = loc["Settings.Preview.Hover.Header"];
            ToggleImageHoverPreview.OnContent = loc["Settings.Preview.Hover.On"];
            ToggleImageHoverPreview.OffContent = loc["Settings.Preview.Hover.Off"];
        }
        if (PreviewMaxSizeLabel is not null) PreviewMaxSizeLabel.Text = loc["Settings.Preview.MaxSize"];
        if (PreviewFormatsNote is not null) PreviewFormatsNote.Text = loc["Settings.Preview.FormatsNote"];

        // Category 5: Font Sizes
        if (FontsHeaderTitle is not null) FontsHeaderTitle.Text = loc["Settings.Fonts.Header"];
        if (FontsHeaderSub is not null) FontsHeaderSub.Text = loc["Settings.Fonts.Sub"];
        if (FontGlobalCardTitle is not null) FontGlobalCardTitle.Text = loc["Settings.Fonts.GlobalCardTitle"];
        if (UseGlobalFontSizeCheckBox is not null) UseGlobalFontSizeCheckBox.Content = loc["Settings.Fonts.UseGlobalCheckBox"];
        if (FontGlobalSliderLabel is not null) FontGlobalSliderLabel.Text = loc["Settings.Fonts.GlobalSliderLabel"];
        if (FontMainWinCardTitle is not null) FontMainWinCardTitle.Text = loc["Settings.Fonts.MainWinCardTitle"];
        if (FontFileListLabel is not null) FontFileListLabel.Text = loc["Settings.Fonts.FileList"];
        if (FontFileListDetailsLabel is not null) FontFileListDetailsLabel.Text = loc["Settings.Fonts.FileListDetails"];
        if (FontTabsLabel is not null) FontTabsLabel.Text = loc["Settings.Fonts.Tabs"];
        if (FontDriveBarLabel is not null) FontDriveBarLabel.Text = loc["Settings.Fonts.DriveBar"];
        if (FontAddressBarLabel is not null) FontAddressBarLabel.Text = loc["Settings.Fonts.AddressBar"];
        if (FontBreadcrumbsLabel is not null) FontBreadcrumbsLabel.Text = loc["Settings.Fonts.Breadcrumbs"];
        if (FontStatusBarLabel is not null) FontStatusBarLabel.Text = loc["Settings.Fonts.StatusBar"];
        if (FontFunctionBarLabel is not null) FontFunctionBarLabel.Text = loc["Settings.Fonts.FunctionBar"];
        if (FontChildWinCardTitle is not null) FontChildWinCardTitle.Text = loc["Settings.Fonts.ChildWinCardTitle"];
        if (FontChildWinCardDesc is not null) FontChildWinCardDesc.Text = loc["Settings.Fonts.ChildWinCardDesc"];
        if (FontQuickViewerLabel is not null) FontQuickViewerLabel.Text = loc["Settings.Fonts.QuickViewer"];
        if (FontEditorLabel is not null) FontEditorLabel.Text = loc["Settings.Fonts.Editor"];
        if (FontBatchRenameLabel is not null) FontBatchRenameLabel.Text = loc["Settings.Fonts.BatchRename"];
        if (FontDirectorySyncLabel is not null) FontDirectorySyncLabel.Text = loc["Settings.Fonts.DirectorySync"];
        if (FontDeepSearchLabel is not null) FontDeepSearchLabel.Text = loc["Settings.Fonts.DeepSearch"];
        if (FontDuplicateFinderLabel is not null) FontDuplicateFinderLabel.Text = loc["Settings.Fonts.DuplicateFinder"];
        if (FontStorageAnalyzerLabel is not null) FontStorageAnalyzerLabel.Text = loc["Settings.Fonts.StorageAnalyzer"];
        if (FontFtpManagerLabel is not null) FontFtpManagerLabel.Text = loc["Settings.Fonts.RemoteManager"];
        if (FontArchiveDialogLabel is not null) FontArchiveDialogLabel.Text = loc["Settings.Fonts.ArchiveDialog"];
        if (FontAboutWindowLabel is not null) FontAboutWindowLabel.Text = loc["Settings.Fonts.AboutWindow"];
        if (ResetFontSizesButton is not null) ResetFontSizesButton.Content = loc["Settings.Fonts.ResetButton"];

        // Category 6: Session, Updates & Windows
        if (SessionHeaderTitle is not null) SessionHeaderTitle.Text = loc["Settings.Session.Header"];
        if (SessionHeaderSub is not null) SessionHeaderSub.Text = loc["Settings.Session.Sub"];
        if (UpdateCardTitle is not null) UpdateCardTitle.Text = "🚀 " + loc["Settings.Update.Title"];
        if (ToggleCheckUpdatesStartup is not null)
        {
            ToggleCheckUpdatesStartup.Header = loc["Settings.Update.Toggle.Header"];
            ToggleCheckUpdatesStartup.OnContent = loc["Settings.Update.Toggle.On"];
            ToggleCheckUpdatesStartup.OffContent = loc["Settings.Update.Toggle.Off"];
        }
        if (CheckUpdatesNowButton is not null) CheckUpdatesNowButton.Content = loc["Settings.Update.CheckNow"];
        if (SessionCardTitle is not null) SessionCardTitle.Text = loc["Settings.SessionCard.Title"];
        if (ToggleSaveSession is not null)
        {
            ToggleSaveSession.Header = loc["Settings.SessionCard.Toggle.Header"];
            ToggleSaveSession.OnContent = loc["Settings.SessionCard.Toggle.On"];
            ToggleSaveSession.OffContent = loc["Settings.SessionCard.Toggle.Off"];
        }
        if (SessionCardDesc is not null) SessionCardDesc.Text = loc["Settings.SessionCard.Desc"];
        if (SaveSessionNowButton is not null) SaveSessionNowButton.Content = loc["Settings.SessionCard.SaveNowButton"];
        if (WindowBoundsCardTitle is not null) WindowBoundsCardTitle.Text = loc["Settings.BoundsCard.Title"];
        if (WindowBoundsCardDesc is not null) WindowBoundsCardDesc.Text = loc["Settings.BoundsCard.Desc"];
        if (BoundsMainWinLabel is not null) BoundsMainWinLabel.Text = loc["Settings.BoundsCard.MainWin"];
        if (BoundsSettingsWinLabel is not null) BoundsSettingsWinLabel.Text = loc["Settings.BoundsCard.SettingsWin"];
        if (BoundsChildWinLabel is not null) BoundsChildWinLabel.Text = loc["Settings.BoundsCard.ChildWin"];
        if (ResetWindowBoundsButton is not null) ResetWindowBoundsButton.Content = loc["Settings.BoundsCard.ResetButton"];
        if (FactoryResetCardTitle is not null) FactoryResetCardTitle.Text = loc["Settings.ResetCard.Title"];
        if (FactoryResetCardDesc is not null) FactoryResetCardDesc.Text = loc["Settings.ResetCard.Desc"];
        if (FactoryResetButton is not null) FactoryResetButton.Content = loc["Settings.ResetCard.Button"];

        // Footer
        if (SettingsFooterNote is not null) SettingsFooterNote.Text = loc["Settings.Footer.AutoSaveNote"];
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

    private void OnDefaultViewModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressViewModeEvent) return;
        if (DefaultViewModeComboBox.SelectedItem is ComboBoxItem { Tag: FileViewMode mode })
        {
            Settings.DefaultViewMode = mode;
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

    // 4. Storage Analyzer Treemap category colors
    private void OnTreemapColorPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && tag.Split(':', 2) is [var catStr, var hex] &&
            Enum.TryParse<ModernExplorer.Core.FileSystem.StorageCategory>(catStr, ignoreCase: true, out var cat))
        {
            Settings.SetTreemapCategoryColor(cat, hex);
            SyncTreemapColorHexBoxes();
            Bindings.Update();
        }
    }

    private void OnApplyTreemapHexColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string catStr } &&
            Enum.TryParse<ModernExplorer.Core.FileSystem.StorageCategory>(catStr, ignoreCase: true, out var cat))
        {
            string inputHex = cat switch
            {
                ModernExplorer.Core.FileSystem.StorageCategory.VideoMedia => TreemapVideoHexBox.Text,
                ModernExplorer.Core.FileSystem.StorageCategory.ArchiveImage => TreemapArchiveHexBox.Text,
                ModernExplorer.Core.FileSystem.StorageCategory.VirtualDiskSystem => TreemapVdiskHexBox.Text,
                ModernExplorer.Core.FileSystem.StorageCategory.CodeDocument => TreemapCodeHexBox.Text,
                ModernExplorer.Core.FileSystem.StorageCategory.PictureGraphic => TreemapPictureHexBox.Text,
                ModernExplorer.Core.FileSystem.StorageCategory.BinaryExecutable => TreemapBinaryHexBox.Text,
                _ => TreemapOtherHexBox.Text,
            };
            Settings.SetTreemapCategoryColor(cat, inputHex);
            SyncTreemapColorHexBoxes();
            Bindings.Update();
        }
    }

    private void OnResetTreemapColorsClick(object sender, RoutedEventArgs e)
    {
        Settings.ResetTreemapColorsToDefaults();
        SyncTreemapColorHexBoxes();
        Bindings.Update();
        _owner.ViewModel.StatusNotification = LocalizationService.T("Notify.TreemapColorsReset");
    }

    private void SyncTreemapColorHexBoxes()
    {
        if (TreemapVideoHexBox is not null) TreemapVideoHexBox.Text = Settings.TreemapVideoMediaColor;
        if (TreemapArchiveHexBox is not null) TreemapArchiveHexBox.Text = Settings.TreemapArchiveImageColor;
        if (TreemapVdiskHexBox is not null) TreemapVdiskHexBox.Text = Settings.TreemapVirtualDiskSystemColor;
        if (TreemapCodeHexBox is not null) TreemapCodeHexBox.Text = Settings.TreemapCodeDocumentColor;
        if (TreemapPictureHexBox is not null) TreemapPictureHexBox.Text = Settings.TreemapPictureGraphicColor;
        if (TreemapBinaryHexBox is not null) TreemapBinaryHexBox.Text = Settings.TreemapBinaryExecutableColor;
        if (TreemapOtherHexBox is not null) TreemapOtherHexBox.Text = Settings.TreemapOtherTempColor;
    }

    private void OnSaveSessionNowClick(object sender, RoutedEventArgs e)
    {
        _owner.ViewModel.SaveSessionImmediately();
        _owner.ViewModel.StatusNotification = Settings.SaveSession
            ? LocalizationService.T("Notify.SessionSaved")
            : LocalizationService.T("Notify.SessionDisabled");
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
        _owner.ViewModel.StatusNotification = LocalizationService.T("Notify.BoundsReset");
    }

    private void OnResetFontSizesClick(object sender, RoutedEventArgs e)
    {
        Settings.ResetFontSizesToDefaults();
        Bindings.Update();
        _owner.ViewModel.StatusNotification = LocalizationService.T("Notify.FontsReset");
    }

    private void OnResetAllSettingsClick(object sender, RoutedEventArgs e)
    {
        Settings.ResetAllToDefaults();
        ActivePanelBorderHexBox.Text = Settings.ActivePanelBorderColor;
        BorderColorHexBox.Text = Settings.SelectionBorderColor;
        MarkedTextColorHexBox.Text = Settings.MarkedItemTextColor;
        SyncTreemapColorHexBoxes();
        PopulateLanguageComboBox();
        PopulateDefaultViewModeComboBox();
        ApplyLocalization();
        Bindings.Update();
        _owner.ViewModel.StatusNotification = LocalizationService.T("Notify.FactoryReset");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
