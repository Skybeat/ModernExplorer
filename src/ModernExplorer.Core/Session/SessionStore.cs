using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernExplorer.Core.Session;

/// <summary>
/// Loads and saves the session atomically (temp file + replace). <see cref="RequestSave"/> is debounced.
/// </summary>
public sealed class SessionStore : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _file;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;
    private readonly object _lock = new();
    private Func<SessionState>? _pendingSnapshot;

    public SessionStore(string file, TimeSpan? debounce = null)
    {
        _file = file;
        _delay = debounce ?? TimeSpan.FromMilliseconds(250);
        _timer = new Timer(_ => FlushPending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string FilePath => _file;

    public SessionState? Load()
    {
        try
        {
            if (!File.Exists(_file)) return null;
            var json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Back up corrupted session file so it is not lost on the next save.
            try { File.Copy(_file, _file + ".corrupt", overwrite: true); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Schedules a snapshot function to be invoked after the debounce period.
    /// The function must be thread-safe (runs on the timer thread) —
    /// therefore the application passes pre-captured snapshots.
    /// </summary>
    public void RequestSave(Func<SessionState> snapshot)
    {
        lock (_lock)
        {
            _pendingSnapshot = snapshot;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void FlushPending()
    {
        Func<SessionState>? snap;
        lock (_lock)
        {
            snap = _pendingSnapshot;
            _pendingSnapshot = null;
        }
        if (snap is not null) Save(snap());
    }

    public void Save(SessionState state)
    {
        lock (_lock)
        {
            _pendingSnapshot = null;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            WriteUnlocked(state);
        }
    }

    private void WriteUnlocked(SessionState state)
    {
        state.SavedAt = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
        if (File.Exists(_file)) File.Replace(tmp, _file, null, ignoreMetadataErrors: true);
        else File.Move(tmp, _file);
    }

    public void CancelPending()
    {
        lock (_lock)
        {
            _pendingSnapshot = null;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        FlushPending();
    }
}

