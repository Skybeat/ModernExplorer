using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Session;

namespace ModernExplorer.Core.Settings;

/// <summary>Supported color themes for the application.</summary>
public enum AppTheme
{
    Dark,
    Light,
}

/// <summary>
/// Persistable application settings (theme, language, update check, session persistence,
/// border and highlight colors, image preview size, window bounds, and font sizes).
/// </summary>
public sealed class AppSettings
{
    public const string DefaultLanguage = LocalizationService.SystemLanguageCode;
    public const string DefaultSelectionBorderColor = "#FFFFFF";
    public const string DefaultActivePanelBorderColor = "#0078D4";
    public const string DefaultMarkedItemTextColor = "#38BDF8";

    public const string DefaultTreemapVideoMediaColor = "#3B82F6";
    public const string DefaultTreemapArchiveImageColor = "#8B5CF6";
    public const string DefaultTreemapVirtualDiskSystemColor = "#F97316";
    public const string DefaultTreemapCodeDocumentColor = "#10B981";
    public const string DefaultTreemapPictureGraphicColor = "#06B6D4";
    public const string DefaultTreemapBinaryExecutableColor = "#EAB308";
    public const string DefaultTreemapOtherTempColor = "#64748B";

    public const double DefaultImageHoverPreviewSize = 320.0;
    public const double MinImageHoverPreviewSize = 160.0;
    public const double MaxImageHoverPreviewSize = 720.0;

    public const double DefaultFileListRowHeight = 26.0;
    public const double MinFileListRowHeight = 22.0;
    public const double MaxFileListRowHeight = 40.0;

    public const int DefaultMainWindowWidth = 1480;
    public const int DefaultMainWindowHeight = 900;
    public const int DefaultSettingsWindowWidth = 1040;
    public const int DefaultSettingsWindowHeight = 760;
    public const int DefaultChildWindowWidth = 860;
    public const int DefaultChildWindowHeight = 640;

    public const double DefaultGlobalFontSize = 13.0;
    public const double DefaultChildWindowFontSize = 13.0;
    public const double DefaultFileListFontSize = 13.0;
    public const double DefaultFileListDetailsFontSize = 12.0;
    public const double DefaultTabFontSize = 12.0;
    public const double DefaultDriveBarFontSize = 12.0;
    public const double DefaultAddressBarFontSize = 13.0;
    public const double DefaultBreadcrumbFontSize = 11.0;
    public const double DefaultStatusBarFontSize = 11.5;
    public const double DefaultFunctionBarFontSize = 11.5;

    public const double MinFontSize = 8.0;
    public const double MaxFontSize = 26.0;

    public int Version { get; set; } = 1;

    /// <summary>Active color theme (Dark or Light).</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>
    /// Configured UI language ("system" for OS default, or a two-letter ISO language code such as "de", "en", "es").
    /// </summary>
    public string Language { get; set; } = DefaultLanguage;

    /// <summary>
    /// Indicates whether the application automatically checks GitHub for a new release on startup.
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Indicates whether the session (tabs, open paths, sorting, layout) is saved
    /// and restored on the next launch. Enabled by default (<c>true</c>).
    /// </summary>
    public bool SaveSession { get; set; } = true;

    /// <summary>
    /// Default file view mode for newly opened tabs (Details, List, Tiles, LargeIcons, etc.).
    /// </summary>
    public FileViewMode DefaultViewMode { get; set; } = FileViewMode.Details;

    /// <summary>
    /// Indicates whether hidden and system files are shown by default.
    /// </summary>
    public bool ShowHiddenFilesByDefault { get; set; }

    /// <summary>
    /// Indicates whether file extensions (e.g., .txt, .png) are shown in the file list name column.
    /// </summary>
    public bool ShowFileExtensions { get; set; } = true;

    /// <summary>
    /// Indicates whether folders are always sorted before files in the file list.
    /// </summary>
    public bool SortFoldersFirst { get; set; } = true;

    /// <summary>
    /// Indicates whether a confirmation dialog is shown before moving items to the Recycle Bin (F8).
    /// </summary>
    public bool ConfirmDeleteToRecycleBin { get; set; } = true;

    /// <summary>
    /// Indicates whether marking a folder (Space/Insert) automatically calculates its total size.
    /// </summary>
    public bool AutoCalculateFolderSizeOnMark { get; set; } = true;

    /// <summary>
    /// Indicates whether the drive bar above the tabs is visible.
    /// </summary>
    public bool ShowDriveBar { get; set; } = true;

    /// <summary>
    /// Indicates whether the F1–F12 function key bar at the bottom of the window is visible.
    /// </summary>
    public bool ShowFunctionKeyBar { get; set; } = true;

    /// <summary>
    /// Indicates whether descriptive feature and button tooltips are shown when hovering over UI buttons.
    /// </summary>
    public bool ShowToolTips { get; set; } = true;

    /// <summary>
    /// Minimum row height of an entry in the file list in pixels (22–40 px, default: 26 px).
    /// </summary>
    public double FileListRowHeight { get; set; } = DefaultFileListRowHeight;

    /// <summary>
    /// Indicates whether hovering over an image file displays a floating preview card.
    /// Enabled by default (<c>true</c>).
    /// </summary>
    public bool EnableImageHoverPreview { get; set; } = true;

    /// <summary>
    /// Maximum width of the hover image preview in pixels (160–720 px, default: 320 px).
    /// </summary>
    public double ImageHoverPreviewSize { get; set; } = DefaultImageHoverPreviewSize;

    /// <summary>
    /// Border color around the currently focused item in the file list (hex format #RRGGBB).
    /// Default is White (<c>#FFFFFF</c>).
    /// </summary>
    public string SelectionBorderColor { get; set; } = DefaultSelectionBorderColor;

    /// <summary>
    /// Border color around the active explorer panel (hex format #RRGGBB).
    /// Default is Blue (<c>#0078D4</c>).
    /// </summary>
    public string ActivePanelBorderColor { get; set; } = DefaultActivePanelBorderColor;

    /// <summary>
    /// Text color for marked entries (with checkmark ✓) in the file/folder list (hex format #RRGGBB).
    /// Default is Cyan-Blue (<c>#38BDF8</c>).
    /// </summary>
    public string MarkedItemTextColor { get; set; } = DefaultMarkedItemTextColor;

    /// <summary>Customizable colors for the 7 file categories in the Storage Analyzer Treemap.</summary>
    public string TreemapVideoMediaColor { get; set; } = DefaultTreemapVideoMediaColor;
    public string TreemapArchiveImageColor { get; set; } = DefaultTreemapArchiveImageColor;
    public string TreemapVirtualDiskSystemColor { get; set; } = DefaultTreemapVirtualDiskSystemColor;
    public string TreemapCodeDocumentColor { get; set; } = DefaultTreemapCodeDocumentColor;
    public string TreemapPictureGraphicColor { get; set; } = DefaultTreemapPictureGraphicColor;
    public string TreemapBinaryExecutableColor { get; set; } = DefaultTreemapBinaryExecutableColor;
    public string TreemapOtherTempColor { get; set; } = DefaultTreemapOtherTempColor;

    /// <summary>Saved bounds of the main window.</summary>
    public int MainWindowWidth { get; set; } = DefaultMainWindowWidth;
    public int MainWindowHeight { get; set; } = DefaultMainWindowHeight;
    public bool MainWindowIsMaximized { get; set; }

    /// <summary>Saved bounds of the settings window.</summary>
    public int SettingsWindowWidth { get; set; } = DefaultSettingsWindowWidth;
    public int SettingsWindowHeight { get; set; } = DefaultSettingsWindowHeight;
    public bool SettingsWindowIsMaximized { get; set; }

    /// <summary>Saved bounds of child windows (Quick-Viewer / Editor).</summary>
    public int ChildWindowWidth { get; set; } = DefaultChildWindowWidth;
    public int ChildWindowHeight { get; set; } = DefaultChildWindowHeight;
    public bool ChildWindowIsMaximized { get; set; }

    /// <summary>
    /// If enabled, a single global font size (<see cref="GlobalFontSize"/>) is used across the main window and all child windows.
    /// </summary>
    public bool UseGlobalFontSize { get; set; }

    /// <summary>Global font size applied everywhere when <see cref="UseGlobalFontSize"/> is enabled.</summary>
    public double GlobalFontSize { get; set; } = DefaultGlobalFontSize;

    /// <summary>Font size for file and folder names in the file list.</summary>
    public double FileListFontSize { get; set; } = DefaultFileListFontSize;

    /// <summary>Font size for detail columns (size, modified date, type) and column headers.</summary>
    public double FileListDetailsFontSize { get; set; } = DefaultFileListDetailsFontSize;

    /// <summary>Font size for tab titles.</summary>
    public double TabFontSize { get; set; } = DefaultTabFontSize;

    /// <summary>Font size for drive buttons above the tabs.</summary>
    public double DriveBarFontSize { get; set; } = DefaultDriveBarFontSize;

    /// <summary>Font size for the address bar and filter box.</summary>
    public double AddressBarFontSize { get; set; } = DefaultAddressBarFontSize;

    /// <summary>Font size for breadcrumb segments.</summary>
    public double BreadcrumbFontSize { get; set; } = DefaultBreadcrumbFontSize;

    /// <summary>Font size for the status bar at the bottom of each panel.</summary>
    public double StatusBarFontSize { get; set; } = DefaultStatusBarFontSize;

    /// <summary>Font size for the F1–F12 function key bar.</summary>
    public double FunctionBarFontSize { get; set; } = DefaultFunctionBarFontSize;

    /// <summary>Individual font sizes for each Child Window when <see cref="UseGlobalFontSize"/> is disabled.</summary>
    public double QuickViewerFontSize { get; set; } = DefaultChildWindowFontSize;
    public double EditorFontSize { get; set; } = DefaultChildWindowFontSize;
    public double StorageAnalyzerFontSize { get; set; } = DefaultChildWindowFontSize;
    public double DeepSearchFontSize { get; set; } = DefaultChildWindowFontSize;
    public double DuplicateFinderFontSize { get; set; } = DefaultChildWindowFontSize;
    public double DirectorySyncFontSize { get; set; } = DefaultChildWindowFontSize;
    public double BatchRenameFontSize { get; set; } = DefaultChildWindowFontSize;
    public double FtpManagerFontSize { get; set; } = DefaultChildWindowFontSize;
    public double ArchiveDialogFontSize { get; set; } = DefaultChildWindowFontSize;
    public double AboutWindowFontSize { get; set; } = DefaultChildWindowFontSize;

    public AppSettings CloneNormalized() => new()
    {
        Version = Version,
        Theme = Theme,
        Language = LocalizationService.NormalizeLanguageCode(Language),
        CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
        SaveSession = SaveSession,
        DefaultViewMode = Enum.IsDefined(typeof(FileViewMode), DefaultViewMode) ? DefaultViewMode : FileViewMode.Details,
        ShowHiddenFilesByDefault = ShowHiddenFilesByDefault,
        ShowFileExtensions = ShowFileExtensions,
        SortFoldersFirst = SortFoldersFirst,
        ConfirmDeleteToRecycleBin = ConfirmDeleteToRecycleBin,
        AutoCalculateFolderSizeOnMark = AutoCalculateFolderSizeOnMark,
        ShowDriveBar = ShowDriveBar,
        ShowFunctionKeyBar = ShowFunctionKeyBar,
        ShowToolTips = ShowToolTips,
        FileListRowHeight = ClampRowHeight(FileListRowHeight, DefaultFileListRowHeight),
        EnableImageHoverPreview = EnableImageHoverPreview,
        ImageHoverPreviewSize = ClampPreviewSize(ImageHoverPreviewSize, DefaultImageHoverPreviewSize),
        SelectionBorderColor = NormalizeHexColor(SelectionBorderColor, DefaultSelectionBorderColor),
        ActivePanelBorderColor = NormalizeHexColor(ActivePanelBorderColor, DefaultActivePanelBorderColor),
        MarkedItemTextColor = NormalizeHexColor(MarkedItemTextColor, DefaultMarkedItemTextColor),
        TreemapVideoMediaColor = NormalizeHexColor(TreemapVideoMediaColor, DefaultTreemapVideoMediaColor),
        TreemapArchiveImageColor = NormalizeHexColor(TreemapArchiveImageColor, DefaultTreemapArchiveImageColor),
        TreemapVirtualDiskSystemColor = NormalizeHexColor(TreemapVirtualDiskSystemColor, DefaultTreemapVirtualDiskSystemColor),
        TreemapCodeDocumentColor = NormalizeHexColor(TreemapCodeDocumentColor, DefaultTreemapCodeDocumentColor),
        TreemapPictureGraphicColor = NormalizeHexColor(TreemapPictureGraphicColor, DefaultTreemapPictureGraphicColor),
        TreemapBinaryExecutableColor = NormalizeHexColor(TreemapBinaryExecutableColor, DefaultTreemapBinaryExecutableColor),
        TreemapOtherTempColor = NormalizeHexColor(TreemapOtherTempColor, DefaultTreemapOtherTempColor),
        MainWindowWidth = ClampDimension(MainWindowWidth, 640, 7680, DefaultMainWindowWidth),
        MainWindowHeight = ClampDimension(MainWindowHeight, 420, 4320, DefaultMainWindowHeight),
        MainWindowIsMaximized = MainWindowIsMaximized,
        SettingsWindowWidth = ClampDimension(SettingsWindowWidth <= 720 ? DefaultSettingsWindowWidth : SettingsWindowWidth, 860, 3840, DefaultSettingsWindowWidth),
        SettingsWindowHeight = ClampDimension(SettingsWindowHeight, 560, 2160, DefaultSettingsWindowHeight),
        SettingsWindowIsMaximized = SettingsWindowIsMaximized,
        ChildWindowWidth = ClampDimension(ChildWindowWidth, 460, 3840, DefaultChildWindowWidth),
        ChildWindowHeight = ClampDimension(ChildWindowHeight, 340, 2160, DefaultChildWindowHeight),
        ChildWindowIsMaximized = ChildWindowIsMaximized,
        UseGlobalFontSize = UseGlobalFontSize,
        GlobalFontSize = ClampFont(GlobalFontSize, DefaultGlobalFontSize),
        FileListFontSize = ClampFont(FileListFontSize, DefaultFileListFontSize),
        FileListDetailsFontSize = ClampFont(FileListDetailsFontSize, DefaultFileListDetailsFontSize),
        TabFontSize = ClampFont(TabFontSize, DefaultTabFontSize),
        DriveBarFontSize = ClampFont(DriveBarFontSize, DefaultDriveBarFontSize),
        AddressBarFontSize = ClampFont(AddressBarFontSize, DefaultAddressBarFontSize),
        BreadcrumbFontSize = ClampFont(BreadcrumbFontSize, DefaultBreadcrumbFontSize),
        StatusBarFontSize = ClampFont(StatusBarFontSize, DefaultStatusBarFontSize),
        FunctionBarFontSize = ClampFont(FunctionBarFontSize, DefaultFunctionBarFontSize),
        QuickViewerFontSize = ClampFont(QuickViewerFontSize, DefaultChildWindowFontSize),
        EditorFontSize = ClampFont(EditorFontSize, DefaultChildWindowFontSize),
        StorageAnalyzerFontSize = ClampFont(StorageAnalyzerFontSize, DefaultChildWindowFontSize),
        DeepSearchFontSize = ClampFont(DeepSearchFontSize, DefaultChildWindowFontSize),
        DuplicateFinderFontSize = ClampFont(DuplicateFinderFontSize, DefaultChildWindowFontSize),
        DirectorySyncFontSize = ClampFont(DirectorySyncFontSize, DefaultChildWindowFontSize),
        BatchRenameFontSize = ClampFont(BatchRenameFontSize, DefaultChildWindowFontSize),
        FtpManagerFontSize = ClampFont(FtpManagerFontSize, DefaultChildWindowFontSize),
        ArchiveDialogFontSize = ClampFont(ArchiveDialogFontSize, DefaultChildWindowFontSize),
        AboutWindowFontSize = ClampFont(AboutWindowFontSize, DefaultChildWindowFontSize),
    };

    public static double ClampFont(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return fallback;
        return Math.Clamp(Math.Round(value * 2.0) / 2.0, MinFontSize, MaxFontSize);
    }

    public static double ClampRowHeight(double value, double fallback = DefaultFileListRowHeight)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return fallback;
        return Math.Clamp(Math.Round(value), MinFileListRowHeight, MaxFileListRowHeight);
    }

    public static double ClampPreviewSize(double value, double fallback = DefaultImageHoverPreviewSize)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return fallback;
        return Math.Clamp(Math.Round(value / 10.0) * 10.0, MinImageHoverPreviewSize, MaxImageHoverPreviewSize);
    }

    public static int ClampDimension(int value, int min, int max, int fallback) =>
        value <= 0 ? fallback : Math.Clamp(value, min, max);

    public static string NormalizeHexColor(string? input, string fallback = DefaultSelectionBorderColor)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var trimmed = input.Trim();
        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;

        var hex = trimmed[1..];
        if (hex.Length is not (6 or 8))
            return fallback;

        foreach (var ch in hex)
        {
            if (!Uri.IsHexDigit(ch))
                return fallback;
        }

        return "#" + hex.ToUpperInvariant();
    }
}
