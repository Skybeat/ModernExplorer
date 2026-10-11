using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Session;
using ModernExplorer.Core.Settings;

namespace ModernExplorer.Core.ViewModels;

/// <summary>
/// Identifies a specific child window or dialog for per-window font size customization.
/// </summary>
public enum ChildWindowFontTarget
{
    General = 0,
    QuickViewer = 1,
    Editor = 2,
    StorageAnalyzer = 3,
    DeepSearch = 4,
    DuplicateFinder = 5,
    DirectorySync = 6,
    BatchRename = 7,
    FtpManager = 8,
    ArchiveDialog = 9,
    AboutWindow = 10,
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private SettingsStore? _store;
    private bool _suppressSave;
    private bool _applyingGlobalFont;

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
    private bool _showToolTips = true;

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
    private string _treemapVideoMediaColor = AppSettings.DefaultTreemapVideoMediaColor;

    [ObservableProperty]
    private string _treemapArchiveImageColor = AppSettings.DefaultTreemapArchiveImageColor;

    [ObservableProperty]
    private string _treemapVirtualDiskSystemColor = AppSettings.DefaultTreemapVirtualDiskSystemColor;

    [ObservableProperty]
    private string _treemapCodeDocumentColor = AppSettings.DefaultTreemapCodeDocumentColor;

    [ObservableProperty]
    private string _treemapPictureGraphicColor = AppSettings.DefaultTreemapPictureGraphicColor;

    [ObservableProperty]
    private string _treemapBinaryExecutableColor = AppSettings.DefaultTreemapBinaryExecutableColor;

    [ObservableProperty]
    private string _treemapOtherTempColor = AppSettings.DefaultTreemapOtherTempColor;

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
    [NotifyPropertyChangedFor(nameof(IsIndividualFontSizesEnabled))]
    private bool _useGlobalFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlobalFontSizeText))]
    private double _globalFontSize = AppSettings.DefaultGlobalFontSize;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuickViewerFontSizeText))]
    private double _quickViewerFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorFontSizeText))]
    private double _editorFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageAnalyzerFontSizeText))]
    private double _storageAnalyzerFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeepSearchFontSizeText))]
    private double _deepSearchFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuplicateFinderFontSizeText))]
    private double _duplicateFinderFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectorySyncFontSizeText))]
    private double _directorySyncFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchRenameFontSizeText))]
    private double _batchRenameFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FtpManagerFontSizeText))]
    private double _ftpManagerFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArchiveDialogFontSizeText))]
    private double _archiveDialogFontSize = AppSettings.DefaultChildWindowFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AboutWindowFontSizeText))]
    private double _aboutWindowFontSize = AppSettings.DefaultChildWindowFontSize;

    public bool IsDarkTheme => Theme == AppTheme.Dark;
    public bool IsLightTheme => Theme == AppTheme.Light;
    public string ThemeDisplayName => Theme == AppTheme.Light
        ? LocalizationService.T("Settings.Theme.LightName")
        : LocalizationService.T("Settings.Theme.DarkName");

    public IReadOnlyList<LanguageOption> AvailableLanguages => LocalizationService.SupportedLanguages;

    public bool IsIndividualFontSizesEnabled => !UseGlobalFontSize;

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

    public string GlobalFontSizeText => $"{GlobalFontSize:0.#} pt";
    public string FileListFontSizeText => $"{FileListFontSize:0.#} pt";
    public string FileListDetailsFontSizeText => $"{FileListDetailsFontSize:0.#} pt";
    public string TabFontSizeText => $"{TabFontSize:0.#} pt";
    public string DriveBarFontSizeText => $"{DriveBarFontSize:0.#} pt";
    public string AddressBarFontSizeText => $"{AddressBarFontSize:0.#} pt";
    public string BreadcrumbFontSizeText => $"{BreadcrumbFontSize:0.#} pt";
    public string StatusBarFontSizeText => $"{StatusBarFontSize:0.#} pt";
    public string FunctionBarFontSizeText => $"{FunctionBarFontSize:0.#} pt";

    public string QuickViewerFontSizeText => $"{QuickViewerFontSize:0.#} pt";
    public string EditorFontSizeText => $"{EditorFontSize:0.#} pt";
    public string StorageAnalyzerFontSizeText => $"{StorageAnalyzerFontSize:0.#} pt";
    public string DeepSearchFontSizeText => $"{DeepSearchFontSize:0.#} pt";
    public string DuplicateFinderFontSizeText => $"{DuplicateFinderFontSize:0.#} pt";
    public string DirectorySyncFontSizeText => $"{DirectorySyncFontSize:0.#} pt";
    public string BatchRenameFontSizeText => $"{BatchRenameFontSize:0.#} pt";
    public string FtpManagerFontSizeText => $"{FtpManagerFontSize:0.#} pt";
    public string ArchiveDialogFontSizeText => $"{ArchiveDialogFontSize:0.#} pt";
    public string AboutWindowFontSizeText => $"{AboutWindowFontSize:0.#} pt";

    public event EventHandler? ThemeChanged;
    public event EventHandler? LanguageChanged;
    public event EventHandler? SaveSessionChanged;
    public event EventHandler? FontSizesChanged;
    public event EventHandler? SelectionBorderColorChanged;
    public event EventHandler? ActivePanelBorderColorChanged;
    public event EventHandler? MarkedItemTextColorChanged;
    public event EventHandler? TreemapColorsChanged;
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
            ShowToolTips = norm.ShowToolTips;
            FileListRowHeight = norm.FileListRowHeight;
            EnableImageHoverPreview = norm.EnableImageHoverPreview;
            ImageHoverPreviewSize = norm.ImageHoverPreviewSize;
            SelectionBorderColor = norm.SelectionBorderColor;
            ActivePanelBorderColor = norm.ActivePanelBorderColor;
            MarkedItemTextColor = norm.MarkedItemTextColor;
            TreemapVideoMediaColor = norm.TreemapVideoMediaColor;
            TreemapArchiveImageColor = norm.TreemapArchiveImageColor;
            TreemapVirtualDiskSystemColor = norm.TreemapVirtualDiskSystemColor;
            TreemapCodeDocumentColor = norm.TreemapCodeDocumentColor;
            TreemapPictureGraphicColor = norm.TreemapPictureGraphicColor;
            TreemapBinaryExecutableColor = norm.TreemapBinaryExecutableColor;
            TreemapOtherTempColor = norm.TreemapOtherTempColor;
            MainWindowWidth = norm.MainWindowWidth;
            MainWindowHeight = norm.MainWindowHeight;
            MainWindowIsMaximized = norm.MainWindowIsMaximized;
            SettingsWindowWidth = norm.SettingsWindowWidth;
            SettingsWindowHeight = norm.SettingsWindowHeight;
            SettingsWindowIsMaximized = norm.SettingsWindowIsMaximized;
            ChildWindowWidth = norm.ChildWindowWidth;
            ChildWindowHeight = norm.ChildWindowHeight;
            ChildWindowIsMaximized = norm.ChildWindowIsMaximized;
            UseGlobalFontSize = norm.UseGlobalFontSize;
            GlobalFontSize = norm.GlobalFontSize;
            FileListFontSize = norm.FileListFontSize;
            FileListDetailsFontSize = norm.FileListDetailsFontSize;
            TabFontSize = norm.TabFontSize;
            DriveBarFontSize = norm.DriveBarFontSize;
            AddressBarFontSize = norm.AddressBarFontSize;
            BreadcrumbFontSize = norm.BreadcrumbFontSize;
            StatusBarFontSize = norm.StatusBarFontSize;
            FunctionBarFontSize = norm.FunctionBarFontSize;
            QuickViewerFontSize = norm.QuickViewerFontSize;
            EditorFontSize = norm.EditorFontSize;
            StorageAnalyzerFontSize = norm.StorageAnalyzerFontSize;
            DeepSearchFontSize = norm.DeepSearchFontSize;
            DuplicateFinderFontSize = norm.DuplicateFinderFontSize;
            DirectorySyncFontSize = norm.DirectorySyncFontSize;
            BatchRenameFontSize = norm.BatchRenameFontSize;
            FtpManagerFontSize = norm.FtpManagerFontSize;
            ArchiveDialogFontSize = norm.ArchiveDialogFontSize;
            AboutWindowFontSize = norm.AboutWindowFontSize;
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
        ShowToolTips = ShowToolTips,
        FileListRowHeight = AppSettings.ClampRowHeight(FileListRowHeight),
        EnableImageHoverPreview = EnableImageHoverPreview,
        ImageHoverPreviewSize = AppSettings.ClampPreviewSize(ImageHoverPreviewSize),
        SelectionBorderColor = AppSettings.NormalizeHexColor(SelectionBorderColor, AppSettings.DefaultSelectionBorderColor),
        ActivePanelBorderColor = AppSettings.NormalizeHexColor(ActivePanelBorderColor, AppSettings.DefaultActivePanelBorderColor),
        MarkedItemTextColor = AppSettings.NormalizeHexColor(MarkedItemTextColor, AppSettings.DefaultMarkedItemTextColor),
        TreemapVideoMediaColor = AppSettings.NormalizeHexColor(TreemapVideoMediaColor, AppSettings.DefaultTreemapVideoMediaColor),
        TreemapArchiveImageColor = AppSettings.NormalizeHexColor(TreemapArchiveImageColor, AppSettings.DefaultTreemapArchiveImageColor),
        TreemapVirtualDiskSystemColor = AppSettings.NormalizeHexColor(TreemapVirtualDiskSystemColor, AppSettings.DefaultTreemapVirtualDiskSystemColor),
        TreemapCodeDocumentColor = AppSettings.NormalizeHexColor(TreemapCodeDocumentColor, AppSettings.DefaultTreemapCodeDocumentColor),
        TreemapPictureGraphicColor = AppSettings.NormalizeHexColor(TreemapPictureGraphicColor, AppSettings.DefaultTreemapPictureGraphicColor),
        TreemapBinaryExecutableColor = AppSettings.NormalizeHexColor(TreemapBinaryExecutableColor, AppSettings.DefaultTreemapBinaryExecutableColor),
        TreemapOtherTempColor = AppSettings.NormalizeHexColor(TreemapOtherTempColor, AppSettings.DefaultTreemapOtherTempColor),
        MainWindowWidth = MainWindowWidth,
        MainWindowHeight = MainWindowHeight,
        MainWindowIsMaximized = MainWindowIsMaximized,
        SettingsWindowWidth = SettingsWindowWidth,
        SettingsWindowHeight = SettingsWindowHeight,
        SettingsWindowIsMaximized = SettingsWindowIsMaximized,
        ChildWindowWidth = ChildWindowWidth,
        ChildWindowHeight = ChildWindowHeight,
        ChildWindowIsMaximized = ChildWindowIsMaximized,
        UseGlobalFontSize = UseGlobalFontSize,
        GlobalFontSize = GlobalFontSize,
        FileListFontSize = FileListFontSize,
        FileListDetailsFontSize = FileListDetailsFontSize,
        TabFontSize = TabFontSize,
        DriveBarFontSize = DriveBarFontSize,
        AddressBarFontSize = AddressBarFontSize,
        BreadcrumbFontSize = BreadcrumbFontSize,
        StatusBarFontSize = StatusBarFontSize,
        FunctionBarFontSize = FunctionBarFontSize,
        QuickViewerFontSize = QuickViewerFontSize,
        EditorFontSize = EditorFontSize,
        StorageAnalyzerFontSize = StorageAnalyzerFontSize,
        DeepSearchFontSize = DeepSearchFontSize,
        DuplicateFinderFontSize = DuplicateFinderFontSize,
        DirectorySyncFontSize = DirectorySyncFontSize,
        BatchRenameFontSize = BatchRenameFontSize,
        FtpManagerFontSize = FtpManagerFontSize,
        ArchiveDialogFontSize = ArchiveDialogFontSize,
        AboutWindowFontSize = AboutWindowFontSize,
    };

    public string GetTreemapCategoryColor(StorageCategory category) => category switch
    {
        StorageCategory.VideoMedia => TreemapVideoMediaColor,
        StorageCategory.ArchiveImage => TreemapArchiveImageColor,
        StorageCategory.VirtualDiskSystem => TreemapVirtualDiskSystemColor,
        StorageCategory.CodeDocument => TreemapCodeDocumentColor,
        StorageCategory.PictureGraphic => TreemapPictureGraphicColor,
        StorageCategory.BinaryExecutable => TreemapBinaryExecutableColor,
        _ => TreemapOtherTempColor,
    };

    public void SetTreemapCategoryColor(StorageCategory category, string hexColor)
    {
        switch (category)
        {
            case StorageCategory.VideoMedia:
                TreemapVideoMediaColor = AppSettings.NormalizeHexColor(hexColor, AppSettings.DefaultTreemapVideoMediaColor);
                break;
            case StorageCategory.ArchiveImage:
                TreemapArchiveImageColor = AppSettings.NormalizeHexColor(hexColor, AppSettings.DefaultTreemapArchiveImageColor);
                break;
            case StorageCategory.VirtualDiskSystem:
                TreemapVirtualDiskSystemColor = AppSettings.NormalizeHexColor(hexColor, AppSettings.DefaultTreemapVirtualDiskSystemColor);
                break;
            case StorageCategory.CodeDocument:
                TreemapCodeDocumentColor = AppSettings.NormalizeHexColor(hexColor, AppSettings.DefaultTreemapCodeDocumentColor);
                break;
            case StorageCategory.PictureGraphic:
                TreemapPictureGraphicColor = AppSettings.NormalizeHexColor(hexColor, AppSettings.DefaultTreemapPictureGraphicColor);
                break;
            case StorageCategory.BinaryExecutable:
                TreemapBinaryExecutableColor = AppSettings.NormalizeHexColor(hexColor, AppSettings.DefaultTreemapBinaryExecutableColor);
                break;
            default:
                TreemapOtherTempColor = AppSettings.NormalizeHexColor(hexColor, AppSettings.DefaultTreemapOtherTempColor);
                break;
        }
    }

    public void ResetTreemapColorsToDefaults()
    {
        TreemapVideoMediaColor = AppSettings.DefaultTreemapVideoMediaColor;
        TreemapArchiveImageColor = AppSettings.DefaultTreemapArchiveImageColor;
        TreemapVirtualDiskSystemColor = AppSettings.DefaultTreemapVirtualDiskSystemColor;
        TreemapCodeDocumentColor = AppSettings.DefaultTreemapCodeDocumentColor;
        TreemapPictureGraphicColor = AppSettings.DefaultTreemapPictureGraphicColor;
        TreemapBinaryExecutableColor = AppSettings.DefaultTreemapBinaryExecutableColor;
        TreemapOtherTempColor = AppSettings.DefaultTreemapOtherTempColor;
    }

    public string GetColorSettingHex(ColorSettingTarget target) => target switch
    {
        ColorSettingTarget.ActivePanelBorder => ActivePanelBorderColor,
        ColorSettingTarget.SelectionBorder => SelectionBorderColor,
        ColorSettingTarget.MarkedItemText => MarkedItemTextColor,
        ColorSettingTarget.TreemapVideoMedia => TreemapVideoMediaColor,
        ColorSettingTarget.TreemapArchiveImage => TreemapArchiveImageColor,
        ColorSettingTarget.TreemapVirtualDiskSystem => TreemapVirtualDiskSystemColor,
        ColorSettingTarget.TreemapCodeDocument => TreemapCodeDocumentColor,
        ColorSettingTarget.TreemapPictureGraphic => TreemapPictureGraphicColor,
        ColorSettingTarget.TreemapBinaryExecutable => TreemapBinaryExecutableColor,
        _ => TreemapOtherTempColor,
    };

    public static string GetDefaultColorSettingHex(ColorSettingTarget target) => target switch
    {
        ColorSettingTarget.ActivePanelBorder => AppSettings.DefaultActivePanelBorderColor,
        ColorSettingTarget.SelectionBorder => AppSettings.DefaultSelectionBorderColor,
        ColorSettingTarget.MarkedItemText => AppSettings.DefaultMarkedItemTextColor,
        ColorSettingTarget.TreemapVideoMedia => AppSettings.DefaultTreemapVideoMediaColor,
        ColorSettingTarget.TreemapArchiveImage => AppSettings.DefaultTreemapArchiveImageColor,
        ColorSettingTarget.TreemapVirtualDiskSystem => AppSettings.DefaultTreemapVirtualDiskSystemColor,
        ColorSettingTarget.TreemapCodeDocument => AppSettings.DefaultTreemapCodeDocumentColor,
        ColorSettingTarget.TreemapPictureGraphic => AppSettings.DefaultTreemapPictureGraphicColor,
        ColorSettingTarget.TreemapBinaryExecutable => AppSettings.DefaultTreemapBinaryExecutableColor,
        _ => AppSettings.DefaultTreemapOtherTempColor,
    };

    public void SetColorSettingHex(ColorSettingTarget target, string hexColor)
    {
        switch (target)
        {
            case ColorSettingTarget.ActivePanelBorder:
                SetActivePanelBorderColor(hexColor);
                break;
            case ColorSettingTarget.SelectionBorder:
                SetSelectionBorderColor(hexColor);
                break;
            case ColorSettingTarget.MarkedItemText:
                SetMarkedItemTextColor(hexColor);
                break;
            case ColorSettingTarget.TreemapVideoMedia:
                SetTreemapCategoryColor(StorageCategory.VideoMedia, hexColor);
                break;
            case ColorSettingTarget.TreemapArchiveImage:
                SetTreemapCategoryColor(StorageCategory.ArchiveImage, hexColor);
                break;
            case ColorSettingTarget.TreemapVirtualDiskSystem:
                SetTreemapCategoryColor(StorageCategory.VirtualDiskSystem, hexColor);
                break;
            case ColorSettingTarget.TreemapCodeDocument:
                SetTreemapCategoryColor(StorageCategory.CodeDocument, hexColor);
                break;
            case ColorSettingTarget.TreemapPictureGraphic:
                SetTreemapCategoryColor(StorageCategory.PictureGraphic, hexColor);
                break;
            case ColorSettingTarget.TreemapBinaryExecutable:
                SetTreemapCategoryColor(StorageCategory.BinaryExecutable, hexColor);
                break;
            default:
                SetTreemapCategoryColor(StorageCategory.OtherTemp, hexColor);
                break;
        }
    }

    public void SetColorSettingRgb(ColorSettingTarget target, byte r, byte g, byte b) =>
        SetColorSettingHex(target, AppSettings.FormatHexRgb(r, g, b));

    public void ResetColorSettingToDefault(ColorSettingTarget target) =>
        SetColorSettingHex(target, GetDefaultColorSettingHex(target));

    public double GetChildWindowFontSize(ChildWindowFontTarget target)
    {
        if (UseGlobalFontSize)
            return AppSettings.ClampFont(GlobalFontSize, AppSettings.DefaultGlobalFontSize);

        return target switch
        {
            ChildWindowFontTarget.QuickViewer => AppSettings.ClampFont(QuickViewerFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.Editor => AppSettings.ClampFont(EditorFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.StorageAnalyzer => AppSettings.ClampFont(StorageAnalyzerFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.DeepSearch => AppSettings.ClampFont(DeepSearchFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.DuplicateFinder => AppSettings.ClampFont(DuplicateFinderFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.DirectorySync => AppSettings.ClampFont(DirectorySyncFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.BatchRename => AppSettings.ClampFont(BatchRenameFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.FtpManager => AppSettings.ClampFont(FtpManagerFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.ArchiveDialog => AppSettings.ClampFont(ArchiveDialogFontSize, AppSettings.DefaultChildWindowFontSize),
            ChildWindowFontTarget.AboutWindow => AppSettings.ClampFont(AboutWindowFontSize, AppSettings.DefaultChildWindowFontSize),
            _ => AppSettings.ClampFont(GlobalFontSize, AppSettings.DefaultChildWindowFontSize),
        };
    }

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
        UseGlobalFontSize = false;
        GlobalFontSize = AppSettings.DefaultGlobalFontSize;
        FileListFontSize = AppSettings.DefaultFileListFontSize;
        FileListDetailsFontSize = AppSettings.DefaultFileListDetailsFontSize;
        TabFontSize = AppSettings.DefaultTabFontSize;
        DriveBarFontSize = AppSettings.DefaultDriveBarFontSize;
        AddressBarFontSize = AppSettings.DefaultAddressBarFontSize;
        BreadcrumbFontSize = AppSettings.DefaultBreadcrumbFontSize;
        StatusBarFontSize = AppSettings.DefaultStatusBarFontSize;
        FunctionBarFontSize = AppSettings.DefaultFunctionBarFontSize;
        QuickViewerFontSize = AppSettings.DefaultChildWindowFontSize;
        EditorFontSize = AppSettings.DefaultChildWindowFontSize;
        StorageAnalyzerFontSize = AppSettings.DefaultChildWindowFontSize;
        DeepSearchFontSize = AppSettings.DefaultChildWindowFontSize;
        DuplicateFinderFontSize = AppSettings.DefaultChildWindowFontSize;
        DirectorySyncFontSize = AppSettings.DefaultChildWindowFontSize;
        BatchRenameFontSize = AppSettings.DefaultChildWindowFontSize;
        FtpManagerFontSize = AppSettings.DefaultChildWindowFontSize;
        ArchiveDialogFontSize = AppSettings.DefaultChildWindowFontSize;
        AboutWindowFontSize = AppSettings.DefaultChildWindowFontSize;
    }

    public void ResetAllToDefaults()
    {
        ApplyFromModel(new AppSettings(), persist: true);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        ActivePanelBorderColorChanged?.Invoke(this, EventArgs.Empty);
        SelectionBorderColorChanged?.Invoke(this, EventArgs.Empty);
        MarkedItemTextColorChanged?.Invoke(this, EventArgs.Empty);
        TreemapColorsChanged?.Invoke(this, EventArgs.Empty);
        FontSizesChanged?.Invoke(this, EventArgs.Empty);
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
    partial void OnShowToolTipsChanged(bool value) => Persist();
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

    partial void OnTreemapVideoMediaColorChanged(string value) => OnTreemapColorChanged();
    partial void OnTreemapArchiveImageColorChanged(string value) => OnTreemapColorChanged();
    partial void OnTreemapVirtualDiskSystemColorChanged(string value) => OnTreemapColorChanged();
    partial void OnTreemapCodeDocumentColorChanged(string value) => OnTreemapColorChanged();
    partial void OnTreemapPictureGraphicColorChanged(string value) => OnTreemapColorChanged();
    partial void OnTreemapBinaryExecutableColorChanged(string value) => OnTreemapColorChanged();
    partial void OnTreemapOtherTempColorChanged(string value) => OnTreemapColorChanged();

    private void OnTreemapColorChanged()
    {
        Persist();
        TreemapColorsChanged?.Invoke(this, EventArgs.Empty);
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

    partial void OnUseGlobalFontSizeChanged(bool value)
    {
        if (value)
        {
            ApplyGlobalFontSizeToAll(GlobalFontSize);
        }
        else
        {
            OnFontChanged();
        }
    }

    partial void OnGlobalFontSizeChanged(double value)
    {
        if (UseGlobalFontSize)
        {
            ApplyGlobalFontSizeToAll(value);
        }
        else
        {
            OnFontChanged();
        }
    }

    private void ApplyGlobalFontSizeToAll(double globalSize)
    {
        if (_applyingGlobalFont) return;
        _applyingGlobalFont = true;
        bool prevSuppress = _suppressSave;
        _suppressSave = true;
        try
        {
            double clamped = AppSettings.ClampFont(globalSize, AppSettings.DefaultGlobalFontSize);
            FileListFontSize = clamped;
            FileListDetailsFontSize = AppSettings.ClampFont(clamped - 0.5, AppSettings.DefaultFileListDetailsFontSize);
            TabFontSize = AppSettings.ClampFont(clamped - 0.5, AppSettings.DefaultTabFontSize);
            DriveBarFontSize = AppSettings.ClampFont(clamped - 0.5, AppSettings.DefaultDriveBarFontSize);
            AddressBarFontSize = clamped;
            BreadcrumbFontSize = AppSettings.ClampFont(clamped - 1.0, AppSettings.DefaultBreadcrumbFontSize);
            StatusBarFontSize = AppSettings.ClampFont(clamped - 1.0, AppSettings.DefaultStatusBarFontSize);
            FunctionBarFontSize = AppSettings.ClampFont(clamped - 1.0, AppSettings.DefaultFunctionBarFontSize);

            QuickViewerFontSize = clamped;
            EditorFontSize = clamped;
            StorageAnalyzerFontSize = clamped;
            DeepSearchFontSize = clamped;
            DuplicateFinderFontSize = clamped;
            DirectorySyncFontSize = clamped;
            BatchRenameFontSize = clamped;
            FtpManagerFontSize = clamped;
            ArchiveDialogFontSize = clamped;
            AboutWindowFontSize = clamped;
        }
        finally
        {
            _suppressSave = prevSuppress;
            _applyingGlobalFont = false;
        }

        OnFontChanged();
    }

    partial void OnFileListFontSizeChanged(double value) => OnFontChanged();
    partial void OnFileListDetailsFontSizeChanged(double value) => OnFontChanged();
    partial void OnTabFontSizeChanged(double value) => OnFontChanged();
    partial void OnDriveBarFontSizeChanged(double value) => OnFontChanged();
    partial void OnAddressBarFontSizeChanged(double value) => OnFontChanged();
    partial void OnBreadcrumbFontSizeChanged(double value) => OnFontChanged();
    partial void OnStatusBarFontSizeChanged(double value) => OnFontChanged();
    partial void OnFunctionBarFontSizeChanged(double value) => OnFontChanged();

    partial void OnQuickViewerFontSizeChanged(double value) => OnFontChanged();
    partial void OnEditorFontSizeChanged(double value) => OnFontChanged();
    partial void OnStorageAnalyzerFontSizeChanged(double value) => OnFontChanged();
    partial void OnDeepSearchFontSizeChanged(double value) => OnFontChanged();
    partial void OnDuplicateFinderFontSizeChanged(double value) => OnFontChanged();
    partial void OnDirectorySyncFontSizeChanged(double value) => OnFontChanged();
    partial void OnBatchRenameFontSizeChanged(double value) => OnFontChanged();
    partial void OnFtpManagerFontSizeChanged(double value) => OnFontChanged();
    partial void OnArchiveDialogFontSizeChanged(double value) => OnFontChanged();
    partial void OnAboutWindowFontSizeChanged(double value) => OnFontChanged();

    private void OnFontChanged()
    {
        if (_applyingGlobalFont) return;
        Persist();
        FontSizesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Persist()
    {
        if (_suppressSave || _store is null) return;
        _store.Save(ToModel());
    }
}
