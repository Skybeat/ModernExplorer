using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Localization;

namespace ModernExplorer.Core.ViewModels;

public sealed partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(FileEntry entry) => Entry = entry;

    public SettingsViewModel Settings => SettingsViewModel.Current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(DisplayName), nameof(SizeText), nameof(ModifiedText), nameof(TypeText), nameof(TileSecondaryText), nameof(ContentModifiedLabelText), nameof(ContentSizeLabelText), nameof(Glyph), nameof(Opacity))]
    private FileEntry _entry;

    /// <summary>Commander mark state (Space/Insert), independent of the cursor focus.</summary>
    [ObservableProperty]
    private bool _isMarked;

    /// <summary>Indicates whether this item is currently the selected/focused item in the list.</summary>
    [ObservableProperty]
    private bool _isFocused;

    /// <summary>Calculated folder size (after marking a folder with Space).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(TileSecondaryText), nameof(ContentSizeLabelText))]
    private long? _computedFolderSize;

    public string Name => Entry.Name;

    public string DisplayName
    {
        get
        {
            if (Entry.IsDirectory || Entry.IsParentLink || Settings.ShowFileExtensions)
                return Entry.Name;
            var stem = Path.GetFileNameWithoutExtension(Entry.Name);
            return string.IsNullOrEmpty(stem) ? Entry.Name : stem;
        }
    }

    public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(TileSecondaryText));
        OnPropertyChanged(nameof(ContentModifiedLabelText));
        OnPropertyChanged(nameof(ContentSizeLabelText));
    }

    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsParentLink => Entry.IsParentLink;
    public bool IsImageFile => !Entry.IsDirectory && !Entry.IsParentLink && IsSupportedImageExtension(Entry.Extension);

    public static bool IsSupportedImageExtension(string? extension) =>
        extension?.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tif" or ".tiff" or ".jfif";

    public long EffectiveSize => Entry.IsDirectory ? ComputedFolderSize ?? 0 : Entry.Size;

    public string SizeText =>
        Entry.IsParentLink ? "" :
        Entry.IsDirectory ? (ComputedFolderSize is { } s ? Format.Size(s) : LocalizationService.T("File.FolderTag")) :
        Format.Size(Entry.Size);

    public string ModifiedText => Entry.IsParentLink ? "" : Entry.LastWriteTime.ToString("dd.MM.yyyy HH:mm");

    public string TypeText => Entry.IsParentLink ? "" : Entry.IsDirectory ? LocalizationService.T("File.FolderType") :
        Entry.Extension.Length > 1 ? Entry.Extension[1..].ToUpperInvariant() : LocalizationService.T("File.FileType");

    public string TileSecondaryText =>
        Entry.IsParentLink ? "" :
        Entry.IsDirectory ? (ComputedFolderSize is { } s ? Format.Size(s) : ModifiedText) :
        SizeText;

    public string ContentModifiedLabelText =>
        Entry.IsParentLink ? "" : $"{LocalizationService.T("Col.Modified")}: {ModifiedText}";

    public string ContentSizeLabelText =>
        Entry.IsParentLink ? "" :
        Entry.IsDirectory && ComputedFolderSize is null ? "" :
        $"{LocalizationService.T("Col.Size")}: {SizeText}";

    public double Opacity => Entry.IsHidden ? 0.55 : 1.0;

    /// <summary>Segoe Fluent Icons Glyph.</summary>
    public string Glyph => Entry.IsParentLink ? "\uE74A" : Entry.IsDirectory ? "\uE8B7" : Entry.Extension.ToLowerInvariant() switch
    {
        ".txt" or ".md" or ".log" or ".ini" or ".cfg" => "\uE8A5",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" => "\uEB9F",
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "\uE8D6",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "\uE714",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "\uF012",
        ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => "\uE756",
        ".cs" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".xaml" or ".html" or ".css" or ".cpp" or ".h" => "\uE943",
        ".pdf" => "\uEA90",
        _ => "\uE7C3",
    };
}

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Size(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return v >= 100 ? $"{v:0} {Units[u]}" : v >= 10 ? $"{v:0.#} {Units[u]}" : $"{v:0.##} {Units[u]}";
    }

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}

