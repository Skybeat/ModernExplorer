namespace ModernExplorer.Core.FileSystem;

public enum FsChangeKind { Created, Deleted, Changed, Renamed, Overflow }

public sealed record FsChange(FsChangeKind Kind, string FullPath, string? OldFullPath = null);

/// <summary>Collects events and delivers them in batches after a quiet period.</summary>
public sealed class Debouncer<T> : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly object _lock = new();
    private readonly List<T> _items = [];
    private readonly Timer _timer;

    public event Action<IReadOnlyList<T>>? Batch;

    public Debouncer(TimeSpan delay)
    {
        _delay = delay;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _items.Add(item);
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Flush()
    {
        T[] items;
        lock (_lock)
        {
            if (_items.Count == 0) return;
            items = [.. _items];
            _items.Clear();
        }
        Batch?.Invoke(items);
    }

    public void Dispose() => _timer.Dispose();
}

/// <summary>
/// One <see cref="FileSystemWatcher"/> per path, reference-counted across all tabs.
/// Events are debounced by 150 ms and delivered in batches (on a background thread!).
/// </summary>
public sealed class WatcherHub : IDisposable
{
    private sealed class Entry : IDisposable
    {
        public required FileSystemWatcher Watcher { get; init; }
        public required Debouncer<FsChange> Debouncer { get; init; }
        public int Refs;
        public void Dispose() { Watcher.Dispose(); Debouncer.Dispose(); }
    }

    private readonly Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _debounce;

    public WatcherHub(TimeSpan? debounce = null) => _debounce = debounce ?? TimeSpan.FromMilliseconds(150);

    public int ActiveWatcherCount { get { lock (_map) return _map.Count; } }

    public IDisposable Subscribe(string path, Action<IReadOnlyList<FsChange>> onBatch)
    {
        lock (_map)
        {
            if (!_map.TryGetValue(path, out var e))
            {
                FileSystemWatcher w;
                try
                {
                    w = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                       NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Attributes,
                        InternalBufferSize = 64 * 1024,
                    };
                }
                catch (ArgumentException)
                {
                    return EmptyDisposable.Instance; // Path does not exist / cannot be watched
                }

                var d = new Debouncer<FsChange>(_debounce);
                w.Created += (_, a) => d.Add(new FsChange(FsChangeKind.Created, a.FullPath));
                w.Deleted += (_, a) => d.Add(new FsChange(FsChangeKind.Deleted, a.FullPath));
                w.Changed += (_, a) => d.Add(new FsChange(FsChangeKind.Changed, a.FullPath));
                w.Renamed += (_, a) => d.Add(new FsChange(FsChangeKind.Renamed, a.FullPath, a.OldFullPath));
                w.Error += (_, _) => d.Add(new FsChange(FsChangeKind.Overflow, path));
                try { w.EnableRaisingEvents = true; }
                catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
                {
                    w.Dispose(); d.Dispose();
                    return EmptyDisposable.Instance;
                }

                e = new Entry { Watcher = w, Debouncer = d };
                _map[path] = e;
            }

            e.Debouncer.Batch += onBatch;
            e.Refs++;
            return new Subscription(this, path, onBatch);
        }
    }

    private void Release(string path, Action<IReadOnlyList<FsChange>> onBatch)
    {
        lock (_map)
        {
            if (!_map.TryGetValue(path, out var e)) return;
            e.Debouncer.Batch -= onBatch;
            if (--e.Refs <= 0)
            {
                _map.Remove(path);
                e.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_map)
        {
            foreach (var e in _map.Values) e.Dispose();
            _map.Clear();
        }
    }

    private sealed class Subscription(WatcherHub hub, string path, Action<IReadOnlyList<FsChange>> cb) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) hub.Release(path, cb);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}

