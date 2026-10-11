using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace ModernExplorer.Core.Favorites;

/// <summary>
/// Represents a bookmarked directory or remote URI in the Favorites Bar (Ctrl+D).
/// </summary>
public sealed record FavoriteItem(
    string Label,
    string Path,
    string IconGlyph = "⭐")
{
    public string ToolTipText => $"{Label} ({Path})";
}

/// <summary>
/// Persists and manages user favorite/bookmark folders for the Favorites Bar (Ctrl+D).
/// </summary>
public sealed class FavoritesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;

    public ObservableCollection<FavoriteItem> Items { get; } = [];

    public event EventHandler? FavoritesChanged;

    public FavoritesStore(string? filePath = null)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModernExplorer");
        Directory.CreateDirectory(baseDir);
        _filePath = filePath ?? Path.Combine(baseDir, "favorites.json");
        LoadOrCreateDefaults();
    }

    public bool IsBookmarked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var norm = NormalizePath(path);
        return Items.Any(i => string.Equals(NormalizePath(i.Path), norm, StringComparison.OrdinalIgnoreCase));
    }

    public bool ToggleBookmark(string path, string? customLabel = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var norm = NormalizePath(path);
        var existing = Items.FirstOrDefault(i => string.Equals(NormalizePath(i.Path), norm, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Items.Remove(existing);
            Save();
            FavoritesChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        AddBookmark(path, customLabel);
        return true;
    }

    public void AddBookmark(string path, string? customLabel = null, string? iconGlyph = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var norm = NormalizePath(path);
        if (Items.Any(i => string.Equals(NormalizePath(i.Path), norm, StringComparison.OrdinalIgnoreCase)))
            return;

        string label = !string.IsNullOrWhiteSpace(customLabel)
            ? customLabel.Trim()
            : DeriveDefaultLabel(norm);

        string glyph = iconGlyph ?? (norm.Contains("://", StringComparison.Ordinal) ? "🌐" : "⭐");
        Items.Add(new FavoriteItem(label, norm, glyph));
        Save();
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveBookmark(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var norm = NormalizePath(path);
        var existing = Items.FirstOrDefault(i => string.Equals(NormalizePath(i.Path), norm, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Items.Remove(existing);
            Save();
            FavoritesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RenameBookmark(string path, string newLabel)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(newLabel))
            return;

        var norm = NormalizePath(path);
        int idx = -1;
        for (int i = 0; i < Items.Count; i++)
        {
            if (string.Equals(NormalizePath(Items[i].Path), norm, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        if (idx >= 0)
        {
            Items[idx] = Items[idx] with { Label = newLabel.Trim() };
            Save();
            FavoritesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(Items.ToList(), JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    private void LoadOrCreateDefaults()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<List<FavoriteItem>>(json, JsonOptions);
                if (loaded is { Count: > 0 })
                {
                    Items.Clear();
                    foreach (var item in loaded)
                        Items.Add(item);
                    return;
                }
            }
        }
        catch { }

        Items.Clear();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string downloads = Path.Combine(userProfile, "Downloads");

        if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
            Items.Add(new FavoriteItem("Desktop", desktop, "🖥"));
        if (!string.IsNullOrEmpty(downloads) && Directory.Exists(downloads))
            Items.Add(new FavoriteItem("Downloads", downloads, "📥"));
        if (!string.IsNullOrEmpty(documents) && Directory.Exists(documents))
            Items.Add(new FavoriteItem("Dokumente", documents, "📄"));
        if (!string.IsNullOrEmpty(userProfile) && Directory.Exists(userProfile))
            Items.Add(new FavoriteItem("Benutzer", userProfile, "👤"));

        Save();
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal))
            return trimmed.TrimEnd('/');

        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed;
        }
    }

    private static string DeriveDefaultLabel(string path)
    {
        if (path.Contains("://", StringComparison.Ordinal))
            return path;

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}

