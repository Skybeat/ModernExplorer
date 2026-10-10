using System.IO.Enumeration;
using System.Runtime.CompilerServices;

namespace ModernExplorer.Core.FileSystem;

/// <summary>
/// Fast directory enumeration based on <see cref="FileSystemEnumerable{TResult}"/>
/// (internally uses NtQueryDirectoryFile with a large buffer — comparable to FIND_FIRST_EX_LARGE_FETCH,
/// without extra stat calls per entry). Yields batches so the UI can insert items in chunks.
/// </summary>
public static class DirectoryEnumerator
{
    public const int DefaultBatchSize = 500;

    public static async IAsyncEnumerable<IReadOnlyList<FileEntry>> EnumerateBatchesAsync(
        string path,
        bool includeHidden,
        int batchSize = DefaultBatchSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = System.Threading.Channels.Channel.CreateBounded<IReadOnlyList<FileEntry>>(4);

        var producer = Task.Run(() =>
        {
            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
                    BufferSize = 64 * 1024,
                };

                var enumerable = new FileSystemEnumerable<FileEntry>(path, Transform, options);
                var batch = new List<FileEntry>(batchSize);
                foreach (var entry in enumerable)
                {
                    ct.ThrowIfCancellationRequested();
                    batch.Add(entry);
                    if (batch.Count >= batchSize)
                    {
                        channel.Writer.WriteAsync(batch, ct).AsTask().GetAwaiter().GetResult();
                        batch = new List<FileEntry>(batchSize);
                    }
                }
                if (batch.Count > 0) channel.Writer.WriteAsync(batch, ct).AsTask().GetAwaiter().GetResult();
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, ct);

        await foreach (var b in channel.Reader.ReadAllAsync(ct))
            yield return b;

        await producer;
    }

    private static FileEntry Transform(ref FileSystemEntry e)
    {
        var isDir = e.IsDirectory;
        return new FileEntry(
            e.FileName.ToString(),
            e.ToFullPath(),
            isDir,
            isDir ? 0 : e.Length,
            e.LastWriteTimeUtc.LocalDateTime,
            e.Attributes);
    }
}

