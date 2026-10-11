using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Settings;
using ModernExplorer.Core.ViewModels;
using Windows.UI;

namespace ModernExplorer.App;

/// <summary>
/// Converts <see cref="FileEntry"/>, <see cref="FileItemViewModel"/>, or drive paths
/// into the native Windows Explorer system icon (<see cref="WriteableBitmap"/>)
/// and provides static x:Bind helpers for selection and focus borders.
/// </summary>
public sealed class ShellIconConverter : IValueConverter
{
    private static readonly Dictionary<ShellIconData, ImageSource> BitmapCache = [];
    private static readonly Dictionary<string, ImageSource> ThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> ThumbnailLruQueue = new();
    private const int MaxThumbnailCacheEntries = 300;
    private static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".ico", ".tif", ".tiff"
    };
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public static ImageSource? ToImageSource(FileEntry? entry) =>
        entry is null ? null : GetOrCreateBitmap(ShellIconExtractor.GetIconForEntry(entry));

    public static ImageSource? ToLargeImageSource(FileEntry? entry)
    {
        if (entry is null) return null;

        if (!entry.IsDirectory && !entry.IsParentLink && !entry.IsArchiveVirtualEntry && !entry.IsRemoteEntry &&
            entry.Size > 0 && entry.Size <= 40L * 1024 * 1024 &&
            ThumbnailExtensions.Contains(entry.Extension) &&
            File.Exists(entry.FullPath))
        {
            string cacheKey = $"{entry.FullPath}|{entry.LastWriteTime.Ticks}";
            if (ThumbnailCache.TryGetValue(cacheKey, out var cachedThumb))
                return cachedThumb;

            try
            {
                var bmp = new BitmapImage
                {
                    DecodePixelWidth = 128,
                    DecodePixelType = DecodePixelType.Logical,
                };
                bmp.UriSource = new Uri(entry.FullPath);

                if (ThumbnailCache.Count >= MaxThumbnailCacheEntries && ThumbnailLruQueue.Count > 0)
                {
                    var oldest = ThumbnailLruQueue.Dequeue();
                    ThumbnailCache.Remove(oldest);
                }
                ThumbnailCache[cacheKey] = bmp;
                ThumbnailLruQueue.Enqueue(cacheKey);
                return bmp;
            }
            catch
            {
                // Fall back to shell icon if URI or image fails
            }
        }

        return GetOrCreateBitmap(ShellIconExtractor.GetLargeIconForEntry(entry));
    }

    public static ImageSource? DriveToImageSource(string? rootPath) =>
        string.IsNullOrEmpty(rootPath) ? null : GetOrCreateBitmap(ShellIconExtractor.GetDriveIcon(rootPath));

    public static Brush SelectionBorderBrush(bool isFocused, string? hexColor) =>
        isFocused ? new SolidColorBrush(ParseHexColor(hexColor, AppSettings.DefaultSelectionBorderColor)) : TransparentBrush;

    public static Brush DriveButtonBackground(bool isSelected, string? activePanelHexColor, AppTheme theme)
    {
        if (isSelected)
        {
            var c = ParseHexColor(activePanelHexColor, AppSettings.DefaultActivePanelBorderColor);
            byte alpha = theme == AppTheme.Light ? (byte)42 : (byte)58;
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        return theme == AppTheme.Light
            ? new SolidColorBrush(Color.FromArgb(14, 0, 0, 0))
            : new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
    }

    public static Brush DriveButtonBorderBrush(bool isSelected, string? activePanelHexColor, AppTheme theme)
    {
        if (isSelected)
            return new SolidColorBrush(ParseHexColor(activePanelHexColor, AppSettings.DefaultActivePanelBorderColor));

        return theme == AppTheme.Light
            ? new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))
            : new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
    }

    public static Thickness DriveButtonBorderThickness(bool isSelected) =>
        isSelected ? new Thickness(1.5) : new Thickness(1);

    public static Windows.UI.Text.FontWeight DriveButtonFontWeight(bool isSelected) =>
        isSelected ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

    public static Brush ItemTextForeground(bool isMarked, string? markedHexColor, AppTheme theme)
    {
        if (isMarked)
            return new SolidColorBrush(ParseHexColor(markedHexColor, AppSettings.DefaultMarkedItemTextColor));

        return theme == AppTheme.Light
            ? new SolidColorBrush(Color.FromArgb(255, 24, 24, 28))
            : new SolidColorBrush(Color.FromArgb(255, 243, 243, 247));
    }

    public static Brush HexToBrush(string? hexColor) =>
        new SolidColorBrush(ParseHexColor(hexColor));

    public static Color ParseHexColor(string? hexColor, string fallback = AppSettings.DefaultSelectionBorderColor)
    {
        var normalized = AppSettings.NormalizeHexColor(hexColor, fallback);
        var hex = normalized.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return Color.FromArgb(255, r, g, b);
        }

        if (hex.Length == 8 &&
            byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a) &&
            byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r8) &&
            byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g8) &&
            byte.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b8))
        {
            return Color.FromArgb(a, r8, g8, b8);
        }

        return Colors.White;
    }

    private static ImageSource? GetOrCreateBitmap(ShellIconData? data)
    {
        if (data is null) return null;
        if (BitmapCache.TryGetValue(data, out var cached))
            return cached;

        var bmp = new WriteableBitmap(data.Width, data.Height);
        using (var stream = bmp.PixelBuffer.AsStream())
        {
            stream.Write(data.BgraPixels, 0, data.BgraPixels.Length);
        }
        bmp.Invalidate();
        BitmapCache[data] = bmp;
        return bmp;
    }

    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            FileEntry entry => ToImageSource(entry),
            FileItemViewModel item => ToImageSource(item.Entry),
            DriveItemViewModel drive => DriveToImageSource(drive.RootPath),
            string driveRoot => DriveToImageSource(driveRoot),
            _ => null,
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
