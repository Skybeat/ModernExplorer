using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernExplorer.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> atomically as a JSON file.
/// </summary>
public sealed class SettingsStore(string filePath)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _lock = new();

    public string FilePath { get; } = filePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return (loaded ?? new AppSettings()).CloneNormalized();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            var normalized = settings.CloneNormalized();
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(normalized, JsonOptions));
            if (File.Exists(FilePath))
                File.Replace(tmp, FilePath, null, ignoreMetadataErrors: true);
            else
                File.Move(tmp, FilePath);
        }
    }
}

