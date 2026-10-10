using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernExplorer.Core.FileSystem;

public sealed class FileTemplate
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string DefaultName { get; set; } = "Neue Datei.txt";
    public string? Content { get; set; }
    public string? SourceFile { get; set; }
    public string Encoding { get; set; } = "utf-8";

    public override string ToString() => Label;

    /// <summary>Replaces placeholders and returns rendered text + cursor offset (if <c>${cursor}</c> is present).</summary>
    public (string Text, int? CursorOffset) Render(TemplateContext ctx)
    {
        var text = Content ?? "";
        text = text.Replace("${baseName}", Path.GetFileNameWithoutExtension(ctx.FileName))
                   .Replace("${fileName}", ctx.FileName)
                   .Replace("${user}", ctx.User);

        // ${date:FORMAT}
        int idx;
        while ((idx = text.IndexOf("${date:", StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf('}', idx);
            if (end < 0) break;
            var fmt = text[(idx + 7)..end];
            text = string.Concat(text.AsSpan(0, idx), ctx.Now.ToString(fmt), text.AsSpan(end + 1));
        }
        text = text.Replace("${date}", ctx.Now.ToString("yyyy-MM-dd"));

        int? cursor = null;
        var c = text.IndexOf("${cursor}", StringComparison.Ordinal);
        if (c >= 0) { cursor = c; text = text.Remove(c, "${cursor}".Length); }
        return (text, cursor);
    }

    public async Task WriteAsync(Stream target, TemplateContext ctx, string templatesDirectory, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(SourceFile))
        {
            var src = Path.IsPathRooted(SourceFile) ? SourceFile : Path.Combine(templatesDirectory, SourceFile);
            await using var s = File.OpenRead(src);
            await s.CopyToAsync(target, ct);
            return;
        }

        var (text, _) = Render(ctx);
        if (text.Length == 0 && Encoding != "utf-8-bom") return;

        Encoding enc = Encoding.ToLowerInvariant() switch
        {
            "utf-8-bom" => new UTF8Encoding(true),
            "utf-16" or "unicode" => new UnicodeEncoding(false, true),
            _ => new UTF8Encoding(false),
        };
        var preamble = enc.GetPreamble();
        if (preamble.Length > 0) await target.WriteAsync(preamble, ct);
        await target.WriteAsync(enc.GetBytes(text), ct);
    }
}

public sealed record TemplateContext(string FileName, DateTime Now, string User);

public sealed class TemplateCatalog
{
    public int Version { get; set; } = 1;
    public string Default { get; set; } = "text";
    public List<FileTemplate> Templates { get; set; } = [];
}

/// <summary>Loads embedded default templates and overlays them with the user's <c>templates.json</c>.</summary>
public sealed class TemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public IReadOnlyList<FileTemplate> Templates { get; private set; } = [];
    public FileTemplate Default { get; private set; } = new() { Id = "text", Label = "Textdatei" };
    public string TemplatesDirectory { get; }

    public TemplateStore(string templatesDirectory) => TemplatesDirectory = templatesDirectory;

    public void Load(string? userFile)
    {
        var catalog = Deserialize(ReadEmbedded()) ?? new TemplateCatalog();
        var byId = catalog.Templates.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var order = catalog.Templates.Select(t => t.Id).ToList();
        var defaultId = catalog.Default;

        if (userFile is not null && File.Exists(userFile))
        {
            try
            {
                var user = Deserialize(File.ReadAllText(userFile));
                if (user is not null)
                {
                    foreach (var t in user.Templates)
                    {
                        if (!byId.ContainsKey(t.Id)) order.Add(t.Id);
                        byId[t.Id] = t;
                    }
                    if (!string.IsNullOrEmpty(user.Default)) defaultId = user.Default;
                }
            }
            catch (JsonException) { /* Malformed user file -> keep defaults */ }
        }

        Templates = order.Select(id => byId[id]).ToList();
        Default = byId.TryGetValue(defaultId, out var d) ? d : Templates.FirstOrDefault() ?? Default;
    }

    private static TemplateCatalog? Deserialize(string json) => JsonSerializer.Deserialize<TemplateCatalog>(json, JsonOptions);

    private static string ReadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ModernExplorer.Core.default-templates.json")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

