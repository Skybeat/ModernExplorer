using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ModernExplorer.Core.Commands;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Input;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.Session;
using ModernExplorer.Core.Update;
using ModernExplorer.Core.Viewer;
using ModernExplorer.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace ModernExplorer.App.Dialogs;

public sealed partial class ShellDialogService(MainWindow owner) : IShellUi, IConflictResolver
{
    private XamlRoot Root => owner.RootElement.XamlRoot;

    public async Task<NewFileRequest?> ShowNewFileDialogAsync(
        string directory,
        IReadOnlyList<FileTemplate> templates,
        FileTemplate selected,
        Func<FileTemplate, string> suggestName,
        Func<string, string?> validate)
    {
        var currentTemplate = selected;
        var suggested = suggestName(currentTemplate);
        bool userEditedName = false;
        bool suppressTextChange = false;

        var templateCombo = new ComboBox
        {
            Header = LocalizationService.T("NewFile.Template"),
            ItemsSource = templates,
            SelectedItem = currentTemplate,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var nameBox = new TextBox
        {
            Header = LocalizationService.T("NewFile.FileName"),
            Text = suggested,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.IndianRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var openCheck = new CheckBox
        {
            Content = LocalizationService.T("NewFile.OpenInEditor"),
            IsChecked = false,
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = LocalizationService.Format("NewFile.TargetFolder", directory),
            Opacity = 0.7,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        panel.Children.Add(templateCombo);
        panel.Children.Add(nameBox);
        panel.Children.Add(errorText);
        panel.Children.Add(openCheck);

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = LocalizationService.T("NewFile.Title"),
            Content = panel,
            PrimaryButtonText = LocalizationService.T("NewFile.Create"),
            CloseButtonText = LocalizationService.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        void ValidateNow()
        {
            var err = validate(NameValidator.Normalize(nameBox.Text));
            errorText.Text = err ?? "";
            errorText.Visibility = err is null ? Visibility.Collapsed : Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = err is null;
        }

        templateCombo.SelectionChanged += (_, _) =>
        {
            if (templateCombo.SelectedItem is not FileTemplate tpl) return;
            currentTemplate = tpl;
            if (!userEditedName)
            {
                suppressTextChange = true;
                nameBox.Text = suggestName(tpl);
                suppressTextChange = false;
                SelectStem(nameBox);
            }
            ValidateNow();
        };

        nameBox.TextChanged += (_, _) =>
        {
            if (!suppressTextChange) userEditedName = true;
            ValidateNow();
        };

        nameBox.Loaded += (_, _) =>
        {
            nameBox.Focus(FocusState.Programmatic);
            SelectStem(nameBox);
        };

        ValidateNow();
        var res = await dialog.ShowAsync();
        if (res != ContentDialogResult.Primary) return null;

        return new NewFileRequest(nameBox.Text, currentTemplate, openCheck.IsChecked == true);
    }

    public async Task<string?> ShowNewFolderDialogAsync(string directory, Func<string, string?> validate)
    {
        var suggested = NameValidator.MakeUnique(directory, LocalizationService.T("NewFolder.DefaultName"));
        return await PromptAsync(
            LocalizationService.T("NewFolder.Title"),
            LocalizationService.T("NewFolder.Prompt"),
            suggested,
            suggested.Length,
            validate);
    }

    public async Task<string?> PromptAsync(string title, string label, string initial, int selectLength, Func<string, string?>? validate = null)
    {
        var box = new TextBox
        {
            Header = label,
            Text = initial,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.IndianRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var panel = new StackPanel { Spacing = 8, MinWidth = 400 };
        panel.Children.Add(box);
        panel.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = title,
            Content = panel,
            PrimaryButtonText = LocalizationService.T("Common.Ok"),
            CloseButtonText = LocalizationService.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        void ValidateNow()
        {
            if (validate is null) return;
            var err = validate(NameValidator.Normalize(box.Text));
            errorText.Text = err ?? "";
            errorText.Visibility = err is null ? Visibility.Collapsed : Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = err is null;
        }

        box.TextChanged += (_, _) => ValidateNow();
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.Select(0, Math.Clamp(selectLength, 0, box.Text.Length));
        };

        ValidateNow();
        var res = await dialog.ShowAsync();
        return res == ContentDialogResult.Primary ? box.Text : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, bool destructive = false)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            PrimaryButtonText = primaryText,
            CloseButtonText = LocalizationService.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        var res = await dialog.ShowAsync();
        return res == ContentDialogResult.Primary;
    }

    public void ShowMessage(string title, string message)
    {
        owner.DispatcherQueue.TryEnqueue(async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root,
                RequestedTheme = owner.RootElement.RequestedTheme,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
                CloseButtonText = LocalizationService.T("Common.Close"),
            };
            await dialog.ShowAsync();
        });
    }

    public Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var choice = ConflictChoice.Skip;
                var applyAll = new CheckBox { Content = LocalizationService.T("Conflict.ApplyAll") };

                var combo = new ComboBox
                {
                    Header = LocalizationService.T("Conflict.ChooseAction"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = new[]
                    {
                        LocalizationService.T("Conflict.Overwrite"),
                        LocalizationService.T("Conflict.Skip"),
                        LocalizationService.T("Conflict.Rename"),
                        LocalizationService.T("Conflict.KeepNewer"),
                    },
                    SelectedIndex = 0,
                };

                var infoText = new TextBlock
                {
                    Text = LocalizationService.Format(
                        "Conflict.InfoFormat",
                        conflict.Destination,
                        $"{Format.Size(conflict.SourceSize)} · {conflict.SourceModified:dd.MM.yyyy HH:mm}",
                        $"{Format.Size(conflict.DestinationSize)} · {conflict.DestinationModified:dd.MM.yyyy HH:mm}"),
                    TextWrapping = TextWrapping.Wrap,
                };

                var panel = new StackPanel { Spacing = 10, MinWidth = 440 };
                panel.Children.Add(infoText);
                panel.Children.Add(combo);
                panel.Children.Add(applyAll);

                var dialog = new ContentDialog
                {
                    XamlRoot = Root,
                    RequestedTheme = owner.RootElement.RequestedTheme,
                    Title = LocalizationService.T("Conflict.Title"),
                    Content = panel,
                    PrimaryButtonText = LocalizationService.T("Conflict.Execute"),
                    CloseButtonText = LocalizationService.T("Conflict.CancelOp"),
                    DefaultButton = ContentDialogButton.Primary,
                };

                var res = await dialog.ShowAsync();
                if (res != ContentDialogResult.Primary)
                {
                    tcs.TrySetResult(new ConflictDecision(ConflictChoice.Cancel, false));
                    return;
                }

                choice = combo.SelectedIndex switch
                {
                    0 => ConflictChoice.Overwrite,
                    1 => ConflictChoice.Skip,
                    2 => ConflictChoice.Rename,
                    3 => ConflictChoice.KeepNewer,
                    _ => ConflictChoice.Skip,
                };
                tcs.TrySetResult(new ConflictDecision(choice, applyAll.IsChecked == true));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    public void FocusFileList(PanelSide side) => owner.FocusFileList(side);
    public void FocusAddressBar() => owner.FocusActiveAddressBar();
    public void FocusFilter() => owner.FocusActiveFilterBox();
    public void ToggleFullScreen() => owner.ToggleFullScreen();

    public void ShowCommandPalette() => _ = ShowCommandPaletteDialogAsync();
    public void ShowHelp() => _ = ShowHelpDialogAsync();

    public void OpenNewWindow(TabState tab) => owner.OpenTearOutWindow(tab);

    public void OpenViewer(string path) => _ = ShowQuickViewerAsync(path);
    public void OpenEditor(string path) => _ = ShowEditorAsync(path, null);
    public void OpenEditorWithSaveCallback(string path, Func<string, Task> onSaveAsync) => _ = ShowEditorAsync(path, onSaveAsync);

    public void SetClipboardText(string text)
    {
        var pkg = new DataPackage();
        pkg.SetText(text);
        Clipboard.SetContent(pkg);
    }

    public async Task SetClipboardFilesAsync(IReadOnlyList<string> paths, bool cut)
    {
        var items = new List<IStorageItem>();
        foreach (var p in paths)
        {
            try
            {
                if (Directory.Exists(p)) items.Add(await StorageFolder.GetFolderFromPathAsync(p));
                else if (File.Exists(p)) items.Add(await StorageFile.GetFileFromPathAsync(p));
            }
            catch { }
        }
        if (items.Count == 0) return;
        var pkg = new DataPackage
        {
            RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy,
        };
        pkg.SetStorageItems(items);
        Clipboard.SetContent(pkg);
    }

    public async Task<(IReadOnlyList<string> Paths, bool Cut)?> GetClipboardFilesAsync()
    {
        var view = Clipboard.GetContent();
        if (!view.Contains(StandardDataFormats.StorageItems)) return null;
        var items = await view.GetStorageItemsAsync();
        var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        bool cut = view.RequestedOperation == DataPackageOperation.Move;
        return (paths, cut);
    }

    private async Task ShowCommandPaletteDialogAsync()
    {
        var vm = owner.ViewModel;
        var all = vm.Commands.All.Select(c =>
        {
            var b = vm.KeyBindings.FindFirstForCommand(c.Id);
            return new PaletteItem(c.Id, $"[{c.Category}] {c.Title}", b?.KeyText ?? "");
        }).ToList();

        var searchBox = new TextBox
        {
            PlaceholderText = LocalizationService.T("Palette.SearchPlaceholder"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var list = new ListView
        {
            ItemsSource = all,
            SelectedIndex = 0,
            Height = 320,
            DisplayMemberPath = nameof(PaletteItem.Display),
        };

        var panel = new StackPanel { Spacing = 8, MinWidth = 520 };
        panel.Children.Add(searchBox);
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = LocalizationService.T("Palette.Title"),
            Content = panel,
            PrimaryButtonText = LocalizationService.T("Palette.Execute"),
            CloseButtonText = LocalizationService.T("Common.Close"),
            DefaultButton = ContentDialogButton.Primary,
        };

        searchBox.TextChanged += (_, _) =>
        {
            var q = searchBox.Text.Trim();
            var filtered = string.IsNullOrEmpty(q)
                ? all
                : all.Where(i => i.Display.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                 i.Id.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = filtered;
            if (filtered.Count > 0) list.SelectedIndex = 0;
        };

        searchBox.Loaded += (_, _) => searchBox.Focus(FocusState.Programmatic);

        var res = await dialog.ShowAsync();
        if (res == ContentDialogResult.Primary && list.SelectedItem is PaletteItem chosen)
            vm.Commands.Execute(chosen.Id, null, FocusContext.For(ContextFlags.FileList, ContextFlags.Panel, ContextFlags.HasFocusedItem));
    }

    private sealed record PaletteItem(string Id, string Label, string Key)
    {
        public string Display => string.IsNullOrEmpty(Key) ? Label : $"{Label}   ({Key})";
    }

    private async Task ShowHelpDialogAsync()
    {
        string helpText = LocalizationService.Instance.EffectiveLanguage != "de"
            ? """
              Function Keys (Commander + ModernExplorer):
                F1              Help & Keyboard Reference
                F2              Rename
                F3              Quick Viewer (Text, Markdown, JSON, Log with Follow, Hex)
                F4              Internal Editor (atomic save with Ctrl+S)
                F5 / Shift+F5   Copy to opposite panel / with dialog
                F6 / Shift+F6   Move to opposite panel / with dialog
                F7              Create new file (with templates)
                Shift+F7        Create new folder (supports nested a\b\c)
                F8 / Del        Delete to Recycle Bin
                Shift+F8        Delete permanently
                F9 / Shift+F9   Open Terminal / as Administrator
                F10             Open Command Palette
                F11             Toggle Fullscreen
                F12             Toggle Single/Dual Panel (Shift+F12: swap, Ctrl+F12: equalize)

              Navigation & Selection:
                Tab             Switch between left and right panel
                Enter / Back    Open / Parent directory
                Alt+← / Alt+→   History back / forward
                Ctrl+L / Alt+D  Focus address bar
                Ctrl+F          Focus inline filter
                Space / Ins     Select item (+ calculate folder size)
                Ctrl+A / Num *  Select all / Invert selection

              Tab Management:
                Ctrl+T / Ctrl+W New Tab / Close Tab (Middle-click also closes)
                Ctrl+Shift+T    Restore closed tab
                Ctrl+Shift+D    Duplicate tab
                Ctrl+K Ctrl+P   Pin tab
                Ctrl+K Ctrl+L   Lock tab (subfolders open in new tab)
                Ctrl+Tab        Next tab (Ctrl+Shift+Tab: previous, Ctrl+1..9: direct)
                Ctrl+Shift+←/→  Move tab to opposite panel
                Ctrl+Shift+N    Tear out tab into new window

              Power Tools & Views:
                Ctrl+M          Batch Rename Tool
                Ctrl+Z          Undo last action
                Ctrl+B          Flat Branch View
                Ctrl+Q          Integrated Quick Preview Panel
                Ctrl+D          Toggle current folder as favorite
                Alt+F7          Recursive Deep Search (also Ctrl+Shift+F)
                Ctrl+Shift+S    Compare & Synchronize Directories
                Ctrl+Shift+W    Storage Analyzer & Treemap (WinDirStat view)
                Alt+F5 / Alt+F9 Pack to archive / Extract archive (.zip, .7z, .tar.gz)
                Ctrl+Shift+1..8 Select view mode directly (Icons, List, Details, Tiles, Content)
              """
            : """
              Funktionstasten (Commander + ModernExplorer):
                F1              Hilfe & Tastaturübersicht
                F2              Umbenennen
                F3              Quick-Viewer (Text, Markdown, JSON, Log mit Follow, Hex)
                F4              Interner Editor (atomares Speichern mit Strg+S)
                F5 / Shift+F5   Kopieren ins Gegenpanel / mit Dialog
                F6 / Shift+F6   Verschieben ins Gegenpanel / mit Dialog
                F7              Neue Datei erstellen (mit Vorlagen/Templates)
                Shift+F7        Neuen Ordner erstellen (auch verschachtelt a\b\c)
                F8 / Entf       In den Papierkorb löschen
                Shift+F8        Endgültig löschen
                F9 / Shift+F9   Terminal öffnen / als Administrator
                F10             Command Palette öffnen
                F11             Vollbild umschalten
                F12             Single-/Dual-Panel umschalten (Shift+F12: tauschen, Strg+F12: angleichen)

              Navigation & Auswahl:
                Tab             Zwischen linkem und rechtem Panel wechseln
                Enter / Rück    Öffnen / Übergeordneter Ordner
                Alt+← / Alt+→   Verlauf zurück / vorwärts
                Strg+L / Alt+D  Adressleiste fokussieren
                Strg+F          Inline-Filter fokussieren
                Leertaste/Einfg Eintrag markieren (+ Ordnergröße berechnen)
                Strg+A / Num *  Alles markieren / Auswahl invertieren

              Tab-Management:
                Strg+T / Strg+W Neuer Tab / Tab schließen (Mittelklick schließt ebenfalls)
                Strg+Shift+T    Geschlossenen Tab wiederherstellen
                Strg+Shift+D    Tab duplizieren
                Strg+K Strg+P   Tab anheften (Pin)
                Strg+K Strg+L   Tab sperren (Lock – Unterordner öffnen neuen Tab)
                Strg+Tab        Nächster Tab (Strg+Shift+Tab: vorheriger, Strg+1..9: direkt)
                Strg+Shift+←/→  Tab ins Gegenpanel verschieben
                Strg+Shift+N    Tab in neues Fenster herauslösen

              Power-Werkzeuge & Ansichten:
                Strg+M          Mehrfach-Umbenennungstool (Batch-Rename)
                Strg+Z          Letzte Aktion rückgängig machen (Undo)
                Strg+B          Flache Ordneransicht (Branch View)
                Strg+Q          Integriertes Schnellvorschau-Panel
                Strg+D          Aktuellen Ordner als Favorit merken / entfernen
                Alt+F7          Rekursive Tiefensuche (auch Strg+Shift+F)
                Strg+Shift+S    Verzeichnisse vergleichen & synchronisieren
                Strg+Shift+W    Speicherplatz-Analyse & Treemap (WinDirStat-Ansicht)
                Alt+F5 / Alt+F9 In Archiv packen / Archiv entpacken (.zip, .7z, .tar.gz)
                Strg+Shift+1..8 Ansichtsmodus direkt wählen (Symbole, Liste, Details, Kacheln, Inhalt)
              """;

        var scroll = new ScrollViewer
        {
            MaxHeight = 460,
            Content = new TextBlock
            {
                Text = helpText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = owner.ViewModel.Settings.GetChildWindowFontSize(ChildWindowFontTarget.AboutWindow),
                IsTextSelectionEnabled = true,
            },
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = LocalizationService.T("Help.WindowTitle"),
            Content = scroll,
            CloseButtonText = LocalizationService.T("Common.Close"),
        };
        await dialog.ShowAsync();
    }

    private async Task ShowQuickViewerAsync(string path) =>
        await QuickViewerWindowBuilder.ShowAsync(owner, this, path);

    private async Task ShowEditorAsync(string path, Func<string, Task>? onSaveAsync = null)
    {
        var fi = new FileInfo(path);
        if (fi.Exists && fi.Length > FileContentService.LargeFileWarningBytes)
        {
            var proceed = await ConfirmAsync(
                LocalizationService.T("Editor.LargeFileTitle"),
                LocalizationService.Format("Editor.LargeFileMsg", Format.Size(fi.Length)),
                LocalizationService.T("Editor.OpenQuickViewer"));
            if (proceed)
            {
                await ShowQuickViewerAsync(path);
                return;
            }
        }

        LoadedDocument doc;
        try
        {
            doc = await FileContentService.LoadPreviewAsync(path, maxBytes: 10 * 1024 * 1024);
        }
        catch (Exception ex)
        {
            ShowMessage(LocalizationService.T("Editor.ErrorTitle"), ex.Message);
            return;
        }

        var statusLabel = new TextBlock
        {
            Text = $"{doc.Encoding.WebName.ToUpperInvariant()} · {(doc.NewLine == "\r\n" ? "CRLF" : "LF")} · {Format.Size(doc.FileSize)}",
            Opacity = 0.75,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var editorBox = new TextBox
        {
            Text = doc.Content,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(editorBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(editorBox, ScrollBarVisibility.Auto);

        async Task SaveNowAsync()
        {
            try
            {
                await FileContentService.SaveAtomicAsync(path, editorBox.Text, doc.Encoding, doc.NewLine);
                if (onSaveAsync is not null)
                {
                    statusLabel.Text = LocalizationService.T("Editor.UploadingRemote");
                    await onSaveAsync(path);
                    statusLabel.Text = LocalizationService.Format("Editor.SavedRemoteAt", DateTime.Now.ToString("HH:mm:ss"), doc.Encoding.WebName.ToUpperInvariant());
                }
                else
                {
                    statusLabel.Text = LocalizationService.Format("Editor.SavedAt", DateTime.Now.ToString("HH:mm:ss"), doc.Encoding.WebName.ToUpperInvariant());
                    if (owner.ViewModel.ActivePanel.ActiveTab is { } t)
                        await t.ReloadAsync(Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = LocalizationService.Format("Editor.SaveError", ex.Message);
            }
        }

        Window? win = null;

        editorBox.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.S && VirtualKeyMapper.GetCurrentModifiers().HasFlag(KeyModifiers.Ctrl))
            {
                e.Handled = true;
                await SaveNowAsync();
            }
        };

        var saveBtn = new Button
        {
            Content = LocalizationService.T("Editor.SaveBtn"),
            Padding = new Thickness(12, 6, 12, 6),
        };
        saveBtn.Click += async (_, _) => await SaveNowAsync();

        var closeBtn = new Button
        {
            Content = LocalizationService.T("Editor.CloseBtn"),
            Padding = new Thickness(12, 6, 12, 6),
        };
        closeBtn.Click += (_, _) => win?.Close();

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(statusLabel, 0);
        var btnStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        btnStack.Children.Add(saveBtn);
        btnStack.Children.Add(closeBtn);
        Grid.SetColumn(btnStack, 1);
        footerGrid.Children.Add(statusLabel);
        footerGrid.Children.Add(btnStack);

        var bodyGrid = new Grid { RowSpacing = 10, Padding = new Thickness(16, 12, 16, 14) };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(editorBox, 0);
        Grid.SetRow(footerGrid, 1);
        bodyGrid.Children.Add(editorBox);
        bodyGrid.Children.Add(footerGrid);

        string fileName = Path.GetFileName(path);
        win = CreateResizableChildWindow(
            LocalizationService.Format("Editor.WindowTitle", fileName),
            "✏️",
            bodyGrid,
            fontTarget: ChildWindowFontTarget.Editor,
            titleProvider: () => LocalizationService.Format("Editor.WindowTitle", fileName));

        void OnEditorLanguageChanged(object? s, EventArgs e)
        {
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                saveBtn.Content = LocalizationService.T("Editor.SaveBtn");
                closeBtn.Content = LocalizationService.T("Editor.CloseBtn");
            });
        }

        LocalizationService.Instance.LanguageChanged += OnEditorLanguageChanged;
        win.Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnEditorLanguageChanged;
        win.Activate();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, TextBlock> ChildWindowTitleBlocks = new();

    internal static void UpdateChildWindowTitle(Window? win, string newTitle)
    {
        if (win is null) return;
        win.Title = newTitle;
        if (ChildWindowTitleBlocks.TryGetValue(win, out var tb))
            tb.Text = newTitle;
    }

    internal Window CreateResizableChildWindow(
        string title,
        string iconGlyph,
        UIElement bodyContent,
        int? defaultWidth = null,
        int? defaultHeight = null,
        ChildWindowFontTarget fontTarget = ChildWindowFontTarget.General,
        Func<string>? titleProvider = null)
    {
        var settings = owner.ViewModel.Settings;
        var baselineFontSizes = new Dictionary<DependencyObject, double>();

        var root = new Grid
        {
            RequestedTheme = settings.Theme == ModernExplorer.Core.Settings.AppTheme.Light
                ? ElementTheme.Light
                : ElementTheme.Dark,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Custom title bar matching MainWindow and SettingsWindow (no white default title bar in Dark Mode)
        var titleBarBorder = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var titleBarGrid = new Grid();
        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        var dragRegion = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
        };
        Grid.SetColumn(dragRegion, 0);
        titleBarGrid.Children.Add(dragRegion);

        var titleStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        titleStack.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.png")),
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = iconGlyph,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var titleTextBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleStack.Children.Add(titleTextBlock);
        Grid.SetColumn(titleStack, 0);
        titleBarGrid.Children.Add(titleStack);
        titleBarBorder.Child = titleBarGrid;

        Grid.SetRow(titleBarBorder, 0);
        root.Children.Add(titleBarBorder);

        if (bodyContent is FrameworkElement bodyFe)
        {
            Grid.SetRow(bodyFe, 1);
        }
        root.Children.Add(bodyContent);

        var win = new Window
        {
            Title = title,
            Content = root,
        };
        ChildWindowTitleBlocks.AddOrUpdate(win, titleTextBlock);

        void ApplyChildTheme()
        {
            bool isLight = settings.Theme == ModernExplorer.Core.Settings.AppTheme.Light;
            root.Background = new SolidColorBrush(isLight
                ? Color.FromArgb(255, 245, 246, 250)
                : Color.FromArgb(255, 24, 24, 30));
            titleBarBorder.Background = new SolidColorBrush(isLight
                ? Color.FromArgb(255, 235, 238, 244)
                : Color.FromArgb(255, 27, 29, 36));
            titleBarBorder.BorderBrush = new SolidColorBrush(isLight
                ? Color.FromArgb(255, 210, 214, 224)
                : Color.FromArgb(255, 48, 50, 62));

            WindowChromeHelper.ApplyCustomTitleBarAndTheme(win, root, dragRegion, settings.Theme);
        }

        void ApplyChildFontScaling()
        {
            double targetFont = settings.GetChildWindowFontSize(fontTarget);
            ApplyFontScalingToTree(bodyContent, targetFont, baselineFontSizes);
        }

        void ApplyChildTitleLocalization()
        {
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                string updatedTitle = titleProvider is not null ? titleProvider() : win.Title;
                win.Title = updatedTitle;
                titleTextBlock.Text = updatedTitle;
            });
        }

        ApplyChildTheme();
        ApplyChildFontScaling();

        root.Loaded += (_, _) =>
        {
            ApplyChildTheme();
            ApplyChildFontScaling();
            titleTextBlock.Text = win.Title;
        };

        root.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                win.Close();
            }
        };

        void OnThemeChanged(object? s, EventArgs e) => ApplyChildTheme();
        void OnFontSizesChanged(object? s, EventArgs e) => ApplyChildFontScaling();
        void OnLanguageChanged(object? s, EventArgs e) => ApplyChildTitleLocalization();

        settings.ThemeChanged += OnThemeChanged;
        settings.FontSizesChanged += OnFontSizesChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        owner.RegisterChildWindow(win);

        try
        {
            win.SystemBackdrop = new MicaBackdrop();
            if (win.AppWindow is { } aw)
            {
                if (aw.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.IsResizable = true;
                    presenter.IsMaximizable = true;
                    presenter.IsMinimizable = false;
                }

                int w = Math.Max(defaultWidth ?? 860, settings.ChildWindowWidth);
                int h = Math.Max(defaultHeight ?? 640, settings.ChildWindowHeight);

                if (owner.AppWindow is { } ownerAw)
                {
                    var pos = ownerAw.Position;
                    var size = ownerAw.Size;
                    int x = pos.X + (size.Width - w) / 2;
                    int y = pos.Y + (size.Height - h) / 2;
                    aw.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
                }
                else
                {
                    aw.Resize(new Windows.Graphics.SizeInt32(w, h));
                }

                if (settings.ChildWindowIsMaximized && aw.Presenter is Microsoft.UI.Windowing.OverlappedPresenter maxPresenter)
                    maxPresenter.Maximize();

                aw.Changed += (_, args) =>
                {
                    if (args.DidSizeChange || args.DidPresenterChange)
                    {
                        if (aw.Presenter is Microsoft.UI.Windowing.OverlappedPresenter pMin &&
                            pMin.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                        {
                            return;
                        }

                        bool isMax = aw.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p &&
                                     p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
                        settings.UpdateChildWindowBounds(aw.Size.Width, aw.Size.Height, isMax);
                    }
                };
            }
        }
        catch { }

        win.Closed += (_, _) =>
        {
            settings.ThemeChanged -= OnThemeChanged;
            settings.FontSizesChanged -= OnFontSizesChanged;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            try
            {
                if (win.AppWindow is { } aw)
                {
                    if (aw.Presenter is Microsoft.UI.Windowing.OverlappedPresenter pMin &&
                        pMin.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                    {
                        return;
                    }

                    bool isMax = aw.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p &&
                                 p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
                    settings.UpdateChildWindowBounds(aw.Size.Width, aw.Size.Height, isMax);
                }
            }
            catch { }
        };

        return win;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, System.Runtime.CompilerServices.StrongBox<double>> SharedBaselineFontSizes = new();

    internal static void ApplyFontScalingToTree(
        DependencyObject? element,
        double targetFontSize,
        Dictionary<DependencyObject, double>? baselineSizes = null)
    {
        if (element is null) return;
        double scale = Math.Clamp(targetFontSize / 13.0, 0.6, 2.2);
        var visited = new HashSet<DependencyObject>();

        double GetOrStoreBaseline(DependencyObject obj, double currentFontSize)
        {
            if (baselineSizes is not null)
            {
                if (!baselineSizes.TryGetValue(obj, out double b))
                {
                    b = currentFontSize > 0 ? currentFontSize : 13.0;
                    baselineSizes[obj] = b;
                }
                return b;
            }

            if (!SharedBaselineFontSizes.TryGetValue(obj, out var box))
            {
                double b = currentFontSize > 0 ? currentFontSize : 13.0;
                SharedBaselineFontSizes.Add(obj, new System.Runtime.CompilerServices.StrongBox<double>(b));
                return b;
            }
            return box.Value;
        }

        void Walk(DependencyObject? node)
        {
            if (node is null || !visited.Add(node))
                return;

            if (node is TextBlock tb)
            {
                double baseSize = GetOrStoreBaseline(tb, tb.FontSize);
                tb.FontSize = Math.Clamp(Math.Round(baseSize * scale * 2.0) / 2.0, 8.0, 32.0);
            }
            else if (node is Control ctrl)
            {
                double baseSize = GetOrStoreBaseline(ctrl, ctrl.FontSize);
                ctrl.FontSize = Math.Clamp(Math.Round(baseSize * scale * 2.0) / 2.0, 8.0, 32.0);
            }

            if (node is Panel panel)
            {
                foreach (var child in panel.Children)
                    Walk(child);
            }
            else if (node is Border border && border.Child is { } borderChild)
            {
                Walk(borderChild);
            }
            else if (node is ScrollViewer sv && sv.Content is DependencyObject svContent)
            {
                Walk(svContent);
            }
            else if (node is ContentControl cc && cc.Content is DependencyObject ccContent)
            {
                Walk(ccContent);
            }

            if (node is ItemsControl ic && ic.Items is { Count: > 0 })
            {
                foreach (var item in ic.Items)
                {
                    if (item is DependencyObject itemDo)
                        Walk(itemDo);
                }
            }

            try
            {
                int vCount = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < vCount; i++)
                {
                    Walk(VisualTreeHelper.GetChild(node, i));
                }
            }
            catch { }
        }

        Walk(element);
    }

    /// <summary>
    /// Displays an interactive dialog informing the user about a new GitHub Release
    /// and offering an immediate self-update download/installation.
    /// </summary>
    public async Task ShowUpdateAvailableDialogAsync(UpdateCheckResult checkResult, GitHubUpdateService updateService)
    {
        if (checkResult.LatestRelease is not { } release) return;

        var loc = LocalizationService.Instance;
        var panel = new StackPanel { Spacing = 10, MinWidth = 540 };

        panel.Children.Add(new TextBlock
        {
            Text = loc["Update.AvailableHeading"],
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
        });

        var verGrid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        verGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        verGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        verGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        verGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var curLabel = new TextBlock { Text = loc["Update.CurrentVersion"], Opacity = 0.75 };
        var curVal = new TextBlock { Text = $"v{GitHubUpdateService.GetCurrentVersionString()}", FontFamily = new FontFamily("Consolas") };
        Grid.SetRow(curLabel, 0); Grid.SetColumn(curLabel, 0);
        Grid.SetRow(curVal, 0); Grid.SetColumn(curVal, 1);

        var newLabel = new TextBlock { Text = loc["Update.LatestVersion"], Opacity = 0.75 };
        var newVal = new TextBlock
        {
            Text = $"{release.TagName} ({release.Title})",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 56, 189, 248)),
            FontFamily = new FontFamily("Consolas"),
        };
        Grid.SetRow(newLabel, 1); Grid.SetColumn(newLabel, 0);
        Grid.SetRow(newVal, 1); Grid.SetColumn(newVal, 1);

        verGrid.Children.Add(curLabel);
        verGrid.Children.Add(curVal);
        verGrid.Children.Add(newLabel);
        verGrid.Children.Add(newVal);
        panel.Children.Add(verGrid);

        // Header row: Release Notes label on the left + direct clickable browser link on the right
        var notesHeaderGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        notesHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        notesHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var notesTitle = new TextBlock
        {
            Text = $"📋 {loc["Update.ReleaseNotes"]}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(notesTitle, 0);
        notesHeaderGrid.Children.Add(notesTitle);

        var browserLinkBtn = new HyperlinkButton
        {
            Content = $"🌐 {loc["Update.OpenGitHub"]} ({release.TagName}) ↗",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(browserLinkBtn, release.HtmlUrl);
        browserLinkBtn.Click += (_, _) => GitHubUpdateService.OpenUrlInBrowser(release.HtmlUrl);
        Grid.SetColumn(browserLinkBtn, 1);
        notesHeaderGrid.Children.Add(browserLinkBtn);

        panel.Children.Add(notesHeaderGrid);

        string formattedNotes = GitHubUpdateService.FormatReleaseNotesForDisplay(release.Body);
        if (!string.IsNullOrWhiteSpace(formattedNotes))
        {
            var notesTextBlock = new TextBlock
            {
                Text = formattedNotes,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                LineHeight = 20,
                FontSize = 12.5,
                Padding = new Thickness(2, 0, 6, 2),
            };

            var notesScroll = new ScrollViewer
            {
                Content = notesTextBlock,
                Height = 240,
                MinHeight = 180,
                MaxHeight = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var notesContainer = new Border
            {
                Child = notesScroll,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 8, 10),
                BorderThickness = new Thickness(1),
                Background = Application.Current.Resources.TryGetValue("ExplorerPanelBackgroundBrush", out var bg) && bg is Brush bgBrush
                    ? bgBrush
                    : new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                BorderBrush = Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var bd) && bd is Brush bdBrush
                    ? bdBrush
                    : new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)),
            };

            panel.Children.Add(notesContainer);
        }

        var dlg = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = $"🚀 {loc["Update.AvailableTitle"]} – {release.TagName}",
            Content = panel,
            PrimaryButtonText = loc["Update.UpdateNow"],
            SecondaryButtonText = loc["Update.OpenGitHub"],
            CloseButtonText = loc["Update.Later"],
            DefaultButton = ContentDialogButton.Primary,
        };
        dlg.Resources["ContentDialogMaxWidth"] = 700.0;
        dlg.Resources["ContentDialogMaxHeight"] = 680.0;

        var choice = await dlg.ShowAsync();
        if (choice == ContentDialogResult.Primary)
        {
            owner.ViewModel.StatusNotification = LocalizationService.Format("Update.DownloadingMsg", release.TagName);
            try
            {
                bool shouldExit = await updateService.DownloadAndInstallUpdateAsync(release);
                if (shouldExit)
                {
                    owner.PrepareForUpdateAndExit();
                }
            }
            catch (Exception ex)
            {
                ShowMessage(loc["Update.ErrorTitle"], ex.Message);
            }
        }
        else if (choice == ContentDialogResult.Secondary)
        {
            GitHubUpdateService.OpenUrlInBrowser(release.HtmlUrl);
        }
    }

    private static void SelectStem(TextBox box)
    {
        int stem = NameValidator.StemLength(box.Text);
        box.Select(0, Math.Clamp(stem, 0, box.Text.Length));
    }
}


