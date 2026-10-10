using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModernExplorer.Core.Input;

/// <summary>
/// Loads embedded default key bindings and overlays them with the user's configuration file.
/// A <c>-</c> prefix on the command removes an existing binding.
/// </summary>
public sealed class KeyBindingStore
{
    private volatile IReadOnlyList<KeyBinding> _bindings = [];
    private readonly List<string> _warnings = [];

    public IReadOnlyList<KeyBinding> Bindings => _bindings;
    public IReadOnlyList<string> Warnings => _warnings;

    public event EventHandler? Changed;

    public static KeyBindingStore LoadDefaultsWithUserOverride(string? userFile)
    {
        var store = new KeyBindingStore();
        store.Reload(userFile);
        return store;
    }

    public void Reload(string? userFile)
    {
        _warnings.Clear();
        var list = new List<KeyBinding>();
        Apply(list, ReadEmbedded("ModernExplorer.Core.default-keybindings.json"), "default");

        if (userFile is not null && File.Exists(userFile))
        {
            try { Apply(list, File.ReadAllText(userFile), Path.GetFileName(userFile)); }
            catch (Exception ex) { _warnings.Add($"{userFile}: {ex.Message}"); }
        }

        _bindings = list;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Loads exclusively from JSON text layers (for unit tests).</summary>
    public void LoadFromJson(params string[] jsonLayers)
    {
        _warnings.Clear();
        var list = new List<KeyBinding>();
        foreach (var j in jsonLayers) Apply(list, j, "json");
        _bindings = list;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IEnumerable<KeyBinding> MatchPrefix(IReadOnlyList<KeyChord> pressed) =>
        _bindings.Where(b => b.StartsWith(pressed));

    public KeyBinding? FindFirstForCommand(string command, FocusContext? ctx = null) =>
        _bindings.FirstOrDefault(b => b.Command == command && (ctx is null || b.When.Evaluate(ctx)));

    private void Apply(List<KeyBinding> list, string json, string source)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new FormatException("Root muss ein Objekt sein");

        if (root["bindings"] is not JsonArray arr) return;

        foreach (var item in arr.OfType<JsonObject>())
        {
            var key = item["key"]?.GetValue<string>();
            var command = item["command"]?.GetValue<string>();
            if (key is null || command is null) { _warnings.Add($"{source}: 'key' und 'command' erforderlich"); continue; }

            IReadOnlyList<KeyChord> seq;
            WhenExpression when;
            try
            {
                seq = KeySequence.Parse(key);
                when = WhenExpression.Parse(item["when"]?.GetValue<string>());
            }
            catch (FormatException ex) { _warnings.Add($"{source}: {ex.Message}"); continue; }

            if (command.StartsWith('-'))
            {
                var target = command[1..];
                list.RemoveAll(b => b.Command == target && b.Sequence.SequenceEqual(seq));
                continue;
            }

            if (seq.Any(ReservedKeys.IsReserved))
            {
                _warnings.Add($"{source}: '{key}' ist eine reservierte Windows-Tastenkombination und wird ignoriert");
                continue;
            }

            var args = item["args"] is JsonObject a ? (JsonObject)a.DeepClone() : null;
            var allowText = item["allowInTextInput"]?.GetValue<bool>() ?? false;

            // Same sequence + same context -> user binding replaces default.
            list.RemoveAll(b => b.Sequence.SequenceEqual(seq) && b.When.Source == when.Source);
            list.Add(new KeyBinding(seq, command, when, args, allowText));
        }
    }

    private static string ReadEmbedded(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                      ?? throw new InvalidOperationException($"Ressource {name} fehlt");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

