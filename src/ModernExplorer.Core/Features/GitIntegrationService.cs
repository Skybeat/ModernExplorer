using System.Diagnostics;
using System.IO;
using System.Text;
using ModernExplorer.Core.FileSystem;

namespace ModernExplorer.Core.Features;

public sealed record GitChangedFileItem(
    string RelativePath,
    string FullPath,
    string StatusBadge,
    bool IsStaged,
    bool IsUntracked);

public sealed record GitCommitLogItem(
    string ShortHash,
    string Author,
    string RelativeDate,
    string Subject);

public sealed record GitRepositoryOverview(
    bool IsGitRepository,
    string RepoRootPath,
    string CurrentBranch,
    IReadOnlyList<string> LocalBranches,
    IReadOnlyList<GitChangedFileItem> ChangedFiles,
    IReadOnlyList<GitCommitLogItem> RecentCommits);

/// <summary>
/// Provides extended Git repository inspection and actions: branch listing, branch switching (git checkout),
/// changed files status, HEAD file content retrieval for side-by-side visual diffing, staging, and commit log.
/// </summary>
public static class GitIntegrationService
{
    public static async Task<GitRepositoryOverview> InspectRepositoryAsync(string? directoryPath, CancellationToken ct = default)
    {
        var repoRoot = GitStatusService.FindRepositoryRoot(directoryPath);
        if (string.IsNullOrEmpty(repoRoot))
        {
            return new GitRepositoryOverview(false, "", "", [], [], []);
        }

        return await Task.Run(() =>
        {
            var status = GitStatusService.GetStatusForDirectoryAsync(repoRoot, ct).GetAwaiter().GetResult();
            string currentBranch = string.IsNullOrEmpty(status.BranchName) ? "HEAD" : status.BranchName;

            var branches = new List<string>();
            var branchRes = RunGit(repoRoot, "branch --format=%(refname:short)");
            if (branchRes.ExitCode == 0)
            {
                foreach (var line in branchRes.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var b = line.Trim();
                    if (!string.IsNullOrEmpty(b))
                        branches.Add(b);
                }
            }
            if (!branches.Contains(currentBranch, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentBranch))
                branches.Insert(0, currentBranch);

            var changedFiles = new List<GitChangedFileItem>();
            var statusRes = RunGit(repoRoot, "status --porcelain=v1 -uall");
            if (statusRes.ExitCode == 0)
            {
                foreach (var line in statusRes.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length < 4) continue;
                    char idxStatus = line[0];
                    char wtStatus = line[1];
                    string rel = line[3..].Trim().Trim('"');
                    int arrow = rel.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrow >= 0) rel = rel[(arrow + 4)..].Trim().Trim('"');

                    bool untracked = idxStatus == '?' && wtStatus == '?';
                    bool staged = idxStatus is not ' ' and not '?';
                    string badge = untracked ? "U" : $"{(idxStatus == ' ' ? "" : idxStatus.ToString())}{(wtStatus == ' ' ? "" : wtStatus.ToString())}";
                    if (string.IsNullOrEmpty(badge)) badge = "M";

                    string full = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    changedFiles.Add(new GitChangedFileItem(rel, full, badge, staged, untracked));
                }
            }

            var commits = new List<GitCommitLogItem>();
            var logRes = RunGit(repoRoot, "log -n 25 --pretty=format:%h|%an|%cr|%s");
            if (logRes.ExitCode == 0)
            {
                foreach (var line in logRes.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|', 4);
                    if (parts.Length == 4)
                        commits.Add(new GitCommitLogItem(parts[0], parts[1], parts[2], parts[3]));
                }
            }

            return new GitRepositoryOverview(true, repoRoot, currentBranch, branches, changedFiles, commits);
        }, ct);
    }

    public static async Task<(bool Success, string Message)> CheckoutBranchAsync(string repoRoot, string branchName)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(branchName))
            return (false, "Ungültiger Branch-Name.");

        return await Task.Run(() =>
        {
            var res = RunGit(repoRoot, $"checkout \"{branchName.Trim()}\"");
            string msg = string.IsNullOrWhiteSpace(res.StdOut) ? res.StdErr : res.StdOut;
            return (res.ExitCode == 0, msg.Trim());
        });
    }

    public static async Task<string> GetHeadFileContentAsync(string repoRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(relativePath))
            return "";

        return await Task.Run(() =>
        {
            string gitPath = relativePath.Replace('\\', '/');
            var res = RunGit(repoRoot, $"show \"HEAD:{gitPath}\"");
            return res.ExitCode == 0 ? res.StdOut : "";
        });
    }

    public static async Task<(bool Success, string Message)> StageFileAsync(string repoRoot, string relativePath)
    {
        return await Task.Run(() =>
        {
            var res = RunGit(repoRoot, $"add -- \"{relativePath.Replace('\\', '/')}\"");
            return (res.ExitCode == 0, (res.StdOut + " " + res.StdErr).Trim());
        });
    }

    public static async Task<(bool Success, string Message)> DiscardFileChangesAsync(string repoRoot, string relativePath)
    {
        return await Task.Run(() =>
        {
            var res = RunGit(repoRoot, $"checkout -- \"{relativePath.Replace('\\', '/')}\"");
            return (res.ExitCode == 0, (res.StdOut + " " + res.StdErr).Trim());
        });
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGit(string workingDir, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return (-1, "", "Git konnte nicht gestartet werden.");

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);
            return (proc.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}

