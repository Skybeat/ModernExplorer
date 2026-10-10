namespace ModernExplorer.Core.Input;

/// <summary>
/// Normalized key names. The app translates platform-specific key codes (VirtualKey) into these names.
/// </summary>
public static class KeyNames
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = "Back", ["Rücktaste"] = "Back", ["Back"] = "Back",
        ["Esc"] = "Escape", ["Escape"] = "Escape",
        ["Return"] = "Enter", ["Enter"] = "Enter", ["Eingabe"] = "Enter",
        ["Del"] = "Delete", ["Entf"] = "Delete", ["Delete"] = "Delete",
        ["Ins"] = "Insert", ["Einfg"] = "Insert", ["Insert"] = "Insert",
        ["PgUp"] = "PageUp", ["PageUp"] = "PageUp", ["Bild↑"] = "PageUp",
        ["PgDn"] = "PageDown", ["PageDown"] = "PageDown", ["Bild↓"] = "PageDown",
        ["Space"] = "Space", ["Leertaste"] = "Space",
        ["Tab"] = "Tab", ["Home"] = "Home", ["Pos1"] = "Home", ["End"] = "End", ["Ende"] = "End",
        ["Left"] = "Left", ["Right"] = "Right", ["Up"] = "Up", ["Down"] = "Down",
        ["Add"] = "Add", ["Plus"] = "Add", ["NumPlus"] = "Add",
        ["Subtract"] = "Subtract", ["Minus"] = "Subtract", ["NumMinus"] = "Subtract",
        ["Multiply"] = "Multiply", ["NumMultiply"] = "Multiply",
        ["Divide"] = "Divide", ["Apps"] = "Apps", ["Menu"] = "Apps",
    };

    public static string? Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Aliases.TryGetValue(name, out var a)) return a;

        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c.ToString();
            return null;
        }

        if ((name[0] == 'F' || name[0] == 'f') && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return "F" + f;

        // "D1" (similar to VirtualKey.Number1) / "NumPad1"
        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && name.Length == 7 && char.IsDigit(name[6]))
            return "NumPad" + name[6];

        return null;
    }
}

