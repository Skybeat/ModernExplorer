using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ModernExplorer.Core.Features;

public sealed record FileSplitResult(
    string SourceFilePath,
    long TotalBytes,
    IReadOnlyList<string> CreatedPartPaths,
    string? ManifestPath);

public sealed record FileJoinResult(
    string OutputFilePath,
    long TotalBytes,
    int JoinedPartCount);

public sealed record FileShredResult(
    int ShreddedFileCount,
    long ShreddedBytes,
    IReadOnlyList<string> Errors);

/// <summary>
/// Provides large-file splitting (.001, .002, ...), multi-part rejoining,
/// and multi-pass cryptographic file shredding (1-pass, 3-pass DoD, 7-pass).
/// </summary>
public static class FileSplitAndShredService
{
    private static readonly Regex PartSuffixRegex = new(@"\.(\d{3,4})$", RegexOptions.Compiled);

    public static bool IsSplitPartFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        return PartSuffixRegex.IsMatch(Path.GetFileName(filePath));
    }

    public static IReadOnlyList<(string Label, long Bytes)> PresetPartSizes { get; } =
    [
        ("10 MB (E-Mail / Chat)", 10L * 1024 * 1024),
        ("25 MB (Anhang-Limit)", 25L * 1024 * 1024),
        ("100 MB (Archiv-Block)", 100L * 1024 * 1024),
        ("700 MB (CD-ROM)", 700L * 1024 * 1024),
        ("1 GB (Cloud-Chunk)", 1024L * 1024 * 1024),
        ("4 GB (FAT32-Limit)", 4095L * 1024 * 1024),
    ];

    public static async Task<FileSplitResult> SplitFileAsync(
        string sourceFilePath,
        string outputDirectory,
        long partSizeBytes,
        bool generateSha256Manifest = true,
        IProgress<(int PartNumber, int TotalParts, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Quelldatei nicht gefunden.", sourceFilePath);

        if (partSizeBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(partSizeBytes), "Teilgröße muss mindestens 1 KB betragen.");

        Directory.CreateDirectory(outputDirectory);

        return await Task.Run(async () =>
        {
            var fi = new FileInfo(sourceFilePath);
            long totalBytes = fi.Length;
            int totalParts = totalBytes == 0 ? 1 : (int)((totalBytes + partSizeBytes - 1) / partSizeBytes);
            int digits = totalParts >= 1000 ? 4 : 3;

            var createdParts = new List<string>(totalParts);
            byte[] buffer = new byte[128 * 1024];

            await using var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, buffer.Length);
            long totalProcessed = 0;

            for (int partIdx = 1; partIdx <= totalParts; partIdx++)
            {
                ct.ThrowIfCancellationRequested();
                string suffix = partIdx.ToString($"D{digits}", CultureInfo.InvariantCulture);
                string partFileName = $"{fi.Name}.{suffix}";
                string partFullPath = Path.Combine(outputDirectory, partFileName);

                long remainingInPart = partSizeBytes;
                await using (var partStream = new FileStream(partFullPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length))
                {
                    while (remainingInPart > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        int toRead = (int)Math.Min(buffer.Length, remainingInPart);
                        int read = await sourceStream.ReadAsync(buffer.AsMemory(0, toRead), ct);
                        if (read <= 0)
                            break;

                        await partStream.WriteAsync(buffer.AsMemory(0, read), ct);
                        remainingInPart -= read;
                        totalProcessed += read;

                        double pct = totalBytes > 0 ? Math.Clamp(totalProcessed * 100.0 / totalBytes, 0, 100) : 100;
                        progress?.Report((partIdx, totalParts, pct));
                    }
                }

                createdParts.Add(partFullPath);
            }

            string? manifestPath = null;
            if (generateSha256Manifest && totalBytes > 0)
            {
                var origChecksum = await ChecksumBatchService.ComputeSingleFileAsync(sourceFilePath, ct);
                if (origChecksum is not null)
                {
                    manifestPath = Path.Combine(outputDirectory, $"{fi.Name}.sha256");
                    string manifestContent = ChecksumBatchService.GenerateManifestContent([origChecksum], ChecksumAlgorithm.Sha256);
                    await File.WriteAllTextAsync(manifestPath, manifestContent, ct);
                }
            }

            return new FileSplitResult(sourceFilePath, totalBytes, createdParts, manifestPath);
        }, ct);
    }

    public static IReadOnlyList<string> DiscoverSplitParts(string anyPartPath)
    {
        if (string.IsNullOrWhiteSpace(anyPartPath))
            return [];

        string dir = Path.GetDirectoryName(anyPartPath) ?? "";
        string fileName = Path.GetFileName(anyPartPath);
        var match = PartSuffixRegex.Match(fileName);
        if (!match.Success)
            return File.Exists(anyPartPath) ? [anyPartPath] : [];

        string baseFileName = fileName[..match.Index];
        int digits = match.Groups[1].Value.Length;

        var parts = new List<string>();
        for (int idx = 1; idx <= 9999; idx++)
        {
            string candidate = Path.Combine(dir, $"{baseFileName}.{idx.ToString($"D{digits}", CultureInfo.InvariantCulture)}");
            if (File.Exists(candidate))
                parts.Add(candidate);
            else
                break;
        }

        return parts;
    }

    public static async Task<FileJoinResult> JoinFilePartsAsync(
        string anyPartPath,
        string? outputFilePath = null,
        IProgress<(int CurrentPart, int TotalParts, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        var parts = DiscoverSplitParts(anyPartPath);
        if (parts.Count == 0)
            throw new FileNotFoundException("Keine zusammengehörigen .001-Teildateien gefunden.", anyPartPath);

        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            string dir = Path.GetDirectoryName(parts[0]) ?? "";
            string firstFileName = Path.GetFileName(parts[0]);
            var match = PartSuffixRegex.Match(firstFileName);
            string restoredName = match.Success ? firstFileName[..match.Index] : firstFileName + ".joined";
            outputFilePath = Path.Combine(dir, restoredName);
        }

        string targetOut = outputFilePath!;
        Directory.CreateDirectory(Path.GetDirectoryName(targetOut) ?? ".");

        return await Task.Run(async () =>
        {
            long totalExpected = parts.Sum(p => new FileInfo(p).Length);
            long written = 0;
            byte[] buffer = new byte[128 * 1024];

            await using var outStream = new FileStream(targetOut, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length);
            for (int i = 0; i < parts.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await using var inStream = new FileStream(parts[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite, buffer.Length);
                int read;
                while ((read = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await outStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                    double pct = totalExpected > 0 ? Math.Clamp(written * 100.0 / totalExpected, 0, 100) : 100;
                    progress?.Report((i + 1, parts.Count, pct));
                }
            }

            return new FileJoinResult(targetOut, written, parts.Count);
        }, ct);
    }

    public static async Task<FileShredResult> SecureShredAsync(
        IEnumerable<string> targetPaths,
        int passes = 3,
        IProgress<(string CurrentFile, int Pass, int TotalPasses)>? progress = null,
        CancellationToken ct = default)
    {
        passes = Math.Clamp(passes, 1, 7);

        return await Task.Run(() =>
        {
            var filesToShred = new List<string>();
            var dirsToRemove = new List<string>();

            foreach (var p in targetPaths)
            {
                if (File.Exists(p))
                {
                    filesToShred.Add(p);
                }
                else if (Directory.Exists(p))
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(p, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
                            filesToShred.Add(f);
                        foreach (var d in Directory.EnumerateDirectories(p, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
                            dirsToRemove.Add(d);
                        dirsToRemove.Add(p);
                    }
                    catch { }
                }
            }

            int shreddedCount = 0;
            long shreddedBytes = 0;
            var errors = new List<string>();
            byte[] buffer = new byte[64 * 1024];

            foreach (var filePath in filesToShred)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(filePath);
                    if (!fi.Exists) continue;
                    fi.Attributes = FileAttributes.Normal;
                    long length = fi.Length;

                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.WriteThrough))
                    {
                        for (int pass = 1; pass <= passes; pass++)
                        {
                            ct.ThrowIfCancellationRequested();
                            progress?.Report((fi.Name, pass, passes));
                            fs.Position = 0;

                            bool zeroPass = (pass == passes); // Final pass zeroes out sectors
                            bool onesPass = (passes >= 3 && pass == 1);

                            long remaining = length;
                            while (remaining > 0)
                            {
                                int chunk = (int)Math.Min(buffer.Length, remaining);
                                if (zeroPass)
                                    Array.Clear(buffer, 0, chunk);
                                else if (onesPass)
                                    Array.Fill(buffer, (byte)0xFF, 0, chunk);
                                else
                                    RandomNumberGenerator.Fill(buffer.AsSpan(0, chunk));

                                fs.Write(buffer, 0, chunk);
                                remaining -= chunk;
                            }
                            fs.Flush(flushToDisk: true);
                        }
                        fs.SetLength(0);
                    }

                    // Obfuscate filename & timestamps before unlinking
                    string dir = Path.GetDirectoryName(filePath) ?? "";
                    string obfName = Path.Combine(dir, Guid.NewGuid().ToString("N")[..12]);
                    string finalPath = filePath;
                    try
                    {
                        File.Move(filePath, obfName);
                        finalPath = obfName;
                    }
                    catch { }

                    try
                    {
                        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        File.SetCreationTimeUtc(finalPath, epoch);
                        File.SetLastWriteTimeUtc(finalPath, epoch);
                        File.SetLastAccessTimeUtc(finalPath, epoch);
                    }
                    catch { }

                    File.Delete(finalPath);
                    shreddedCount++;
                    shreddedBytes += length;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            foreach (var dir in dirsToRemove.OrderByDescending(d => d.Length))
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(dir)}: {ex.Message}");
                }
            }

            return new FileShredResult(shreddedCount, shreddedBytes, errors);
        }, ct);
    }
}

