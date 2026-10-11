using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace ModernExplorer.Core.Features;

public sealed record ImageExifMetadata(
    string FilePath,
    int Width,
    int Height,
    DateTime? DateTaken,
    string? CameraMake,
    string? CameraModel,
    int? IsoSpeed)
{
    public string ResolutionText => Width > 0 && Height > 0 ? $"{Width}x{Height}" : "";
    public bool HasAnyMetadata => Width > 0 || Height > 0 || DateTaken.HasValue || !string.IsNullOrEmpty(CameraModel);
}

public sealed record BatchAttributeTimestampOptions
{
    public bool SetCreationTime { get; init; }
    public DateTime CreationTimeLocal { get; init; } = DateTime.Now;

    public bool SetLastWriteTime { get; init; }
    public DateTime LastWriteTimeLocal { get; init; } = DateTime.Now;

    public bool SetLastAccessTime { get; init; }
    public DateTime LastAccessTimeLocal { get; init; } = DateTime.Now;

    public bool CopyExifDateTakenToTimestamps { get; init; }

    public bool ShiftTimestamps { get; init; }
    public TimeSpan ShiftOffset { get; init; } = TimeSpan.Zero;

    public bool ModifyAttributes { get; init; }
    public bool? ReadOnly { get; init; }
    public bool? Hidden { get; init; }
    public bool? Archive { get; init; }
    public bool? System { get; init; }

    public bool IncludeSubdirectories { get; init; }
}

public sealed record BatchAttributeTimestampResult(
    int ModifiedCount,
    IReadOnlyList<string> Errors);

/// <summary>
/// Lightweight binary image/EXIF metadata parser (PNG, JPEG, GIF, BMP, WEBP)
/// and batch file timestamp + attribute editor.
/// </summary>
public static class MetadataAndAttributeService
{
    public static ImageExifMetadata InspectMediaMetadata(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new ImageExifMetadata(filePath ?? "", 0, 0, null, null, null, null);

        try
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            return ext switch
            {
                ".png" => ParsePng(filePath, fs),
                ".jpg" or ".jpeg" or ".jfif" => ParseJpegAndExif(filePath, fs),
                ".gif" => ParseGif(filePath, fs),
                ".bmp" => ParseBmp(filePath, fs),
                ".webp" => ParseWebp(filePath, fs),
                _ => new ImageExifMetadata(filePath, 0, 0, null, null, null, null),
            };
        }
        catch
        {
            return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);
        }
    }

    public static BatchAttributeTimestampResult ApplyBatchChanges(
        IEnumerable<string> targetPaths,
        BatchAttributeTimestampOptions options)
    {
        var allTargets = new List<string>();
        foreach (var path in targetPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (File.Exists(path) || Directory.Exists(path))
            {
                allTargets.Add(path);
                if (options.IncludeSubdirectories && Directory.Exists(path))
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
                            allTargets.Add(sub);
                    }
                    catch { }
                }
            }
        }

        int modified = 0;
        var errors = new List<string>();

        foreach (var path in allTargets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                bool isDir = Directory.Exists(path);
                bool isFile = !isDir && File.Exists(path);
                if (!isDir && !isFile) continue;

                // Temporarily clear ReadOnly before changing timestamps if needed
                FileAttributes currentAttrs = File.GetAttributes(path);
                bool wasReadOnly = (currentAttrs & FileAttributes.ReadOnly) != 0;
                if (wasReadOnly && (options.SetCreationTime || options.SetLastWriteTime || options.SetLastAccessTime || options.ShiftTimestamps || options.CopyExifDateTakenToTimestamps))
                {
                    File.SetAttributes(path, currentAttrs & ~FileAttributes.ReadOnly);
                }

                if (options.CopyExifDateTakenToTimestamps && isFile)
                {
                    var exif = InspectMediaMetadata(path);
                    if (exif.DateTaken is { } dt)
                    {
                        File.SetCreationTime(path, dt);
                        File.SetLastWriteTime(path, dt);
                    }
                }

                if (options.SetCreationTime)
                {
                    if (isDir) Directory.SetCreationTime(path, options.CreationTimeLocal);
                    else File.SetCreationTime(path, options.CreationTimeLocal);
                }

                if (options.SetLastWriteTime)
                {
                    if (isDir) Directory.SetLastWriteTime(path, options.LastWriteTimeLocal);
                    else File.SetLastWriteTime(path, options.LastWriteTimeLocal);
                }

                if (options.SetLastAccessTime)
                {
                    if (isDir) Directory.SetLastAccessTime(path, options.LastAccessTimeLocal);
                    else File.SetLastAccessTime(path, options.LastAccessTimeLocal);
                }

                if (options.ShiftTimestamps && options.ShiftOffset != TimeSpan.Zero)
                {
                    if (isDir)
                    {
                        Directory.SetCreationTime(path, Directory.GetCreationTime(path) + options.ShiftOffset);
                        Directory.SetLastWriteTime(path, Directory.GetLastWriteTime(path) + options.ShiftOffset);
                    }
                    else
                    {
                        File.SetCreationTime(path, File.GetCreationTime(path) + options.ShiftOffset);
                        File.SetLastWriteTime(path, File.GetLastWriteTime(path) + options.ShiftOffset);
                    }
                }

                if (options.ModifyAttributes)
                {
                    FileAttributes next = currentAttrs;
                    next = ApplyFlag(next, FileAttributes.ReadOnly, options.ReadOnly);
                    next = ApplyFlag(next, FileAttributes.Hidden, options.Hidden);
                    next = ApplyFlag(next, FileAttributes.Archive, options.Archive);
                    next = ApplyFlag(next, FileAttributes.System, options.System);
                    File.SetAttributes(path, next);
                }
                else if (wasReadOnly)
                {
                    File.SetAttributes(path, currentAttrs);
                }

                modified++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return new BatchAttributeTimestampResult(modified, errors);
    }

    private static FileAttributes ApplyFlag(FileAttributes attrs, FileAttributes flag, bool? state) => state switch
    {
        true => attrs | flag,
        false => attrs & ~flag,
        null => attrs,
    };

    private static ImageExifMetadata ParsePng(string filePath, Stream s)
    {
        Span<byte> buf = stackalloc byte[24];
        if (s.Read(buf) < 24)
            return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);

        // PNG signature + IHDR chunk at offset 16..23
        if (buf[0] == 0x89 && buf[1] == 0x50 && buf[2] == 0x4E && buf[3] == 0x47)
        {
            int width = BinaryPrimitives.ReadInt32BigEndian(buf.Slice(16, 4));
            int height = BinaryPrimitives.ReadInt32BigEndian(buf.Slice(20, 4));
            return new ImageExifMetadata(filePath, Math.Max(0, width), Math.Max(0, height), null, null, null, null);
        }
        return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);
    }

    private static ImageExifMetadata ParseGif(string filePath, Stream s)
    {
        Span<byte> buf = stackalloc byte[10];
        if (s.Read(buf) < 10)
            return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);

        if (buf[0] == 'G' && buf[1] == 'I' && buf[2] == 'F')
        {
            int width = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(6, 2));
            int height = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(8, 2));
            return new ImageExifMetadata(filePath, width, height, null, null, null, null);
        }
        return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);
    }

    private static ImageExifMetadata ParseBmp(string filePath, Stream s)
    {
        Span<byte> buf = stackalloc byte[26];
        if (s.Read(buf) < 26)
            return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);

        if (buf[0] == 'B' && buf[1] == 'M')
        {
            int width = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(18, 4)));
            int height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(22, 4)));
            return new ImageExifMetadata(filePath, width, height, null, null, null, null);
        }
        return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);
    }

    private static ImageExifMetadata ParseWebp(string filePath, Stream s)
    {
        Span<byte> buf = stackalloc byte[30];
        if (s.Read(buf) < 30)
            return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);

        if (buf[0] == 'R' && buf[1] == 'I' && buf[2] == 'F' && buf[3] == 'F' &&
            buf[8] == 'W' && buf[9] == 'E' && buf[10] == 'B' && buf[11] == 'P')
        {
            if (buf[12] == 'V' && buf[13] == 'P' && buf[14] == '8' && buf[15] == ' ')
            {
                int width = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(26, 2)) & 0x3FFF;
                int height = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(28, 2)) & 0x3FFF;
                return new ImageExifMetadata(filePath, width, height, null, null, null, null);
            }
        }
        return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);
    }

    private static ImageExifMetadata ParseJpegAndExif(string filePath, Stream s)
    {
        if (s.ReadByte() != 0xFF || s.ReadByte() != 0xD8)
            return new ImageExifMetadata(filePath, 0, 0, null, null, null, null);

        int width = 0;
        int height = 0;
        DateTime? dateTaken = null;
        string? make = null;
        string? model = null;
        int? iso = null;

        Span<byte> sof = stackalloc byte[5];
        while (s.Position < s.Length)
        {
            int b = s.ReadByte();
            if (b < 0) break;
            if (b != 0xFF) continue;

            int marker = s.ReadByte();
            while (marker == 0xFF)
                marker = s.ReadByte();
            if (marker < 0 || marker == 0xDA || marker == 0xD9) // SOS or EOI
                break;

            int lenHi = s.ReadByte();
            int lenLo = s.ReadByte();
            if (lenHi < 0 || lenLo < 0) break;
            int segLen = (lenHi << 8) | lenLo;
            if (segLen < 2) break;

            long nextPos = s.Position + segLen - 2;

            // APP1 (EXIF)
            if (marker == 0xE1 && segLen > 14 && segLen <= 65535)
            {
                byte[] app1 = new byte[segLen - 2];
                if (s.Read(app1, 0, app1.Length) == app1.Length)
                {
                    TryParseExifSegment(app1, ref dateTaken, ref make, ref model, ref iso);
                }
            }
            // SOF0 / SOF1 / SOF2 (Frame header with dimensions)
            else if (marker is 0xC0 or 0xC1 or 0xC2 && segLen >= 7)
            {
                if (s.Read(sof) == 5)
                {
                    height = BinaryPrimitives.ReadUInt16BigEndian(sof.Slice(1, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(sof.Slice(3, 2));
                }
            }

            if (nextPos > s.Length) break;
            s.Position = nextPos;
        }

        return new ImageExifMetadata(filePath, width, height, dateTaken, make, model, iso);
    }

    private static void TryParseExifSegment(
        ReadOnlySpan<byte> seg,
        ref DateTime? dateTaken,
        ref string? make,
        ref string? model,
        ref int? iso)
    {
        if (seg.Length < 14) return;
        if (seg[0] != 'E' || seg[1] != 'x' || seg[2] != 'i' || seg[3] != 'f' || seg[4] != 0 || seg[5] != 0)
            return;

        var tiff = seg[6..];
        bool littleEndian = tiff[0] == 'I' && tiff[1] == 'I';
        if (!littleEndian && !(tiff[0] == 'M' && tiff[1] == 'M'))
            return;

        uint ifd0Offset = ReadU32(tiff.Slice(4, 4), littleEndian);
        if (ifd0Offset + 2 > tiff.Length)
            return;

        uint exifSubIfdOffset = 0;
        ParseIfd(tiff, (int)ifd0Offset, littleEndian, ref dateTaken, ref make, ref model, ref iso, ref exifSubIfdOffset);
        if (exifSubIfdOffset > 0 && exifSubIfdOffset + 2 <= tiff.Length)
        {
            uint dummy = 0;
            ParseIfd(tiff, (int)exifSubIfdOffset, littleEndian, ref dateTaken, ref make, ref model, ref iso, ref dummy);
        }
    }

    private static void ParseIfd(
        ReadOnlySpan<byte> tiff,
        int offset,
        bool le,
        ref DateTime? dateTaken,
        ref string? make,
        ref string? model,
        ref int? iso,
        ref uint exifSubIfdOffset)
    {
        if (offset < 0 || offset + 2 > tiff.Length) return;
        ushort count = ReadU16(tiff.Slice(offset, 2), le);
        int pos = offset + 2;

        for (int i = 0; i < count; i++)
        {
            if (pos + 12 > tiff.Length) break;
            var entry = tiff.Slice(pos, 12);
            ushort tag = ReadU16(entry[..2], le);
            ushort type = ReadU16(entry.Slice(2, 2), le);
            uint numValues = ReadU32(entry.Slice(4, 4), le);
            uint valOffset = ReadU32(entry.Slice(8, 4), le);

            // 0x010F = Make, 0x0110 = Model, 0x0132 = DateTime, 0x9003 = DateTimeOriginal, 0x8769 = ExifOffset, 0x8827 = ISOSpeedRatings
            if (tag == 0x8769)
            {
                exifSubIfdOffset = valOffset;
            }
            else if (tag == 0x8827 && type == 3)
            {
                iso = ReadU16(entry.Slice(8, 2), le);
            }
            else if (type == 2 && numValues > 1 && numValues < 128 && valOffset + numValues <= tiff.Length)
            {
                string ascii = Encoding.ASCII.GetString(tiff.Slice((int)valOffset, (int)numValues)).TrimEnd('\0', ' ');
                if (tag == 0x010F && string.IsNullOrEmpty(make))
                    make = ascii;
                else if (tag == 0x0110 && string.IsNullOrEmpty(model))
                    model = ascii;
                else if (tag is 0x9003 or 0x0132 && !dateTaken.HasValue)
                {
                    if (DateTime.TryParseExact(ascii, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        dateTaken = dt;
                }
            }

            pos += 12;
        }
    }

    private static ushort ReadU16(ReadOnlySpan<byte> b, bool le) =>
        le ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);

    private static uint ReadU32(ReadOnlySpan<byte> b, bool le) =>
        le ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);
}
