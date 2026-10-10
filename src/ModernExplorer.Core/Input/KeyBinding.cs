using System.Text.Json.Nodes;

namespace ModernExplorer.Core.Input;

public sealed record KeyBinding(
    IReadOnlyList<KeyChord> Sequence,
    string Command,
    WhenExpression When,
    JsonObject? Args,
    bool AllowInTextInput)
{
    public string KeyText => KeySequence.Format(Sequence);

    public bool StartsWith(IReadOnlyList<KeyChord> prefix)
    {
        if (prefix.Count > Sequence.Count) return false;
        for (int i = 0; i < prefix.Count; i++)
            if (!Sequence[i].Equals(prefix[i])) return false;
        return true;
    }
}

/// <summary>
/// Key combinations that must never be overridden by the application (Windows system shortcuts).
/// </summary>
public static class ReservedKeys
{
    private static readonly HashSet<KeyChord> Reserved =
    [
        KeyChord.Parse("Alt+Tab"),
        KeyChord.Parse("Alt+F4"),
        KeyChord.Parse("Ctrl+Alt+Delete"),
        KeyChord.Parse("Ctrl+Shift+Escape"),
        KeyChord.Parse("Shift+F10"),
        KeyChord.Parse("Apps"),
        KeyChord.Parse("Alt+Escape"),
        KeyChord.Parse("Alt+Space"),
    ];

    public static bool IsReserved(KeyChord chord) =>
        chord.Modifiers.HasFlag(KeyModifiers.Win) || Reserved.Contains(chord);
}

