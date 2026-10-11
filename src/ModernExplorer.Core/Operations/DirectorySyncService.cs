using System.IO;
using System.Security.Cryptography;

namespace ModernExplorer.Core.Operations;

/// <summary>
/// Comparison strategy for comparing two directories.
/// </summary>
public enum SyncCompareMode
{
    SizeAndTimestamp = 0,
    SizeOnly = 1,
    Sha256Content = 2,
}

/// <summary>
/// Comparison status of a relative file path between Left and Right directories.
/// </summary>
public enum SyncItemStatus
{
    Equal = 0,
    LeftNewer = 1,
    RightNewer = 2,
    DifferentContent = 3,
    LeftOnly = 4,
    RightOnly = 5,
}

/// <summary>
/// Synchronization direction to apply during directory synchronization.
/// </summary>
public enum SyncDirectionMode
{
    BidirectionalMerge = 0,
    LeftToRightMirror = 1,
    RightToLeftMirror = 2,
}

/// <summary>
/// Represents a single compared file between the Left and Right panels.
/// </summary>
public sealed record SyncComparisonItem(
    string RelativePath,
    string? LeftFullPath,
    string? RightFullPath,
    long? LeftSize,
    long? RightSize,
    DateTime? LeftModified,
    DateTime? RightModified,
    SyncItemStatus Status)
{
    public bool IsDifferent => Status != SyncItemStatus.Equal;

    public string StatusSymbol => Status switch
    {
        SyncItemStatus.Equal => "=",
        SyncItemStatus.LeftNewer => "→ (Links neuer)",
        SyncItemStatus.RightNewer => "← (Rechts neuer)",
        SyncItemStatus.DifferentContent => "≠ (Ungleich)",
        SyncItemStatus.LeftOnly => "→ (Nur links)",
        SyncItemStatus.RightOnly => "← (Nur rechts)",
        _ => "?"
    };
}

/// <summary>
/// Result of executing a directory synchronization.
/// </summary>
public sealed record SyncExecutionResult(
    int CopiedLeftToRight,
    int CopiedRightToLeft,
    int DeletedOrphans,
    IReadOnlyList<string> Errors);

/// <summary>
/// Compares and synchronizes two directories (Left and Right panels) by size, timestamp, or SHA-256 hash.
/// </summary>
public static class DirectorySyncService
{
    public static async Task<IReadOnlyList<SyncComparisonItem>> CompareDirectoriesAsync(
        string leftRoot,
        string rightRoot,
        SyncCompareMode compareMode = SyncCompareMode.SizeAndTimestamp,
        bool includeSubdirectories = true,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var leftFiles = CollectFiles(leftRoot, includeSubdirectories, ct);
            var rightFiles = CollectFiles(rightRoot, includeSubdirectories, ct);

            var allKeys = leftFiles.Keys
                .Union(rightFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var results = new List<SyncComparisonItem>(allKeys.Count);
            foreach (var relPath in allKeys)
            {
                ct.ThrowIfCancellationRequested();

                bool hasLeft = leftFiles.TryGetValue(relPath, out var leftInfo);
                bool hasRight = rightFiles.TryGetValue(relPath, out var rightInfo);

                if (hasLeft && !hasRight)
                {
                    results.Add(new SyncComparisonItem(
                        relPath,
                        leftInfo!.FullName,
                        null,
                        leftInfo.Length,
                        null,
                        leftInfo.LastWriteTime,
                        null,
                        SyncItemStatus.LeftOnly));
                }
                else if (!hasLeft && hasRight)
                {
                    results.Add(new SyncComparisonItem(
                        relPath,
                        null,
                        rightInfo!.FullName,
                        null,
                        rightInfo.Length,
                        null,
                        rightInfo.LastWriteTime,
                        SyncItemStatus.RightOnly));
                }
                else if (leftInfo is not null && rightInfo is not null)
                {
                    var status = CompareFilePair(leftInfo, rightInfo, compareMode);
                    results.Add(new SyncComparisonItem(
                        relPath,
                        leftInfo.FullName,
                        rightInfo.FullName,
                        leftInfo.Length,
                        rightInfo.Length,
                        leftInfo.LastWriteTime,
                        rightInfo.LastWriteTime,
                        status));
                }
            }

            return (IReadOnlyList<SyncComparisonItem>)results;
        }, ct);
    }

    public static async Task<SyncExecutionResult> SynchronizeAsync(
        string leftRoot,
        string rightRoot,
        IEnumerable<SyncComparisonItem> items,
        SyncDirectionMode directionMode,
        bool deleteOrphans = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            int copiedL2R = 0;
            int copiedR2L = 0;
            int deleted = 0;
            var errors = new List<string>();

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (item.Status == SyncItemStatus.Equal)
                    continue;

                try
                {
                    string leftTarget = Path.Combine(leftRoot, item.RelativePath);
                    string rightTarget = Path.Combine(rightRoot, item.RelativePath);

                    switch (directionMode)
                    {
                        case SyncDirectionMode.BidirectionalMerge:
                            if (item.Status is SyncItemStatus.LeftOnly or SyncItemStatus.LeftNewer or SyncItemStatus.DifferentContent)
                            {
                                if (item.LeftFullPath is not null)
                                {
                                    progress?.Report($"→ {item.RelativePath}");
                                    CopyFileWithParents(item.LeftFullPath, rightTarget);
                                    copiedL2R++;
                                }
                            }
                            else if (item.Status is SyncItemStatus.RightOnly or SyncItemStatus.RightNewer)
                            {
                                if (item.RightFullPath is not null)
                                {
                                    progress?.Report($"← {item.RelativePath}");
                                    CopyFileWithParents(item.RightFullPath, leftTarget);
                                    copiedR2L++;
                                }
                            }
                            break;

                        case SyncDirectionMode.LeftToRightMirror:
                            if (item.Status is SyncItemStatus.LeftOnly or SyncItemStatus.LeftNewer or SyncItemStatus.RightNewer or SyncItemStatus.DifferentContent)
                            {
                                if (item.LeftFullPath is not null)
                                {
                                    progress?.Report($"→ {item.RelativePath}");
                                    CopyFileWithParents(item.LeftFullPath, rightTarget);
                                    copiedL2R++;
                                }
                            }
                            else if (item.Status == SyncItemStatus.RightOnly && deleteOrphans && item.RightFullPath is not null)
                            {
                                progress?.Report($"✕ {item.RelativePath}");
                                File.Delete(item.RightFullPath);
                                deleted++;
                            }
                            break;

                        case SyncDirectionMode.RightToLeftMirror:
                            if (item.Status is SyncItemStatus.RightOnly or SyncItemStatus.RightNewer or SyncItemStatus.LeftNewer or SyncItemStatus.DifferentContent)
                            {
                                if (item.RightFullPath is not null)
                                {
                                    progress?.Report($"← {item.RelativePath}");
                                    CopyFileWithParents(item.RightFullPath, leftTarget);
                                    copiedR2L++;
                                }
                            }
                            else if (item.Status == SyncItemStatus.LeftOnly && deleteOrphans && item.LeftFullPath is not null)
                            {
                                progress?.Report($"✕ {item.RelativePath}");
                                File.Delete(item.LeftFullPath);
                                deleted++;
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.RelativePath}: {ex.Message}");
                }
            }

            return new SyncExecutionResult(copiedL2R, copiedR2L, deleted, errors);
        }, ct);
    }

    private static void CopyFileWithParents(string sourcePath, string destPath)
    {
        var parent = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        File.Copy(sourcePath, destPath, overwrite: true);
        try
        {
            File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(sourcePath));
        }
        catch { }
    }

    private static SyncItemStatus CompareFilePair(FileInfo left, FileInfo right, SyncCompareMode mode)
    {
        if (mode == SyncCompareMode.SizeOnly)
        {
            return left.Length == right.Length ? SyncItemStatus.Equal : SyncItemStatus.DifferentContent;
        }

        if (mode == SyncCompareMode.Sha256Content)
        {
            if (left.Length != right.Length)
                return left.LastWriteTimeUtc >= right.LastWriteTimeUtc ? SyncItemStatus.LeftNewer : SyncItemStatus.RightNewer;

            string h1 = ComputeSha256Hex(left.FullName);
            string h2 = ComputeSha256Hex(right.FullName);
            if (string.Equals(h1, h2, StringComparison.OrdinalIgnoreCase))
                return SyncItemStatus.Equal;

            return left.LastWriteTimeUtc >= right.LastWriteTimeUtc ? SyncItemStatus.LeftNewer : SyncItemStatus.RightNewer;
        }

        // SizeAndTimestamp (allowing up to 2 seconds FAT/SMB timestamp tolerance)
        double diffSeconds = (left.LastWriteTimeUtc - right.LastWriteTimeUtc).TotalSeconds;
        if (Math.Abs(diffSeconds) <= 2.0 && left.Length == right.Length)
            return SyncItemStatus.Equal;

        if (diffSeconds > 2.0)
            return SyncItemStatus.LeftNewer;
        if (diffSeconds < -2.0)
            return SyncItemStatus.RightNewer;

        return SyncItemStatus.DifferentContent;
    }

    private static string ComputeSha256Hex(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash);
    }

    private static Dictionary<string, FileInfo> CollectFiles(string rootDir, bool recursive, CancellationToken ct)
    {
        var map = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
            return map;

        var stack = new Stack<string>();
        stack.Push(rootDir);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            try
            {
                var di = new DirectoryInfo(current);
                foreach (var fi in di.EnumerateFiles())
                {
                    var rel = Path.GetRelativePath(rootDir, fi.FullName);
                    map[rel] = fi;
                }

                if (recursive)
                {
                    foreach (var sub in di.EnumerateDirectories())
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                            stack.Push(sub.FullName);
                    }
                }
            }
            catch { }
        }

        return map;
    }
}

