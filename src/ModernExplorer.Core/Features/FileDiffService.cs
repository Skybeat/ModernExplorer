using System.IO;
using System.Text;

namespace ModernExplorer.Core.Features;

public enum DiffLineKind
{
    Unchanged = 0,
    Added = 1,
    Removed = 2,
    Modified = 3,
}

public sealed record DiffLinePair(
    int? LeftLineNumber,
    int? RightLineNumber,
    string LeftText,
    string RightText,
    DiffLineKind Kind,
    int HunkIndex);

public sealed record DiffHunk(
    int HunkIndex,
    int FirstRowIndex,
    int RowCount,
    DiffLineKind DominantKind,
    int LeftStartIndex,
    int LeftCount,
    int RightStartIndex,
    int RightCount,
    IReadOnlyList<string> LeftLines,
    IReadOnlyList<string> RightLines);

public sealed record FileDiffResult(
    string LeftPath,
    string RightPath,
    string LeftRawText,
    string RightRawText,
    IReadOnlyList<DiffLinePair> Rows,
    IReadOnlyList<DiffHunk> Hunks,
    int AddedCount,
    int RemovedCount,
    int ModifiedCount,
    int UnchangedCount,
    bool IgnoreWhitespace)
{
    public bool AreIdentical => Hunks.Count == 0 && AddedCount == 0 && RemovedCount == 0 && ModifiedCount == 0;
    public int TotalDifferences => Hunks.Count;
}

/// <summary>
/// Side-by-side line diff engine with LCS alignment, hunk grouping, block navigation,
/// and interactive block merging (Left → Right or Right → Left).
/// </summary>
public static class FileDiffService
{
    public static async Task<FileDiffResult> CompareFilesAsync(
        string leftPath,
        string rightPath,
        bool ignoreWhitespace = false,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            string leftText = File.Exists(leftPath) ? ReadTextSafe(leftPath) : "";
            string rightText = File.Exists(rightPath) ? ReadTextSafe(rightPath) : "";
            return ComputeDiff(leftText, rightText, leftPath, rightPath, ignoreWhitespace);
        }, ct);
    }

    public static FileDiffResult ComputeDiff(
        string leftText,
        string rightText,
        string leftPath = "",
        string rightPath = "",
        bool ignoreWhitespace = false)
    {
        leftText ??= "";
        rightText ??= "";

        var leftLines = SplitLines(leftText);
        var rightLines = SplitLines(rightText);

        var matches = ComputeLcsMatches(leftLines, rightLines, ignoreWhitespace);

        var rows = new List<DiffLinePair>();
        var hunks = new List<DiffHunk>();

        int leftIdx = 0;
        int rightIdx = 0;
        int addedCount = 0;
        int removedCount = 0;
        int modifiedCount = 0;
        int unchangedCount = 0;

        foreach (var (matchL, matchR) in matches)
        {
            if (leftIdx < matchL || rightIdx < matchR)
            {
                AppendDifferenceBlock(
                    leftLines, rightLines,
                    leftIdx, matchL - leftIdx,
                    rightIdx, matchR - rightIdx,
                    rows, hunks,
                    ref addedCount, ref removedCount, ref modifiedCount);
            }

            if (matchL < leftLines.Length && matchR < rightLines.Length)
            {
                rows.Add(new DiffLinePair(
                    matchL + 1,
                    matchR + 1,
                    leftLines[matchL],
                    rightLines[matchR],
                    DiffLineKind.Unchanged,
                    HunkIndex: -1));
                unchangedCount++;
            }

            leftIdx = matchL + 1;
            rightIdx = matchR + 1;
        }

        if (leftIdx < leftLines.Length || rightIdx < rightLines.Length)
        {
            AppendDifferenceBlock(
                leftLines, rightLines,
                leftIdx, leftLines.Length - leftIdx,
                rightIdx, rightLines.Length - rightIdx,
                rows, hunks,
                ref addedCount, ref removedCount, ref modifiedCount);
        }

        return new FileDiffResult(
            leftPath,
            rightPath,
            leftText,
            rightText,
            rows,
            hunks,
            addedCount,
            removedCount,
            modifiedCount,
            unchangedCount,
            ignoreWhitespace);
    }

    /// <summary>
    /// Copies the lines of the specified hunk from the Left side into the Right side and recomputes the diff.
    /// </summary>
    public static FileDiffResult ApplyHunkLeftToRight(FileDiffResult diff, int hunkIndex)
    {
        var hunk = diff.Hunks.FirstOrDefault(h => h.HunkIndex == hunkIndex);
        if (hunk is null)
            return diff;

        var rightLines = SplitLines(diff.RightRawText).ToList();
        int removeStart = Math.Clamp(hunk.RightStartIndex, 0, rightLines.Count);
        int removeCount = Math.Clamp(hunk.RightCount, 0, rightLines.Count - removeStart);
        if (removeCount > 0)
            rightLines.RemoveRange(removeStart, removeCount);

        rightLines.InsertRange(removeStart, hunk.LeftLines);
        string updatedRight = string.Join(Environment.NewLine, rightLines);
        return ComputeDiff(diff.LeftRawText, updatedRight, diff.LeftPath, diff.RightPath, diff.IgnoreWhitespace);
    }

    /// <summary>
    /// Copies the lines of the specified hunk from the Right side into the Left side and recomputes the diff.
    /// </summary>
    public static FileDiffResult ApplyHunkRightToLeft(FileDiffResult diff, int hunkIndex)
    {
        var hunk = diff.Hunks.FirstOrDefault(h => h.HunkIndex == hunkIndex);
        if (hunk is null)
            return diff;

        var leftLines = SplitLines(diff.LeftRawText).ToList();
        int removeStart = Math.Clamp(hunk.LeftStartIndex, 0, leftLines.Count);
        int removeCount = Math.Clamp(hunk.LeftCount, 0, leftLines.Count - removeStart);
        if (removeCount > 0)
            leftLines.RemoveRange(removeStart, removeCount);

        leftLines.InsertRange(removeStart, hunk.RightLines);
        string updatedLeft = string.Join(Environment.NewLine, leftLines);
        return ComputeDiff(updatedLeft, diff.RightRawText, diff.LeftPath, diff.RightPath, diff.IgnoreWhitespace);
    }

    private static void AppendDifferenceBlock(
        string[] leftLines,
        string[] rightLines,
        int leftStart,
        int leftCount,
        int rightStart,
        int rightCount,
        List<DiffLinePair> rows,
        List<DiffHunk> hunks,
        ref int addedCount,
        ref int removedCount,
        ref int modifiedCount)
    {
        int hunkIndex = hunks.Count;
        int firstRowIndex = rows.Count;
        int maxRows = Math.Max(leftCount, rightCount);

        var leftSlice = new List<string>(leftCount);
        for (int i = 0; i < leftCount; i++) leftSlice.Add(leftLines[leftStart + i]);

        var rightSlice = new List<string>(rightCount);
        for (int i = 0; i < rightCount; i++) rightSlice.Add(rightLines[rightStart + i]);

        for (int i = 0; i < maxRows; i++)
        {
            bool hasL = i < leftCount;
            bool hasR = i < rightCount;

            DiffLineKind kind;
            if (hasL && hasR)
            {
                kind = DiffLineKind.Modified;
                modifiedCount++;
            }
            else if (hasL)
            {
                kind = DiffLineKind.Removed;
                removedCount++;
            }
            else
            {
                kind = DiffLineKind.Added;
                addedCount++;
            }

            rows.Add(new DiffLinePair(
                hasL ? leftStart + i + 1 : null,
                hasR ? rightStart + i + 1 : null,
                hasL ? leftLines[leftStart + i] : "",
                hasR ? rightLines[rightStart + i] : "",
                kind,
                hunkIndex));
        }

        DiffLineKind dominant = leftCount > 0 && rightCount > 0
            ? DiffLineKind.Modified
            : leftCount > 0 ? DiffLineKind.Removed : DiffLineKind.Added;

        hunks.Add(new DiffHunk(
            hunkIndex,
            firstRowIndex,
            maxRows,
            dominant,
            leftStart,
            leftCount,
            rightStart,
            rightCount,
            leftSlice,
            rightSlice));
    }

    private static List<(int LeftIndex, int RightIndex)> ComputeLcsMatches(
        string[] leftLines,
        string[] rightLines,
        bool ignoreWhitespace)
    {
        int n = leftLines.Length;
        int m = rightLines.Length;
        if (n == 0 || m == 0)
            return [];

        string Norm(string s) => ignoreWhitespace ? NormalizeWhitespace(s) : s;

        // Trim common prefix and suffix for speed on large files
        int prefix = 0;
        while (prefix < n && prefix < m && string.Equals(Norm(leftLines[prefix]), Norm(rightLines[prefix]), StringComparison.Ordinal))
            prefix++;

        int suffix = 0;
        while (suffix < (n - prefix) && suffix < (m - prefix) &&
               string.Equals(Norm(leftLines[n - 1 - suffix]), Norm(rightLines[m - 1 - suffix]), StringComparison.Ordinal))
            suffix++;

        var result = new List<(int, int)>();
        for (int i = 0; i < prefix; i++)
            result.Add((i, i));

        int midN = n - prefix - suffix;
        int midM = m - prefix - suffix;

        if (midN > 0 && midM > 0)
        {
            if ((long)midN * midM <= 4_000_000)
            {
                var dp = new int[midN + 1, midM + 1];
                for (int i = midN - 1; i >= 0; i--)
                {
                    string lVal = Norm(leftLines[prefix + i]);
                    for (int j = midM - 1; j >= 0; j--)
                    {
                        if (string.Equals(lVal, Norm(rightLines[prefix + j]), StringComparison.Ordinal))
                            dp[i, j] = 1 + dp[i + 1, j + 1];
                        else
                            dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                    }
                }

                int x = 0, y = 0;
                while (x < midN && y < midM)
                {
                    if (string.Equals(Norm(leftLines[prefix + x]), Norm(rightLines[prefix + y]), StringComparison.Ordinal))
                    {
                        result.Add((prefix + x, prefix + y));
                        x++;
                        y++;
                    }
                    else if (dp[x + 1, y] >= dp[x, y + 1])
                    {
                        x++;
                    }
                    else
                    {
                        y++;
                    }
                }
            }
            else
            {
                // Fallback greedy window matching for very large files
                int y = 0;
                for (int x = 0; x < midN && y < midM; x++)
                {
                    string lVal = Norm(leftLines[prefix + x]);
                    int limit = Math.Min(midM, y + 80);
                    for (int scan = y; scan < limit; scan++)
                    {
                        if (string.Equals(lVal, Norm(rightLines[prefix + scan]), StringComparison.Ordinal))
                        {
                            result.Add((prefix + x, prefix + scan));
                            y = scan + 1;
                            break;
                        }
                    }
                }
            }
        }

        for (int i = 0; i < suffix; i++)
            result.Add((n - suffix + i, m - suffix + i));

        return result;
    }

    private static string NormalizeWhitespace(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        var sb = new StringBuilder(line.Length);
        bool inWs = false;
        foreach (char c in line.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inWs)
                {
                    sb.Append(' ');
                    inWs = true;
                }
            }
            else
            {
                sb.Append(c);
                inWs = false;
            }
        }
        return sb.ToString();
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
                   .Replace('\r', '\n')
                   .Split('\n');
    }

    private static string ReadTextSafe(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"[Error reading {Path.GetFileName(path)}: {ex.Message}]";
        }
    }
}

