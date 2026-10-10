using ModernExplorer.Core.Commands;

namespace ModernExplorer.Core.Input;

public enum KeyRouteResult
{
    /// <summary>No matching binding — pass the key press through to the control/OS.</summary>
    NotHandled,
    /// <summary>A command was executed.</summary>
    Executed,
    /// <summary>Part of a multi-chord sequence — waiting for the next key.</summary>
    PendingChord,
}

/// <summary>
/// Central keyboard router. Invoked by the UI for every KeyDown event at the window root.
/// </summary>
public sealed class KeyRouter(KeyBindingStore store, ICommandRegistry commands, TimeProvider? time = null)
{
    public static readonly TimeSpan ChordTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<KeyChord> _pending = [];
    private DateTimeOffset _pendingSince;

    /// <summary>Currently pending chord prefix sequence (for display in the status bar).</summary>
    public string? PendingDisplay => _pending.Count == 0 ? null : KeySequence.Format(_pending) + " …";

    public event EventHandler? PendingChanged;

    public KeyRouteResult Handle(KeyChord chord, FocusContext ctx)
    {
        if (ReservedKeys.IsReserved(chord))
        {
            ClearPending();
            return KeyRouteResult.NotHandled;
        }

        if (_pending.Count > 0 && _time.GetUtcNow() - _pendingSince > ChordTimeout)
            ClearPending();

        var hadPending = _pending.Count > 0;
        _pending.Add(chord);

        var candidates = store.MatchPrefix(_pending)
            .Where(b => b.When.Evaluate(ctx))
            .Where(b => !ctx.IsTextInput || b.AllowInTextInput)
            .Where(b => commands.CanExecute(b.Command, ctx))
            .ToList();

        if (candidates.Count == 0)
        {
            ClearPending();
            // Swallow invalid continuation of a chord sequence instead of forwarding it to the control.
            return hadPending ? KeyRouteResult.Executed : KeyRouteResult.NotHandled;
        }

        var exact = candidates.Where(b => b.Sequence.Count == _pending.Count)
                              .OrderByDescending(b => b.When.Specificity)
                              .FirstOrDefault();
        var longer = candidates.Any(b => b.Sequence.Count > _pending.Count);

        if (exact is not null && !longer)
        {
            ClearPending();
            commands.Execute(exact.Command, exact.Args, ctx);
            return KeyRouteResult.Executed;
        }

        if (!longer && exact is null)
        {
            ClearPending();
            return KeyRouteResult.NotHandled;
        }

        // Longer chord sequences exist -> wait (exact binding is deferred in favor of the chord).
        _pendingSince = _time.GetUtcNow();
        PendingChanged?.Invoke(this, EventArgs.Empty);
        return KeyRouteResult.PendingChord;
    }

    public void ClearPending()
    {
        if (_pending.Count == 0) return;
        _pending.Clear();
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }
}

