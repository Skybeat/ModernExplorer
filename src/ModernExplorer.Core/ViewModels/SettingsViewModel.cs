using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Session;
using ModernExplorer.Core.Settings;

namespace ModernExplorer.Core.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private SettingsStore? _store;
    private bool _suppressSave;

    /// <summary>
    /// Current global instance used for live bindings inside DataTemplates.
    /// </summary>
    public static SettingsViewModel Current { get; private set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme), nameof(IsLightTheme), nameof(ThemeDisplayName))]
    private AppTheme _theme = AppTheme.Dark;

    [ObservableProperty]
    private string _language = AppSettings.DefaultLanguage;

    [ObservableProperty]
    private bool _checkForUpdatesOnStartup = true;

    [ObservableProperty]
    private bool _saveSession = true;

    [ObservableProperty]
    private FileViewMode _defaultViewMode = FileViewMode.Details;

    [ObservableProperty]
    private bool _showHiddenFilesByDefault;

    [ObservableProperty]
    private bool _showFileExtensions = true;

    [ObservableProperty]
    private bool _sortFoldersFirst = true;

    [ObservableProperty]
    private bool _confirmDeleteToRecycleBin = true;

    [ObservableProperty]
    private bool _autoCalculateFolderSizeOnMark = true;

    [ObservableProperty]
    private bool _showDriveBar = true;

    [ObservableProperty]
    private bool _showFunctionKeyBar = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileListRowHeightText))]
    private double _fileListRowHeight = AppSettings.DefaultFileListRowHeight;

    [ObservableProperty]
    private bool _enableImageHoverPreview = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageHoverPreviewSizeText), nameof(ImageHoverPreviewHeight), nameof(ImageHoverCardMaxWidth))]
    private double _imageHoverPreviewSize = AppSettings.DefaultImageHoverPreviewSize;

    [ObservableProperty]
    private string _selectionBorderColor = AppSettings.DefaultSelectionBorderColor;

    [ObservableProperty]
    private string _activePanelBorderColor = AppSettings.DefaultActivePanelBorderColor;

    [ObservableProperty]
    private string _markedItemTextColor = AppSettings.DefaultMarkedItemTextColor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainWindowBoundsSummary))]
    private int _mainWindowWidth = AppSettings.DefaultMainWindowWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainWindowBoundsSummary))]
    private int _mainWindowHeight = AppSettings.DefaultMainWindowHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainWindowBoundsSummary))]
    private bool _mainWindowIsMaximized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsWindowBoundsSummary))]
    private int _settingsWindowWidth = AppSettings.DefaultSettingsWindowWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsWindowBoundsSummary))]
    private int _settingsWindowHeight = AppSettings.DefaultSettingsWindowHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsWindowBoundsSummary))]
    private bool _settingsWindowIsMaximized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChildWindowBoundsSummary))]
    private int _childWindowWidth = AppSettings.DefaultChildWindowWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChildWindowBoundsSummary))]
    private int _childWindowHeight = AppSettings.DefaultChildWindowHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChildWindowBoundsSummary))]
    private bool _childWindowIsMaximized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileListFontSizeText))]
    private double _fileListFontSize = AppSettings.DefaultFileListFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileListDetailsFontSizeText))]
    private double _fileListDetailsFontSize = AppSettings.DefaultFileListDetailsFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabFontSizeText))]
    private double _tabFontSize = AppSettings.DefaultTabFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DriveBarFontSizeText))]
    private double _driveBarFontSize = AppSettings.DefaultDriveBarFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddressBarFontSizeText))]
    private double _addressBarFontSize = AppSettings.DefaultAddressBarFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BreadcrumbFontSizeText))]
    private double _breadcrumbFontSize = AppSettings.DefaultBreadcrumbFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarFontSizeText))]
    private double _statusBarFontSize = AppSettings.DefaultStatusBarFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunctionBarFontSizeText))]
    private double _functionBarFontSize = AppSettings.DefaultFunctionBarFontSize;

    public bool IsDarkTheme => Theme == AppTheme.Dark;
    public bool IsLightTheme => Theme == AppTheme.Light;
    public string ThemeDisplayName => Theme == AppTheme.Light
        ? LocalizationService.T("Settings.Theme.LightName")
        : LocalizationService.T("Settings.Theme.DarkName");

    public IReadOnlyList<LanguageOption> AvailableLanguages => LocalizationService.SupportedLanguages;

    public string FileListRowHeightText => $"{FileListRowHeight:0} px";

    public string ImageHoverPreviewSizeText => $"{ImageHoverPreviewSize:0} × {ImageHoverPreviewHeight:0} px";
    public double ImageHoverPreviewHeight => Math.Round(ImageHoverPreviewSize * 0.75);
    public double ImageHoverCardMaxWidth => ImageHoverPreviewSize + 40.0;

    public string MainWindowBoundsSummary => MainWindowIsMaximized
        ? LocalizationService.Format("Settings.Bounds.Maximized", MainWindowWidth, MainWindowHeight)
        : LocalizationService.Format("Settings.Bounds.Normal", MainWindowWidth, MainWindowHeight);

    public string SettingsWindowBoundsSummary => SettingsWindowIsMaximized
        ? LocalizationService.Format("Settings.Bounds.Maximized", SettingsWindowWidth, SettingsWindowHeight)
        : LocalizationService.Format("Settings.Bounds.Normal", SettingsWindowWidth, SettingsWindowHeight);

    public string ChildWindowBoundsSummary => ChildWindowIsMaximized
        ? LocalizationService.Format("Settings.Bounds.Maximized", ChildWindowWidth, ChildWindowHeight)
        : LocalizationService.Format("Settings.Bounds.Normal", ChildWindowWidth, ChildWindowHeight);

    public string FileListFontSizeText => $"{FileListFontSize:0.#} pt";
    public string FileListDetailsFontSizeText => $"{FileListDetailsFontSize:0.#} pt";
    public string TabFontSizeText => $"{TabFontSize:0.#} pt";
    public string DriveBarFontSizeText => $"{DriveBarFontSize:0.#} pt";
    public string AddressBarFontSizeText => $"{AddressBarFontSize:0.#} pt";
    public string BreadcrumbFontSizeText => $"{BreadcrumbFontSize:0.#} pt";
    public string StatusBarFontSizeText => $"{StatusBarFontSize:0.#} pt";
    public string FunctionBarFontSizeText => $"{FunctionBarFontSize:0.#} pt";

    public event EventHandler? ThemeChanged;
    public event EventHandler? LanguageChanged;
    public event EventHandler? SaveSessionChanged;
    public event EventHandler? FontSizesChanged;
    public event EventHandler? SelectionBorderColorChanged;
    public event EventHandler? ActivePanelBorderColorChanged;
    public event EventHandler? MarkedItemTextColorChanged;
    public event EventHandler? ShowHiddenFilesByDefaultChanged;
    public event EventHandler? SortFoldersFirstChanged;
    public event EventHandler? ShowFileExtensionsChanged;
    public event EventHandler? DefaultViewModeChanged;

    public SettingsViewModel(SettingsStore? store = null)
    {
        _store = store;
        Current = this;
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ThemeDisplayName));
            OnPropertyChanged(nameof(MainWindowBoundsSummary));
            OnPropertyChanged(nameof(SettingsWindowBoundsSummary));
            OnPropertyChanged(nameof(ChildWindowBoundsSummary));
        };
        if (store is not null)
            ApplyFromModel(store.Load(), persist: false);
    }

    public void AttachStore(SettingsStore store)
    {
        _store = store;
        Current = this;
        ApplyFromModel(store.Load(), persist: false);
    }

    public void ApplyFromModel(AppSettings model, bool persist = true)
    {
        var norm = model.CloneNormalized();
        _suppressSave = true;
        try
        {
            Theme = norm.Theme;
            Language = norm.Language;
            LocalizationService.Instance.SetLanguage(norm.Language);
            CheckForUpdatesOnStartup = norm.CheckForUpdatesOnStartup;
            SaveSession = norm.SaveSession;
            DefaultViewMode = norm.DefaultViewMode;
            ShowHiddenFilesByDefault = norm.ShowHiddenFilesByDefault;
            ShowFileExtensions = norm.ShowFileExtensions;
            SortFoldersFirst = norm.SortFoldersFirst;
            ConfirmDeleteToRecycleBin = norm.ConfirmDeleteToRecycleBin;
            AutoCalculateFolderSizeOnMark = norm.AutoCalculateFolderSizeOnMark;
            ShowDriveBar = norm.ShowDriveBar;
            ShowFunctionKeyBar = norm.ShowFunctionKeyBar;
            FileListRowHeight = norm.FileListRowHeight;
            EnableImageHoverPreview = norm.EnableImageHoverPreview;
            ImageHoverPreviewSize = norm.ImageHoverPreviewSize;
            SelectionBorderColor = norm.SelectionBorderColor;
            ActivePanelBorderColor = norm.ActivePanelBorderColor;
            MarkedItemTextColor = norm.MarkedItemTextColor;
            MainWindowWidth = norm.MainWindowWidth;
            MainWindowHeight = norm.MainWindowHeight;
            MainWindowIsMaximized = norm.MainWindowIsMaximized;
            SettingsWindowWidth = norm.SettingsWindowWidth;
            SettingsWindowHeight = norm.SettingsWindowHeight;
            SettingsWindowIsMaximized = norm.SettingsWindowIsMaximized;
            ChildWindowWidth = norm.ChildWindowWidth;
            ChildWindowHeight = norm.ChildWindowHeight;
            ChildWindowIsMaximized = norm.ChildWindowIsMaximized;
            FileListFontSize = norm.FileListFontSize;
            FileListDetailsFontSize = norm.FileListDetailsFontSize;
            TabFontSize = norm.TabFontSize;
            DriveBarFontSize = norm.DriveBarFontSize;
            AddressBarFontSize = norm.AddressBarFontSize;
            BreadcrumbFontSize = norm.BreadcrumbFontSize;
            StatusBarFontSize = norm.StatusBarFontSize;
            FunctionBarFontSize = norm.FunctionBarFontSize;
        }
        finally
        {
            _suppressSave = false;
        }

        if (persist)
            Persist();
    }

    public AppSettings ToModel() => new()
    {
        Version = 1,
        Theme = Theme,
        Language = LocalizationService.NormalizeLanguageCode(Language),
        CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
        SaveSession = SaveSession,
        DefaultViewMode = DefaultViewMode,
        ShowHiddenFilesByDefault = ShowHiddenFilesByDefault,
        ShowFileExtensions = ShowFileExtensions,
        SortFoldersFirst = SortFoldersFirst,
        ConfirmDeleteToRecycleBin = ConfirmDeleteToRecycleBin,
        AutoCalculateFolderSizeOnMark = AutoCalculateFolderSizeOnMark,
        ShowDriveBar = ShowDriveBar,
        ShowFunctionKeyBar = ShowFunctionKeyBar,
        FileListRowHeight = AppSettings.ClampRowHeight(FileListRowHeight),
        EnableImageHoverPreview = EnableImageHoverPreview,
        ImageHoverPreviewSize = AppSettings.ClampPreviewSize(ImageHoverPreviewSize),
        SelectionBorderColor = AppSettings.NormalizeHexColor(SelectionBorderColor, AppSettings.DefaultSelectionBorderColor),
        ActivePanelBorderColor = AppSettings.NormalizeHexColor(ActivePanelBorderColor, AppSettings.DefaultActivePanelBorderColor),
        MarkedItemTextColor = AppSettings.NormalizeHexColor(MarkedItemTextColor, AppSettings.DefaultMarkedItemTextColor),
        MainWindowWidth = MainWindowWidth,
        MainWindowHeight = MainWindowHeight,
        MainWindowIsMaximized = MainWindowIsMaximized,
        SettingsWindowWidth = SettingsWindowWidth,
        SettingsWindowHeight = SettingsWindowHeight,
        SettingsWindowIsMaximized = SettingsWindowIsMaximized,
        ChildWindowWidth = ChildWindowWidth,
        ChildWindowHeight = ChildWindowHeight,
        ChildWindowIsMaximized = ChildWindowIsMaximized,
        FileListFontSize = FileListFontSize,
        FileListDetailsFontSize = FileListDetailsFontSize,
        TabFontSize = TabFontSize,
        DriveBarFontSize = DriveBarFontSize,
        AddressBarFontSize = AddressBarFontSize,
        BreadcrumbFontSize = BreadcrumbFontSize,
        StatusBarFontSize = StatusBarFontSize,
        FunctionBarFontSize = FunctionBarFontSize,
    };

    public void SetTheme(AppTheme theme) => Theme = theme;

    public void ToggleTheme() => Theme = Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;

    public void SetLanguage(string languageCode)
    {
        Language = LocalizationService.NormalizeLanguageCode(languageCode);
    }

    public void SetSelectionBorderColor(string hexColor)
    {
        SelectionBorderColor = AppSettings.NormalizeHexColor(hexColor, SelectionBorderColor);
    }

    public void SetActivePanelBorderColor(string hexColor)
    {
        ActivePanelBorderColor = AppSettings.NormalizeHexColor(hexColor, ActivePanelBorderColor);
    }

    public void SetMarkedItemTextColor(string hexColor)
    {
        MarkedItemTextColor = AppSettings.NormalizeHexColor(hexColor, MarkedItemTextColor);
    }

    public void UpdateMainWindowBounds(int width, int height, bool isMaximized)
    {
        _suppressSave = true;
        try
        {
            if (!isMaximized && width >= 640 && height >= 420)
            {
                MainWindowWidth = AppSettings.ClampDimension(width, 640, 7680, MainWindowWidth);
                MainWindowHeight = AppSettings.ClampDimension(height, 420, 4320, MainWindowHeight);
            }
            MainWindowIsMaximized = isMaximized;
        }
        finally
        {
            _suppressSave = false;
        }
        Persist();
    }

    public void UpdateSettingsWindowBounds(int width, int height, bool isMaximized)
    {
        _suppressSave = true;
        try
        {
            if (!isMaximized && width >= 860 && height >= 560)
            {
                SettingsWindowWidth = AppSettings.ClampDimension(width, 860, 3840, SettingsWindowWidth);
                SettingsWindowHeight = AppSettings.ClampDimension(height, 560, 2160, SettingsWindowHeight);
            }
            SettingsWindowIsMaximized = isMaximized;
        }
        finally
        {
            _suppressSave = false;
        }
        Persist();
    }

    public void UpdateChildWindowBounds(int width, int height, bool isMaximized)
    {
        _suppressSave = true;
        try
        {
            if (!isMaximized && width >= 460 && height >= 340)
            {
                ChildWindowWidth = AppSettings.ClampDimension(width, 460, 3840, ChildWindowWidth);
                ChildWindowHeight = AppSettings.ClampDimension(height, 340, 2160, ChildWindowHeight);
            }
            ChildWindowIsMaximized = isMaximized;
        }
        finally
        {
            _suppressSave = false;
        }
        Persist();
    }

    public void ResetWindowBoundsToDefaults()
    {
        _suppressSave = true;
        try
        {
            MainWindowWidth = AppSettings.DefaultMainWindowWidth;
            MainWindowHeight = AppSettings.DefaultMainWindowHeight;
            MainWindowIsMaximized = false;
            SettingsWindowWidth = AppSettings.DefaultSettingsWindowWidth;
            SettingsWindowHeight = AppSettings.DefaultSettingsWindowHeight;
            SettingsWindowIsMaximized = false;
            ChildWindowWidth = AppSettings.DefaultChildWindowWidth;
            ChildWindowHeight = AppSettings.DefaultChildWindowHeight;
            ChildWindowIsMaximized = false;
        }
        finally
        {
            _suppressSave = false;
        }
        Persist();
    }

    public void ResetFontSizesToDefaults()
    {
        FileListFontSize = AppSettings.DefaultFileListFontSize;
        FileListDetailsFontSize = AppSettings.DefaultFileListDetailsFontSize;
        TabFontSize = AppSettings.DefaultTabFontSize;
        DriveBarFontSize = AppSettings.DefaultDriveBarFontSize;
        AddressBarFontSize = AppSettings.DefaultAddressBarFontSize;
        BreadcrumbFontSize = AppSettings.DefaultBreadcrumbFontSize;
        StatusBarFontSize = AppSettings.DefaultStatusBarFontSize;
        FunctionBarFontSize = AppSettings.DefaultFunctionBarFontSize;
    }

    public void ResetAllToDefaults()
    {
        ApplyFromModel(new AppSettings(), persist: true);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        ActivePanelBorderColorChanged?.Invoke(this, EventArgs.Empty);
        SelectionBorderColorChanged?.Invoke(this, EventArgs.Empty);
        MarkedItemTextColorChanged?.Invoke(this, EventArgs.Empty);
        ShowHiddenFilesByDefaultChanged?.Invoke(this, EventArgs.Empty);
        SortFoldersFirstChanged?.Invoke(this, EventArgs.Empty);
        ShowFileExtensionsChanged?.Invoke(this, EventArgs.Empty);
        DefaultViewModeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnThemeChanged(AppTheme value)
    {
        Persist();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnLanguageChanged(string value)
    {
        LocalizationService.Instance.SetLanguage(value);
        Persist();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCheckForUpdatesOnStartupChanged(bool value) => Persist();

    partial void OnSaveSessionChanged(bool value)
    {
        Persist();
        SaveSessionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDefaultViewModeChanged(FileViewMode value)
    {
        Persist();
        DefaultViewModeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnShowHiddenFilesByDefaultChanged(bool value)
    {
        Persist();
        ShowHiddenFilesByDefaultChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnShowFileExtensionsChanged(bool value)
    {
        Persist();
        ShowFileExtensionsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSortFoldersFirstChanged(bool value)
    {
        Persist();
        SortFoldersFirstChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnConfirmDeleteToRecycleBinChanged(bool value) => Persist();
    partial void OnAutoCalculateFolderSizeOnMarkChanged(bool value) => Persist();
    partial void OnShowDriveBarChanged(bool value) => Persist();
    partial void OnShowFunctionKeyBarChanged(bool value) => Persist();
    partial void OnFileListRowHeightChanged(double value) => Persist();

    partial void OnEnableImageHoverPreviewChanged(bool value) => Persist();
    partial void OnImageHoverPreviewSizeChanged(double value) => Persist();

    partial void OnSelectionBorderColorChanged(string value)
    {
        Persist();
        SelectionBorderColorChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActivePanelBorderColorChanged(string value)
    {
        Persist();
        ActivePanelBorderColorChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnMarkedItemTextColorChanged(string value)
    {
        Persist();
        MarkedItemTextColorChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnMainWindowWidthChanged(int value) => Persist();
    partial void OnMainWindowHeightChanged(int value) => Persist();
    partial void OnMainWindowIsMaximizedChanged(bool value) => Persist();
    partial void OnSettingsWindowWidthChanged(int value) => Persist();
    partial void OnSettingsWindowHeightChanged(int value) => Persist();
    partial void OnSettingsWindowIsMaximizedChanged(bool value) => Persist();
    partial void OnChildWindowWidthChanged(int value) => Persist();
    partial void OnChildWindowHeightChanged(int value) => Persist();
    partial void OnChildWindowIsMaximizedChanged(bool value) => Persist();

    partial void OnFileListFontSizeChanged(double value) => OnFontChanged();
    partial void OnFileListDetailsFontSizeChanged(double value) => OnFontChanged();
    partial void OnTabFontSizeChanged(double value) => OnFontChanged();
    partial void OnDriveBarFontSizeChanged(double value) => OnFontChanged();
    partial void OnAddressBarFontSizeChanged(double value) => OnFontChanged();
    partial void OnBreadcrumbFontSizeChanged(double value) => OnFontChanged();
    partial void OnStatusBarFontSizeChanged(double value) => OnFontChanged();
    partial void OnFunctionBarFontSizeChanged(double value) => OnFontChanged();

    private void OnFontChanged()
    {
        Persist();
        FontSizesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Persist()
    {
        if (_suppressSave || _store is null) return;
        _store.Save(ToModel());
    }
}
