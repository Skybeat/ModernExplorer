using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.Archive;
using ModernExplorer.Core.Commands;
using ModernExplorer.Core.Favorites;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Input;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.Remote;
using ModernExplorer.Core.Session;
using ModernExplorer.Core.Settings;

namespace ModernExplorer.Core.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxClosedTabs = 25;

    private readonly WatcherHub _watcherHub;
    private readonly FileOperationQueue _operationQueue;
    private readonly FileCreationService _creationService;
    private readonly SessionStore _sessionStore;
    private readonly IUiDispatcher _dispatcher;
    private readonly IShellUi _ui;
    private readonly Stack<ClosedTab> _closedTabs = new();
    private bool _isInitialized;
    private bool _isDisposing;

    public Guid WindowId { get; private set; } = Guid.NewGuid();
    public SettingsViewModel Settings { get; }
    public KeyBindingStore KeyBindings { get; }
    public CommandRegistry Commands { get; } = new();
    public KeyRouter Router { get; }
    public PanelViewModel Left { get; }
    public PanelViewModel Right { get; }
    public FavoritesStore Favorites { get; }
    public UndoHistoryService UndoHistory { get; } = new();
    public ObservableCollection<OperationItemViewModel> Operations { get; } = [];

    public bool CanUndo => UndoHistory.CanUndo;
    public string UndoDescription => UndoHistory.NextUndoDescription is { } d ? $"Rückgängig: {d} (Strg+Z)" : "Rückgängig (Strg+Z)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePanel), nameof(OppositePanel))]
    private PanelSide _activeSide = PanelSide.Left;

    [ObservableProperty]
    private bool _isDualMode = true;

    [ObservableProperty]
    private double _splitRatio = 0.5;

    [ObservableProperty]
    private string? _pendingChordText;

    [ObservableProperty]
    private string? _statusNotification;

    [ObservableProperty]
    private bool _isOperationsPanelOpen;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isQuickPreviewPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(F1Label), nameof(F2Label), nameof(F3Label), nameof(F4Label), nameof(F5Label), nameof(F6Label), nameof(F7Label), nameof(F8Label), nameof(F9Label), nameof(F10Label), nameof(F11Label), nameof(F12Label))]
    private bool _isShiftPressed;

    public PanelViewModel ActivePanel => ActiveSide == PanelSide.Left ? Left : Right;
    public PanelViewModel OppositePanel => ActiveSide == PanelSide.Left ? Right : Left;

    public string F1Label => LocalizationService.T("Fn.F1");
    public string F2Label => IsShiftPressed ? $"{LocalizationService.T("Fn.F2")}+" : LocalizationService.T("Fn.F2");
    public string F3Label => LocalizationService.T("Fn.F3");
    public string F4Label => LocalizationService.T("Fn.F4");
    public string F5Label => IsShiftPressed ? $"{LocalizationService.T("Fn.F5")}…" : LocalizationService.T("Fn.F5");
    public string F6Label => IsShiftPressed ? $"{LocalizationService.T("Fn.F6")}…" : LocalizationService.T("Fn.F6");
    public string F7Label => IsShiftPressed ? LocalizationService.T("Toolbar.NewFolder") : LocalizationService.T("Toolbar.NewFile");
    public string F8Label => IsShiftPressed ? $"{LocalizationService.T("Fn.F8")}!" : LocalizationService.T("Fn.F8");
    public string F9Label => IsShiftPressed ? $"Admin {LocalizationService.T("Fn.F9")}" : LocalizationService.T("Fn.F9");
    public string F10Label => LocalizationService.T("Fn.F10");
    public string F11Label => LocalizationService.T("Fn.F11");
    public string F12Label => IsShiftPressed ? LocalizationService.T("Fn.F12.Swap") : LocalizationService.T("Fn.F12");

    public MainViewModel(
        KeyBindingStore keyBindings,
        WatcherHub watcherHub,
        FileOperationQueue operationQueue,
        FileCreationService creationService,
        SessionStore sessionStore,
        IUiDispatcher dispatcher,
        IShellUi ui,
        SettingsStore? settingsStore = null,
        FavoritesStore? favoritesStore = null)
    {
        Settings = new SettingsViewModel(settingsStore);
        KeyBindings = keyBindings;
        _watcherHub = watcherHub;
        _operationQueue = operationQueue;
        _creationService = creationService;
        _sessionStore = sessionStore;
        _dispatcher = dispatcher;
        _ui = ui;
        Favorites = favoritesStore ?? new FavoritesStore();

        Left = new PanelViewModel(PanelSide.Left, watcherHub, dispatcher, OnTabClosed) { IsActivePanel = true };
        Right = new PanelViewModel(PanelSide.Right, watcherHub, dispatcher, OnTabClosed) { IsActivePanel = false };

        Left.StateChanged += (_, _) =>
        {
            UpdateTabsBookmarkedState();
            RequestSessionSave();
        };
        Right.StateChanged += (_, _) =>
        {
            UpdateTabsBookmarkedState();
            RequestSessionSave();
        };

        Favorites.FavoritesChanged += (_, _) => UpdateTabsBookmarkedState();
        UndoHistory.HistoryChanged += (_, _) =>
        {
            _dispatcher.Post(() =>
            {
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(UndoDescription));
            });
        };

        Settings.SaveSessionChanged += (_, _) =>
        {
            if (Settings.SaveSession)
                SaveSessionImmediately();
            else
                _sessionStore.CancelPending();
        };

        Settings.ShowHiddenFilesByDefaultChanged += (_, _) =>
        {
            foreach (var t in Left.Tabs.Concat(Right.Tabs))
                t.ShowHidden = Settings.ShowHiddenFilesByDefault;
        };

        Settings.SortFoldersFirstChanged += (_, _) =>
        {
            foreach (var t in Left.Tabs.Concat(Right.Tabs))
                t.ApplySortFoldersFirst(Settings.SortFoldersFirst);
        };

        Settings.ShowFileExtensionsChanged += (_, _) =>
        {
            foreach (var t in Left.Tabs.Concat(Right.Tabs))
                t.RefreshDisplayNames();
        };

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(F1Label));
            OnPropertyChanged(nameof(F2Label));
            OnPropertyChanged(nameof(F3Label));
            OnPropertyChanged(nameof(F4Label));
            OnPropertyChanged(nameof(F5Label));
            OnPropertyChanged(nameof(F6Label));
            OnPropertyChanged(nameof(F7Label));
            OnPropertyChanged(nameof(F8Label));
            OnPropertyChanged(nameof(F9Label));
            OnPropertyChanged(nameof(F10Label));
            OnPropertyChanged(nameof(F11Label));
            OnPropertyChanged(nameof(F12Label));
        };

        Router = new KeyRouter(KeyBindings, Commands);
        Router.PendingChanged += (_, _) => PendingChordText = Router.PendingDisplay;

        _operationQueue.Enqueued += OnOperationEnqueued;
        _operationQueue.Finished += OnOperationFinished;

        RegisterCommands();
    }

    partial void OnActiveSideChanged(PanelSide value)
    {
        Left.IsActivePanel = value == PanelSide.Left;
        Right.IsActivePanel = value == PanelSide.Right;
        RequestSessionSave();
    }

    partial void OnIsDualModeChanged(bool value)
    {
        if (!value && ActiveSide == PanelSide.Right)
            ActiveSide = PanelSide.Left;
        RequestSessionSave();
    }

    public async Task InitializeAsync(TabState? singleStartupTab = null)
    {
        _isInitialized = false;

        if (singleStartupTab is not null)
        {
            IsDualMode = false;
            await Left.AddTabFromStateAsync(singleStartupTab, activate: true);
            await Right.CreateTabAsync(singleStartupTab.Path, activate: true);
            _isInitialized = true;
            return;
        }

        if (Settings.SaveSession)
        {
            var session = _sessionStore.Load();
            if (session is { IsEmpty: false })
            {
                foreach (var closed in Enumerable.Reverse(session.ClosedTabs))
                    _closedTabs.Push(closed);

                var win = session.Windows[0];
                WindowId = win.Id;
                IsDualMode = win.Layout.Dual;
                SplitRatio = Math.Clamp(win.Layout.SplitRatio, 0.15, 0.85);

                if (win.Left.Tabs.Count > 0)
                {
                    foreach (var t in win.Left.Tabs) await Left.AddTabFromStateAsync(t, activate: false);
                    Left.GoToTab(Math.Clamp(win.Left.ActiveIndex, 0, Left.Tabs.Count - 1));
                }
                if (win.Right.Tabs.Count > 0)
                {
                    foreach (var t in win.Right.Tabs) await Right.AddTabFromStateAsync(t, activate: false);
                    Right.GoToTab(Math.Clamp(win.Right.ActiveIndex, 0, Right.Tabs.Count - 1));
                }

                ActiveSide = IsDualMode ? win.Layout.ActivePanel : PanelSide.Left;
            }
        }

        if (Left.Tabs.Count == 0)
            await Left.CreateTabAsync(Environment.CurrentDirectory, activate: true);
        if (Right.Tabs.Count == 0)
            await Right.CreateTabAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), activate: true);

        _isInitialized = true;
        if (Settings.SaveSession)
            SaveSessionImmediately();
    }

    public FocusContext BuildFocusContext(bool isFileListFocused, bool isAddressBarFocused, bool isTextInput)
    {
        var tab = ActivePanel.ActiveTab;
        var ctx = new FocusContext { IsTextInput = isTextInput };
        ctx.Set(ContextFlags.Panel, true);
        ctx.Set(ContextFlags.IsDual, IsDualMode);
        if (isFileListFocused) ctx.Set(ContextFlags.FileList, true);
        if (isAddressBarFocused) ctx.Set(ContextFlags.AddressBar, true);
        if (tab?.FocusedItem is not null) ctx.Set(ContextFlags.HasFocusedItem, true);
        if (tab?.Items.Any(i => i.IsMarked) == true) ctx.Set(ContextFlags.HasSelection, true);
        return ctx;
    }

    public SessionState CreateSessionSnapshot() => new()
    {
        Version = 1,
        SavedAt = DateTime.UtcNow,
        Windows =
        [
            new WindowState
            {
                Id = WindowId,
                Layout = new LayoutState
                {
                    Dual = IsDualMode,
                    SplitRatio = SplitRatio,
                    ActivePanel = ActiveSide,
                },
                Left = Left.ToState(),
                Right = Right.ToState(),
            }
        ],
        ClosedTabs = _closedTabs.Take(MaxClosedTabs).ToList(),
    };

    public void RequestSessionSave()
    {
        if (!_isInitialized || _isDisposing || !Settings.SaveSession) return;
        if (Left.Tabs.Count == 0 || Right.Tabs.Count == 0) return;
        var snap = CreateSessionSnapshot();
        _sessionStore.RequestSave(() => snap);
    }

    public void SaveSessionImmediately()
    {
        if (_isDisposing) return;
        if (!Settings.SaveSession)
        {
            _sessionStore.CancelPending();
            return;
        }
        if (Left.Tabs.Count == 0 || Right.Tabs.Count == 0) return;
        _sessionStore.Save(CreateSessionSnapshot());
    }

    private void OnTabClosed(TabState state, PanelSide side)
    {
        _closedTabs.Push(new ClosedTab { Tab = state, Panel = side, ClosedAt = DateTime.UtcNow });
    }

    private void OnOperationEnqueued(object? sender, OperationHandle handle)
    {
        _dispatcher.Post(() =>
        {
            Operations.Insert(0, new OperationItemViewModel(handle, _dispatcher));
            IsOperationsPanelOpen = true;
        });
    }

    private void OnOperationFinished(object? sender, OperationHandle handle)
    {
        _dispatcher.Post(() =>
        {
            if (handle.Status == OperationStatus.Failed && handle.Error is not null)
                StatusNotification = $"Fehler: {handle.Error.Message}";
            else if (handle.Errors.Count > 0)
                StatusNotification = handle.Errors[0];
            else
                StatusNotification = $"{handle.Operation.Describe()} – abgeschlossen";
        });
    }

    private void RegisterCommands()
    {
        Commands
            .Register("app.help", "Hilfe & Tastatur-Shortcuts anzeigen", "Anwendung", _ => _ui.ShowHelp())
            .Register("app.settings", "Einstellungen öffnen / schließen", "Anwendung", _ => IsSettingsOpen = !IsSettingsOpen)
            .Register("palette.open", "Command Palette öffnen", "Anwendung", _ => _ui.ShowCommandPalette())
            .Register("view.fullscreen", "Vollbild umschalten", "Ansicht", _ => _ui.ToggleFullScreen())
            .Register("layout.toggleDual", "Single-/Dual-Panel umschalten", "Layout", _ =>
            {
                IsDualMode = !IsDualMode;
                _ui.FocusFileList(ActiveSide);
            })
            .Register("layout.swapPanels", "Linkes und rechtes Panel tauschen", "Layout", async _ =>
            {
                if (!IsDualMode) IsDualMode = true;
                var leftPath = Left.ActiveTab?.CurrentPath;
                var rightPath = Right.ActiveTab?.CurrentPath;
                if (leftPath is not null && rightPath is not null)
                {
                    await Left.ActiveTab!.NavigateAsync(rightPath);
                    await Right.ActiveTab!.NavigateAsync(leftPath);
                }
            })
            .Register("layout.syncPanels", "Gegenpanel auf aktuellen Pfad setzen", "Layout", async _ =>
            {
                var path = ActivePanel.ActiveTab?.CurrentPath;
                if (path is null) return;
                if (!IsDualMode) IsDualMode = true;
                if (OppositePanel.ActiveTab is not null)
                    await OppositePanel.ActiveTab.NavigateAsync(path);
            })

            // Navigation
            .Register("nav.open", "Eintrag öffnen", "Navigation", async _ => await OpenFocusedEntryAsync())
            .Register("nav.openInBackgroundTab", "Ordner in Hintergrund-Tab öffnen", "Navigation", async _ =>
            {
                var f = ActivePanel.ActiveTab?.FocusedItem;
                if (f is { IsDirectory: true })
                    await ActivePanel.CreateTabAsync(f.FullPath, activate: false);
            })
            .Register("nav.openInOppositePanel", "Ordner im Gegenpanel öffnen", "Navigation", async _ =>
            {
                var f = ActivePanel.ActiveTab?.FocusedItem;
                if (f is { IsDirectory: true })
                {
                    if (!IsDualMode) IsDualMode = true;
                    if (OppositePanel.ActiveTab is not null)
                        await OppositePanel.ActiveTab.NavigateAsync(f.FullPath);
                }
            })
            .Register("nav.up", "Übergeordneter Ordner", "Navigation", async _ =>
            {
                if (ActivePanel.ActiveTab is { } t) await t.GoUpAsync();
            })
            .Register("nav.back", "Zurück im Verlauf", "Navigation", async _ =>
            {
                if (ActivePanel.ActiveTab is { } t) await t.GoBackAsync();
            })
            .Register("nav.forward", "Vorwärts im Verlauf", "Navigation", async _ =>
            {
                if (ActivePanel.ActiveTab is { } t) await t.GoForwardAsync();
            })
            .Register("nav.focusAddress", "Adressleiste fokussieren", "Navigation", _ => _ui.FocusAddressBar())
            .Register("filter.focus", "Inline-Filter fokussieren", "Navigation", _ => _ui.FocusFilter())
            .Register("panel.switch", "Zum anderen Panel wechseln", "Navigation", _ =>
            {
                if (!IsDualMode) return;
                ActiveSide = ActiveSide == PanelSide.Left ? PanelSide.Right : PanelSide.Left;
                _ui.FocusFileList(ActiveSide);
            })
            .Register("panel.focusList", "Dateiliste fokussieren / Filter leeren", "Navigation", _ =>
            {
                if (ActivePanel.ActiveTab is { } t && !string.IsNullOrEmpty(t.Filter))
                    t.Filter = "";
                _ui.FocusFileList(ActiveSide);
            })

            // View & sorting
            .Register("view.sort", "Sortierung ändern", "Ansicht", ctx =>
            {
                if (ActivePanel.ActiveTab is not { } t) return;
                var col = ctx.Arg("column", "name").ToLowerInvariant() switch
                {
                    "size" => SortColumn.Size,
                    "modified" => SortColumn.Modified,
                    "type" => SortColumn.Type,
                    _ => SortColumn.Name,
                };
                t.ApplySort(col);
            })
            .Register("view.refresh", "Verzeichnis aktualisieren", "Ansicht", async _ =>
            {
                if (ActivePanel.ActiveTab is { } t) await t.ReloadAsync();
            })
            .Register("view.toggleHidden", "Versteckte Dateien ein-/ausblenden", "Ansicht", async _ =>
            {
                if (ActivePanel.ActiveTab is { } t) await t.ToggleHiddenAsync();
            })
            .Register("view.mode.cycle", "Ansichtsmodus durchschalten", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.CycleViewMode();
                RequestSessionSave();
            })
            .Register("view.mode.extraLargeIcons", "Ansicht: Extra große Symbole (Ctrl+Shift+1)", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.SetViewMode(FileViewMode.ExtraLargeIcons);
                RequestSessionSave();
            })
            .Register("view.mode.largeIcons", "Ansicht: Große Symbole (Ctrl+Shift+2)", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.SetViewMode(FileViewMode.LargeIcons);
                RequestSessionSave();
            })
            .Register("view.mode.mediumIcons", "Ansicht: Mittelgroße Symbole (Ctrl+Shift+3)", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.SetViewMode(FileViewMode.MediumIcons);
                RequestSessionSave();
            })
            .Register("view.mode.smallIcons", "Ansicht: Kleine Symbole (Ctrl+Shift+4)", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.SetViewMode(FileViewMode.SmallIcons);
                RequestSessionSave();
            })
            .Register("view.mode.list", "Ansicht: Liste (Ctrl+Shift+5)", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.SetViewMode(FileViewMode.List);
                RequestSessionSave();
            })
            .Register("view.mode.details", "Ansicht: Details (Ctrl+Shift+6)", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.SetViewMode(FileViewMode.Details);
                RequestSessionSave();
            })
            .Register("view.mode.tiles", "Ansicht: Kacheln (Ctrl+Shift+7)", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.SetViewMode(FileViewMode.Tiles);
                RequestSessionSave();
            })
            .Register("view.mode.content", "Ansicht: Inhalt (Ctrl+Shift+8)", "Ansicht", _ =>
            {
                ActivePanel.ActiveTab?.SetViewMode(FileViewMode.Content);
                RequestSessionSave();
            })
            .Register("view.branchView", "Flache Ordneransicht / Branch View (Strg+B)", "Ansicht", async _ =>
            {
                if (ActivePanel.ActiveTab is { } t) await t.ToggleBranchViewAsync();
            })
            .Register("view.quickPreview", "Schnellvorschau-Panel ein-/ausblenden (Strg+Q)", "Ansicht", _ =>
            {
                IsQuickPreviewPanelOpen = !IsQuickPreviewPanelOpen;
            })
            .Register("favorites.toggle", "Ordner als Favorit merken / entfernen (Strg+D)", "Navigation", _ =>
            {
                ToggleCurrentFolderBookmark();
            })

            // Selection & clipboard
            .Register("select.toggle", "Eintrag markieren / abwählen", "Auswahl", _ => ActivePanel.ActiveTab?.ToggleMarkFocusedAndAdvance())
            .Register("select.all", "Alle Einträge markieren", "Auswahl", _ => ActivePanel.ActiveTab?.SelectAll())
            .Register("select.invert", "Auswahl invertieren", "Auswahl", _ => ActivePanel.ActiveTab?.InvertSelection())
            .Register("clipboard.copyPath", "Vollständigen Pfad kopieren", "Zwischenablage", _ =>
            {
                if (ActivePanel.ActiveTab is not { } t) return;
                var paths = t.GetTargetPaths();
                if (paths.Count == 0 && !string.IsNullOrEmpty(t.CurrentPath)) paths = [t.CurrentPath];
                _ui.SetClipboardText(string.Join(Environment.NewLine, paths));
                StatusNotification = "Pfad in Zwischenablage kopiert";
            })
            .Register("clipboard.copy", "Dateien in Zwischenablage kopieren", "Zwischenablage", async _ =>
            {
                var paths = ActivePanel.ActiveTab?.GetTargetPaths() ?? [];
                if (paths.Count == 0) return;
                await _ui.SetClipboardFilesAsync(paths, cut: false);
                StatusNotification = $"{paths.Count} Element(e) kopiert";
            })
            .Register("clipboard.cut", "Dateien in Zwischenablage ausschneiden", "Zwischenablage", async _ =>
            {
                var paths = ActivePanel.ActiveTab?.GetTargetPaths() ?? [];
                if (paths.Count == 0) return;
                await _ui.SetClipboardFilesAsync(paths, cut: true);
                StatusNotification = $"{paths.Count} Element(e) ausgeschnitten";
            })
            .Register("clipboard.paste", "Aus Zwischenablage einfügen", "Zwischenablage", async _ =>
            {
                if (ActivePanel.ActiveTab is not { } t || string.IsNullOrEmpty(t.CurrentPath)) return;
                var clip = await _ui.GetClipboardFilesAsync();
                if (clip is null || clip.Value.Paths.Count == 0) return;
                var kind = clip.Value.Cut ? OpKind.Move : OpKind.Copy;
                _operationQueue.Enqueue(new FileOperation(kind, clip.Value.Paths, t.CurrentPath, new OperationOptions()));
            })
            .Register("edit.undo", "Letzte Dateioperation rückgängig machen (Strg+Z)", "Bearbeiten", async _ =>
            {
                await UndoLastOperationAsync();
            })

            // File operations (F2–F9) & Tools
            .Register("file.rename", "Umbenennen (F2)", "Datei", async _ => await RenameFocusedAsync())
            .Register("file.batchRename", "Mehrfach-Umbenennungstool (Strg+M)", "Datei", async _ => await BatchRenameSelectedAsync())
            .Register("file.view", "Quick-Viewer öffnen (F3)", "Datei", async _ => await OpenViewerForFocusedAsync())
            .Register("file.edit", "Im Editor öffnen (F4)", "Datei", async _ => await OpenEditorForFocusedAsync())
            .Register("fileOp.copy", "Kopieren (F5)", "Datei", async ctx =>
                await CopyOrMoveAsync(OpKind.Copy, ctx.Arg("showDialog", false)))
            .Register("fileOp.move", "Verschieben (F6)", "Datei", async ctx =>
                await CopyOrMoveAsync(OpKind.Move, ctx.Arg("showDialog", false)))
            .Register("file.createNew", "Neue Datei erstellen (F7)", "Datei", async _ => await CreateNewFileAsync())
            .Register("folder.createNew", "Neuen Ordner erstellen (Shift+F7)", "Datei", async _ => await CreateNewFolderAsync())
            .Register("fileOp.delete", "Löschen (F8)", "Datei", async ctx =>
                await DeleteTargetsAsync(ctx.Arg("recycle", true)))
            .Register("terminal.open", "Terminal im aktuellen Pfad öffnen (F9)", "Werkzeuge", ctx =>
                OpenTerminal(ctx.Arg("elevated", false)))
            .Register("tools.deepSearch", "Rekursive Tiefensuche (Alt+F7)", "Werkzeuge", async _ => await OpenDeepSearchAsync())
            .Register("tools.syncDirs", "Verzeichnisse vergleichen & synchronisieren (Strg+Shift+S)", "Werkzeuge", async _ => await OpenDirectorySyncAsync())
            .Register("tools.findDuplicates", "Duplikat-Finder öffnen", "Werkzeuge", async _ => await OpenDuplicateFinderAsync())
            .Register("tools.treeSize", "Speicherplatz-Analyse / Ordnergrößen (TreeSize)", "Werkzeuge", async _ =>
            {
                if (ActivePanel.ActiveTab is { } t) await t.AnalyzeDirectorySizesAsync(sortBySizeDescending: true);
            })
            .Register("tools.remoteConnect", "FTP / FTPS / SFTP-Verbindungsmanager (Strg+Shift+C)", "Werkzeuge", async _ => await OpenRemoteConnectDialogAsync())
            .Register("archive.pack", "In Archiv packen (.zip / .tar.gz / .7z) (Alt+F5)", "Archiv", async _ => await PackSelectedToArchiveAsync())
            .Register("archive.extract", "Archiv entpacken (Alt+F9)", "Archiv", async _ => await ExtractSelectedArchiveAsync())
            .Register("remote.chmod", "Remote-Berechtigungen ändern (chmod)", "Remote", async _ => await ChangeRemotePermissionsAsync())

            // Tabs
            .Register("tab.new", "Neuer Tab (Strg+T)", "Tabs", async _ => await ActivePanel.CreateTabAsync())
            .Register("tab.close", "Tab schließen (Strg+W)", "Tabs", async _ => await ActivePanel.CloseTabAsync())
            .Register("tab.reopen", "Geschlossenen Tab wiederherstellen (Strg+Shift+T)", "Tabs", async _ =>
            {
                if (_closedTabs.Count == 0) return;
                var closed = _closedTabs.Pop();
                var panel = closed.Panel == PanelSide.Right && IsDualMode ? Right : Left;
                await panel.AddTabFromStateAsync(closed.Tab, activate: true);
            })
            .Register("tab.duplicate", "Tab duplizieren (Strg+Shift+D)", "Tabs", async _ => await ActivePanel.DuplicateActiveTabAsync())
            .Register("tab.togglePin", "Tab anheften/lösen (Strg+K Strg+P)", "Tabs", _ => ActivePanel.TogglePinActiveTab())
            .Register("tab.toggleLock", "Tab sperren/entsperren (Strg+K Strg+L)", "Tabs", _ => ActivePanel.ToggleLockActiveTab())
            .Register("tab.next", "Nächster Tab (Strg+Tab)", "Tabs", _ => ActivePanel.NextTab())
            .Register("tab.previous", "Vorheriger Tab (Strg+Shift+Tab)", "Tabs", _ => ActivePanel.PreviousTab())
            .Register("tab.goto", "Zu Tab N wechseln", "Tabs", ctx => ActivePanel.GoToTab(ctx.Arg("index", 0)))
            .Register("tab.gotoLast", "Zum letzten Tab wechseln (Strg+9)", "Tabs", _ => ActivePanel.GoToLastTab())
            .Register("tab.moveLeft", "Tab nach links verschieben", "Tabs", _ => ActivePanel.MoveActiveTabLeft())
            .Register("tab.moveRight", "Tab nach rechts verschieben", "Tabs", _ => ActivePanel.MoveActiveTabRight())
            .Register("tab.moveToOppositePanel", "Tab ins Gegenpanel verschieben", "Tabs", async _ => await MoveActiveTabToOppositePanelAsync())
            .Register("window.newWithTab", "Tab in neues Fenster herauslösen (Strg+Shift+N)", "Tabs", _ =>
            {
                var state = ActivePanel.DetachTab();
                if (state is not null) _ui.OpenNewWindow(state);
            });
    }

    public void UpdateTabsBookmarkedState()
    {
        foreach (var tab in Left.Tabs.Concat(Right.Tabs))
            tab.IsBookmarked = Favorites.IsBookmarked(tab.CurrentPath);
    }

    public void ToggleCurrentFolderBookmark()
    {
        if (ActivePanel.ActiveTab is not { } tab || string.IsNullOrWhiteSpace(tab.CurrentPath))
            return;
        bool added = Favorites.ToggleBookmark(tab.CurrentPath, tab.Title);
        tab.IsBookmarked = added;
        StatusNotification = added
            ? $"Favorit hinzugefügt: {tab.Title}"
            : $"Favorit entfernt: {tab.Title}";
    }

    public async Task NavigateToFavoriteAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (ActivePanel.ActiveTab is { } tab)
            await tab.NavigateAsync(path, addToHistory: true);
    }

    public async Task UndoLastOperationAsync()
    {
        if (!UndoHistory.CanUndo)
        {
            StatusNotification = "Keine rückgängig machbare Aktion im Verlauf";
            return;
        }

        var res = await Task.Run(() => UndoHistory.UndoLast());
        StatusNotification = res.Summary;
        if (Left.ActiveTab is not null) await Left.ActiveTab.ReloadAsync();
        if (Right.ActiveTab is not null) await Right.ActiveTab.ReloadAsync();
        if (!res.Success)
            _ui.ShowMessage("Rückgängig", res.Summary);
    }

    public async Task BatchRenameSelectedAsync()
    {
        if (ActivePanel.ActiveTab is not { } tab || tab.IsVirtualArchive || tab.IsRemoteConnection)
            return;

        var marked = tab.Items.Where(i => i.IsMarked && !i.IsParentLink).Select(i => i.Entry).ToList();
        if (marked.Count == 0 && tab.FocusedItem is { IsParentLink: false } f)
            marked.Add(f.Entry);
        if (marked.Count == 0)
            marked = tab.Items.Where(i => !i.IsParentLink).Select(i => i.Entry).ToList();
        if (marked.Count == 0)
            return;

        if (await _ui.ShowBatchRenameDialogAsync(marked))
            await tab.ReloadAsync();
    }

    public async Task OpenDeepSearchAsync()
    {
        var dir = ActivePanel.ActiveTab is { IsVirtualArchive: false, IsRemoteConnection: false, IsSearchResultsTab: false } t && !string.IsNullOrEmpty(t.CurrentPath)
            ? t.CurrentPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await _ui.ShowDeepSearchDialogAsync(dir);
    }

    public async Task OpenDirectorySyncAsync()
    {
        string leftDir = Left.ActiveTab is { IsVirtualArchive: false, IsRemoteConnection: false } lt && Directory.Exists(lt.CurrentPath)
            ? lt.CurrentPath
            : Environment.CurrentDirectory;
        string rightDir = Right.ActiveTab is { IsVirtualArchive: false, IsRemoteConnection: false } rt && Directory.Exists(rt.CurrentPath)
            ? rt.CurrentPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (await _ui.ShowDirectorySyncDialogAsync(leftDir, rightDir))
        {
            if (Left.ActiveTab is not null) await Left.ActiveTab.ReloadAsync();
            if (Right.ActiveTab is not null) await Right.ActiveTab.ReloadAsync();
        }
    }

    public async Task OpenDuplicateFinderAsync()
    {
        var dir = ActivePanel.ActiveTab is { IsVirtualArchive: false, IsRemoteConnection: false, IsSearchResultsTab: false } t && Directory.Exists(t.CurrentPath)
            ? t.CurrentPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (await _ui.ShowDuplicateFinderDialogAsync(dir))
        {
            if (ActivePanel.ActiveTab is not null)
                await ActivePanel.ActiveTab.ReloadAsync();
        }
    }

    public async Task OpenRemoteConnectDialogAsync()
    {
        var uri = await _ui.ShowRemoteConnectionManagerDialogAsync();
        if (!string.IsNullOrWhiteSpace(uri) && ActivePanel.ActiveTab is { } tab)
            await tab.NavigateAsync(uri, addToHistory: true);
    }

    public async Task PackSelectedToArchiveAsync()
    {
        if (ActivePanel.ActiveTab is not { IsVirtualArchive: false, IsRemoteConnection: false } srcTab)
            return;

        var sources = srcTab.GetTargetPaths();
        if (sources.Count == 0) return;

        string defaultDestDir = IsDualMode && OppositePanel.ActiveTab is { IsVirtualArchive: false, IsRemoteConnection: false } opp && Directory.Exists(opp.CurrentPath)
            ? opp.CurrentPath
            : srcTab.CurrentPath;

        var createdArchive = await _ui.ShowCreateArchiveDialogAsync(sources, defaultDestDir);
        if (!string.IsNullOrWhiteSpace(createdArchive))
        {
            StatusNotification = $"Archiv erstellt: {Path.GetFileName(createdArchive)}";
            if (Left.ActiveTab is not null) await Left.ActiveTab.ReloadAsync();
            if (Right.ActiveTab is not null) await Right.ActiveTab.ReloadAsync();
        }
    }

    public async Task ExtractSelectedArchiveAsync()
    {
        if (ActivePanel.ActiveTab is not { } srcTab) return;

        string? archivePath = null;
        if (srcTab.IsVirtualArchive && ArchiveService.TryParseVirtualArchivePath(srcTab.CurrentPath, out var parsedArch, out _))
        {
            archivePath = parsedArch;
        }
        else if (srcTab.FocusedItem is { IsDirectory: false } focusedFile && ArchiveService.IsSupportedArchiveFile(focusedFile.FullPath))
        {
            archivePath = focusedFile.FullPath;
        }

        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            _ui.ShowMessage("Archiv entpacken", "Bitte wählen Sie eine unterstützte Archivdatei (.zip, .7z, .tar.gz, .rar) aus.");
            return;
        }

        string defaultDest = IsDualMode && OppositePanel.ActiveTab is { IsVirtualArchive: false, IsRemoteConnection: false } opp && Directory.Exists(opp.CurrentPath)
            ? Path.Combine(opp.CurrentPath, Path.GetFileNameWithoutExtension(archivePath))
            : Path.Combine(Path.GetDirectoryName(archivePath) ?? "", Path.GetFileNameWithoutExtension(archivePath));

        var destDir = await _ui.PromptAsync("Archiv entpacken (Alt+F9)", $"Zielordner für „{Path.GetFileName(archivePath)}“:", defaultDest, defaultDest.Length);
        if (string.IsNullOrWhiteSpace(destDir)) return;

        try
        {
            StatusNotification = $"Entpacke {Path.GetFileName(archivePath)}…";
            int count = await ArchiveService.ExtractArchiveAsync(archivePath, destDir);
            StatusNotification = $"{count} Datei(en) nach {destDir} entpackt";
            if (Left.ActiveTab is not null) await Left.ActiveTab.ReloadAsync();
            if (Right.ActiveTab is not null) await Right.ActiveTab.ReloadAsync();
        }
        catch (Exception ex)
        {
            _ui.ShowMessage("Entpacken fehlgeschlagen", ex.Message);
        }
    }

    public async Task ChangeRemotePermissionsAsync()
    {
        if (ActivePanel.ActiveTab is not { IsRemoteConnection: true } tab || tab.FocusedItem is not { IsParentLink: false } item)
            return;

        string initialOctal = "755";
        var oct = await _ui.PromptAsync("Berechtigungen ändern (chmod)", $"Oktaler Modus (z.B. 755 oder 644) für „{item.Name}“:", initialOctal, initialOctal.Length);
        if (string.IsNullOrWhiteSpace(oct)) return;

        try
        {
            short mode = Convert.ToInt16(oct.Trim(), 8);
            await RemoteFileSystemService.ChangePermissionsAsync(item.FullPath, mode);
            StatusNotification = $"chmod {oct.Trim()} auf {item.Name} angewendet";
            await tab.ReloadAsync(item.Name);
        }
        catch (Exception ex)
        {
            _ui.ShowMessage("chmod fehlgeschlagen", ex.Message);
        }
    }

    public async Task OpenViewerForFocusedAsync()
    {
        if (ActivePanel.ActiveTab is not { } tab || tab.FocusedItem is not { IsParentLink: false } item)
            return;

        try
        {
            if (item.Entry.IsArchiveVirtualEntry)
            {
                if (item.IsDirectory) return;
                var temp = await ArchiveService.ExtractVirtualEntryToTempFileAsync(item.FullPath);
                if (temp is not null) _ui.OpenViewer(temp);
                return;
            }

            if (item.Entry.IsRemoteEntry)
            {
                if (item.IsDirectory) return;
                StatusNotification = $"Lade Vorschau für {item.Name}…";
                var temp = await RemoteFileSystemService.DownloadToTempFileAsync(item.FullPath);
                StatusNotification = null;
                _ui.OpenViewer(temp);
                return;
            }

            _ui.OpenViewer(item.FullPath);
        }
        catch (Exception ex)
        {
            _ui.ShowMessage("Vorschau fehlgeschlagen", ex.Message);
        }
    }

    public async Task OpenEditorForFocusedAsync()
    {
        if (ActivePanel.ActiveTab is not { } tab || tab.FocusedItem is not { IsDirectory: false, IsParentLink: false } item)
            return;

        try
        {
            if (item.Entry.IsArchiveVirtualEntry)
            {
                var temp = await ArchiveService.ExtractVirtualEntryToTempFileAsync(item.FullPath);
                if (temp is not null) _ui.OpenEditor(temp);
                return;
            }

            if (item.Entry.IsRemoteEntry)
            {
                StatusNotification = $"Lade {item.Name} zum Bearbeiten herunter…";
                string remoteTargetUri = item.FullPath;
                string itemName = item.Name;
                var temp = await RemoteFileSystemService.DownloadToTempFileAsync(remoteTargetUri);
                StatusNotification = $"Remote-Datei geöffnet: {itemName}";
                _ui.OpenEditorWithSaveCallback(temp, async savedLocalPath =>
                {
                    await RemoteFileSystemService.UploadSingleFileAsync(savedLocalPath, remoteTargetUri);
                    StatusNotification = $"Remote gespeichert: {itemName}";
                    await tab.ReloadAsync(itemName);
                });
                return;
            }

            _ui.OpenEditor(item.FullPath);
        }
        catch (Exception ex)
        {
            _ui.ShowMessage("Editor konnte nicht geöffnet werden", ex.Message);
        }
    }

    public async Task OpenFocusedEntryAsync()
    {
        if (ActivePanel.ActiveTab is not { } t || t.FocusedItem is not { } item) return;
        if (item.IsParentLink)
        {
            await t.GoUpAsync();
            return;
        }
        if (item.IsDirectory)
        {
            await t.NavigateAsync(item.FullPath);
            return;
        }

        // Open supported archives (.zip, .7z, .tar.gz, .rar) seamlessly like virtual folders
        if (!t.IsVirtualArchive && !t.IsRemoteConnection && ArchiveService.IsSupportedArchiveFile(item.FullPath))
        {
            await t.NavigateAsync(ArchiveService.BuildVirtualArchivePath(item.FullPath));
            return;
        }

        if (item.Entry.IsArchiveVirtualEntry || item.Entry.IsRemoteEntry)
        {
            await OpenViewerForFocusedAsync();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ui.ShowMessage("Öffnen fehlgeschlagen", ex.Message);
        }
    }

    public async Task CreateNewFileAsync()
    {
        if (ActivePanel.ActiveTab is not { } tab || string.IsNullOrEmpty(tab.CurrentPath)) return;
        if (tab.IsVirtualArchive || tab.IsRemoteConnection || tab.IsSearchResultsTab) return;

        var templates = _creationService.Templates.Templates;
        var def = _creationService.Templates.Default;

        var req = await _ui.ShowNewFileDialogAsync(
            tab.CurrentPath,
            templates,
            def,
            tpl => _creationService.SuggestName(tab.CurrentPath, tpl),
            NameValidator.Validate);

        if (req is null) return;

        var result = await _creationService.CreateFileAsync(tab.CurrentPath, req.Name, req.Template, autoRename: false);
        if (!result.Success || result.FullPath is null)
        {
            _ui.ShowMessage("Datei konnte nicht erstellt werden", result.Error ?? "Unbekannter Fehler");
            return;
        }

        UndoHistory.RecordCreateFile(result.FullPath);
        await tab.ReloadAsync(Path.GetFileName(result.FullPath));
        if (req.OpenAfterCreate)
            _ui.OpenEditor(result.FullPath);
    }

    public async Task CreateNewFolderAsync()
    {
        if (ActivePanel.ActiveTab is not { } tab || string.IsNullOrEmpty(tab.CurrentPath)) return;
        if (tab.IsVirtualArchive || tab.IsSearchResultsTab) return;

        var name = await _ui.ShowNewFolderDialogAsync(tab.CurrentPath, NameValidator.ValidateRelativePath);
        if (string.IsNullOrWhiteSpace(name)) return;

        if (tab.IsRemoteConnection)
        {
            try
            {
                await RemoteFileSystemService.CreateDirectoryAsync(tab.CurrentPath, name.Trim());
                await tab.ReloadAsync(name.Trim());
            }
            catch (Exception ex)
            {
                _ui.ShowMessage("Remote-Ordner konnte nicht erstellt werden", ex.Message);
            }
            return;
        }

        var result = _creationService.CreateFolder(tab.CurrentPath, name);
        if (!result.Success || result.FullPath is null)
        {
            if (result.FullPath is not null && Directory.Exists(result.FullPath))
                tab.SelectByPath(result.FullPath);
            _ui.ShowMessage("Ordner konnte nicht erstellt werden", result.Error ?? "Unbekannter Fehler");
            return;
        }

        UndoHistory.RecordCreateFolder(result.FullPath);
        await tab.ReloadAsync(Path.GetFileName(result.FullPath));
    }

    public async Task RenameFocusedAsync()
    {
        if (ActivePanel.ActiveTab is not { } tab || tab.FocusedItem is not { IsParentLink: false } item) return;
        if (tab.IsVirtualArchive) return;

        int stemLen = item.IsDirectory ? item.Name.Length : NameValidator.StemLength(item.Name);
        var newName = await _ui.PromptAsync("Umbenennen (F2)", $"Neuer Name für „{item.Name}“:", item.Name, stemLen, NameValidator.Validate);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        if (tab.IsRemoteConnection)
        {
            try
            {
                await RemoteFileSystemService.RenameAsync(item.FullPath, newName.Trim());
                await tab.ReloadAsync(newName.Trim());
            }
            catch (Exception ex)
            {
                _ui.ShowMessage("Remote-Umbenennen fehlgeschlagen", ex.Message);
            }
            return;
        }

        string oldFullPath = item.FullPath;
        var handle = await _operationQueue.RunAsync(new FileOperation(OpKind.Rename, [oldFullPath], null, new OperationOptions())
        {
            NewName = newName,
        });

        if (handle.Status == OperationStatus.Failed && handle.Error is not null)
        {
            _ui.ShowMessage("Umbenennen fehlgeschlagen", handle.Error.Message);
            return;
        }

        var parentDir = Path.GetDirectoryName(oldFullPath);
        if (!string.IsNullOrEmpty(parentDir))
            UndoHistory.RecordRename(oldFullPath, Path.Combine(parentDir, newName));

        await tab.ReloadAsync(newName);
    }

    public async Task CopyOrMoveAsync(OpKind kind, bool showDialog)
    {
        if (ActivePanel.ActiveTab is not { } srcTab) return;
        var sources = srcTab.GetTargetPaths();
        if (sources.Count == 0) return;

        var defaultTarget = IsDualMode && OppositePanel.ActiveTab is { } opp && !string.IsNullOrEmpty(opp.CurrentPath)
            ? opp.CurrentPath
            : srcTab.CurrentPath;

        string? destination = defaultTarget;
        if (showDialog || string.Equals(defaultTarget, srcTab.CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            var verb = kind == OpKind.Copy ? "Kopieren (F5)" : "Verschieben (F6)";
            destination = await _ui.PromptAsync(verb, $"Zielordner für {sources.Count} Element(e):", defaultTarget, defaultTarget.Length);
            if (string.IsNullOrWhiteSpace(destination)) return;
        }

        // Virtual archive extraction on F5
        if (srcTab.IsVirtualArchive)
        {
            if (ArchiveService.TryParseVirtualArchivePath(srcTab.CurrentPath, out var archFile, out _))
            {
                try
                {
                    StatusNotification = $"Entpacke aus {Path.GetFileName(archFile)}…";
                    int extracted = 0;
                    foreach (var s in sources)
                    {
                        extracted += await ArchiveService.ExtractArchiveAsync(s, destination!);
                    }
                    StatusNotification = $"{extracted} Datei(en) entpackt";
                    if (OppositePanel.ActiveTab is not null) await OppositePanel.ActiveTab.ReloadAsync();
                }
                catch (Exception ex)
                {
                    _ui.ShowMessage("Entpacken fehlgeschlagen", ex.Message);
                }
            }
            return;
        }

        // Remote download (Remote -> Local)
        if (srcTab.IsRemoteConnection && !RemoteFileSystemService.IsRemoteUri(destination))
        {
            try
            {
                StatusNotification = $"Lade {sources.Count} Element(e) herunter…";
                int count = await RemoteFileSystemService.DownloadEntriesAsync(sources, destination!);
                if (kind == OpKind.Move)
                {
                    foreach (var s in sources)
                    {
                        var entry = srcTab.Items.FirstOrDefault(i => i.FullPath == s);
                        await RemoteFileSystemService.DeleteAsync(s, entry?.IsDirectory ?? false);
                    }
                    await srcTab.ReloadAsync();
                }
                StatusNotification = $"{count} Datei(en) heruntergeladen";
                if (OppositePanel.ActiveTab is not null) await OppositePanel.ActiveTab.ReloadAsync();
            }
            catch (Exception ex)
            {
                _ui.ShowMessage("Download fehlgeschlagen", ex.Message);
            }
            return;
        }

        // Remote upload (Local -> Remote)
        if (!srcTab.IsRemoteConnection && RemoteFileSystemService.IsRemoteUri(destination))
        {
            try
            {
                StatusNotification = $"Lade {sources.Count} Element(e) hoch…";
                int count = await RemoteFileSystemService.UploadEntriesAsync(sources, destination!);
                StatusNotification = $"{count} Datei(en) hochgeladen";
                if (OppositePanel.ActiveTab is not null) await OppositePanel.ActiveTab.ReloadAsync();
            }
            catch (Exception ex)
            {
                _ui.ShowMessage("Upload fehlgeschlagen", ex.Message);
            }
            return;
        }

        // Local Copy / Move: record in UndoHistory
        if (!string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination))
        {
            if (kind == OpKind.Copy)
            {
                var created = sources.Select(s => Path.Combine(destination, Path.GetFileName(s.TrimEnd('\\', '/')))).ToList();
                UndoHistory.RecordCopy(created, $"Kopieren ({sources.Count} Elemente)");
            }
            else if (kind == OpKind.Move)
            {
                var moves = sources.Select(s => (s, Path.Combine(destination, Path.GetFileName(s.TrimEnd('\\', '/'))))).ToList();
                UndoHistory.RecordMove(moves, $"Verschieben ({sources.Count} Elemente)");
            }
        }

        _operationQueue.Enqueue(new FileOperation(kind, sources, destination, new OperationOptions()));
    }

    public async Task DeleteTargetsAsync(bool recycle)
    {
        if (ActivePanel.ActiveTab is not { } tab || tab.IsVirtualArchive) return;
        var targets = tab.GetTargetPaths();
        if (targets.Count == 0) return;

        if (!recycle || Settings.ConfirmDeleteToRecycleBin || tab.IsRemoteConnection)
        {
            var what = targets.Count == 1 ? $"„{Path.GetFileName(targets[0].TrimEnd('\\', '/'))}“" : $"{targets.Count} ausgewählte Elemente";
            var title = (recycle && !tab.IsRemoteConnection) ? "In den Papierkorb verschieben (F8)" : "Endgültig löschen (Shift+F8)";
            var msg = (recycle && !tab.IsRemoteConnection)
                ? $"Möchten Sie {what} in den Papierkorb verschieben?"
                : $"Möchten Sie {what} unwiderruflich löschen?";

            if (!await _ui.ConfirmAsync(title, msg, (recycle && !tab.IsRemoteConnection) ? "In Papierkorb" : "Endgültig löschen", destructive: !recycle || tab.IsRemoteConnection))
                return;
        }

        if (tab.IsRemoteConnection)
        {
            try
            {
                foreach (var target in targets)
                {
                    var item = tab.Items.FirstOrDefault(i => i.FullPath == target);
                    await RemoteFileSystemService.DeleteAsync(target, item?.IsDirectory ?? false);
                }
                StatusNotification = $"{targets.Count} Remote-Element(e) gelöscht";
                await tab.ReloadAsync();
            }
            catch (Exception ex)
            {
                _ui.ShowMessage("Remote-Löschen fehlgeschlagen", ex.Message);
            }
            return;
        }

        _operationQueue.Enqueue(new FileOperation(OpKind.Delete, targets, null, new OperationOptions { Recycle = recycle }));
    }

    public async Task MoveActiveTabToOppositePanelAsync(TabViewModel? tab = null, bool duplicate = false)
    {
        if (!IsDualMode) IsDualMode = true;
        tab ??= ActivePanel.ActiveTab;
        if (tab is null) return;

        var state = duplicate ? tab.ToState() : ActivePanel.DetachTab(tab);
        if (state is null) return;
        if (duplicate)
        {
            state.Id = Guid.NewGuid();
            state.Pinned = false;
        }
        await OppositePanel.AddTabFromStateAsync(state, activate: true);
        ActiveSide = OppositePanel.Side;
    }

    private void OpenTerminal(bool elevated)
    {
        var dir = ActivePanel.ActiveTab?.CurrentPath ?? Environment.CurrentDirectory;
        if (ArchiveService.IsVirtualArchivePath(dir) || RemoteFileSystemService.IsRemoteUri(dir) || dir.StartsWith("search://", StringComparison.OrdinalIgnoreCase))
            dir = Environment.CurrentDirectory;

        if (OperatingSystem.IsMacOS())
        {
            try
            {
                Process.Start(new ProcessStartInfo("open")
                {
                    ArgumentList = { "-a", "Terminal", dir },
                    UseShellExecute = false,
                });
            }
            catch (Exception ex)
            {
                _ui.ShowMessage("Terminal konnte nicht gestartet werden", ex.Message);
            }
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            string[] candidates = ["x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "alacritty", "kitty", "xterm"];
            foreach (var term in candidates)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(term)
                    {
                        WorkingDirectory = dir,
                        UseShellExecute = false,
                    });
                    return;
                }
                catch
                {
                    // Try next installed terminal emulator
                }
            }
            _ui.ShowMessage("Terminal konnte nicht gestartet werden", "Kein kompatibler Terminal-Emulator gefunden.");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo("wt.exe")
            {
                Arguments = $"-d \"{dir}\"",
                UseShellExecute = true,
                Verb = elevated ? "runas" : "",
            };
            Process.Start(psi);
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe")
                {
                    WorkingDirectory = dir,
                    UseShellExecute = true,
                    Verb = elevated ? "runas" : "",
                });
            }
            catch (Exception ex)
            {
                _ui.ShowMessage("Terminal konnte nicht gestartet werden", ex.Message);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposing) return;
        SaveSessionImmediately();
        _isDisposing = true;
        _isInitialized = false;
        _sessionStore.CancelPending();
        Left.Dispose();
        Right.Dispose();
        _sessionStore.Dispose();
        _watcherHub.Dispose();
        await _operationQueue.DisposeAsync();
    }
}

