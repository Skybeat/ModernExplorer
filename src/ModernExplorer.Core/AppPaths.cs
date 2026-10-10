namespace ModernExplorer.Core;

/// <summary>Central storage locations for configuration and application state.</summary>
public sealed class AppPaths
{
    public string RoamingDir { get; }
    public string LocalDir { get; }

    public AppPaths(string? roamingOverride = null, string? localOverride = null)
    {
        RoamingDir = roamingOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModernExplorer");
        LocalDir = localOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModernExplorer");
    }

    public string SettingsFile => Path.Combine(RoamingDir, "settings.json");
    public string KeyBindingsFile => Path.Combine(RoamingDir, "keybindings.json");
    public string TemplatesFile => Path.Combine(RoamingDir, "templates.json");
    public string TemplatesDir => Path.Combine(RoamingDir, "templates");
    public string SessionFile => Path.Combine(LocalDir, "session.json");
    public string LogDir => Path.Combine(LocalDir, "logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RoamingDir);
        Directory.CreateDirectory(LocalDir);
        Directory.CreateDirectory(TemplatesDir);
        Directory.CreateDirectory(LogDir);

        if (!File.Exists(KeyBindingsFile))
        {
            File.WriteAllText(KeyBindingsFile,
                """
                {
                  // Custom key bindings. Override the built-in defaults.
                  // Example:  { "key": "Ctrl+N", "command": "file.createNew", "when": "fileList" }
                  // Prefix "-" removes a default: { "key": "Delete", "command": "-fileOp.delete", "when": "fileList" }
                  "version": 1,
                  "bindings": [
                  ]
                }
                """);
        }
    }
}

