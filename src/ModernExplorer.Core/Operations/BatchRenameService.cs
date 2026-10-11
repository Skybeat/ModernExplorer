using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ModernExplorer.Core.FileSystem;

namespace ModernExplorer.Core.Operations;

/// <summary>
/// Case transformation mode for batch renaming.
/// </summary>
public enum BatchCaseTransform
{
    None = 0,
    Lowercase = 1,
    Uppercase = 2,
    TitleCase = 3,
    SentenceCase = 4,
}

/// <summary>
/// Configuration parameters for the multi-file Batch Rename tool (Ctrl+M).
/// Supports placeholders: [N] (original name), [N1-5] (substring 1..5), [E] (extension),
/// [C] (counter), [Y]/[M]/[D]/[h]/[m]/[s] (file modification date/time), [P] (parent folder name).
/// </summary>
public sealed record BatchRenameOptions
{
    public string NamePattern { get; init; } = "[N]";
    public string ExtensionPattern { get; init; } = "[E]";
    public string SearchText { get; init; } = "";
    public string ReplaceText { get; init; } = "";
    public bool UseRegex { get; init; }
    public bool CaseSensitive { get; init; }
    public BatchCaseTransform CaseTransform { get; init; } = BatchCaseTransform.None;
    public int CounterStart { get; init; } = 1;
    public int CounterStep { get; init; } = 1;
    public int CounterDigits { get; init; } = 3;
}

/// <summary>
/// Represents a single file/folder preview row in the Batch Rename tool.
/// </summary>
public sealed record BatchRenamePreviewItem(
    string OriginalPath,
    string OriginalName,
    string NewName,
    string NewFullPath,
    bool IsDirectory,
    bool WillChange,
    bool HasConflict,
    string? ConflictReason = null);

/// <summary>
/// Result of executing a batch rename operation.
/// </summary>
public sealed record BatchRenameResult(
    int RenamedCount,
    IReadOnlyList<(string OldPath, string NewPath)> RenamedPairs,
    IReadOnlyList<string> Errors);

/// <summary>
/// Provides Total Commander-style pattern-based multi-rename preview and safe two-phase execution.
/// </summary>
public static class BatchRenameService
{
    private static readonly Regex SubstringTokenRegex = new(@"\[N(\d+)(?:-(\d+))?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CounterWidthTokenRegex = new(@"\[C(?::(\d+))?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<BatchRenamePreviewItem> GeneratePreview(
        IReadOnlyList<FileEntry> entries,
        BatchRenameOptions options)
    {
        if (entries.Count == 0)
            return [];

        var rawItems = new List<(FileEntry Entry, string ProposedName, string ProposedFullPath)>(entries.Count);
        int counter = options.CounterStart;
        int step = options.CounterStep == 0 ? 1 : options.CounterStep;
        int digits = Math.Clamp(options.CounterDigits, 1, 10);

        foreach (var entry in entries)
        {
            if (entry.IsParentLink)
                continue;

            string baseName = entry.IsDirectory
                ? entry.Name
                : Path.GetFileNameWithoutExtension(entry.Name);

            string ext = entry.IsDirectory
                ? ""
                : Path.GetExtension(entry.Name).TrimStart('.');

            string parentName = "";
            try
            {
                var dir = Path.GetDirectoryName(entry.FullPath);
                if (!string.IsNullOrEmpty(dir))
                    parentName = new DirectoryInfo(dir).Name;
            }
            catch { }

            var dt = entry.LastWriteTime == default ? DateTime.Now : entry.LastWriteTime;

            string expandedBase = ExpandPattern(
                string.IsNullOrWhiteSpace(options.NamePattern) ? "[N]" : options.NamePattern,
                baseName,
                ext,
                parentName,
                dt,
                counter,
                digits);

            string expandedExt = entry.IsDirectory
                ? ""
                : ExpandPattern(
                    options.ExtensionPattern ?? "[E]",
                    baseName,
                    ext,
                    parentName,
                    dt,
                    counter,
                    digits);

            string combined = entry.IsDirectory || string.IsNullOrEmpty(expandedExt)
                ? expandedBase
                : $"{expandedBase}.{expandedExt.TrimStart('.')}";

            // Apply Search & Replace
            if (!string.IsNullOrEmpty(options.SearchText))
            {
                combined = ApplySearchReplace(combined, options.SearchText, options.ReplaceText ?? "", options.UseRegex, options.CaseSensitive);
            }

            // Apply Case Transform (preserving extension casing unless transforming whole name)
            combined = ApplyCaseTransform(combined, entry.IsDirectory, options.CaseTransform);

            string parentDir = Path.GetDirectoryName(entry.FullPath) ?? "";
            string proposedFullPath = string.IsNullOrEmpty(parentDir)
                ? combined
                : Path.Combine(parentDir, combined);

            rawItems.Add((entry, combined, proposedFullPath));
            counter += step;
        }

        // Detect conflicts (invalid filename chars, duplicate target names, or existing unrelated files on disk)
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var targetCounts = rawItems
            .GroupBy(x => x.ProposedFullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var sourceSet = rawItems
            .Select(x => x.Entry.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<BatchRenamePreviewItem>(rawItems.Count);
        foreach (var (entry, proposedName, proposedFullPath) in rawItems)
        {
            bool willChange = !string.Equals(entry.Name, proposedName, StringComparison.Ordinal);
            bool hasConflict = false;
            string? reason = null;

            if (string.IsNullOrWhiteSpace(proposedName) || proposedName is "." or "..")
            {
                hasConflict = true;
                reason = "Ungültiger leerer Dateiname";
            }
            else if (proposedName.Any(c => invalidChars.Contains(c)))
            {
                hasConflict = true;
                reason = "Enthält ungültige Zeichen";
            }
            else if (targetCounts.TryGetValue(proposedFullPath, out int count) && count > 1)
            {
                hasConflict = true;
                reason = "Doppelter Zielname in der Auswahl";
            }
            else if (willChange &&
                     !string.Equals(entry.FullPath, proposedFullPath, StringComparison.OrdinalIgnoreCase) &&
                     !sourceSet.Contains(proposedFullPath) &&
                     (File.Exists(proposedFullPath) || Directory.Exists(proposedFullPath)))
            {
                hasConflict = true;
                reason = "Zieldatei existiert bereits";
            }

            result.Add(new BatchRenamePreviewItem(
                entry.FullPath,
                entry.Name,
                proposedName,
                proposedFullPath,
                entry.IsDirectory,
                willChange,
                hasConflict,
                reason));
        }

        return result;
    }

    public static BatchRenameResult ExecuteBatchRename(IReadOnlyList<BatchRenamePreviewItem> previewItems)
    {
        var toRename = previewItems.Where(i => i.WillChange && !i.HasConflict).ToList();
        if (toRename.Count == 0)
            return new BatchRenameResult(0, [], []);

        var errors = new List<string>();
        var stage1 = new List<(BatchRenamePreviewItem Item, string TempPath)>();
        var renamedPairs = new List<(string OldPath, string NewPath)>();

        // Phase 1: Move to temporary names in the same folder to prevent collisions when swapping names
        foreach (var item in toRename)
        {
            try
            {
                var dir = Path.GetDirectoryName(item.OriginalPath) ?? "";
                var tempPath = Path.Combine(dir, $".__me_ren_{Guid.NewGuid():N}_{item.OriginalName}");
                if (item.IsDirectory)
                    Directory.Move(item.OriginalPath, tempPath);
                else
                    File.Move(item.OriginalPath, tempPath);

                stage1.Add((item, tempPath));
            }
            catch (Exception ex)
            {
                errors.Add($"{item.OriginalName}: {ex.Message}");
            }
        }

        // Phase 2: Move from temporary names to final target names
        foreach (var (item, tempPath) in stage1)
        {
            try
            {
                if (item.IsDirectory)
                    Directory.Move(tempPath, item.NewFullPath);
                else
                    File.Move(tempPath, item.NewFullPath);

                renamedPairs.Add((item.OriginalPath, item.NewFullPath));
            }
            catch (Exception ex)
            {
                errors.Add($"{item.OriginalName} -> {item.NewName}: {ex.Message}");
                // Roll back to original path if phase 2 fails
                try
                {
                    if (item.IsDirectory)
                        Directory.Move(tempPath, item.OriginalPath);
                    else
                        File.Move(tempPath, item.OriginalPath);
                }
                catch { }
            }
        }

        return new BatchRenameResult(renamedPairs.Count, renamedPairs, errors);
    }

    private static string ExpandPattern(
        string pattern,
        string baseName,
        string extension,
        string parentName,
        DateTime dt,
        int counter,
        int defaultDigits)
    {
        string output = SubstringTokenRegex.Replace(pattern, m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out int start1Based) || start1Based < 1)
                return baseName;

            int startIdx = start1Based - 1;
            if (startIdx >= baseName.Length)
                return "";

            if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out int end1Based) && end1Based >= start1Based)
            {
                int len = Math.Min(baseName.Length - startIdx, end1Based - start1Based + 1);
                return baseName.Substring(startIdx, len);
            }

            return baseName[startIdx..];
        });

        output = CounterWidthTokenRegex.Replace(output, m =>
        {
            int width = m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out int w)
                ? Math.Clamp(w, 1, 10)
                : defaultDigits;
            return counter.ToString($"D{width}", CultureInfo.InvariantCulture);
        });

        output = output
            .Replace("[N]", baseName, StringComparison.OrdinalIgnoreCase)
            .Replace("[E]", extension, StringComparison.OrdinalIgnoreCase)
            .Replace("[P]", parentName, StringComparison.OrdinalIgnoreCase)
            .Replace("[Y]", dt.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("[y]", dt.ToString("yy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("[M]", dt.ToString("MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("[D]", dt.ToString("dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("[h]", dt.ToString("HH", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("[m]", dt.ToString("mm", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("[s]", dt.ToString("ss", CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return output;
    }

    private static string ApplySearchReplace(
        string input,
        string search,
        string replace,
        bool useRegex,
        bool caseSensitive)
    {
        if (string.IsNullOrEmpty(search))
            return input;

        if (useRegex)
        {
            try
            {
                var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.Replace(input, search, replace, opts, TimeSpan.FromMilliseconds(250));
            }
            catch
            {
                return input;
            }
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return input.Replace(search, replace, comparison);
    }

    private static string ApplyCaseTransform(string fileName, bool isDirectory, BatchCaseTransform transform)
    {
        if (transform == BatchCaseTransform.None || string.IsNullOrEmpty(fileName))
            return fileName;

        string ext = isDirectory ? "" : Path.GetExtension(fileName);
        string stem = isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName);

        string transformedStem = transform switch
        {
            BatchCaseTransform.Lowercase => stem.ToLowerInvariant(),
            BatchCaseTransform.Uppercase => stem.ToUpperInvariant(),
            BatchCaseTransform.TitleCase => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(stem.ToLowerInvariant()),
            BatchCaseTransform.SentenceCase => stem.Length > 0
                ? char.ToUpperInvariant(stem[0]) + stem[1..].ToLowerInvariant()
                : stem,
            _ => stem,
        };

        string transformedExt = transform switch
        {
            BatchCaseTransform.Uppercase => ext.ToUpperInvariant(),
            _ => ext.ToLowerInvariant(),
        };

        return isDirectory ? transformedStem : transformedStem + transformedExt;
    }
}

