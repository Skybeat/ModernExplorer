using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ModernExplorer.Core.Features;

public enum ChecksumAlgorithm
{
    Sha256 = 0,
    Sha512 = 1,
    Md5 = 2,
    Crc32 = 3,
}

public enum ChecksumVerifyStatus
{
    VerifiedOk = 0,
    HashMismatch = 1,
    FileMissing = 2,
    ReadError = 3,
}

public sealed record FileChecksumEntry(
    string FileName,
    string FullPath,
    long SizeBytes,
    string Crc32,
    string Md5,
    string Sha256,
    string Sha512)
{
    public string GetHash(ChecksumAlgorithm algorithm) => algorithm switch
    {
        ChecksumAlgorithm.Sha256 => Sha256,
        ChecksumAlgorithm.Sha512 => Sha512,
        ChecksumAlgorithm.Md5 => Md5,
        ChecksumAlgorithm.Crc32 => Crc32,
        _ => Sha256,
    };

    public bool MatchesExpectedHash(string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
            return false;

        string clean = expectedHash.Trim().Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return string.Equals(clean, Sha256, StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, Sha512, StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, Md5, StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, Crc32, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ChecksumVerificationRow(
    string RelativeOrFileName,
    string FullPath,
    ChecksumAlgorithm Algorithm,
    string ExpectedHash,
    string ActualHash,
    ChecksumVerifyStatus Status);

/// <summary>
/// Multi-file checksum calculator, manifest generator (.sha256, .sha512, .md5, .sfv),
/// and manifest verifier.
/// </summary>
public static class ChecksumBatchService
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    public static bool IsManifestFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".sha256" or ".sha512" or ".md5" or ".sfv" or ".sha256sum" or ".md5sum";
    }

    public static async Task<IReadOnlyList<FileChecksumEntry>> ComputeChecksumsAsync(
        IEnumerable<string> paths,
        IProgress<(int Current, int Total, string FileName)>? progress = null,
        CancellationToken ct = default)
    {
        var fileList = new List<string>();
        foreach (var p in paths)
        {
            if (File.Exists(p))
                fileList.Add(p);
            else if (Directory.Exists(p))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(p, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                        fileList.Add(f);
                }
                catch { }
            }
        }

        var results = new List<FileChecksumEntry>(fileList.Count);
        int total = fileList.Count;
        int current = 0;

        foreach (var filePath in fileList)
        {
            ct.ThrowIfCancellationRequested();
            current++;
            string fileName = Path.GetFileName(filePath);
            progress?.Report((current, total, fileName));

            var entry = await ComputeSingleFileAsync(filePath, ct);
            if (entry is not null)
                results.Add(entry);
        }

        return results;
    }

    public static async Task<FileChecksumEntry?> ComputeSingleFileAsync(string filePath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var fi = new FileInfo(filePath);
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
                using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);

                uint crc = 0xFFFFFFFFu;
                byte[] buffer = new byte[65536];
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    md5.AppendData(buffer, 0, read);
                    sha256.AppendData(buffer, 0, read);
                    sha512.AppendData(buffer, 0, read);

                    for (int i = 0; i < read; i++)
                    {
                        crc = (crc >> 8) ^ Crc32Table[(crc ^ buffer[i]) & 0xFF];
                    }
                }

                crc ^= 0xFFFFFFFFu;

                return new FileChecksumEntry(
                    fi.Name,
                    fi.FullName,
                    fi.Length,
                    crc.ToString("X8"),
                    Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant(),
                    Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(),
                    Convert.ToHexString(sha512.GetHashAndReset()).ToLowerInvariant());
            }
            catch
            {
                return null;
            }
        }, ct);
    }

    public static string GenerateManifestContent(
        IReadOnlyList<FileChecksumEntry> entries,
        ChecksumAlgorithm algorithm,
        string? baseDirectory = null)
    {
        var sb = new StringBuilder();
        if (algorithm == ChecksumAlgorithm.Crc32)
        {
            sb.AppendLine($"; Generated by ModernExplorer on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        foreach (var e in entries)
        {
            string relName = e.FileName;
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                try
                {
                    relName = Path.GetRelativePath(baseDirectory, e.FullPath).Replace('\\', '/');
                }
                catch
                {
                    relName = e.FileName;
                }
            }

            if (algorithm == ChecksumAlgorithm.Crc32)
            {
                sb.AppendLine($"{relName} {e.Crc32}");
            }
            else
            {
                sb.AppendLine($"{e.GetHash(algorithm)} *{relName}");
            }
        }

        return sb.ToString();
    }

    public static string GetDefaultManifestExtension(ChecksumAlgorithm algorithm) => algorithm switch
    {
        ChecksumAlgorithm.Sha256 => ".sha256",
        ChecksumAlgorithm.Sha512 => ".sha512",
        ChecksumAlgorithm.Md5 => ".md5",
        ChecksumAlgorithm.Crc32 => ".sfv",
        _ => ".sha256",
    };

    public static async Task<IReadOnlyList<ChecksumVerificationRow>> VerifyManifestFileAsync(
        string manifestFilePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(manifestFilePath))
            return [];

        string baseDir = Path.GetDirectoryName(manifestFilePath) ?? "";
        string content = await File.ReadAllTextAsync(manifestFilePath, ct);
        string ext = Path.GetExtension(manifestFilePath).ToLowerInvariant();
        return await VerifyManifestContentAsync(content, baseDir, ext, ct);
    }

    public static async Task<IReadOnlyList<ChecksumVerificationRow>> VerifyManifestContentAsync(
        string manifestContent,
        string baseDirectory,
        string manifestExtensionHint = ".sha256",
        CancellationToken ct = default)
    {
        var lines = (manifestContent ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var rows = new List<ChecksumVerificationRow>();
        bool isSfvHint = string.Equals(manifestExtensionHint, ".sfv", StringComparison.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            ct.ThrowIfCancellationRequested();
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            string expectedHash;
            string relFile;

            if (isSfvHint || (line.Length > 9 && IsHex(line[^8..]) && line[^9] == ' ' && !line.Contains(" *", StringComparison.Ordinal)))
            {
                // SFV format: filename.ext 1A2B3C4D
                int lastSpace = line.LastIndexOf(' ');
                if (lastSpace <= 0) continue;
                relFile = line[..lastSpace].Trim().Trim('"');
                expectedHash = line[(lastSpace + 1)..].Trim();
            }
            else
            {
                // GNU coreutils format: <hash> *<filename> or <hash>  <filename>
                int firstSpace = line.IndexOf(' ');
                if (firstSpace <= 0) continue;
                expectedHash = line[..firstSpace].Trim();
                relFile = line[(firstSpace + 1)..].Trim();
                if (relFile.StartsWith('*'))
                    relFile = relFile[1..].Trim();
            }

            ChecksumAlgorithm algo = DetectAlgorithmByHashLength(expectedHash);
            string fullPath = Path.Combine(baseDirectory, relFile.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                rows.Add(new ChecksumVerificationRow(relFile, fullPath, algo, expectedHash, "", ChecksumVerifyStatus.FileMissing));
                continue;
            }

            var computed = await ComputeSingleFileAsync(fullPath, ct);
            if (computed is null)
            {
                rows.Add(new ChecksumVerificationRow(relFile, fullPath, algo, expectedHash, "", ChecksumVerifyStatus.ReadError));
                continue;
            }

            string actual = computed.GetHash(algo);
            bool ok = string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
            rows.Add(new ChecksumVerificationRow(
                relFile,
                fullPath,
                algo,
                expectedHash,
                actual,
                ok ? ChecksumVerifyStatus.VerifiedOk : ChecksumVerifyStatus.HashMismatch));
        }

        return rows;
    }

    public static ChecksumAlgorithm DetectAlgorithmByHashLength(string hash) => (hash?.Trim().Length ?? 0) switch
    {
        8 => ChecksumAlgorithm.Crc32,
        32 => ChecksumAlgorithm.Md5,
        128 => ChecksumAlgorithm.Sha512,
        _ => ChecksumAlgorithm.Sha256,
    };

    private static bool IsHex(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }
        return true;
    }
}
