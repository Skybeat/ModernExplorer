using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.ViewModels;

namespace ModernExplorer.Core.Features;

public enum SmartExportFormat
{
    FullPaths = 0,
    FileNamesOnly = 1,
    RelativePaths = 2,
    ForwardSlashPaths = 3,
    MarkdownFileLinks = 4,
    CsvTable = 5,
    MarkdownTable = 6,
    JsonArray = 7,
    HtmlTable = 8,
}

/// <summary>
/// Formats file and folder collections into paths, Markdown file:// links, CSV reports,
/// Markdown tables, JSON arrays, or HTML tables for clipboard or file export.
/// </summary>
public static class SmartExportService
{
    public static string FormatEntries(
        IReadOnlyList<FileEntry> entries,
        SmartExportFormat format,
        string? baseDirectory = null)
    {
        var items = entries.Where(e => !e.IsParentLink).ToList();
        if (items.Count == 0)
            return "";

        return format switch
        {
            SmartExportFormat.FullPaths => string.Join(Environment.NewLine, items.Select(i => i.FullPath)),
            SmartExportFormat.FileNamesOnly => string.Join(Environment.NewLine, items.Select(i => i.Name)),
            SmartExportFormat.RelativePaths => string.Join(Environment.NewLine, items.Select(i => GetRelativeSafe(i.FullPath, baseDirectory, i.Name))),
            SmartExportFormat.ForwardSlashPaths => string.Join(Environment.NewLine, items.Select(i => i.FullPath.Replace('\\', '/'))),
            SmartExportFormat.MarkdownFileLinks => string.Join(Environment.NewLine, items.Select(i =>
            {
                string uriPath = i.FullPath.Replace('\\', '/');
                if (!uriPath.StartsWith('/'))
                    uriPath = "/" + uriPath;
                return $"- [{i.Name}](file://{uriPath})";
            })),
            SmartExportFormat.CsvTable => BuildCsvTable(items),
            SmartExportFormat.MarkdownTable => BuildMarkdownTable(items),
            SmartExportFormat.JsonArray => BuildJsonArray(items),
            SmartExportFormat.HtmlTable => BuildHtmlTable(items),
            _ => string.Join(Environment.NewLine, items.Select(i => i.FullPath)),
        };
    }

    public static string GetRecommendedFileExtension(SmartExportFormat format) => format switch
    {
        SmartExportFormat.CsvTable => ".csv",
        SmartExportFormat.MarkdownTable or SmartExportFormat.MarkdownFileLinks => ".md",
        SmartExportFormat.JsonArray => ".json",
        SmartExportFormat.HtmlTable => ".html",
        _ => ".txt",
    };

    private static string GetRelativeSafe(string fullPath, string? baseDir, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return fallbackName;
        try
        {
            return Path.GetRelativePath(baseDir, fullPath);
        }
        catch
        {
            return fallbackName;
        }
    }

    private static string BuildCsvTable(IReadOnlyList<FileEntry> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Type,SizeBytes,SizeFormatted,Modified,FullPath");
        foreach (var i in items)
        {
            string type = i.IsDirectory ? "Folder" : (string.IsNullOrEmpty(i.Extension) ? "File" : i.Extension.TrimStart('.').ToUpperInvariant());
            string sizeFmt = i.IsDirectory ? "<DIR>" : Format.Size(i.Size);
            string mod = i.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            sb.AppendLine($"{EscapeCsv(i.Name)},{EscapeCsv(type)},{(i.IsDirectory ? 0 : i.Size)},{EscapeCsv(sizeFmt)},{EscapeCsv(mod)},{EscapeCsv(i.FullPath)}");
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string val)
    {
        if (string.IsNullOrEmpty(val)) return "\"\"";
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
            return "\"" + val.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return val;
    }

    private static string BuildMarkdownTable(IReadOnlyList<FileEntry> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Name | Typ | Größe | Geändert am | Pfad |");
        sb.AppendLine("| :--- | :--- | ---: | :--- | :--- |");
        foreach (var i in items)
        {
            string type = i.IsDirectory ? "📁 Ordner" : (string.IsNullOrEmpty(i.Extension) ? "📄 Datei" : i.Extension.TrimStart('.').ToUpperInvariant());
            string size = i.IsDirectory ? "—" : Format.Size(i.Size);
            string mod = i.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            string safeName = i.Name.Replace("|", "\\|", StringComparison.Ordinal);
            string safePath = i.FullPath.Replace("|", "\\|", StringComparison.Ordinal);
            sb.AppendLine($"| `{safeName}` | {type} | {size} | {mod} | `{safePath}` |");
        }
        return sb.ToString();
    }

    private static string BuildJsonArray(IReadOnlyList<FileEntry> items)
    {
        var dto = items.Select(i => new
        {
            name = i.Name,
            isDirectory = i.IsDirectory,
            extension = i.Extension,
            sizeBytes = i.IsDirectory ? 0 : i.Size,
            sizeFormatted = i.IsDirectory ? "<DIR>" : Format.Size(i.Size),
            modified = i.LastWriteTime.ToString("O", CultureInfo.InvariantCulture),
            fullPath = i.FullPath,
        }).ToList();

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildHtmlTable(IReadOnlyList<FileEntry> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>ModernExplorer Export</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#0f172a;color:#f8fafc;padding:20px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #334155;padding:8px 12px;text-align:left}th{background:#1e293b}</style></head><body>");
        sb.AppendLine("<table><thead><tr><th>Name</th><th>Typ</th><th>Größe</th><th>Geändert</th><th>Pfad</th></tr></thead><tbody>");
        foreach (var i in items)
        {
            string type = i.IsDirectory ? "Ordner" : i.Extension;
            string size = i.IsDirectory ? "&lt;DIR&gt;" : Format.Size(i.Size);
            string mod = i.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(i.Name)}</td><td>{System.Net.WebUtility.HtmlEncode(type)}</td><td>{size}</td><td>{mod}</td><td>{System.Net.WebUtility.HtmlEncode(i.FullPath)}</td></tr>");
        }
        sb.AppendLine("</tbody></table></body></html>");
        return sb.ToString();
    }
}
