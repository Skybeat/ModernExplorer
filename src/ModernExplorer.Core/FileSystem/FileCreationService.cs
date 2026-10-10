using System.Text;

namespace ModernExplorer.Core.FileSystem;

public enum CreateStatus { Created, Invalid, AccessDenied, Failed }

public sealed record CreateResult(CreateStatus Status, string? FullPath, string? Error)
{
    public bool Success => Status == CreateStatus.Created;
    public static CreateResult Ok(string path) => new(CreateStatus.Created, path, null);
    public static CreateResult Invalid(string error) => new(CreateStatus.Invalid, null, error);
}

/// <summary>
/// File and folder creation (F7 / Shift+F7) with atomic collision handling.
/// </summary>
public sealed class FileCreationService(TemplateStore templates)
{
    public TemplateStore Templates => templates;

    /// <summary>
    /// Creates a file atomically (<see cref="FileMode.CreateNew"/>). If the name already exists and
    /// <paramref name="autoRename"/> is true, "Name (2).ext" etc. is tried.
    /// </summary>
    public async Task<CreateResult> CreateFileAsync(string directory, string rawName, FileTemplate? template,
        bool autoRename = false, CancellationToken ct = default)
    {
        var name = NameValidator.Normalize(rawName);
        var error = NameValidator.Validate(name);
        if (error is not null) return CreateResult.Invalid(error);

        template ??= templates.Default;

        for (int attempt = 1; attempt < 1000; attempt++)
        {
            var candidate = attempt == 1 ? name : NameValidator.WithSuffix(name, attempt);
            var full = Path.Combine(directory, candidate);

            if (Directory.Exists(full))
            {
                if (!autoRename) return CreateResult.Invalid($"Ein Ordner „{candidate}“ existiert bereits.");
                continue;
            }

            try
            {
                await using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await template.WriteAsync(fs, new TemplateContext(candidate, DateTime.Now, Environment.UserName),
                        templates.TemplatesDirectory, ct);
                }
                return CreateResult.Ok(full);
            }
            catch (IOException) when (File.Exists(full))
            {
                if (!autoRename) return CreateResult.Invalid($"„{candidate}“ existiert bereits.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return new CreateResult(CreateStatus.AccessDenied, full, "Zugriff verweigert – Administratorrechte erforderlich? " + ex.Message);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException)
            {
                return new CreateResult(CreateStatus.Failed, full, ex.Message);
            }
        }
        return CreateResult.Invalid("Kein freier Name gefunden.");
    }

    /// <summary>
    /// Creates a (potentially nested) folder. "a\b\c" creates all levels.
    /// Returns an error with the existing path on collision.
    /// </summary>
    public CreateResult CreateFolder(string directory, string rawRelative)
    {
        var relative = NameValidator.Normalize(rawRelative).Replace('/', '\\').Trim('\\');
        var parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
        relative = string.Join('\\', parts);

        var error = NameValidator.ValidateRelativePath(relative);
        if (error is not null) return CreateResult.Invalid(error);

        var full = Path.Combine(directory, relative);
        if (Directory.Exists(full)) return new CreateResult(CreateStatus.Invalid, full, $"Ordner „{relative}“ existiert bereits.");
        if (File.Exists(full)) return CreateResult.Invalid($"Eine Datei „{relative}“ existiert bereits.");

        try
        {
            Directory.CreateDirectory(full);
            // Return path of the top-most newly created level (so it gets selected in the list).
            return CreateResult.Ok(Path.Combine(directory, parts[0]));
        }
        catch (UnauthorizedAccessException ex) { return new CreateResult(CreateStatus.AccessDenied, full, ex.Message); }
        catch (IOException ex) { return new CreateResult(CreateStatus.Failed, full, ex.Message); }
    }

    /// <summary>Suggested name for the creation dialog: free name based on the template default.</summary>
    public string SuggestName(string directory, FileTemplate template) =>
        NameValidator.MakeUnique(directory, template.DefaultName);
}

