using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ModernExplorer.Core.FileSystem;

/// <summary>
/// Raw data of an extracted Windows Shell icon (32-bit BGRA, premultiplied alpha for WinUI WriteableBitmap).
/// </summary>
public sealed record ShellIconData(int Width, int Height, byte[] BgraPixels);

/// <summary>
/// Extracts 1:1 native Windows Explorer system icons via <c>SHGetFileInfoW</c>
/// (folders, special folders, drives, file types, and embedded icons from .exe/.lnk/.ico).
/// </summary>
public static class ShellIconExtractor
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    private static readonly ConcurrentDictionary<string, ShellIconData?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PerInstanceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".url", ".cur", ".ani", ".scr", ".cpl", ".msc",
    };

    public static ShellIconData? GetIconForEntry(FileEntry entry) =>
        GetIconForEntryCore(entry, small: true);

    public static ShellIconData? GetLargeIconForEntry(FileEntry entry) =>
        GetIconForEntryCore(entry, small: false);

    private static ShellIconData? GetIconForEntryCore(FileEntry entry, bool small)
    {
        string prefix = small ? "S:" : "L:";
        if (entry.IsDirectory)
        {
            if (entry.IsParentLink)
                return small ? GetStandardFolderIcon() : GetLargeStandardFolderIcon();

            // Special folders (Downloads, Desktop, Documents, etc.) have ReadOnly or System attributes on Windows
            bool isCustomizedFolder = (entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0;
            if (isCustomizedFolder)
            {
                return Cache.GetOrAdd(prefix + "PATH:" + entry.FullPath,
                    _ => ExtractIcon(entry.FullPath, FILE_ATTRIBUTE_DIRECTORY, useFileAttributes: false, small: small)
                         ?? (small ? GetStandardFolderIcon() : GetLargeStandardFolderIcon()));
            }

            return small ? GetStandardFolderIcon() : GetLargeStandardFolderIcon();
        }

        var ext = entry.Extension;
        if (!string.IsNullOrEmpty(ext) && PerInstanceExtensions.Contains(ext))
        {
            return Cache.GetOrAdd(prefix + "FILE:" + entry.FullPath,
                _ => ExtractIcon(entry.FullPath, FILE_ATTRIBUTE_NORMAL, useFileAttributes: false, small: small)
                     ?? (small ? GetExtensionIcon(ext) : GetLargeExtensionIcon(ext)));
        }

        return small ? GetExtensionIcon(ext) : GetLargeExtensionIcon(ext);
    }

    public static ShellIconData? GetDriveIcon(string rootPath) =>
        Cache.GetOrAdd("DRIVE:" + rootPath, _ => ExtractIcon(rootPath, FILE_ATTRIBUTE_DIRECTORY, useFileAttributes: false, small: true));

    public static ShellIconData? GetStandardFolderIcon() =>
        Cache.GetOrAdd("DIR:__STANDARD__", _ => ExtractIcon("Folder", FILE_ATTRIBUTE_DIRECTORY, useFileAttributes: true, small: true));

    public static ShellIconData? GetLargeStandardFolderIcon() =>
        Cache.GetOrAdd("L:DIR:__STANDARD__", _ => ExtractIcon("Folder", FILE_ATTRIBUTE_DIRECTORY, useFileAttributes: true, small: false));

    public static ShellIconData? GetExtensionIcon(string extension)
    {
        var key = string.IsNullOrEmpty(extension) ? "EXT:__NONE__" : "EXT:" + extension.ToLowerInvariant();
        var dummyName = string.IsNullOrEmpty(extension) ? "file" : "file" + extension.ToLowerInvariant();
        return Cache.GetOrAdd(key, _ => ExtractIcon(dummyName, FILE_ATTRIBUTE_NORMAL, useFileAttributes: true, small: true));
    }

    public static ShellIconData? GetLargeExtensionIcon(string extension)
    {
        var key = string.IsNullOrEmpty(extension) ? "L:EXT:__NONE__" : "L:EXT:" + extension.ToLowerInvariant();
        var dummyName = string.IsNullOrEmpty(extension) ? "file" : "file" + extension.ToLowerInvariant();
        return Cache.GetOrAdd(key, _ => ExtractIcon(dummyName, FILE_ATTRIBUTE_NORMAL, useFileAttributes: true, small: false));
    }

    private static ShellIconData? ExtractIcon(string pathOrName, uint fileAttributes, bool useFileAttributes, bool small = true)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var shfi = new SHFILEINFO();
        uint flags = SHGFI_ICON | (small ? SHGFI_SMALLICON : SHGFI_LARGEICON);
        if (useFileAttributes) flags |= SHGFI_USEFILEATTRIBUTES;

        IntPtr res = SHGetFileInfoW(pathOrName, fileAttributes, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            return ConvertHIconToBgra(shfi.hIcon);
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    private static ShellIconData? ConvertHIconToBgra(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo))
            return null;

        try
        {
            IntPtr hBitmap = iconInfo.hbmColor != IntPtr.Zero ? iconInfo.hbmColor : iconInfo.hbmMask;
            if (hBitmap == IntPtr.Zero) return null;

            if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out var bmp) == 0 || bmp.bmWidth <= 0 || bmp.bmHeight <= 0)
                return null;

            int width = bmp.bmWidth;
            int height = iconInfo.hbmColor != IntPtr.Zero ? bmp.bmHeight : bmp.bmHeight / 2;
            if (height <= 0) return null;

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;

            try
            {
                var bmi = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // Top-Down DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                };

                var pixels = new byte[width * height * 4];
                if (iconInfo.hbmColor != IntPtr.Zero)
                {
                    if (GetDIBits(hdc, iconInfo.hbmColor, 0, (uint)height, pixels, ref bmi, 0) == 0)
                        return null;
                }

                bool hasAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0) { hasAlpha = true; break; }
                }

                if (hasAlpha)
                {
                    // WinUI WriteableBitmap expects premultiplied BGRA8
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte a = pixels[i + 3];
                        pixels[i + 0] = (byte)(pixels[i + 0] * a / 255);
                        pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                        pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                    }
                }
                else if (iconInfo.hbmMask != IntPtr.Zero)
                {
                    var maskPixels = new byte[width * height * 4];
                    var maskBmi = bmi;
                    if (GetDIBits(hdc, iconInfo.hbmMask, 0, (uint)height, maskPixels, ref maskBmi, 0) != 0)
                    {
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            bool transparent = maskPixels[i] != 0;
                            if (transparent)
                            {
                                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
                            }
                            else
                            {
                                pixels[i + 3] = 255;
                            }
                        }
                    }
                }

                return new ShellIconData(width, height, pixels);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObject, int nCount, out BITMAP lpObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}

