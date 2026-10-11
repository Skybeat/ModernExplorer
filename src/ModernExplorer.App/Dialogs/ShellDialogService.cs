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
            Header = "Vorlage (Template)",
            ItemsSource = templates,
            SelectedItem = currentTemplate,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var nameBox = new TextBox
        {
            Header = "Dateiname",
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
            Content = "Nach dem Erstellen im internen Editor öffnen (F4)",
            IsChecked = false,
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Zielordner: {directory}",
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
            Title = "Neue Datei erstellen (F7)",
            Content = panel,
            PrimaryButtonText = "Erstellen",
            CloseButtonText = "Abbrechen",
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
        var suggested = NameValidator.MakeUnique(directory, "Neuer Ordner");
        return await PromptAsync(
            "Neuen Ordner erstellen (Shift+F7)",
            "Ordnername (Tipp: „a\\b\\c“ legt verschachtelte Ordner an):",
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
            PrimaryButtonText = "OK",
            CloseButtonText = "Abbrechen",
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
            CloseButtonText = "Abbrechen",
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
                CloseButtonText = "Schließen",
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
                var applyAll = new CheckBox { Content = "Für alle weiteren Konflikte in diesem Vorgang übernehmen" };

                var combo = new ComboBox
                {
                    Header = "Aktion wählen",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = new[]
                    {
                        "Überschreiben (Zieldatei ersetzen)",
                        "Überspringen (Zieldatei behalten)",
                        "Automatisch umbenennen (Suffix anhängen)",
                        "Neuere Datei behalten",
                    },
                    SelectedIndex = 0,
                };

                var infoText = new TextBlock
                {
                    Text = $"Ziel existiert bereits:\n{conflict.Destination}\n\n" +
                           $"Quelle: {Format.Size(conflict.SourceSize)} · {conflict.SourceModified:dd.MM.yyyy HH:mm}\n" +
                           $"Ziel:   {Format.Size(conflict.DestinationSize)} · {conflict.DestinationModified:dd.MM.yyyy HH:mm}",
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
                    Title = "Dateikonflikt",
                    Content = panel,
                    PrimaryButtonText = "Ausführen",
                    CloseButtonText = "Vorgang abbrechen",
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
            PlaceholderText = "Befehl suchen (z. B. „Neue Datei“, „Tab“, „Sortierung“) …",
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
            Title = "Command Palette (F10)",
            Content = panel,
            PrimaryButtonText = "Ausführen",
            CloseButtonText = "Schließen",
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
        const string helpText =
            """
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
                FontSize = 13,
                IsTextSelectionEnabled = true,
            },
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = "ModernExplorer – Tastatur-Referenz (F1)",
            Content = scroll,
            CloseButtonText = "Schließen",
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
                "Große Datei (> 50 MB)",
                $"Die Datei ist {Format.Size(fi.Length)} groß. Möchten Sie stattdessen den schnellen Quick-Viewer (F3) öffnen?",
                "Quick-Viewer öffnen");
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
            ShowMessage("Editor Fehler", ex.Message);
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
                    statusLabel.Text = "Lade auf Remote-Server hoch…";
                    await onSaveAsync(path);
                    statusLabel.Text = $"Remote gespeichert um {DateTime.Now:HH:mm:ss} · {doc.Encoding.WebName.ToUpperInvariant()}";
                }
                else
                {
                    statusLabel.Text = $"Gespeichert um {DateTime.Now:HH:mm:ss} · {doc.Encoding.WebName.ToUpperInvariant()}";
                    if (owner.ViewModel.ActivePanel.ActiveTab is { } t)
                        await t.ReloadAsync(Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Fehler beim Speichern: {ex.Message}";
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
            Content = "Speichern (Strg+S)",
            Padding = new Thickness(12, 6, 12, 6),
        };
        saveBtn.Click += async (_, _) => await SaveNowAsync();

        var closeBtn = new Button
        {
            Content = "Schließen (Esc)",
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

        win = CreateResizableChildWindow($"Editor (F4) – {Path.GetFileName(path)}", "✏️", bodyGrid);
        win.Activate();
    }

    internal Window CreateResizableChildWindow(string title, string iconGlyph, UIElement bodyContent)
    {
        var settings = owner.ViewModel.Settings;

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
        titleStack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
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

        ApplyChildTheme();

        root.Loaded += (_, _) => ApplyChildTheme();

        root.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                win.Close();
            }
        };

        void OnThemeChanged(object? s, EventArgs e) => ApplyChildTheme();

        settings.ThemeChanged += OnThemeChanged;
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

                int w = settings.ChildWindowWidth;
                int h = settings.ChildWindowHeight;

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

    /// <summary>
    /// Displays an interactive dialog informing the user about a new GitHub Release
    /// and offering an immediate self-update download/installation.
    /// </summary>
    public async Task ShowUpdateAvailableDialogAsync(UpdateCheckResult checkResult, GitHubUpdateService updateService)
    {
        if (checkResult.LatestRelease is not { } release) return;

        var loc = LocalizationService.Instance;
        var panel = new StackPanel { Spacing = 10, MinWidth = 460 };

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

        if (!string.IsNullOrWhiteSpace(release.Body))
        {
            panel.Children.Add(new TextBlock
            {
                Text = loc["Update.ReleaseNotes"],
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Margin = new Thickness(0, 4, 0, 0),
            });

            var notesBox = new TextBox
            {
                Text = release.Body.Trim(),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 220,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
            };
            ScrollViewer.SetVerticalScrollBarVisibility(notesBox, ScrollBarVisibility.Auto);
            panel.Children.Add(notesBox);
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


