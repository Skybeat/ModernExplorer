using System.Text;
using System.Text.RegularExpressions;

namespace ModernExplorer.Core.FileSystem;

/// <summary>Validation of file and folder names according to Windows naming rules.</summary>
public static partial class NameValidator
{
    public const int MaxComponentLength = 255;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };

    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Normalizes (NFC, trim) a user-entered name.</summary>
    public static string Normalize(string name) => name.Normalize(NormalizationForm.FormC).Trim();

    /// <summary>Returns an error message or <c>null</c> if the name is valid.</summary>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Name darf nicht leer sein.";
        if (name is "." or "..") return "„.“ und „..“ sind nicht erlaubt.";
        if (name.Length > MaxComponentLength) return $"Name ist zu lang (max. {MaxComponentLength} Zeichen).";

        foreach (var c in name)
        {
            if (c < 32) return "Steuerzeichen sind nicht erlaubt.";
            if (Array.IndexOf(InvalidChars, c) >= 0) return $"Ungültiges Zeichen: {c}  (nicht erlaubt: < > : \" / \\ | ? *)";
        }

        if (name.EndsWith('.') || name.EndsWith(' ')) return "Name darf nicht mit Punkt oder Leerzeichen enden.";

        var stem = name.Split('.')[0].TrimEnd();
        if (ReservedNames.Contains(stem)) return $"„{stem}“ ist ein reservierter Gerätename.";

        return null;
    }

    /// <summary>
    /// Validates a multi-component relative path (e.g. "a\b\c" for nested folders).
    /// </summary>
    public static string? ValidateRelativePath(string relative)
    {
        var parts = relative.Split(['\\', '/'], StringSplitOptions.None);
        if (parts.Length == 0) return "Name darf nicht leer sein.";
        foreach (var p in parts)
        {
            var err = Validate(p);
            if (err is not null) return err;
        }
        return null;
    }

    /// <summary><c>"Report.txt", 2</c> -> <c>"Report (2).txt"</c>. Replaces any existing numeric suffix.</summary>
    public static string WithSuffix(string name, int n)
    {
        var ext = Path.GetExtension(name);
        if (ext.Length == name.Length) ext = ""; // ".gitignore" -> entire name is stem
        var stem = SuffixRegex().Replace(name[..^ext.Length], "");
        return $"{stem} ({n}){ext}";
    }

    /// <summary>Finds an available name in the directory (case-insensitive check).</summary>
    public static string MakeUnique(string directory, string name)
    {
        if (!Exists(Path.Combine(directory, name))) return name;
        for (int i = 2; i < 10_000; i++)
        {
            var candidate = WithSuffix(name, i);
            if (!Exists(Path.Combine(directory, candidate))) return candidate;
        }
        throw new IOException("Kein freier Name gefunden.");
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Length of the name without extension — used for pre-selecting text in the name dialog.</summary>
    public static int StemLength(string name)
    {
        var ext = Path.GetExtension(name);
        return ext.Length == 0 || ext.Length == name.Length ? name.Length : name.Length - ext.Length;
    }

    [GeneratedRegex(@"\s\(\d+\)$")]
    private static partial Regex SuffixRegex();
}
