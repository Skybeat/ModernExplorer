using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.ViewModels;

namespace ModernExplorer.Core.Viewer;

public enum PreviewMode
{
    Text,
    Markdown,
    Json,
    Xml,
    Csv,
    Log,
    Image,
    Pdf,
    Media,
    Archive,
    Directory,
    BinaryHex
}

public sealed record ArchiveEntryPreview(
    string FullName,
    long CompressedSize,
    long UncompressedSize,
    DateTime LastModified,
    bool IsDirectory)
{
    public string SizeText => IsDirectory ? "<DIR>" : Format.Size(UncompressedSize);
    public string CompressedText => IsDirectory ? "" : Format.Size(CompressedSize);
    public string RatioText => IsDirectory || UncompressedSize <= 0
        ? "–"
        : $"{Math.Clamp(100.0 * CompressedSize / UncompressedSize, 0, 999):0.#} %";
    public string ModifiedText => LastModified == default ? "" : LastModified.ToString("dd.MM.yyyy HH:mm");
}

public sealed record CsvTablePreview(
    IReadOnlyList<string> Headers,
    IReadOnlyList<string[]> Rows,
    char Delimiter,
    int TotalLineCount);

public sealed record DirectoryOverview(
    string FullPath,
    int FileCount,
    int FolderCount,
    long TotalSize,
    DateTime CreatedTime,
    DateTime LastWriteTime,
    FileAttributes Attributes,
    IReadOnlyList<(string Extension, int Count, long Size)> TopExtensions,
    IReadOnlyList<FileEntry> Entries);

public sealed record LoadedDocument(
    string FullPath,
    string Content,
    Encoding Encoding,
    string NewLine,
    PreviewMode Mode,
    long FileSize,
    bool IsTruncated,
    string FileTypeDescription = "",
    string HexPreview = "",
    string Sha256Hex = "",
    IReadOnlyList<ArchiveEntryPreview>? ArchiveEntries = null,
    CsvTablePreview? CsvTable = null,
    DirectoryOverview? DirectoryInfo = null);

/// <summary>
/// Intelligent file type detection, fast loading (including images, PDF, media, ZIP archives,
/// CSV tables, JSON/XML formatting, Markdown, logs, encoding detection, and hex dump),
/// and atomic saving for Quick Viewer (F3) and the internal Editor (F4).
/// </summary>
public static class FileContentService
{
    public const int DefaultPreviewMaxBytes = 256 * 1024; // 256 KB for instant text preview
    public const int LargeFileWarningBytes = 50 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff", ".jfif", ".svg"
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma",
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".nupkg", ".jar", ".war", ".apk", ".whl", ".vsix", ".docx", ".xlsx", ".pptx", ".odt", ".ods"
    };

    private static readonly HashSet<string> XmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".xaml", ".csproj", ".vbproj", ".fsproj", ".slnx", ".props", ".targets",
        ".config", ".nuspec", ".manifest", ".resx", ".html", ".htm", ".xhtml"
    };

    static FileContentService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    public static bool IsSvgFile(string path) =>
        string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase);

    public static bool IsMediaFile(string path) =>
        MediaExtensions.Contains(Path.GetExtension(path));

    public static bool IsArchiveFile(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path));

    public static PreviewMode DetectMode(string path, ReadOnlySpan<byte> sample)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            return PreviewMode.Directory;

        var ext = Path.GetExtension(path).ToLowerInvariant();

        // 1. Image formats (by file extension or magic header)
        if (ImageExtensions.Contains(ext) || HasImageSignature(sample))
            return PreviewMode.Image;

        // 2. PDF documents
        if (ext == ".pdf" || HasPdfSignature(sample))
            return PreviewMode.Pdf;

        // 3. Audio & video files
        if (MediaExtensions.Contains(ext))
            return PreviewMode.Media;

        // 4. ZIP-based archives & packages
        if (ArchiveExtensions.Contains(ext))
            return PreviewMode.Archive;

        // 5. Structured text formats
        if (ext is ".md" or ".markdown" or ".mdx")
            return PreviewMode.Markdown;

        if (ext is ".json" or ".jsonl" or ".geojson" or ".har")
            return PreviewMode.Json;

        if (XmlExtensions.Contains(ext))
            return PreviewMode.Xml;

        if (ext is ".csv" or ".tsv")
            return PreviewMode.Csv;

        if (ext is ".log" or ".err" or ".out")
            return PreviewMode.Log;

        // 6. Binary vs. text file
        if (LooksBinary(sample))
            return PreviewMode.BinaryHex;

        return PreviewMode.Text;
    }

    public static string DescribeFileSignature(string path, ReadOnlySpan<byte> sample, PreviewMode mode)
    {
        if (mode == PreviewMode.Directory)
            return "Verzeichnis / Ordner";

        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (sample.Length >= 8 && sample[0] == 0x89 && sample[1] == 0x50 && sample[2] == 0x4E && sample[3] == 0x47)
            return "PNG-Bilddatei (Portable Network Graphics)";
        if (sample.Length >= 3 && sample[0] == 0xFF && sample[1] == 0xD8 && sample[2] == 0xFF)
            return "JPEG-Bilddatei";
        if (sample.Length >= 6 && sample[0] == (byte)'G' && sample[1] == (byte)'I' && sample[2] == (byte)'F')
            return "GIF-Grafik";
        if (sample.Length >= 12 && sample[0] == (byte)'R' && sample[1] == (byte)'I' && sample[2] == (byte)'F' && sample[3] == (byte)'F' &&
            sample[8] == (byte)'W' && sample[9] == (byte)'E' && sample[10] == (byte)'B' && sample[11] == (byte)'P')
            return "WebP-Bilddatei";
        if (sample.Length >= 2 && sample[0] == (byte)'B' && sample[1] == (byte)'M')
            return "BMP-Bitmap-Bild";
        if (sample.Length >= 4 && sample[0] == 0x00 && sample[1] == 0x00 && sample[2] == 0x01 && sample[3] == 0x00)
            return "Windows-Icon (.ico)";
        if (ext == ".svg")
            return "SVG-Vektorgrafik (Scalable Vector Graphics)";
        if (HasPdfSignature(sample) || ext == ".pdf")
            return "PDF-Dokument (Portable Document Format)";
        if (sample.Length >= 2 && sample[0] == (byte)'M' && sample[1] == (byte)'Z')
            return ext is ".dll" ? "Windows Dynamic Link Library (PE/COFF)" : "Windows Ausführbare Programmdatei (PE/COFF)";
        if (sample.Length >= 4 && sample[0] == (byte)'P' && sample[1] == (byte)'K' && sample[2] == 0x03 && sample[3] == 0x04)
            return $"ZIP-basiertes Archiv / Paket ({(string.IsNullOrEmpty(ext) ? ".zip" : ext)})";
        if (sample.Length >= 15 && Encoding.ASCII.GetString(sample[..15]).StartsWith("SQLite format 3", StringComparison.Ordinal))
            return "SQLite 3 Datenbank";
        if (sample.Length >= 6 && sample[0] == 0x37 && sample[1] == 0x7A && sample[2] == 0xBC && sample[3] == 0xAF)
            return "7-Zip Archiv (.7z)";
        if (sample.Length >= 4 && sample[0] == (byte)'R' && sample[1] == (byte)'a' && sample[2] == (byte)'r' && sample[3] == (byte)'!')
            return "RAR-Archiv (.rar)";

        return mode switch
        {
            PreviewMode.Image => $"Bilddatei ({ext.ToUpperInvariant().TrimStart('.')})",
            PreviewMode.Media => $"Audio-/Videodatei ({ext.ToUpperInvariant().TrimStart('.')})",
            PreviewMode.Archive => $"Archiv / Paket ({ext.ToUpperInvariant().TrimStart('.')})",
            PreviewMode.Json => "JSON-Datenstruktur",
            PreviewMode.Xml => $"XML/Markup-Dokument ({ext})",
            PreviewMode.Csv => ext == ".tsv" ? "TSV-Tabellendaten (Tab-getrennt)" : "CSV-Tabellendaten",
            PreviewMode.Markdown => "Markdown-Dokument",
            PreviewMode.Log => "Protokoll-/Logdatei",
            PreviewMode.BinaryHex => string.IsNullOrEmpty(ext) ? "Binärdatei" : $"Binärdatei ({ext})",
            _ => DescribeTextExtension(ext),
        };
    }

    private static string DescribeTextExtension(string ext) => ext switch
    {
        ".cs" => "C#-Quellcode",
        ".xaml" => "XAML-Layoutdatei",
        ".py" => "Python-Skript",
        ".js" or ".mjs" => "JavaScript-Datei",
        ".ts" or ".tsx" => "TypeScript-Datei",
        ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp" => "C/C++-Quellcode",
        ".rs" => "Rust-Quellcode",
        ".go" => "Go-Quellcode",
        ".java" => "Java-Quellcode",
        ".sql" => "SQL-Skript",
        ".ps1" or ".psm1" => "PowerShell-Skript",
        ".bat" or ".cmd" => "Windows Batch-Skript",
        ".sh" => "Shell-Skript",
        ".yaml" or ".yml" => "YAML-Konfiguration",
        ".toml" => "TOML-Konfiguration",
        ".ini" or ".cfg" or ".conf" => "Konfigurationsdatei",
        ".css" or ".scss" => "Stylesheet (CSS)",
        ".txt" => "Textdatei",
        _ => string.IsNullOrEmpty(ext) ? "Textdatei" : $"Text-/Codedatei ({ext})",
    };

    public static async Task<LoadedDocument> LoadPreviewAsync(string path, int maxBytes = DefaultPreviewMaxBytes, CancellationToken ct = default)
    {
        if (Directory.Exists(path))
        {
            var dirOverview = await Task.Run(() => LoadDirectoryOverview(path), ct);
            var summaryText = FormatDirectoryOverviewText(dirOverview);
            return new LoadedDocument(
                FullPath: path,
                Content: summaryText,
                Encoding: Encoding.UTF8,
                NewLine: Environment.NewLine,
                Mode: PreviewMode.Directory,
                FileSize: dirOverview.TotalSize,
                IsTruncated: false,
                FileTypeDescription: "Verzeichnis / Ordner",
                DirectoryInfo: dirOverview);
        }

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        long size = fs.Length;
        int toRead = (int)Math.Min(size, maxBytes);
        var buffer = new byte[toRead];
        int read = await fs.ReadAtLeastAsync(buffer, toRead, throwOnEndOfStream: false, ct);

        string sha256 = "";
        if (size <= 64 * 1024 * 1024) // Fast SHA-256 computation for files up to 64 MB
        {
            try
            {
                fs.Position = 0;
                var hashBytes = await SHA256.HashDataAsync(fs, ct);
                sha256 = Convert.ToHexString(hashBytes);
            }
            catch { }
        }

        var mode = DetectMode(path, buffer.AsSpan(0, Math.Min(read, 4096)));
        var typeDesc = DescribeFileSignature(path, buffer.AsSpan(0, Math.Min(read, 64)), mode);
        var hexPreview = FormatHexDump(buffer.AsSpan(0, Math.Min(read, 16 * 1024)));

        if (mode == PreviewMode.Archive)
        {
            var entries = await Task.Run(() => ReadZipArchiveEntries(path), ct);
            var archiveText = FormatArchiveEntriesText(entries);
            return new LoadedDocument(
                FullPath: path,
                Content: archiveText,
                Encoding: Encoding.UTF8,
                NewLine: Environment.NewLine,
                Mode: mode,
                FileSize: size,
                IsTruncated: entries.Count >= 1000,
                FileTypeDescription: typeDesc,
                HexPreview: hexPreview,
                Sha256Hex: sha256,
                ArchiveEntries: entries);
        }

        if (mode is PreviewMode.Image or PreviewMode.Pdf or PreviewMode.Media)
        {
            // Also provide XML source text for SVG files
            string rawText = hexPreview;
            var slice = buffer.AsSpan(0, read);
            if (IsSvgFile(path) && !LooksBinary(slice))
            {
                var (svgEnc, svgBom) = DetectEncoding(slice);
                rawText = svgEnc.GetString(slice[svgBom..]);
            }

            return new LoadedDocument(
                FullPath: path,
                Content: rawText,
                Encoding: Encoding.UTF8,
                NewLine: Environment.NewLine,
                Mode: mode,
                FileSize: size,
                IsTruncated: size > 16 * 1024,
                FileTypeDescription: typeDesc,
                HexPreview: hexPreview,
                Sha256Hex: sha256);
        }

        if (mode == PreviewMode.BinaryHex)
        {
            return new LoadedDocument(
                FullPath: path,
                Content: hexPreview,
                Encoding: Encoding.UTF8,
                NewLine: Environment.NewLine,
                Mode: mode,
                FileSize: size,
                IsTruncated: size > 16 * 1024,
                FileTypeDescription: typeDesc,
                HexPreview: hexPreview,
                Sha256Hex: sha256);
        }

        var textSlice = buffer.AsSpan(0, read);
        var (encoding, bomLen) = DetectEncoding(textSlice);
        var text = encoding.GetString(textSlice[bomLen..]);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        CsvTablePreview? csvTable = null;
        if (mode == PreviewMode.Csv)
        {
            var ext = Path.GetExtension(path);
            char? forcedSep = string.Equals(ext, ".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : null;
            csvTable = ParseCsvPreview(text, forcedSep);
        }

        return new LoadedDocument(
            FullPath: path,
            Content: text,
            Encoding: encoding,
            NewLine: newLine,
            Mode: mode,
            FileSize: size,
            IsTruncated: size > toRead,
            FileTypeDescription: typeDesc,
            HexPreview: hexPreview,
            Sha256Hex: sha256,
            CsvTable: csvTable);
    }

    public static string FormatStructuredText(string content, PreviewMode mode)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;

        if (mode == PreviewMode.Json)
        {
            try
            {
                using var jdoc = JsonDocument.Parse(content);
                return JsonSerializer.Serialize(jdoc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return content;
            }
        }

        if (mode == PreviewMode.Xml)
        {
            try
            {
                var xdoc = XDocument.Parse(content);
                return xdoc.Declaration is not null
                    ? $"{xdoc.Declaration}{Environment.NewLine}{xdoc}"
                    : xdoc.ToString();
            }
            catch
            {
                return content;
            }
        }

        return content;
    }

    public static CsvTablePreview ParseCsvPreview(string text, char? forcedDelimiter = null, int maxRows = 300)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CsvTablePreview([], [], ';', 0);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return new CsvTablePreview([], [], ';', 0);

        char delimiter = forcedDelimiter ?? DetectCsvDelimiter(lines.Take(10));
        var headers = ParseCsvLine(lines[0], delimiter);
        int colCount = Math.Max(1, headers.Count);

        var rows = new List<string[]>(Math.Min(lines.Length - 1, maxRows));
        for (int i = 1; i < lines.Length && rows.Count < maxRows; i++)
        {
            var fields = ParseCsvLine(lines[i], delimiter);
            if (fields.Count > colCount)
            {
                for (int c = colCount; c < fields.Count; c++)
                    headers.Add($"Spalte {c + 1}");
                colCount = headers.Count;
            }
            var row = new string[colCount];
            for (int c = 0; c < colCount; c++)
                row[c] = c < fields.Count ? fields[c] : "";
            rows.Add(row);
        }

        return new CsvTablePreview(headers, rows, delimiter, lines.Length);
    }

    private static char DetectCsvDelimiter(IEnumerable<string> sampleLines)
    {
        char[] candidates = [';', ',', '\t', '|'];
        char best = ';';
        int bestScore = -1;

        foreach (var cand in candidates)
        {
            int count = sampleLines.Sum(l => l.Count(ch => ch == cand));
            if (count > bestScore)
            {
                bestScore = count;
                best = cand;
            }
        }
        return bestScore > 0 ? best : ',';
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim());
        return result;
    }

    public static IReadOnlyList<ArchiveEntryPreview> ReadZipArchiveEntries(string path, int maxEntries = 1000)
    {
        var list = new List<ArchiveEntryPreview>();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in zip.Entries.Take(maxEntries))
            {
                bool isDir = string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/');
                list.Add(new ArchiveEntryPreview(
                    FullName: entry.FullName,
                    CompressedSize: entry.CompressedLength,
                    UncompressedSize: entry.Length,
                    LastModified: entry.LastWriteTime.LocalDateTime,
                    IsDirectory: isDir));
            }
        }
        catch { }
        return list;
    }

    public static DirectoryOverview LoadDirectoryOverview(string path, int maxEntries = 300)
    {
        var di = new DirectoryInfo(path);
        var entries = new List<FileEntry>();
        var extStats = new Dictionary<string, (int Count, long Size)>(StringComparer.OrdinalIgnoreCase);
        int fileCount = 0;
        int folderCount = 0;
        long totalSize = 0;

        try
        {
            foreach (var fsi in di.EnumerateFileSystemInfos())
            {
                if (fsi is DirectoryInfo subDir)
                {
                    folderCount++;
                    if (entries.Count < maxEntries)
                        entries.Add(new FileEntry(subDir.Name, subDir.FullName, true, 0, subDir.LastWriteTime, subDir.Attributes));
                }
                else if (fsi is FileInfo fi)
                {
                    fileCount++;
                    long len = 0;
                    try { len = fi.Length; } catch { }
                    totalSize += len;

                    var ext = string.IsNullOrEmpty(fi.Extension) ? "(ohne Endung)" : fi.Extension.ToLowerInvariant();
                    if (extStats.TryGetValue(ext, out var prev))
                        extStats[ext] = (prev.Count + 1, prev.Size + len);
                    else
                        extStats[ext] = (1, len);

                    if (entries.Count < maxEntries)
                        entries.Add(new FileEntry(fi.Name, fi.FullName, false, len, fi.LastWriteTime, fi.Attributes));
                }
            }
        }
        catch { }

        var topExts = extStats
            .OrderByDescending(kv => kv.Value.Size)
            .ThenByDescending(kv => kv.Value.Count)
            .Take(8)
            .Select(kv => (kv.Key, kv.Value.Count, kv.Value.Size))
            .ToList();

        return new DirectoryOverview(
            FullPath: di.FullName,
            FileCount: fileCount,
            FolderCount: folderCount,
            TotalSize: totalSize,
            CreatedTime: di.Exists ? di.CreationTime : default,
            LastWriteTime: di.Exists ? di.LastWriteTime : default,
            Attributes: di.Exists ? di.Attributes : FileAttributes.Directory,
            TopExtensions: topExts,
            Entries: entries);
    }

    private static string FormatArchiveEntriesText(IReadOnlyList<ArchiveEntryPreview> entries)
    {
        if (entries.Count == 0) return "(Keine Einträge im Archiv gefunden oder Archiv ist verschlüsselt/ungültig)";
        var sb = new StringBuilder();
        sb.AppendLine($"{"Größe",12}  {"Gepackt",12}  {"Rate",7}  {"Geändert",16}  Pfad");
        sb.AppendLine(new string('─', 90));
        foreach (var e in entries)
        {
            sb.AppendLine($"{e.SizeText,12}  {e.CompressedText,12}  {e.RatioText,7}  {e.ModifiedText,16}  {e.FullName}");
        }
        return sb.ToString();
    }

    private static string FormatDirectoryOverviewText(DirectoryOverview info)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ordner:      {info.FullPath}");
        sb.AppendLine($"Inhalt:      {info.FolderCount} Unterordner, {info.FileCount} Dateien");
        sb.AppendLine($"Größe (Top): {Format.Size(info.TotalSize)} ({info.TotalSize:N0} Bytes)");
        sb.AppendLine($"Erstellt:    {info.CreatedTime:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"Geändert:    {info.LastWriteTime:dd.MM.yyyy HH:mm:ss}");
        if (info.TopExtensions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Häufigste Dateitypen:");
            foreach (var (ext, count, size) in info.TopExtensions)
                sb.AppendLine($"  {ext,-14} {count,6} Datei(en)   {Format.Size(size),10}");
        }
        return sb.ToString();
    }

    private static bool HasImageSignature(ReadOnlySpan<byte> s)
    {
        if (s.Length >= 8 && s[0] == 0x89 && s[1] == 0x50 && s[2] == 0x4E && s[3] == 0x47) return true; // PNG
        if (s.Length >= 3 && s[0] == 0xFF && s[1] == 0xD8 && s[2] == 0xFF) return true; // JPEG
        if (s.Length >= 6 && s[0] == (byte)'G' && s[1] == (byte)'I' && s[2] == (byte)'F' && s[3] == (byte)'8') return true; // GIF
        if (s.Length >= 12 && s[0] == (byte)'R' && s[1] == (byte)'I' && s[2] == (byte)'F' && s[3] == (byte)'F' &&
            s[8] == (byte)'W' && s[9] == (byte)'E' && s[10] == (byte)'B' && s[11] == (byte)'P') return true; // WEBP
        return false;
    }

    private static bool HasPdfSignature(ReadOnlySpan<byte> s) =>
        s.Length >= 5 && s[0] == (byte)'%' && s[1] == (byte)'P' && s[2] == (byte)'D' && s[3] == (byte)'F' && s[4] == (byte)'-';

    /// <summary>Reads the tail of a (potentially very large) log file for follow mode (<c>tail -f</c>).</summary>
    public static async Task<string> ReadTailAsync(string path, int tailBytes = 64 * 1024, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        long len = fs.Length;
        long start = Math.Max(0, len - tailBytes);
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[len - start];
        int read = await fs.ReadAtLeastAsync(buf, buf.Length, throwOnEndOfStream: false, ct);
        var text = Encoding.UTF8.GetString(buf, 0, read);
        if (start > 0)
        {
            int firstNl = text.IndexOf('\n');
            if (firstNl >= 0 && firstNl + 1 < text.Length)
                text = text[(firstNl + 1)..];
        }
        return text;
    }

    /// <summary>Filters log lines by level (ERROR, WARN, INFO) or search term.</summary>
    public static string FilterLogLines(string content, string? levelFilter, string? searchText)
    {
        if (string.IsNullOrEmpty(levelFilter) && string.IsNullOrWhiteSpace(searchText))
            return content;

        var sb = new StringBuilder();
        foreach (var line in content.Split('\n'))
        {
            if (!string.IsNullOrEmpty(levelFilter) && !string.Equals(levelFilter, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (!line.Contains(levelFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            if (!string.IsNullOrWhiteSpace(searchText) && !line.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                continue;
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Saves a file atomically (temp file + replace) while preserving encoding &amp; line endings.</summary>
    public static async Task SaveAtomicAsync(string path, string content, Encoding encoding, string newLine, CancellationToken ct = default)
    {
        var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
        if (newLine == "\r\n") normalized = normalized.Replace("\n", "\r\n");

        var dir = Path.GetDirectoryName(path)!;
        var tmp = Path.Combine(dir, $".~save-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                var preamble = encoding.GetPreamble();
                if (preamble.Length > 0) await fs.WriteAsync(preamble, ct);
                var bytes = encoding.GetBytes(normalized);
                await fs.WriteAsync(bytes, ct);
                await fs.FlushAsync(ct);
            }

            if (File.Exists(path))
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            else
                File.Move(tmp, path);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    public static (Encoding Encoding, int BomLength) DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(true), 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (new UnicodeEncoding(false, true), 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (new UnicodeEncoding(true, true), 2);

        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            _ = utf8Strict.GetCharCount(bytes);
            return (new UTF8Encoding(false), 0);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(1252), 0);
        }
    }

    public static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        if (sample.Length >= 2 && ((sample[0] == 0xFF && sample[1] == 0xFE) || (sample[0] == 0xFE && sample[1] == 0xFF)))
            return false; // UTF-16 BOM
        int zeroBytes = 0;
        for (int i = 0; i < sample.Length; i++)
        {
            if (sample[i] == 0)
            {
                zeroBytes++;
                if (zeroBytes >= 2) return true;
            }
        }
        return zeroBytes > 0 && sample.Length < 512;
    }

    public static string FormatHexDump(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        for (int offset = 0; offset < data.Length; offset += 16)
        {
            sb.Append(offset.ToString("X8")).Append("  ");
            int lineLen = Math.Min(16, data.Length - offset);
            for (int i = 0; i < 16; i++)
            {
                if (i < lineLen) sb.Append(data[offset + i].ToString("X2")).Append(' ');
                else sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append(" |");
            for (int i = 0; i < lineLen; i++)
            {
                byte b = data[offset + i];
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            }
            sb.AppendLine("|");
        }
        return sb.ToString();
    }
}
