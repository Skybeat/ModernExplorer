using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.Commands;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Input;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Operations;
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
    public ObservableCollection<OperationItemViewModel> Operations { get; } = [];

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
        SettingsStore? settingsStore = null)
    {
        Settings = new SettingsViewModel(settingsStore);
        KeyBindings = keyBindings;
        _watcherHub = watcherHub;
        _operationQueue = operationQueue;
        _creationService = creationService;
        _sessionStore = sessionStore;
        _dispatcher = dispatcher;
        _ui = ui;

        Left = new PanelViewModel(PanelSide.Left, watcherHub, dispatcher, OnTabClosed) { IsActivePanel = true };
        Right = new PanelViewModel(PanelSide.Right, watcherHub, dispatcher, OnTabClosed) { IsActivePanel = false };

        Left.StateChanged += (_, _) => RequestSessionSave();
        Right.StateChanged += (_, _) => RequestSessionSave();

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

            // File operations (F2–F9)
            .Register("file.rename", "Umbenennen (F2)", "Datei", async _ => await RenameFocusedAsync())
            .Register("file.view", "Quick-Viewer öffnen (F3)", "Datei", _ =>
            {
                var f = ActivePanel.ActiveTab?.FocusedItem;
                if (f is { IsParentLink: false }) _ui.OpenViewer(f.FullPath);
            })
            .Register("file.edit", "Im Editor öffnen (F4)", "Datei", _ =>
            {
                var f = ActivePanel.ActiveTab?.FocusedItem;
                if (f is { IsDirectory: false }) _ui.OpenEditor(f.FullPath);
            })
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

        await tab.ReloadAsync(Path.GetFileName(result.FullPath));
        if (req.OpenAfterCreate)
            _ui.OpenEditor(result.FullPath);
    }

    public async Task CreateNewFolderAsync()
    {
        if (ActivePanel.ActiveTab is not { } tab || string.IsNullOrEmpty(tab.CurrentPath)) return;
        var name = await _ui.ShowNewFolderDialogAsync(tab.CurrentPath, NameValidator.ValidateRelativePath);
        if (string.IsNullOrWhiteSpace(name)) return;

        var result = _creationService.CreateFolder(tab.CurrentPath, name);
        if (!result.Success || result.FullPath is null)
        {
            if (result.FullPath is not null && Directory.Exists(result.FullPath))
                tab.SelectByPath(result.FullPath);
            _ui.ShowMessage("Ordner konnte nicht erstellt werden", result.Error ?? "Unbekannter Fehler");
            return;
        }

        await tab.ReloadAsync(Path.GetFileName(result.FullPath));
    }

    public async Task RenameFocusedAsync()
    {
        if (ActivePanel.ActiveTab is not { } tab || tab.FocusedItem is not { IsParentLink: false } item) return;
        int stemLen = item.IsDirectory ? item.Name.Length : NameValidator.StemLength(item.Name);
        var newName = await _ui.PromptAsync("Umbenennen (F2)", $"Neuer Name für „{item.Name}“:", item.Name, stemLen, NameValidator.Validate);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        var handle = await _operationQueue.RunAsync(new FileOperation(OpKind.Rename, [item.FullPath], null, new OperationOptions())
        {
            NewName = newName,
        });

        if (handle.Status == OperationStatus.Failed && handle.Error is not null)
        {
            _ui.ShowMessage("Umbenennen fehlgeschlagen", handle.Error.Message);
            return;
        }
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

        _operationQueue.Enqueue(new FileOperation(kind, sources, destination, new OperationOptions()));
    }

    public async Task DeleteTargetsAsync(bool recycle)
    {
        if (ActivePanel.ActiveTab is not { } tab) return;
        var targets = tab.GetTargetPaths();
        if (targets.Count == 0) return;

        if (!recycle || Settings.ConfirmDeleteToRecycleBin)
        {
            var what = targets.Count == 1 ? $"„{Path.GetFileName(targets[0].TrimEnd('\\'))}“" : $"{targets.Count} ausgewählte Elemente";
            var title = recycle ? "In den Papierkorb verschieben (F8)" : "Endgültig löschen (Shift+F8)";
            var msg = recycle
                ? $"Möchten Sie {what} in den Papierkorb verschieben?"
                : $"Möchten Sie {what} unwiderruflich löschen?";

            if (!await _ui.ConfirmAsync(title, msg, recycle ? "In Papierkorb" : "Endgültig löschen", destructive: !recycle))
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
