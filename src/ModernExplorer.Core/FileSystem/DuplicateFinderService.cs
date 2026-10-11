using System.IO;
using System.Security.Cryptography;

namespace ModernExplorer.Core.FileSystem;

/// <summary>
/// Represents a group of identical duplicate files sharing the same size and SHA-256 hash.
/// </summary>
public sealed record DuplicateGroup(
    string Sha256Hash,
    long FileSizeBytes,
    IReadOnlyList<FileEntry> Files)
{
    public long WastedBytes => Files.Count > 1 ? FileSizeBytes * (Files.Count - 1) : 0;
}

/// <summary>
/// Result of scanning a directory tree for duplicate files.
/// </summary>
public sealed record DuplicateScanResult(
    IReadOnlyList<DuplicateGroup> Groups,
    int TotalDuplicateFiles,
    long TotalWastedBytes);

/// <summary>
/// Detailed progress payload for the multi-stage duplicate scan.
/// </summary>
public sealed record DuplicateScanProgress(
    string StageMessage,
    double Percentage,
    int ProcessedFiles,
    int TotalFiles,
    bool IsIndeterminate = false);

/// <summary>
/// Result of deleting selected duplicate files.
/// </summary>
public sealed record DuplicateDeleteResult(
    int DeletedCount,
    long FreedBytes,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> Errors);

/// <summary>
/// High-speed multi-stage duplicate file finder (Size -> 16 KB Partial Hash -> Full SHA-256 Hash).
/// </summary>
public static class DuplicateFinderService
{
    public static DuplicateDeleteResult DeleteDuplicateFiles(IEnumerable<string> filePaths)
    {
        var deletedPaths = new List<string>();
        var errors = new List<string>();
        long freedBytes = 0;

        foreach (var rawPath in filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(rawPath))
                    continue;

                var fi = new FileInfo(rawPath);
                long size = fi.Length;
                if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    fi.Attributes &= ~FileAttributes.ReadOnly;
                }

                File.Delete(rawPath);
                deletedPaths.Add(rawPath);
                freedBytes += size;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(rawPath)}: {ex.Message}");
            }
        }

        return new DuplicateDeleteResult(deletedPaths.Count, freedBytes, deletedPaths, errors);
    }

    public static async Task<DuplicateScanResult> FindDuplicatesAsync(
        string rootPath,
        bool includeSubdirectories = true,
        long minFileSizeBytes = 1,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        IProgress<DuplicateScanProgress>? detailedProgress = null)
    {
        return await Task.Run(() =>
        {
            void ReportStage(string message, double percent, int processed, int total, bool indeterminate)
            {
                progress?.Report(message);
                detailedProgress?.Report(new DuplicateScanProgress(message, percent, processed, total, indeterminate));
            }

            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return new DuplicateScanResult([], 0, 0);

            ReportStage("1/3 Sammle Dateien nach Dateigröße…", 0, 0, 0, indeterminate: true);
            var sizeBuckets = new Dictionary<long, List<FileEntry>>();
            var stack = new Stack<string>();
            stack.Push(rootPath);
            int enumeratedCount = 0;

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var fi in di.EnumerateFiles())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (fi.Length < minFileSizeBytes)
                            continue;

                        var rel = Path.GetRelativePath(rootPath, fi.FullName);
                        var entry = new FileEntry(fi.Name, fi.FullName, false, fi.Length, fi.LastWriteTime, fi.Attributes)
                        {
                            RelativeSubPath = rel
                        };

                        if (!sizeBuckets.TryGetValue(fi.Length, out var list))
                        {
                            list = [];
                            sizeBuckets[fi.Length] = list;
                        }
                        list.Add(entry);

                        enumeratedCount++;
                        if (enumeratedCount % 50 == 0)
                        {
                            ReportStage(
                                $"1/3 Sammle Dateien nach Dateigröße… ({enumeratedCount} Dateien erfasst)",
                                0,
                                enumeratedCount,
                                0,
                                indeterminate: true);
                        }
                    }

                    if (includeSubdirectories)
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

            var candidateBuckets = sizeBuckets.Values.Where(b => b.Count > 1).ToList();
            if (candidateBuckets.Count == 0)
            {
                ReportStage("Scan abgeschlossen – keine Duplikate gefunden.", 100, enumeratedCount, enumeratedCount, indeterminate: false);
                return new DuplicateScanResult([], 0, 0);
            }

            // Stage 2: Fast partial hash (first 8 KB + last 8 KB) for files > 16 KB
            int totalStage2Files = candidateBuckets.Sum(b => b.Count);
            int processedStage2Files = 0;
            ReportStage(
                $"2/3 Prüfe Schnell-Hashes für {candidateBuckets.Count} Größen-Gruppen ({totalStage2Files} Dateien)…",
                5,
                0,
                totalStage2Files,
                indeterminate: false);

            var partialCandidates = new List<List<FileEntry>>();
            foreach (var bucket in candidateBuckets)
            {
                ct.ThrowIfCancellationRequested();
                if (bucket[0].Size <= 16384)
                {
                    partialCandidates.Add(bucket);
                    processedStage2Files += bucket.Count;
                    double pctSmall = 5.0 + (processedStage2Files * 45.0 / Math.Max(1, totalStage2Files));
                    ReportStage(
                        $"2/3 Prüfe Schnell-Hashes ({processedStage2Files}/{totalStage2Files})…",
                        pctSmall,
                        processedStage2Files,
                        totalStage2Files,
                        indeterminate: false);
                    continue;
                }

                var byPartial = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);
                foreach (var file in bucket)
                {
                    ct.ThrowIfCancellationRequested();
                    var pHash = ComputePartialHash(file.FullPath);
                    processedStage2Files++;
                    double pct = 5.0 + (processedStage2Files * 45.0 / Math.Max(1, totalStage2Files));
                    ReportStage(
                        $"2/3 Prüfe Schnell-Hashes ({processedStage2Files}/{totalStage2Files}): {file.Name}",
                        pct,
                        processedStage2Files,
                        totalStage2Files,
                        indeterminate: false);

                    if (pHash is null) continue;
                    if (!byPartial.TryGetValue(pHash, out var pList))
                    {
                        pList = [];
                        byPartial[pHash] = pList;
                    }
                    pList.Add(file);
                }

                foreach (var pGroup in byPartial.Values.Where(g => g.Count > 1))
                    partialCandidates.Add(pGroup);
            }

            if (partialCandidates.Count == 0)
            {
                ReportStage("Scan abgeschlossen – keine Duplikate gefunden.", 100, totalStage2Files, totalStage2Files, indeterminate: false);
                return new DuplicateScanResult([], 0, 0);
            }

            // Stage 3: Full SHA-256 hash
            int totalStage3Files = partialCandidates.Sum(b => b.Count);
            int processedStage3Files = 0;
            ReportStage(
                $"3/3 Berechne vollständige SHA-256-Prüfsummen ({totalStage3Files} Kandidaten)…",
                50,
                0,
                totalStage3Files,
                indeterminate: false);

            var groups = new List<DuplicateGroup>();
            foreach (var pGroup in partialCandidates)
            {
                ct.ThrowIfCancellationRequested();
                var byFullHash = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in pGroup)
                {
                    ct.ThrowIfCancellationRequested();
                    var fullHash = ComputeFullSha256(file.FullPath);
                    processedStage3Files++;
                    double pct = 50.0 + (processedStage3Files * 50.0 / Math.Max(1, totalStage3Files));
                    ReportStage(
                        $"3/3 Berechne SHA-256 ({processedStage3Files}/{totalStage3Files}): {file.Name}",
                        pct,
                        processedStage3Files,
                        totalStage3Files,
                        indeterminate: false);

                    if (fullHash is null) continue;
                    if (!byFullHash.TryGetValue(fullHash, out var fList))
                    {
                        fList = [];
                        byFullHash[fullHash] = fList;
                    }
                    fList.Add(file);
                }

                foreach (var (hash, dupFiles) in byFullHash)
                {
                    if (dupFiles.Count > 1)
                    {
                        var ordered = dupFiles.OrderBy(f => f.LastWriteTime).ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
                        groups.Add(new DuplicateGroup(hash, ordered[0].Size, ordered));
                    }
                }
            }

            var sortedGroups = groups
                .OrderByDescending(g => g.WastedBytes)
                .ThenBy(g => g.Files[0].Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int totalDups = sortedGroups.Sum(g => g.Files.Count - 1);
            long totalWasted = sortedGroups.Sum(g => g.WastedBytes);
            ReportStage(
                $"Scan abgeschlossen: {sortedGroups.Count} Gruppe(n), {totalDups} Duplikat(e)",
                100,
                totalStage3Files,
                totalStage3Files,
                indeterminate: false);
            return new DuplicateScanResult(sortedGroups, totalDups, totalWasted);
        }, ct);
    }

    private static string? ComputePartialHash(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[16384];
            int n1 = fs.ReadAtLeast(buffer.AsSpan(0, 8192), 1, throwOnEndOfStream: false);
            int n2 = 0;
            if (fs.Length > 8192)
            {
                fs.Seek(-8192, SeekOrigin.End);
                n2 = fs.ReadAtLeast(buffer.AsSpan(8192, 8192), 1, throwOnEndOfStream: false);
            }
            var digest = SHA256.HashData(buffer.AsSpan(0, n2 > 0 ? 8192 + n2 : n1));
            return Convert.ToHexString(digest);
        }
        catch
        {
            return null;
        }
    }

    private static string? ComputeFullSha256(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var digest = SHA256.HashData(fs);
            return Convert.ToHexString(digest);
        }
        catch
        {
            return null;
        }
    }
}
