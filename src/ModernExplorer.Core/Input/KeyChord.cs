namespace ModernExplorer.Core.Input;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

/// <summary>
/// A single key combination, e.g. <c>Ctrl+Shift+T</c>.
/// Key names are normalized in a UI-independent manner (see <see cref="KeyNames"/>).
/// </summary>
public readonly record struct KeyChord(string Key, KeyModifiers Modifiers)
{
    public static KeyChord Parse(string text)
    {
        if (!TryParse(text, out var chord, out var error))
            throw new FormatException(error);
        return chord;
    }

    public static bool TryParse(string text, out KeyChord chord, out string? error)
    {
        chord = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "Leere Tastenkombination"; return false; }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var mods = KeyModifiers.None;
        string? key = null;

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            // "Ctrl++" -> last part is empty => Plus key
            if (p.Length == 0 && i == parts.Length - 1) p = "Add";

            var mod = ParseModifier(p);
            if (mod != KeyModifiers.None && i < parts.Length - 1)
            {
                mods |= mod;
                continue;
            }
            if (i != parts.Length - 1) { error = $"Unbekannter Modifikator '{p}' in '{text}'"; return false; }

            key = KeyNames.Normalize(p);
            if (key is null) { error = $"Unbekannte Taste '{p}' in '{text}'"; return false; }
        }

        if (key is null) { error = $"Keine Taste in '{text}'"; return false; }
        chord = new KeyChord(key, mods);
        return true;
    }

    private static KeyModifiers ParseModifier(string p) => p.ToLowerInvariant() switch
    {
        "ctrl" or "strg" or "control" => KeyModifiers.Ctrl,
        "shift" or "umschalt" => KeyModifiers.Shift,
        "alt" => KeyModifiers.Alt,
        "win" or "meta" => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };

    public override string ToString()
    {
        var s = "";
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) s += "Ctrl+";
        if (Modifiers.HasFlag(KeyModifiers.Shift)) s += "Shift+";
        if (Modifiers.HasFlag(KeyModifiers.Alt)) s += "Alt+";
        if (Modifiers.HasFlag(KeyModifiers.Win)) s += "Win+";
        return s + Key;
    }

    /// <summary>Formatted representation for UI display.</summary>
    public string ToDisplayString() => ToString()
        .Replace("Ctrl+", "Strg+")
        .Replace("Back", "Rücktaste")
        .Replace("PageUp", "Bild↑")
        .Replace("PageDown", "Bild↓");
}

/// <summary>Parses multi-chord sequences such as <c>"Ctrl+K Ctrl+P"</c>.</summary>
public static class KeySequence
{
    public static IReadOnlyList<KeyChord> Parse(string text)
    {
        var chords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Select(KeyChord.Parse)
                         .ToArray();
        if (chords.Length == 0) throw new FormatException("Leere Tastensequenz");
        return chords;
    }

    public static string Format(IEnumerable<KeyChord> seq) => string.Join(' ', seq.Select(c => c.ToDisplayString()));
}

