using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ModernExplorer.Core.FileSystem;

/// <summary>
/// Search parameters for recursive deep search (Alt+F7 / Ctrl+Shift+F).
/// </summary>
public sealed record DeepSearchQuery
{
    public required string RootPath { get; init; }
    public string NamePattern { get; init; } = "*";
    public bool UseRegex { get; init; }
    public bool IncludeSubdirectories { get; init; } = true;
    public bool IncludeHidden { get; init; } = true;
    public long? MinSizeBytes { get; init; }
    public long? MaxSizeBytes { get; init; }
    public DateTime? ModifiedAfter { get; init; }
    public DateTime? ModifiedBefore { get; init; }
    public string ContentText { get; init; } = "";
    public bool ContentCaseSensitive { get; init; }
    public int MaxResults { get; init; } = 5000;
}

/// <summary>
/// Performs fast recursive file and full-text content searches across subdirectories.
/// </summary>
public static class DeepSearchService
{
    public static async Task<IReadOnlyList<FileEntry>> SearchAsync(
        DeepSearchQuery query,
        IProgress<FileEntry>? onResultFound = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<FileEntry>();
            if (string.IsNullOrWhiteSpace(query.RootPath) || !Directory.Exists(query.RootPath))
                return (IReadOnlyList<FileEntry>)results;

            var nameMatcher = BuildNameMatcher(query.NamePattern, query.UseRegex);
            bool searchContent = !string.IsNullOrEmpty(query.ContentText);
            var stack = new Stack<string>();
            stack.Push(query.RootPath);

            while (stack.Count > 0 && results.Count < query.MaxResults)
            {
                ct.ThrowIfCancellationRequested();
                var dirPath = stack.Pop();

                try
                {
                    var dirInfo = new DirectoryInfo(dirPath);

                    // Match subdirectories if no content filter or size filter is active
                    foreach (var subDir in dirInfo.EnumerateDirectories())
                    {
                        ct.ThrowIfCancellationRequested();
                        bool isHidden = (subDir.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                        if (!query.IncludeHidden && isHidden)
                            continue;

                        if (!searchContent && query.MinSizeBytes is null && query.MaxSizeBytes is null &&
                            MatchesDateFilter(subDir.LastWriteTime, query) &&
                            nameMatcher(subDir.Name))
                        {
                            string relSub = Path.GetRelativePath(query.RootPath, subDir.FullName);
                            var entry = new FileEntry(
                                subDir.Name,
                                subDir.FullName,
                                true,
                                0,
                                subDir.LastWriteTime,
                                subDir.Attributes)
                            {
                                RelativeSubPath = relSub
                            };
                            results.Add(entry);
                            onResultFound?.Report(entry);
                            if (results.Count >= query.MaxResults)
                                break;
                        }

                        if (query.IncludeSubdirectories && (subDir.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            stack.Push(subDir.FullName);
                        }
                    }

                    // Match files
                    foreach (var file in dirInfo.EnumerateFiles())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (results.Count >= query.MaxResults)
                            break;

                        bool isHidden = (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                        if (!query.IncludeHidden && isHidden)
                            continue;

                        if (!nameMatcher(file.Name))
                            continue;

                        if (query.MinSizeBytes.HasValue && file.Length < query.MinSizeBytes.Value)
                            continue;

                        if (query.MaxSizeBytes.HasValue && file.Length > query.MaxSizeBytes.Value)
                            continue;

                        if (!MatchesDateFilter(file.LastWriteTime, query))
                            continue;

                        if (searchContent && !FileContainsText(file.FullName, query.ContentText, query.ContentCaseSensitive, ct))
                            continue;

                        string relSub = Path.GetRelativePath(query.RootPath, file.FullName);
                        var entry = new FileEntry(
                            file.Name,
                            file.FullName,
                            false,
                            file.Length,
                            file.LastWriteTime,
                            file.Attributes)
                        {
                            RelativeSubPath = relSub
                        };
                        results.Add(entry);
                        onResultFound?.Report(entry);
                    }
                }
                catch { }
            }

            return (IReadOnlyList<FileEntry>)results;
        }, ct);
    }

    public static Func<string, bool> BuildNameMatcher(string? pattern, bool useRegex)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Trim() is "*" or "*.*")
            return _ => true;

        var trimmed = pattern.Trim();
        if (useRegex)
        {
            try
            {
                var rx = new Regex(trimmed, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
                return name => rx.IsMatch(name);
            }
            catch
            {
                return name => name.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Support semicolon/comma separated wildcards (e.g. "*.cs; *.xaml") or plain substring
        var parts = trimmed.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var regexList = new List<Regex>();
        foreach (var part in parts)
        {
            if (part.Contains('*') || part.Contains('?'))
            {
                string rxPattern = "^" + Regex.Escape(part).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                regexList.Add(new Regex(rxPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
            else
            {
                string rxPattern = Regex.Escape(part);
                regexList.Add(new Regex(rxPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
        }

        return name => regexList.Any(r => r.IsMatch(name));
    }

    private static bool MatchesDateFilter(DateTime lastWrite, DeepSearchQuery query)
    {
        if (query.ModifiedAfter.HasValue && lastWrite < query.ModifiedAfter.Value)
            return false;
        if (query.ModifiedBefore.HasValue && lastWrite > query.ModifiedBefore.Value)
            return false;
        return true;
    }

    private static bool FileContainsText(string path, string needle, bool caseSensitive, CancellationToken ct)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length == 0 || fi.Length > 64L * 1024 * 1024) // Skip files > 64 MB for text search
                return false;

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            while (reader.ReadLine() is { } line)
            {
                ct.ThrowIfCancellationRequested();
                if (line.Contains(needle, comparison))
                    return true;
            }
        }
        catch { }
        return false;
    }
}

