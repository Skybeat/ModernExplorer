using System.ComponentModel;
using System.Globalization;

namespace ModernExplorer.Core.Localization;

/// <summary>
/// Represents a selectable UI language option in the settings dialog.
/// </summary>
public sealed record LanguageOption(string Code, string DisplayName, string NativeName);

/// <summary>
/// Provides runtime localization for the application with automatic system-language detection
/// and instant live-switching across all views and view models.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public const string SystemLanguageCode = "system";

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new(SystemLanguageCode, "System Default (Auto)", "Auto"),
        new("de", "Deutsch (German)", "Deutsch"),
        new("en", "English", "English"),
        new("es", "Español (Spanish)", "Español"),
        new("fr", "Français (French)", "Français"),
        new("it", "Italiano (Italian)", "Italiano"),
        new("nl", "Nederlands (Dutch)", "Nederlands"),
        new("pl", "Polski (Polish)", "Polski"),
        new("pt", "Português (Portuguese)", "Português"),
        new("ja", "日本語 (Japanese)", "日本語"),
        new("zh", "中文 (Chinese)", "中文"),
    ];

    public static LocalizationService Instance { get; } = new();

    private string _configuredLanguage = SystemLanguageCode;
    private string _resolvedLanguage = DetectSystemLanguageCode();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    /// <summary>
    /// Gets the configured language setting ("system" or a two-letter ISO language code).
    /// </summary>
    public string ConfiguredLanguage => _configuredLanguage;

    /// <summary>
    /// Gets the effective two-letter ISO language code currently active ("de", "en", "es", etc.).
    /// </summary>
    public string EffectiveLanguage => _resolvedLanguage;

    /// <summary>
    /// Indexer for convenient XAML and code-behind lookup by key.
    /// </summary>
    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string Format(string key, params object[] args)
    {
        var pattern = Instance.Get(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    public static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return SystemLanguageCode;

        var trimmed = code.Trim().ToLowerInvariant();
        if (trimmed == SystemLanguageCode)
            return SystemLanguageCode;

        if (trimmed.Length > 2 && trimmed.Contains('-'))
            trimmed = trimmed.Split('-')[0];

        return SupportedLanguages.Any(l => l.Code == trimmed) ? trimmed : SystemLanguageCode;
    }

    public static string DetectSystemLanguageCode(CultureInfo? culture = null)
    {
        var uiCulture = culture ?? CultureInfo.InstalledUICulture;
        var twoLetter = uiCulture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (SupportedLanguages.Any(l => l.Code == twoLetter && l.Code != SystemLanguageCode))
            return twoLetter;

        var currentTwoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (SupportedLanguages.Any(l => l.Code == currentTwoLetter && l.Code != SystemLanguageCode))
            return currentTwoLetter;

        return "en";
    }

    public void SetLanguage(string? languageCode, CultureInfo? systemCultureOverride = null)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        var resolved = normalized == SystemLanguageCode
            ? DetectSystemLanguageCode(systemCultureOverride)
            : normalized;

        bool changed = !string.Equals(_configuredLanguage, normalized, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(_resolvedLanguage, resolved, StringComparison.OrdinalIgnoreCase);

        _configuredLanguage = normalized;
        _resolvedLanguage = resolved;

        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<LanguageOption> GetLocalizedLanguageOptions()
    {
        var sysLabel = Get("Lang.SystemDefault");
        var list = new List<LanguageOption>(SupportedLanguages.Count)
        {
            new(SystemLanguageCode, sysLabel, "Auto")
        };
        list.AddRange(SupportedLanguages.Where(l => l.Code != SystemLanguageCode));
        return list;
    }

    public string Get(string key)
    {
        if (Translations.TryGetValue(_resolvedLanguage, out var dict) && dict.TryGetValue(key, out var val))
            return val;

        if (Translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
            return enVal;

        if (Translations.TryGetValue("de", out var deDict) && deDict.TryGetValue(key, out var deVal))
            return deVal;

        return key;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "Systemsprache (Automatisch)",
            // MainWindow Toolbar & Menus
            ["Toolbar.NewFile"] = "Neue Datei",
            ["Toolbar.NewFile.Tip"] = "Neue Datei aus Vorlage anlegen (Shift+F4)",
            ["Toolbar.NewFolder"] = "Neuer Ordner",
            ["Toolbar.NewFolder.Tip"] = "Neuen Ordner anlegen (F7)",
            ["Toolbar.Copy"] = "Kopieren",
            ["Toolbar.Copy.Tip"] = "Markierte Elemente in das andere Panel kopieren (F5)",
            ["Toolbar.Move"] = "Verschieben",
            ["Toolbar.Move.Tip"] = "Markierte Elemente in das andere Panel verschieben (F6)",
            ["Toolbar.Delete"] = "Löschen",
            ["Toolbar.Delete.Tip"] = "In den Papierkorb verschieben (F8 / Entf) · Shift+F8 endgültig",
            ["Toolbar.Search"] = "Suche",
            ["Toolbar.Search.Tip"] = "Dateisuche im aktuellen Ordner ein-/ausblenden (Strg+F)",
            ["Toolbar.SplitView"] = "Split-Ansicht",
            ["Toolbar.SingleView"] = "Einzel-Ansicht",
            ["Toolbar.SplitToggle.Tip"] = "Zwischen Einzel- und Zwei-Fenster-Ansicht umschalten (F10)",
            ["Toolbar.Hidden"] = "Versteckte",
            ["Toolbar.Hidden.Tip"] = "Versteckte und Systemdateien ein-/ausblenden (Strg+H)",
            ["Toolbar.Terminal"] = "Terminal",
            ["Toolbar.Terminal.Tip"] = "Terminal im aktuellen Pfad öffnen (F9)",
            ["Toolbar.Settings"] = "Einstellungen",
            ["Toolbar.Settings.Tip"] = "Einstellungen öffnen (Theme, Sprache, Schriftgrößen, Session)",
            ["Toolbar.Shortcuts.Tip"] = "Tastenkürzel-Übersicht anzeigen (F1)",
            // Panel & Columns
            ["Panel.NewTab.Tip"] = "Neuer Tab (Strg+T)",
            ["Panel.Back.Tip"] = "Zurück (Alt+Links)",
            ["Panel.Forward.Tip"] = "Vorwärts (Alt+Rechts)",
            ["Panel.Up.Tip"] = "Übergeordneter Ordner (Backspace)",
            ["Panel.Refresh.Tip"] = "Aktualisieren (Strg+R)",
            ["Panel.FilterPlaceholder"] = "Schnellfilter (Strg+F)…",
            ["Col.Name"] = "Name",
            ["Col.Extension"] = "Typ",
            ["Col.Size"] = "Größe",
            ["Col.Modified"] = "Geändert",
            // Search Bar
            ["Search.PatternPlaceholder"] = "Dateiname / Muster (z.B. *.cs oder bericht)",
            ["Search.ContentPlaceholder"] = "Textinhalt (optional)",
            ["Search.Recursive"] = "Unterordner",
            ["Search.Regex"] = "Regex",
            ["Search.Start"] = "Suchen",
            // Function Key Bar
            ["Fn.F1"] = "Hilfe",
            ["Fn.F2"] = "Umbenennen",
            ["Fn.F3"] = "Ansehen",
            ["Fn.F4"] = "Bearbeiten",
            ["Fn.F5"] = "Kopieren",
            ["Fn.F6"] = "Bewegen",
            ["Fn.F7"] = "Ordner+",
            ["Fn.F8"] = "Löschen",
            ["Fn.F9"] = "Terminal",
            ["Fn.F10"] = "Beenden",
            // Settings Window Categories
            ["Settings.Title"] = "ModernExplorer – Einstellungen",
            ["Settings.Header"] = "Einstellungen",
            ["Settings.SubHeader"] = "Alle Änderungen werden sofort live angewendet und gespeichert",
            ["Settings.Close"] = "Schließen",
            ["Settings.Categories"] = "KATEGORIEN",
            ["Settings.Cat.Appearance"] = "🌙  Darstellung & Design",
            ["Settings.Cat.Behavior"] = "📁  Dateiliste & Verhalten",
            ["Settings.Cat.Colors"] = "🎨  Farben & Hervorhebung",
            ["Settings.Cat.Preview"] = "🖼  Bildvorschau",
            ["Settings.Cat.Fonts"] = "🔤  Schriftgrößen",
            ["Settings.Cat.Session"] = "💾  Sitzung & Updates",
            ["Settings.Language.Title"] = "Sprache / Language",
            ["Settings.Language.Desc"] = "Wählt die Sprache der Benutzeroberfläche (Standard: Systemsprache automatisch erkennen).",
            ["Settings.Update.Title"] = "Automatische Update-Prüfung (GitHub Releases)",
            ["Settings.Update.Desc"] = "Prüft bei jedem Start automatisch auf GitHub, ob eine neuere Version verfügbar ist.",
            ["Settings.Update.CheckNow"] = "Jetzt auf Updates prüfen",
            // Update Dialog
            ["Update.AvailableTitle"] = "Neues Update verfügbar",
            ["Update.AvailableHeading"] = "Eine neue Version von ModernExplorer ist verfügbar!",
            ["Update.CurrentVersion"] = "Installierte Version:",
            ["Update.LatestVersion"] = "Neue Version:",
            ["Update.ReleaseNotes"] = "Änderungen in diesem Release:",
            ["Update.UpdateNow"] = "Jetzt aktualisieren",
            ["Update.OpenGitHub"] = "Auf GitHub ansehen",
            ["Update.Later"] = "Später",
            ["Update.UpToDateTitle"] = "Keine Updates verfügbar",
            ["Update.UpToDateMsg"] = "Sie verwenden bereits die aktuellste Version ({0}).",
            ["Update.DownloadingTitle"] = "Update wird heruntergeladen…",
            ["Update.DownloadingMsg"] = "Das Update {0} wird heruntergeladen und installiert. Die App startet danach automatisch neu.",
            ["Update.ErrorTitle"] = "Update-Prüfung fehlgeschlagen",
        },
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "System Default (Auto)",
            ["Toolbar.NewFile"] = "New File",
            ["Toolbar.NewFile.Tip"] = "Create new file from template (Shift+F4)",
            ["Toolbar.NewFolder"] = "New Folder",
            ["Toolbar.NewFolder.Tip"] = "Create new folder (F7)",
            ["Toolbar.Copy"] = "Copy",
            ["Toolbar.Copy.Tip"] = "Copy selected items to the other panel (F5)",
            ["Toolbar.Move"] = "Move",
            ["Toolbar.Move.Tip"] = "Move selected items to the other panel (F6)",
            ["Toolbar.Delete"] = "Delete",
            ["Toolbar.Delete.Tip"] = "Move to Recycle Bin (F8 / Del) · Shift+F8 permanent",
            ["Toolbar.Search"] = "Search",
            ["Toolbar.Search.Tip"] = "Toggle file search in current folder (Ctrl+F)",
            ["Toolbar.SplitView"] = "Split View",
            ["Toolbar.SingleView"] = "Single View",
            ["Toolbar.SplitToggle.Tip"] = "Toggle between single and dual-pane view (F10)",
            ["Toolbar.Hidden"] = "Hidden",
            ["Toolbar.Hidden.Tip"] = "Show or hide hidden and system files (Ctrl+H)",
            ["Toolbar.Terminal"] = "Terminal",
            ["Toolbar.Terminal.Tip"] = "Open terminal in current folder (F9)",
            ["Toolbar.Settings"] = "Settings",
            ["Toolbar.Settings.Tip"] = "Open settings (Theme, Language, Font Sizes, Session)",
            ["Toolbar.Shortcuts.Tip"] = "Show keyboard shortcuts reference (F1)",
            ["Panel.NewTab.Tip"] = "New Tab (Ctrl+T)",
            ["Panel.Back.Tip"] = "Back (Alt+Left)",
            ["Panel.Forward.Tip"] = "Forward (Alt+Right)",
            ["Panel.Up.Tip"] = "Parent Folder (Backspace)",
            ["Panel.Refresh.Tip"] = "Refresh (Ctrl+R)",
            ["Panel.FilterPlaceholder"] = "Quick filter (Ctrl+F)…",
            ["Col.Name"] = "Name",
            ["Col.Extension"] = "Type",
            ["Col.Size"] = "Size",
            ["Col.Modified"] = "Modified",
            ["Search.PatternPlaceholder"] = "File name / pattern (e.g. *.cs or report)",
            ["Search.ContentPlaceholder"] = "Text content (optional)",
            ["Search.Recursive"] = "Subfolders",
            ["Search.Regex"] = "Regex",
            ["Search.Start"] = "Search",
            ["Fn.F1"] = "Help",
            ["Fn.F2"] = "Rename",
            ["Fn.F3"] = "View",
            ["Fn.F4"] = "Edit",
            ["Fn.F5"] = "Copy",
            ["Fn.F6"] = "Move",
            ["Fn.F7"] = "MkDir",
            ["Fn.F8"] = "Delete",
            ["Fn.F9"] = "Terminal",
            ["Fn.F10"] = "Quit",
            ["Settings.Title"] = "ModernExplorer – Settings",
            ["Settings.Header"] = "Settings",
            ["Settings.SubHeader"] = "All changes are applied live and saved automatically",
            ["Settings.Close"] = "Close",
            ["Settings.Categories"] = "CATEGORIES",
            ["Settings.Cat.Appearance"] = "🌙  Appearance & Design",
            ["Settings.Cat.Behavior"] = "📁  File List & Behavior",
            ["Settings.Cat.Colors"] = "🎨  Colors & Highlighting",
            ["Settings.Cat.Preview"] = "🖼  Image Preview",
            ["Settings.Cat.Fonts"] = "🔤  Font Sizes",
            ["Settings.Cat.Session"] = "💾  Session & Updates",
            ["Settings.Language.Title"] = "Language / Sprache",
            ["Settings.Language.Desc"] = "Select the user interface language (Default: detect system language automatically).",
            ["Settings.Update.Title"] = "Automatic Update Check (GitHub Releases)",
            ["Settings.Update.Desc"] = "Automatically check GitHub on startup for newer releases.",
            ["Settings.Update.CheckNow"] = "Check for Updates Now",
            ["Update.AvailableTitle"] = "Update Available",
            ["Update.AvailableHeading"] = "A new version of ModernExplorer is available!",
            ["Update.CurrentVersion"] = "Installed version:",
            ["Update.LatestVersion"] = "Latest version:",
            ["Update.ReleaseNotes"] = "Release notes:",
            ["Update.UpdateNow"] = "Update Now",
            ["Update.OpenGitHub"] = "View on GitHub",
            ["Update.Later"] = "Later",
            ["Update.UpToDateTitle"] = "Up to Date",
            ["Update.UpToDateMsg"] = "You are already running the latest version ({0}).",
            ["Update.DownloadingTitle"] = "Downloading Update…",
            ["Update.DownloadingMsg"] = "Downloading and installing update {0}. The application will restart automatically.",
            ["Update.ErrorTitle"] = "Update Check Failed",
        },
        ["es"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "Idioma del sistema (Automático)",
            ["Toolbar.NewFile"] = "Nuevo archivo",
            ["Toolbar.NewFolder"] = "Nueva carpeta",
            ["Toolbar.Copy"] = "Copiar",
            ["Toolbar.Move"] = "Mover",
            ["Toolbar.Delete"] = "Eliminar",
            ["Toolbar.Search"] = "Buscar",
            ["Toolbar.SplitView"] = "Vista dividida",
            ["Toolbar.SingleView"] = "Vista única",
            ["Toolbar.Hidden"] = "Ocultos",
            ["Toolbar.Terminal"] = "Terminal",
            ["Toolbar.Settings"] = "Ajustes",
            ["Panel.FilterPlaceholder"] = "Filtro rápido (Ctrl+F)…",
            ["Col.Name"] = "Nombre",
            ["Col.Extension"] = "Tipo",
            ["Col.Size"] = "Tamaño",
            ["Col.Modified"] = "Modificado",
            ["Search.PatternPlaceholder"] = "Nombre / patrón (ej. *.cs)",
            ["Search.ContentPlaceholder"] = "Contenido de texto (opcional)",
            ["Search.Recursive"] = "Subcarpetas",
            ["Search.Regex"] = "Regex",
            ["Search.Start"] = "Buscar",
            ["Fn.F1"] = "Ayuda",
            ["Fn.F2"] = "Renombrar",
            ["Fn.F3"] = "Ver",
            ["Fn.F4"] = "Editar",
            ["Fn.F5"] = "Copiar",
            ["Fn.F6"] = "Mover",
            ["Fn.F7"] = "Carpeta+",
            ["Fn.F8"] = "Eliminar",
            ["Fn.F9"] = "Terminal",
            ["Fn.F10"] = "Salir",
            ["Settings.Title"] = "ModernExplorer – Ajustes",
            ["Settings.Header"] = "Ajustes",
            ["Settings.SubHeader"] = "Todos los cambios se aplican y guardan automáticamente",
            ["Settings.Close"] = "Cerrar",
            ["Settings.Categories"] = "CATEGORÍAS",
            ["Settings.Cat.Appearance"] = "🌙  Apariencia y Diseño",
            ["Settings.Cat.Behavior"] = "📁  Lista y Comportamiento",
            ["Settings.Cat.Colors"] = "🎨  Colores y Resaltado",
            ["Settings.Cat.Preview"] = "🖼  Vista previa",
            ["Settings.Cat.Fonts"] = "🔤  Tamaños de fuente",
            ["Settings.Cat.Session"] = "💾  Sesión y Actualizaciones",
            ["Settings.Language.Title"] = "Idioma / Language",
            ["Settings.Language.Desc"] = "Seleccione el idioma de la interfaz (Predeterminado: idioma del sistema).",
            ["Settings.Update.Title"] = "Comprobación automática de actualizaciones",
            ["Settings.Update.Desc"] = "Comprueba automáticamente al iniciar si hay una nueva versión en GitHub.",
            ["Settings.Update.CheckNow"] = "Buscar actualizaciones ahora",
            ["Update.AvailableTitle"] = "Actualización disponible",
            ["Update.AvailableHeading"] = "¡Hay una nueva versión de ModernExplorer disponible!",
            ["Update.CurrentVersion"] = "Versión instalada:",
            ["Update.LatestVersion"] = "Nueva versión:",
            ["Update.ReleaseNotes"] = "Notas de la versión:",
            ["Update.UpdateNow"] = "Actualizar ahora",
            ["Update.OpenGitHub"] = "Ver en GitHub",
            ["Update.Later"] = "Más tarde",
            ["Update.UpToDateTitle"] = "Actualizado",
            ["Update.UpToDateMsg"] = "Ya está utilizando la versión más reciente ({0}).",
        },
        ["fr"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "Langue du système (Auto)",
            ["Toolbar.NewFile"] = "Nouveau fichier",
            ["Toolbar.NewFolder"] = "Nouveau dossier",
            ["Toolbar.Copy"] = "Copier",
            ["Toolbar.Move"] = "Déplacer",
            ["Toolbar.Delete"] = "Supprimer",
            ["Toolbar.Search"] = "Rechercher",
            ["Toolbar.SplitView"] = "Vue double",
            ["Toolbar.SingleView"] = "Vue simple",
            ["Toolbar.Hidden"] = "Cachés",
            ["Toolbar.Terminal"] = "Terminal",
            ["Toolbar.Settings"] = "Paramètres",
            ["Panel.FilterPlaceholder"] = "Filtre rapide (Ctrl+F)…",
            ["Col.Name"] = "Nom",
            ["Col.Extension"] = "Type",
            ["Col.Size"] = "Taille",
            ["Col.Modified"] = "Modifié",
            ["Search.PatternPlaceholder"] = "Nom / motif (ex. *.cs)",
            ["Search.ContentPlaceholder"] = "Contenu texte (optionnel)",
            ["Search.Recursive"] = "Sous-dossiers",
            ["Search.Regex"] = "Regex",
            ["Search.Start"] = "Chercher",
            ["Fn.F1"] = "Aide",
            ["Fn.F2"] = "Renommer",
            ["Fn.F3"] = "Voir",
            ["Fn.F4"] = "Éditer",
            ["Fn.F5"] = "Copier",
            ["Fn.F6"] = "Déplacer",
            ["Fn.F7"] = "Dossier+",
            ["Fn.F8"] = "Supprimer",
            ["Fn.F9"] = "Terminal",
            ["Fn.F10"] = "Quitter",
            ["Settings.Title"] = "ModernExplorer – Paramètres",
            ["Settings.Header"] = "Paramètres",
            ["Settings.SubHeader"] = "Toutes les modifications sont appliquées et enregistrées en direct",
            ["Settings.Close"] = "Fermer",
            ["Settings.Categories"] = "CATÉGORIES",
            ["Settings.Cat.Appearance"] = "🌙  Apparence et Design",
            ["Settings.Cat.Behavior"] = "📁  Fichiers et Comportement",
            ["Settings.Cat.Colors"] = "🎨  Couleurs et Surbrillance",
            ["Settings.Cat.Preview"] = "🖼  Aperçu d'image",
            ["Settings.Cat.Fonts"] = "🔤  Tailles de police",
            ["Settings.Cat.Session"] = "💾  Session et Mises à jour",
            ["Settings.Language.Title"] = "Langue / Language",
            ["Settings.Language.Desc"] = "Choisissez la langue de l'interface (Par défaut : langue du système).",
            ["Settings.Update.Title"] = "Vérification automatique des mises à jour",
            ["Settings.Update.Desc"] = "Vérifie automatiquement au démarrage si une nouvelle version est disponible sur GitHub.",
            ["Settings.Update.CheckNow"] = "Vérifier les mises à jour",
            ["Update.AvailableTitle"] = "Mise à jour disponible",
            ["Update.AvailableHeading"] = "Une nouvelle version de ModernExplorer est disponible !",
            ["Update.CurrentVersion"] = "Version installée :",
            ["Update.LatestVersion"] = "Nouvelle version :",
            ["Update.ReleaseNotes"] = "Notes de version :",
            ["Update.UpdateNow"] = "Mettre à jour maintenant",
            ["Update.OpenGitHub"] = "Voir sur GitHub",
            ["Update.Later"] = "Plus tard",
            ["Update.UpToDateTitle"] = "À jour",
            ["Update.UpToDateMsg"] = "Vous utilisez déjà la dernière version ({0}).",
        },
        ["it"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "Lingua di sistema (Auto)",
            ["Toolbar.NewFile"] = "Nuovo file",
            ["Toolbar.NewFolder"] = "Nuova cartella",
            ["Toolbar.Copy"] = "Copia",
            ["Toolbar.Move"] = "Sposta",
            ["Toolbar.Delete"] = "Elimina",
            ["Toolbar.Search"] = "Cerca",
            ["Toolbar.SplitView"] = "Vista divisa",
            ["Toolbar.SingleView"] = "Vista singola",
            ["Toolbar.Hidden"] = "Nascosti",
            ["Toolbar.Terminal"] = "Terminale",
            ["Toolbar.Settings"] = "Impostazioni",
            ["Panel.FilterPlaceholder"] = "Filtro rapido (Ctrl+F)…",
            ["Col.Name"] = "Nome",
            ["Col.Extension"] = "Tipo",
            ["Col.Size"] = "Dimensione",
            ["Col.Modified"] = "Modificato",
            ["Fn.F1"] = "Aiuto",
            ["Fn.F2"] = "Rinomina",
            ["Fn.F3"] = "Visualizza",
            ["Fn.F4"] = "Modifica",
            ["Fn.F5"] = "Copia",
            ["Fn.F6"] = "Sposta",
            ["Fn.F7"] = "Cartella+",
            ["Fn.F8"] = "Elimina",
            ["Fn.F9"] = "Terminale",
            ["Fn.F10"] = "Esci",
            ["Settings.Title"] = "ModernExplorer – Impostazioni",
            ["Settings.Header"] = "Impostazioni",
            ["Settings.Close"] = "Chiudi",
            ["Update.UpdateNow"] = "Aggiorna ora",
        },
        ["nl"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "Systeemtaal (Automatisch)",
            ["Toolbar.NewFile"] = "Nieuw bestand",
            ["Toolbar.NewFolder"] = "Nieuwe map",
            ["Toolbar.Copy"] = "Kopiëren",
            ["Toolbar.Move"] = "Verplaatsen",
            ["Toolbar.Delete"] = "Verwijderen",
            ["Toolbar.Search"] = "Zoeken",
            ["Toolbar.SplitView"] = "Gesplitst",
            ["Toolbar.SingleView"] = "Enkel",
            ["Toolbar.Hidden"] = "Verborgen",
            ["Toolbar.Terminal"] = "Terminal",
            ["Toolbar.Settings"] = "Instellingen",
            ["Col.Name"] = "Naam",
            ["Col.Extension"] = "Type",
            ["Col.Size"] = "Grootte",
            ["Col.Modified"] = "Gewijzigd",
            ["Fn.F1"] = "Help",
            ["Fn.F2"] = "Hernoemen",
            ["Fn.F3"] = "Bekijken",
            ["Fn.F4"] = "Bewerken",
            ["Fn.F5"] = "Kopiëren",
            ["Fn.F6"] = "Verplaatsen",
            ["Fn.F7"] = "Nieuwe map",
            ["Fn.F8"] = "Verwijderen",
            ["Fn.F9"] = "Terminal",
            ["Fn.F10"] = "Afsluiten",
            ["Settings.Header"] = "Instellingen",
            ["Settings.Close"] = "Sluiten",
            ["Update.UpdateNow"] = "Nu bijwerken",
        },
        ["pl"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "Język systemu (Auto)",
            ["Toolbar.NewFile"] = "Nowy plik",
            ["Toolbar.NewFolder"] = "Nowy folder",
            ["Toolbar.Copy"] = "Kopiuj",
            ["Toolbar.Move"] = "Przenieś",
            ["Toolbar.Delete"] = "Usuń",
            ["Toolbar.Search"] = "Szukaj",
            ["Toolbar.SplitView"] = "Podwójny widok",
            ["Toolbar.SingleView"] = "Pojedynczy widok",
            ["Toolbar.Hidden"] = "Ukryte",
            ["Toolbar.Terminal"] = "Terminal",
            ["Toolbar.Settings"] = "Ustawienia",
            ["Col.Name"] = "Nazwa",
            ["Col.Extension"] = "Typ",
            ["Col.Size"] = "Rozmiar",
            ["Col.Modified"] = "Zmodyfikowano",
            ["Fn.F1"] = "Pomoc",
            ["Fn.F2"] = "Zmień nazwę",
            ["Fn.F3"] = "Podgląd",
            ["Fn.F4"] = "Edytuj",
            ["Fn.F5"] = "Kopiuj",
            ["Fn.F6"] = "Przenieś",
            ["Fn.F7"] = "Nowy folder",
            ["Fn.F8"] = "Usuń",
            ["Fn.F9"] = "Terminal",
            ["Fn.F10"] = "Zakończ",
            ["Settings.Header"] = "Ustawienia",
            ["Settings.Close"] = "Zamknij",
            ["Update.UpdateNow"] = "Aktualizuj teraz",
        },
        ["pt"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "Idioma do sistema (Auto)",
            ["Toolbar.NewFile"] = "Novo arquivo",
            ["Toolbar.NewFolder"] = "Nova pasta",
            ["Toolbar.Copy"] = "Copiar",
            ["Toolbar.Move"] = "Mover",
            ["Toolbar.Delete"] = "Excluir",
            ["Toolbar.Search"] = "Pesquisar",
            ["Toolbar.SplitView"] = "Vista dividida",
            ["Toolbar.SingleView"] = "Vista única",
            ["Toolbar.Hidden"] = "Ocultos",
            ["Toolbar.Terminal"] = "Terminal",
            ["Toolbar.Settings"] = "Configurações",
            ["Col.Name"] = "Nome",
            ["Col.Extension"] = "Tipo",
            ["Col.Size"] = "Tamanho",
            ["Col.Modified"] = "Modificado",
            ["Fn.F1"] = "Ajuda",
            ["Fn.F2"] = "Renomear",
            ["Fn.F3"] = "Ver",
            ["Fn.F4"] = "Editar",
            ["Fn.F5"] = "Copiar",
            ["Fn.F6"] = "Mover",
            ["Fn.F7"] = "Nova pasta",
            ["Fn.F8"] = "Excluir",
            ["Fn.F9"] = "Terminal",
            ["Fn.F10"] = "Sair",
            ["Settings.Header"] = "Configurações",
            ["Settings.Close"] = "Fechar",
            ["Update.UpdateNow"] = "Atualizar agora",
        },
        ["ja"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "システム言語 (自動)",
            ["Toolbar.NewFile"] = "新規ファイル",
            ["Toolbar.NewFolder"] = "新規フォルダ",
            ["Toolbar.Copy"] = "コピー",
            ["Toolbar.Move"] = "移動",
            ["Toolbar.Delete"] = "削除",
            ["Toolbar.Search"] = "検索",
            ["Toolbar.SplitView"] = "分割表示",
            ["Toolbar.SingleView"] = "単一表示",
            ["Toolbar.Hidden"] = "隠しファイル",
            ["Toolbar.Terminal"] = "ターミナル",
            ["Toolbar.Settings"] = "設定",
            ["Col.Name"] = "名前",
            ["Col.Extension"] = "種類",
            ["Col.Size"] = "サイズ",
            ["Col.Modified"] = "更新日時",
            ["Fn.F1"] = "ヘルプ",
            ["Fn.F2"] = "名前変更",
            ["Fn.F3"] = "表示",
            ["Fn.F4"] = "編集",
            ["Fn.F5"] = "コピー",
            ["Fn.F6"] = "移動",
            ["Fn.F7"] = "フォルダ作成",
            ["Fn.F8"] = "削除",
            ["Fn.F9"] = "ターミナル",
            ["Fn.F10"] = "終了",
            ["Settings.Header"] = "設定",
            ["Settings.Close"] = "閉じる",
            ["Update.UpdateNow"] = "今すぐ更新",
        },
        ["zh"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lang.SystemDefault"] = "系统默认语言 (自动)",
            ["Toolbar.NewFile"] = "新建文件",
            ["Toolbar.NewFolder"] = "新建文件夹",
            ["Toolbar.Copy"] = "复制",
            ["Toolbar.Move"] = "移动",
            ["Toolbar.Delete"] = "删除",
            ["Toolbar.Search"] = "搜索",
            ["Toolbar.SplitView"] = "双栏视图",
            ["Toolbar.SingleView"] = "单栏视图",
            ["Toolbar.Hidden"] = "隐藏文件",
            ["Toolbar.Terminal"] = "终端",
            ["Toolbar.Settings"] = "设置",
            ["Col.Name"] = "名称",
            ["Col.Extension"] = "类型",
            ["Col.Size"] = "大小",
            ["Col.Modified"] = "修改日期",
            ["Fn.F1"] = "帮助",
            ["Fn.F2"] = "重命名",
            ["Fn.F3"] = "查看",
            ["Fn.F4"] = "编辑",
            ["Fn.F5"] = "复制",
            ["Fn.F6"] = "移动",
            ["Fn.F7"] = "新建文件夹",
            ["Fn.F8"] = "删除",
            ["Fn.F9"] = "终端",
            ["Fn.F10"] = "退出",
            ["Settings.Header"] = "设置",
            ["Settings.Close"] = "关闭",
            ["Update.UpdateNow"] = "立即更新",
        },
    };
}
