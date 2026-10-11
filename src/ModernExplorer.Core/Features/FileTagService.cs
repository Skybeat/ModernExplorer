using System.IO;
using System.Text.Json;
using ModernExplorer.Core.Localization;

namespace ModernExplorer.Core.Features;

public enum FileTagColor
{
    None = 0,
    Red = 1,
    Orange = 2,
    Yellow = 3,
    Green = 4,
    Blue = 5,
    Purple = 6,
}

public sealed record FileTagEntry(
    string FullPath,
    FileTagColor Color,
    string Note,
    DateTime UpdatedAtUtc);

/// <summary>
/// Manages persistent color tags (labels) and notes attached to files and folders,
/// with support for instant lookup and live filter queries (tag:rot, tag:green, note:...).
/// </summary>
public static class FileTagService
{
    private static readonly object SyncLock = new();
    private static readonly Dictionary<string, FileTagEntry> TagsByPath = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;
    private static string? _customStorePath;

    public static event EventHandler? TagsChanged;

    public static void UseCustomStorePath(string? filePath)
    {
        lock (SyncLock)
        {
            _customStorePath = filePath;
            _loaded = false;
            TagsByPath.Clear();
        }
    }

    public static string GetColorHex(FileTagColor color) => color switch
    {
        FileTagColor.Red => "#EF4444",
        FileTagColor.Orange => "#F97316",
        FileTagColor.Yellow => "#EAB308",
        FileTagColor.Green => "#10B981",
        FileTagColor.Blue => "#3B82F6",
        FileTagColor.Purple => "#A855F7",
        _ => "",
    };

    public static string GetColorBadgeEmoji(FileTagColor color) => color switch
    {
        FileTagColor.Red => "🔴",
        FileTagColor.Orange => "🟠",
        FileTagColor.Yellow => "🟡",
        FileTagColor.Green => "🟢",
        FileTagColor.Blue => "🔵",
        FileTagColor.Purple => "🟣",
        _ => "",
    };

    public static string GetColorDisplayName(FileTagColor color) => color switch
    {
        FileTagColor.Red => LocalizationService.T("Tag.Color.Red"),
        FileTagColor.Orange => LocalizationService.T("Tag.Color.Orange"),
        FileTagColor.Yellow => LocalizationService.T("Tag.Color.Yellow"),
        FileTagColor.Green => LocalizationService.T("Tag.Color.Green"),
        FileTagColor.Blue => LocalizationService.T("Tag.Color.Blue"),
        FileTagColor.Purple => LocalizationService.T("Tag.Color.Purple"),
        _ => LocalizationService.T("Tag.Color.None"),
    };

    public static FileTagEntry? GetTag(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        EnsureLoaded();
        string norm = NormalizePath(fullPath);
        lock (SyncLock)
        {
            return TagsByPath.TryGetValue(norm, out var entry) ? entry : null;
        }
    }

    public static IReadOnlyList<FileTagEntry> GetAllTags()
    {
        EnsureLoaded();
        lock (SyncLock)
        {
            return TagsByPath.Values.OrderByDescending(t => t.UpdatedAtUtc).ToList();
        }
    }

    public static void SetTag(IEnumerable<string> paths, FileTagColor color, string? note)
    {
        EnsureLoaded();
        string trimmedNote = (note ?? "").Trim();

        lock (SyncLock)
        {
            foreach (var raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string norm = NormalizePath(raw);

                if (color == FileTagColor.None && string.IsNullOrEmpty(trimmedNote))
                {
                    TagsByPath.Remove(norm);
                }
                else
                {
                    TagsByPath[norm] = new FileTagEntry(norm, color, trimmedNote, DateTime.UtcNow);
                }
            }
            SaveInternal();
        }

        TagsChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ClearTag(IEnumerable<string> paths) => SetTag(paths, FileTagColor.None, null);

    public static bool MatchesTagFilter(string fullPath, string filterQuery)
    {
        if (string.IsNullOrWhiteSpace(filterQuery))
            return false;

        var entry = GetTag(fullPath);
        if (entry is null)
            return false;

        string q = filterQuery.Trim();
        if (q.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            string val = q["tag:".Length..].Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(val) || val == "*" || val == "any" || val == "alle")
                return entry.Color != FileTagColor.None || !string.IsNullOrEmpty(entry.Note);

            return val switch
            {
                "red" or "rot" or "important" or "wichtig" or "1" => entry.Color == FileTagColor.Red,
                "orange" or "urgent" or "dringend" or "2" => entry.Color == FileTagColor.Orange,
                "yellow" or "gelb" or "wip" or "arbeit" or "3" => entry.Color == FileTagColor.Yellow,
                "green" or "grün" or "gruen" or "done" or "fertig" or "4" => entry.Color == FileTagColor.Green,
                "blue" or "blau" or "project" or "projekt" or "5" => entry.Color == FileTagColor.Blue,
                "purple" or "violett" or "lila" or "review" or "archiv" or "6" => entry.Color == FileTagColor.Purple,
                _ => entry.Note.Contains(val, StringComparison.OrdinalIgnoreCase)
                     || GetColorDisplayName(entry.Color).Contains(val, StringComparison.OrdinalIgnoreCase),
            };
        }

        if (q.StartsWith("note:", StringComparison.OrdinalIgnoreCase))
        {
            string val = q["note:".Length..].Trim();
            if (string.IsNullOrEmpty(val) || val == "*")
                return !string.IsNullOrEmpty(entry.Note);
            return entry.Note.Contains(val, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string NormalizePath(string path) =>
        path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string GetStoreFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_customStorePath)) return _customStorePath;
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModernExplorer");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "file-tags.json");
    }

    private static void EnsureLoaded()
    {
        lock (SyncLock)
        {
            if (_loaded) return;
            _loaded = true;
            TagsByPath.Clear();

            try
            {
                string file = GetStoreFilePath();
                if (!File.Exists(file)) return;
                string json = File.ReadAllText(file);
                var list = JsonSerializer.Deserialize<List<FileTagEntry>>(json);
                if (list is not null)
                {
                    foreach (var item in list)
                    {
                        if (!string.IsNullOrWhiteSpace(item.FullPath))
                            TagsByPath[NormalizePath(item.FullPath)] = item;
                    }
                }
            }
            catch { }
        }
    }

    private static void SaveInternal()
    {
        try
        {
            string file = GetStoreFilePath();
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var list = TagsByPath.Values.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }
        catch { }
    }
}
