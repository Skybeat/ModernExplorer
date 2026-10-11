using System.Diagnostics;
using System.IO;

namespace ModernExplorer.Core.FileSystem;

/// <summary>
/// Snapshot of Git repository status for a directory.
/// </summary>
public sealed record GitRepoStatus(
    bool IsGitRepository,
    string RepoRootPath,
    string BranchName,
    int ModifiedCount,
    int UntrackedCount,
    int StagedCount,
    IReadOnlyDictionary<string, string> EntryStatusBadges)
{
    public static readonly GitRepoStatus NotARepo = new(
        false,
        "",
        "",
        0,
        0,
        0,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public string SummaryBadge
    {
        get
        {
            if (!IsGitRepository || string.IsNullOrEmpty(BranchName))
                return "";

            var parts = new List<string> { $"⎇ {BranchName}" };
            if (StagedCount > 0) parts.Add($"+{StagedCount}");
            if (ModifiedCount > 0) parts.Add($"~{ModifiedCount}");
            if (UntrackedCount > 0) parts.Add($"?{UntrackedCount}");
            return string.Join(" ", parts);
        }
    }
}

/// <summary>
/// Detects Git repositories and queries branch & file working-tree status badges (M, A, U, D, R).
/// </summary>
public static class GitStatusService
{
    public static string? FindRepositoryRoot(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || directoryPath.Contains("://", StringComparison.Ordinal))
            return null;

        try
        {
            var current = new DirectoryInfo(directoryPath);
            while (current is not null)
            {
                var dotGitDir = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(dotGitDir) || File.Exists(dotGitDir))
                    return current.FullName;

                current = current.Parent;
            }
        }
        catch { }

        return null;
    }

    public static async Task<GitRepoStatus> GetStatusForDirectoryAsync(string? directoryPath, CancellationToken ct = default)
    {
        var repoRoot = FindRepositoryRoot(directoryPath);
        if (string.IsNullOrEmpty(repoRoot))
            return GitRepoStatus.NotARepo;

        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "status --porcelain=v1 -b -uall",
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                    return ReadBranchFallback(repoRoot);

                string stdout = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(3000) || proc.ExitCode != 0)
                    return ReadBranchFallback(repoRoot);

                return ParsePorcelainOutput(repoRoot, stdout);
            }
            catch
            {
                return ReadBranchFallback(repoRoot);
            }
        }, ct);
    }

    public static GitRepoStatus ParsePorcelainOutput(string repoRoot, string porcelainOutput)
    {
        string branch = "HEAD";
        int modified = 0;
        int untracked = 0;
        int staged = 0;
        var badges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var lines = porcelainOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var header = line[3..].Trim();
                int dots = header.IndexOf("...", StringComparison.Ordinal);
                branch = dots > 0 ? header[..dots] : header.Split(' ')[0];
                continue;
            }

            if (line.Length < 4)
                continue;

            char indexStatus = line[0];
            char workTreeStatus = line[1];
            string relPath = line[3..].Trim().Trim('"');

            int arrowIdx = relPath.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIdx >= 0)
                relPath = relPath[(arrowIdx + 4)..].Trim().Trim('"');

            string badge;
            if (indexStatus == '?' && workTreeStatus == '?')
            {
                badge = "U";
                untracked++;
            }
            else
            {
                if (indexStatus is not ' ' and not '?')
                    staged++;
                if (workTreeStatus is not ' ' and not '?')
                    modified++;

                char primary = workTreeStatus != ' ' ? workTreeStatus : indexStatus;
                badge = primary switch
                {
                    'M' => "M",
                    'A' => "A",
                    'D' => "D",
                    'R' => "R",
                    'C' => "C",
                    'U' => "!",
                    _ => "M"
                };
            }

            string normalizedRel = relPath.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(repoRoot, normalizedRel);
            badges[fullPath] = badge;

            // Also propagate modified indicator to ancestor directories inside the repository
            var parentDir = Path.GetDirectoryName(fullPath);
            while (!string.IsNullOrEmpty(parentDir) &&
                   parentDir.Length > repoRoot.Length &&
                   parentDir.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (!badges.ContainsKey(parentDir))
                    badges[parentDir] = "●";
                parentDir = Path.GetDirectoryName(parentDir);
            }
        }

        return new GitRepoStatus(true, repoRoot, branch, modified, untracked, staged, badges);
    }

    private static GitRepoStatus ReadBranchFallback(string repoRoot)
    {
        try
        {
            var headFile = Path.Combine(repoRoot, ".git", "HEAD");
            if (File.Exists(headFile))
            {
                var head = File.ReadAllText(headFile).Trim();
                const string prefix = "ref: refs/heads/";
                string branch = head.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? head[prefix.Length..]
                    : (head.Length >= 7 ? head[..7] : "HEAD");
                return new GitRepoStatus(true, repoRoot, branch, 0, 0, 0, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }
        }
        catch { }

        return GitRepoStatus.NotARepo;
    }
}
