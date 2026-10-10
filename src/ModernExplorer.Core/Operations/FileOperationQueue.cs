using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ModernExplorer.Core.Operations;

/// <summary>
/// Queue for file operations: one sequential worker per target volume,
/// different volumes run in parallel. The UI thread is never blocked.
/// </summary>
public sealed class FileOperationQueue : IAsyncDisposable
{
    private sealed record QueuedOp(OperationHandle Handle);

    private readonly ConcurrentDictionary<string, (Channel<QueuedOp> Channel, Task Worker)> _workers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly FileOperationExecutor _executor;
    private readonly ConcurrentDictionary<Guid, OperationHandle> _active = new();

    public FileOperationQueue(IConflictResolver resolver) => _executor = new FileOperationExecutor(resolver);

    public IReadOnlyCollection<OperationHandle> Active => _active.Values.ToList();

    /// <summary>Raised when a new operation is enqueued (any thread).</summary>
    public event EventHandler<OperationHandle>? Enqueued;

    /// <summary>Raised when an operation finishes (worker thread).</summary>
    public event EventHandler<OperationHandle>? Finished;

    public OperationHandle Enqueue(FileOperation op)
    {
        var handle = new OperationHandle(op);
        var key = VolumeKey(op.Destination ?? op.Sources[0]);
        var worker = _workers.GetOrAdd(key, _ => StartWorker());
        _active[op.Id] = handle;
        worker.Channel.Writer.TryWrite(new QueuedOp(handle));
        Enqueued?.Invoke(this, handle);
        return handle;
    }

    /// <summary>Executes the operation and awaits its completion (for simple callers and unit tests).</summary>
    public async Task<OperationHandle> RunAsync(FileOperation op)
    {
        var h = Enqueue(op);
        await h.Completion.ConfigureAwait(false);
        return h;
    }

    internal static string VolumeKey(string path)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(path)) ?? path; }
        catch { return path; }
    }

    private (Channel<QueuedOp>, Task) StartWorker()
    {
        var ch = Channel.CreateUnbounded<QueuedOp>(new UnboundedChannelOptions { SingleReader = true });
        var task = Task.Factory.StartNew(async () =>
        {
            await foreach (var item in ch.Reader.ReadAllAsync())
            {
                var h = item.Handle;
                try
                {
                    if (h.Token.IsCancellationRequested) { h.MarkCancelled(); continue; }
                    h.Start();
                    _executor.Execute(h);
                    h.Complete();
                }
                catch (OperationCanceledException) { h.MarkCancelled(); }
                catch (Exception ex) { h.MarkFailed(ex); }
                finally
                {
                    _active.TryRemove(h.Operation.Id, out _);
                    Finished?.Invoke(this, h);
                }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        return (ch, task);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var h in _active.Values) h.Cancel();
        foreach (var (ch, _) in _workers.Values) ch.Writer.TryComplete();
        await Task.WhenAll(_workers.Values.Select(w => w.Worker)).ConfigureAwait(false);
    }
}

