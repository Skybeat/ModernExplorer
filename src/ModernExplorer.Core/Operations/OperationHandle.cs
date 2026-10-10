using System.Diagnostics;

namespace ModernExplorer.Core.Operations;

/// <summary>
/// Control and observation handle for a queued operation.
/// Events are raised on the worker thread — the UI must dispatch them as needed.
/// </summary>
public sealed class OperationHandle
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _resume = new(true);
    private readonly Stopwatch _sw = new();
    private long _lastReportTicks;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _errors = [];

    public OperationHandle(FileOperation op) => Operation = op;

    public FileOperation Operation { get; }
    public OperationStatus Status { get; private set; } = OperationStatus.Queued;
    public OperationProgress Progress { get; private set; } = OperationProgress.Empty;
    public IReadOnlyList<string> Errors { get { lock (_errors) return [.. _errors]; } }
    public Exception? Error { get; private set; }
    public CancellationToken Token => _cts.Token;
    public Task Completion => _completion.Task;

    /// <summary>Paths created by the operation (e.g. copy targets) — used for post-operation selection.</summary>
    public List<string> CreatedPaths { get; } = [];

    public event EventHandler? ProgressChanged;
    public event EventHandler? StatusChanged;

    public void Cancel()
    {
        _cts.Cancel();
        _resume.Set();
    }

    public void Pause()
    {
        if (Status != OperationStatus.Running) return;
        _resume.Reset();
        SetStatus(OperationStatus.Paused);
    }

    public void Resume()
    {
        if (Status != OperationStatus.Paused) return;
        _resume.Set();
        SetStatus(OperationStatus.Running);
    }

    /// <summary>Blocks the worker while paused. Throws if cancelled.</summary>
    internal void WaitIfPaused()
    {
        _resume.Wait(_cts.Token);
        _cts.Token.ThrowIfCancellationRequested();
    }

    internal void Start()
    {
        _sw.Start();
        SetStatus(OperationStatus.Running);
    }

    internal void AddError(string message)
    {
        lock (_errors) _errors.Add(message);
    }

    /// <summary>Reports progress, throttled to a maximum of 10 updates/s (unless <paramref name="force"/> is true).</summary>
    internal void Report(long bytesDone, long bytesTotal, int filesDone, int filesTotal, string current, bool force = false)
    {
        var now = _sw.ElapsedTicks;
        if (!force && now - _lastReportTicks < Stopwatch.Frequency / 10) return;
        _lastReportTicks = now;
        var secs = _sw.Elapsed.TotalSeconds;
        Progress = new OperationProgress(bytesDone, bytesTotal, filesDone, filesTotal, current,
                                         secs > 0.2 ? bytesDone / secs : 0);
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void Complete()
    {
        SetStatus(OperationStatus.Completed);
        _completion.TrySetResult();
    }

    internal void MarkCancelled()
    {
        SetStatus(OperationStatus.Cancelled);
        _completion.TrySetResult();
    }

    internal void MarkFailed(Exception ex)
    {
        Error = ex;
        SetStatus(OperationStatus.Failed);
        _completion.TrySetResult();
    }

    private void SetStatus(OperationStatus s)
    {
        Status = s;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

