using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using ModernExplorer.Core.FileSystem;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ModernExplorer.Core.Archive;

/// <summary>
/// Supported archive creation formats.
/// </summary>
public enum ArchiveFormat
{
    Zip = 0,
    TarGz = 1,
    SevenZip = 2,
}

/// <summary>
/// Integrated archive manager for browsing (.zip, .tar.gz, .7z, .tar, .rar) as virtual folders,
/// extracting archives, and packing selected files/folders into .zip, .tar.gz, or .7z archives.
/// </summary>
public static class ArchiveService
{
    public const string VirtualSchemePrefix = "archive://";

    private static readonly HashSet<string> BrowsableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".tar", ".gz", ".tgz", ".rar", ".nupkg", ".jar"
    };

    public static bool IsBrowsableArchiveFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsVirtualArchivePath(path))
            return false;

        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return File.Exists(path);

        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && BrowsableExtensions.Contains(ext) && File.Exists(path);
    }

    public static bool IsSupportedArchive(string? path) => IsBrowsableArchiveFile(path);

    public static bool IsSupportedArchiveFile(string? path) => IsBrowsableArchiveFile(path);

    public static bool IsVirtualArchivePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith(VirtualSchemePrefix, StringComparison.OrdinalIgnoreCase);

    public static string BuildVirtualArchivePath(string archiveFilePath, string innerRelativePath = "")
    {
        var cleanInner = innerRelativePath.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(cleanInner)
            ? $"{VirtualSchemePrefix}{archiveFilePath}"
            : $"{VirtualSchemePrefix}{archiveFilePath}|{cleanInner}";
    }

    public static bool TryParseVirtualArchivePath(string? virtualPath, out string archiveFilePath, out string innerRelativePath)
    {
        archiveFilePath = "";
        innerRelativePath = "";
        if (!IsVirtualArchivePath(virtualPath))
            return false;

        var payload = virtualPath![VirtualSchemePrefix.Length..];
        int pipeIdx = payload.IndexOf('|');
        if (pipeIdx < 0)
        {
            archiveFilePath = payload;
            innerRelativePath = "";
        }
        else
        {
            archiveFilePath = payload[..pipeIdx];
            innerRelativePath = payload[(pipeIdx + 1)..].Replace('\\', '/').Trim('/');
        }

        return !string.IsNullOrWhiteSpace(archiveFilePath);
    }

    public static string GetParentPathForVirtualArchive(string virtualPath)
    {
        if (!TryParseVirtualArchivePath(virtualPath, out var archiveFile, out var inner))
            return "";

        if (string.IsNullOrEmpty(inner))
            return Path.GetDirectoryName(archiveFile) ?? archiveFile;

        int slash = inner.LastIndexOf('/');
        return slash < 0
            ? BuildVirtualArchivePath(archiveFile, "")
            : BuildVirtualArchivePath(archiveFile, inner[..slash]);
    }

    /// <summary>
    /// Lists virtual directory and file entries at a given level inside an archive.
    /// </summary>
    public static IReadOnlyList<FileEntry> ListVirtualArchiveEntries(string virtualPath)
    {
        if (!TryParseVirtualArchivePath(virtualPath, out var archiveFile, out var currentPrefix) ||
            !File.Exists(archiveFile))
        {
            return [];
        }

        var entries = new List<FileEntry>();
        string parentTarget = GetParentPathForVirtualArchive(virtualPath);
        if (!string.IsNullOrEmpty(parentTarget))
        {
            entries.Add(FileEntry.ParentLink(parentTarget));
        }

        string prefixWithSlash = string.IsNullOrEmpty(currentPrefix) ? "" : currentPrefix + "/";
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = ArchiveFactory.OpenArchive(archiveFile);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                string normKey = entry.Key.Replace('\\', '/').Trim('/');
                if (string.IsNullOrEmpty(normKey))
                    continue;

                if (!string.IsNullOrEmpty(prefixWithSlash))
                {
                    if (!normKey.StartsWith(prefixWithSlash, StringComparison.OrdinalIgnoreCase))
                        continue;
                    normKey = normKey[prefixWithSlash.Length..];
                }

                if (string.IsNullOrEmpty(normKey))
                    continue;

                int slashIdx = normKey.IndexOf('/');
                if (slashIdx >= 0)
                {
                    // Immediate child directory
                    string childFolderName = normKey[..slashIdx];
                    if (seenFolders.Add(childFolderName))
                    {
                        string childInner = string.IsNullOrEmpty(currentPrefix)
                            ? childFolderName
                            : $"{currentPrefix}/{childFolderName}";
                        entries.Add(new FileEntry(
                            childFolderName,
                            BuildVirtualArchivePath(archiveFile, childInner),
                            true,
                            0,
                            entry.LastModifiedTime ?? DateTime.Now,
                            FileAttributes.Directory)
                        {
                            IsArchiveVirtualEntry = true
                        });
                    }
                }
                else if (entry.IsDirectory)
                {
                    if (seenFolders.Add(normKey))
                    {
                        string childInner = string.IsNullOrEmpty(currentPrefix)
                            ? normKey
                            : $"{currentPrefix}/{normKey}";
                        entries.Add(new FileEntry(
                            normKey,
                            BuildVirtualArchivePath(archiveFile, childInner),
                            true,
                            0,
                            entry.LastModifiedTime ?? DateTime.Now,
                            FileAttributes.Directory)
                        {
                            IsArchiveVirtualEntry = true
                        });
                    }
                }
                else
                {
                    string childInner = string.IsNullOrEmpty(currentPrefix)
                        ? normKey
                        : $"{currentPrefix}/{normKey}";
                    entries.Add(new FileEntry(
                        normKey,
                        BuildVirtualArchivePath(archiveFile, childInner),
                        false,
                        entry.Size,
                        entry.LastModifiedTime ?? DateTime.Now,
                        FileAttributes.Normal)
                    {
                        IsArchiveVirtualEntry = true
                    });
                }
            }
        }
        catch { }

        return entries;
    }

    /// <summary>
    /// Extracts an entire archive or selected virtual archive paths to <paramref name="destinationDirectory"/>.
    /// </summary>
    public static async Task<int> ExtractArchiveAsync(
        string archiveOrVirtualPath,
        string destinationDirectory,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(destinationDirectory);

            string archiveFile = archiveOrVirtualPath;
            string innerPrefix = "";
            if (IsVirtualArchivePath(archiveOrVirtualPath))
            {
                TryParseVirtualArchivePath(archiveOrVirtualPath, out archiveFile, out innerPrefix);
            }

            if (!File.Exists(archiveFile))
                return 0;

            int extractedCount = 0;
            using var archive = ArchiveFactory.OpenArchive(archiveFile);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory || string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                string normKey = entry.Key.Replace('\\', '/').Trim('/');
                if (!string.IsNullOrEmpty(innerPrefix))
                {
                    if (!string.Equals(normKey, innerPrefix, StringComparison.OrdinalIgnoreCase) &&
                        !normKey.StartsWith(innerPrefix + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                string relativeTarget = normKey;
                if (!string.IsNullOrEmpty(innerPrefix) && normKey.StartsWith(innerPrefix + "/", StringComparison.OrdinalIgnoreCase))
                {
                    relativeTarget = normKey[(innerPrefix.Length + 1)..];
                }
                else if (string.Equals(normKey, innerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativeTarget = Path.GetFileName(normKey);
                }

                string fullTarget = Path.Combine(destinationDirectory, relativeTarget.Replace('/', Path.DirectorySeparatorChar));
                var parentDir = Path.GetDirectoryName(fullTarget);
                if (!string.IsNullOrEmpty(parentDir))
                    Directory.CreateDirectory(parentDir);

                progress?.Report(entry.Key);
                entry.WriteToFile(fullTarget, new ExtractionOptions
                {
                    ExtractFullPath = false,
                    Overwrite = true,
                    PreserveFileTime = true,
                });
                extractedCount++;
            }

            return extractedCount;
        }, ct);
    }

    /// <summary>
    /// Extracts a single virtual archive file entry to a temporary local file for Quick-View (F3) or Edit (F4).
    /// </summary>
    public static async Task<string?> ExtractVirtualEntryToTempFileAsync(string virtualPath, CancellationToken ct = default)
    {
        if (!TryParseVirtualArchivePath(virtualPath, out var archiveFile, out var innerPath) ||
            string.IsNullOrEmpty(innerPath) || !File.Exists(archiveFile))
        {
            return null;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "ModernExplorer_ArchivePreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        int count = await ExtractArchiveAsync(virtualPath, tempDir, null, ct);
        if (count == 0)
            return null;

        var extractedFile = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories).FirstOrDefault();
        return extractedFile;
    }

    /// <summary>
    /// Creates a new archive (.zip, .tar.gz, or .7z) from the specified source paths.
    /// </summary>
    public static async Task<string> CreateArchiveAsync(
        IReadOnlyList<string> sourcePaths,
        string outputArchivePath,
        ArchiveFormat format,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var outDir = Path.GetDirectoryName(outputArchivePath);
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            if (File.Exists(outputArchivePath))
                File.Delete(outputArchivePath);

            if (format == ArchiveFormat.SevenZip)
            {
                if (TryCreateSevenZipViaCli(sourcePaths, outputArchivePath))
                    return outputArchivePath;

                // Fallback to standard .zip if 7z.exe is not installed on the system
                format = ArchiveFormat.Zip;
            }

            if (format == ArchiveFormat.TarGz)
            {
                using var fs = new FileStream(outputArchivePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var gz = new GZipStream(fs, CompressionLevel.Optimal);
                using var tar = new TarWriter(gz, leaveOpen: false);

                foreach (var src in sourcePaths)
                {
                    ct.ThrowIfCancellationRequested();
                    AddPathToTar(tar, src, Path.GetDirectoryName(src) ?? "", progress, ct);
                }

                return outputArchivePath;
            }

            // Default: .zip
            using (var zip = ZipFile.Open(outputArchivePath, ZipArchiveMode.Create))
            {
                foreach (var src in sourcePaths)
                {
                    ct.ThrowIfCancellationRequested();
                    AddPathToZip(zip, src, Path.GetDirectoryName(src) ?? "", progress, ct);
                }
            }

            return outputArchivePath;
        }, ct);
    }

    private static void AddPathToZip(ZipArchive zip, string sourcePath, string baseDir, IProgress<string>? progress, CancellationToken ct)
    {
        if (File.Exists(sourcePath))
        {
            string entryName = Path.GetRelativePath(baseDir, sourcePath).Replace('\\', '/');
            progress?.Report(entryName);
            zip.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
        }
        else if (Directory.Exists(sourcePath))
        {
            foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string entryName = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
                progress?.Report(entryName);
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            }
        }
    }

    private static void AddPathToTar(TarWriter tar, string sourcePath, string baseDir, IProgress<string>? progress, CancellationToken ct)
    {
        if (File.Exists(sourcePath))
        {
            string entryName = Path.GetRelativePath(baseDir, sourcePath).Replace('\\', '/');
            progress?.Report(entryName);
            tar.WriteEntry(sourcePath, entryName);
        }
        else if (Directory.Exists(sourcePath))
        {
            foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string entryName = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
                progress?.Report(entryName);
                tar.WriteEntry(file, entryName);
            }
        }
    }

    private static bool TryCreateSevenZipViaCli(IReadOnlyList<string> sourcePaths, string outputArchivePath)
    {
        try
        {
            string[] candidates =
            [
                "7z.exe",
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe"
            ];

            string? sevenZipExe = candidates.FirstOrDefault(c => c == "7z.exe" || File.Exists(c));
            if (sevenZipExe is null)
                return false;

            var quotedSources = string.Join(" ", sourcePaths.Select(p => $"\"{p}\""));
            var psi = new ProcessStartInfo
            {
                FileName = sevenZipExe,
                Arguments = $"a -t7z \"{outputArchivePath}\" {quotedSources} -y",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(120_000);
            return proc.ExitCode == 0 && File.Exists(outputArchivePath);
        }
        catch
        {
            return false;
        }
    }
}
