using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModernExplorer.Core.Archive;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.Remote;
using ModernExplorer.Core.ViewModels;
using Windows.UI;

namespace ModernExplorer.App.Dialogs;

public sealed partial class ShellDialogService
{
    public async Task<bool> ShowBatchRenameDialogAsync(IReadOnlyList<FileEntry> entries)
    {
        var nameMaskBox = new TextBox { Header = "Dateinamen-Maske ([N]=Name, [N1-5]=Teil, [C]=Zähler, [Y][M][D]=Datum, [P]=Ordner)", Text = "[N]" };
        var extMaskBox = new TextBox { Header = "Erweiterungs-Maske ([E]=Original)", Text = "[E]", Width = 180 };

        var maskRow = new Grid { ColumnSpacing = 10 };
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(nameMaskBox, 0);
        Grid.SetColumn(extMaskBox, 1);
        maskRow.Children.Add(nameMaskBox);
        maskRow.Children.Add(extMaskBox);

        var findBox = new TextBox { Header = "Suchen nach", PlaceholderText = "Text oder RegEx…" };
        var replaceBox = new TextBox { Header = "Ersetzen durch", PlaceholderText = "Ersatztext…" };
        var regexCheck = new CheckBox { Content = "RegEx", VerticalAlignment = VerticalAlignment.Bottom };
        var caseCheck = new CheckBox { Content = "Groß/Klein beachten", VerticalAlignment = VerticalAlignment.Bottom };

        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(findBox, 0);
        Grid.SetColumn(replaceBox, 1);
        Grid.SetColumn(regexCheck, 2);
        Grid.SetColumn(caseCheck, 3);
        searchRow.Children.Add(findBox);
        searchRow.Children.Add(replaceBox);
        searchRow.Children.Add(regexCheck);
        searchRow.Children.Add(caseCheck);

        var caseCombo = new ComboBox
        {
            Header = "Groß-/Kleinschreibung",
            ItemsSource = new[]
            {
                "Unverändert",
                "alles kleinschreiben (lowercase)",
                "ALLES GROSSSCHREIBEN (UPPERCASE)",
                "Wortanfänge Groß (Title Case)",
                "Satzanfang groß (Sentence case)"
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var startNum = new NumberBox { Header = "Zähler Start", Value = 1, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 120 };
        var stepNum = new NumberBox { Header = "Schrittweite", Value = 1, Minimum = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 120 };
        var digitsNum = new NumberBox { Header = "Stellen ([C])", Value = 3, Minimum = 1, Maximum = 10, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 120 };

        var counterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        counterRow.Children.Add( caseCombo );
        counterRow.Children.Add(startNum);
        counterRow.Children.Add(stepNum);
        counterRow.Children.Add(digitsNum);

        var previewList = new ListView
        {
            Height = 250,
            SelectionMode = ListViewSelectionMode.None,
        };

        var statusText = new TextBlock { Opacity = 0.8, FontSize = 12 };

        IReadOnlyList<BatchRenamePreviewItem> currentPreview = [];

        void RefreshPreview()
        {
            var opts = new BatchRenameOptions
            {
                NamePattern = string.IsNullOrEmpty(nameMaskBox.Text) ? "[N]" : nameMaskBox.Text,
                ExtensionPattern = extMaskBox.Text ?? "[E]",
                SearchText = findBox.Text ?? "",
                ReplaceText = replaceBox.Text ?? "",
                UseRegex = regexCheck.IsChecked == true,
                CaseSensitive = caseCheck.IsChecked == true,
                CaseTransform = caseCombo.SelectedIndex switch
                {
                    1 => BatchCaseTransform.Lowercase,
                    2 => BatchCaseTransform.Uppercase,
                    3 => BatchCaseTransform.TitleCase,
                    4 => BatchCaseTransform.SentenceCase,
                    _ => BatchCaseTransform.None,
                },
                CounterStart = (int)Math.Max(0, double.IsNaN(startNum.Value) ? 1 : startNum.Value),
                CounterStep = (int)Math.Max(1, double.IsNaN(stepNum.Value) ? 1 : stepNum.Value),
                CounterDigits = (int)Math.Clamp(double.IsNaN(digitsNum.Value) ? 3 : digitsNum.Value, 1, 10),
            };

            currentPreview = BatchRenameService.GeneratePreview(entries, opts);
            previewList.ItemsSource = currentPreview
                .Select(p => $"{p.OriginalName}   →   {p.NewName}" + (p.HasConflict ? $"   [⚠ {p.ConflictReason}]" : (p.WillChange ? "   [✓]" : "")))
                .ToList();

            int changes = currentPreview.Count(p => p.WillChange);
            int conflicts = currentPreview.Count(p => p.HasConflict);
            statusText.Text = $"{entries.Count} Elemente · {changes} Änderung(en) · {conflicts} Konflikt(e)";
        }

        nameMaskBox.TextChanged += (_, _) => RefreshPreview();
        extMaskBox.TextChanged += (_, _) => RefreshPreview();
        findBox.TextChanged += (_, _) => RefreshPreview();
        replaceBox.TextChanged += (_, _) => RefreshPreview();
        regexCheck.Checked += (_, _) => RefreshPreview();
        regexCheck.Unchecked += (_, _) => RefreshPreview();
        caseCheck.Checked += (_, _) => RefreshPreview();
        caseCheck.Unchecked += (_, _) => RefreshPreview();
        caseCombo.SelectionChanged += (_, _) => RefreshPreview();
        startNum.ValueChanged += (_, _) => RefreshPreview();
        stepNum.ValueChanged += (_, _) => RefreshPreview();
        digitsNum.ValueChanged += (_, _) => RefreshPreview();

        RefreshPreview();

        var panel = new StackPanel { Spacing = 10, MinWidth = 680 };
        panel.Children.Add(maskRow);
        panel.Children.Add(searchRow);
        panel.Children.Add(counterRow);
        panel.Children.Add(new TextBlock { Text = "Live-Vorschau:", FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
        panel.Children.Add(previewList);
        panel.Children.Add(statusText);

        var dlg = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = "✏️ Mehrfach-Umbenennungstool (Strg+M)",
            Content = panel,
            PrimaryButtonText = "Jetzt umbenennen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return false;

        var res = await Task.Run(() => BatchRenameService.ExecuteBatchRename(currentPreview));
        if (res.RenamedPairs.Count > 0)
        {
            owner.ViewModel.UndoHistory.RecordBatchRename(res.RenamedPairs);
            owner.ViewModel.StatusNotification = $"{res.RenamedCount} Element(e) umbenannt";
        }

        if (res.Errors.Count > 0)
            ShowMessage("Hinweise beim Umbenennen", string.Join(Environment.NewLine, res.Errors.Take(10)));

        return res.RenamedCount > 0;
    }

    public async Task<bool> ShowDirectorySyncDialogAsync(string leftDir, string rightDir)
    {
        var leftBox = new TextBox { Header = "Linkes Verzeichnis", Text = leftDir };
        var rightBox = new TextBox { Header = "Rechtes Verzeichnis", Text = rightDir };

        var dirGrid = new Grid { ColumnSpacing = 10 };
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftBox, 0);
        Grid.SetColumn(rightBox, 1);
        dirGrid.Children.Add(leftBox);
        dirGrid.Children.Add(rightBox);

        var modeCombo = new ComboBox
        {
            Header = "Vergleichsmodus",
            ItemsSource = new[]
            {
                "Größe & Änderungsdatum (Schnell)",
                "Nur Dateigröße",
                "SHA-256 Dateiinhalt (Bit-genau)"
            },
            SelectedIndex = 0,
            Width = 250,
        };

        var dirCombo = new ComboBox
        {
            Header = "Synchronisations-Richtung",
            ItemsSource = new[]
            {
                "⇄ Beidseitig zusammenführen (Neuere gewinnt)",
                "→ Links nach Rechts spiegeln",
                "← Rechts nach Links spiegeln"
            },
            SelectedIndex = 0,
            Width = 300,
        };

        var deleteOrphansCheck = new CheckBox { Content = "Verwaiste Dateien im Ziel löschen", VerticalAlignment = VerticalAlignment.Bottom };
        var compareBtn = new Button { Content = "🔍 Jetzt vergleichen", VerticalAlignment = VerticalAlignment.Bottom };

        var optsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        optsRow.Children.Add(modeCombo);
        optsRow.Children.Add(dirCombo);
        optsRow.Children.Add(compareBtn);

        var diffList = new ListView { Height = 260, SelectionMode = ListViewSelectionMode.None };
        var summaryText = new TextBlock { Opacity = 0.85, FontSize = 12.5, Text = "Klicken Sie auf „Jetzt vergleichen“, um Unterschiede zu analysieren." };

        IReadOnlyList<SyncComparisonItem> currentDiffs = [];

        async Task RunCompareAsync()
        {
            string l = leftBox.Text.Trim();
            string r = rightBox.Text.Trim();
            if (!Directory.Exists(l) || !Directory.Exists(r))
            {
                summaryText.Text = "⚠ Beide Verzeichnisse müssen existieren.";
                return;
            }

            summaryText.Text = "Vergleiche Verzeichnisse…";
            var mode = modeCombo.SelectedIndex switch
            {
                1 => SyncCompareMode.SizeOnly,
                2 => SyncCompareMode.Sha256Content,
                _ => SyncCompareMode.SizeAndTimestamp,
            };

            currentDiffs = await DirectorySyncService.CompareDirectoriesAsync(l, r, mode, includeSubdirectories: true);
            var nonIdentical = currentDiffs.Where(d => d.Status != SyncItemStatus.Equal).ToList();

            diffList.ItemsSource = nonIdentical.Select(d =>
            {
                string badge = d.Status switch
                {
                    SyncItemStatus.LeftOnly => "[Nur Links →]",
                    SyncItemStatus.RightOnly => "[← Nur Rechts]",
                    SyncItemStatus.LeftNewer => "[Links neuer →]",
                    SyncItemStatus.RightNewer => "[← Rechts neuer]",
                    SyncItemStatus.DifferentContent => "[≠ Unterschiedlich]",
                    _ => "[=]"
                };
                return $"{badge,-18}  {d.RelativePath}   (L: {(d.LeftSize.HasValue ? Format.Size(d.LeftSize.Value) : "-")} | R: {(d.RightSize.HasValue ? Format.Size(d.RightSize.Value) : "-")})";
            }).ToList();

            summaryText.Text = $"Gesamt: {currentDiffs.Count} Dateien · {nonIdentical.Count} Unterschied(e) · {currentDiffs.Count - nonIdentical.Count} identisch";
        }

        compareBtn.Click += async (_, _) => await RunCompareAsync();

        var panel = new StackPanel { Spacing = 10, MinWidth = 720 };
        panel.Children.Add(dirGrid);
        panel.Children.Add(optsRow);
        panel.Children.Add(deleteOrphansCheck);
        panel.Children.Add(summaryText);
        panel.Children.Add(diffList);

        _ = RunCompareAsync();

        var dlg = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = "⇄ Verzeichnisse vergleichen & synchronisieren (Strg+Shift+S)",
            Content = panel,
            PrimaryButtonText = "Synchronisieren",
            CloseButtonText = "Schließen",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return false;

        if (currentDiffs.Count == 0)
            await RunCompareAsync();

        var direction = dirCombo.SelectedIndex switch
        {
            1 => SyncDirectionMode.LeftToRightMirror,
            2 => SyncDirectionMode.RightToLeftMirror,
            _ => SyncDirectionMode.BidirectionalMerge,
        };

        var syncRes = await DirectorySyncService.SynchronizeAsync(
            leftBox.Text.Trim(),
            rightBox.Text.Trim(),
            currentDiffs,
            direction,
            deleteOrphans: deleteOrphansCheck.IsChecked == true);

        owner.ViewModel.StatusNotification = $"Synchronisiert: {syncRes.CopiedLeftToRight} L→R, {syncRes.CopiedRightToLeft} R→L, {syncRes.DeletedOrphans} gelöscht";
        if (syncRes.Errors.Count > 0)
            ShowMessage("Synchronisations-Hinweise", string.Join(Environment.NewLine, syncRes.Errors.Take(10)));

        return true;
    }

    public async Task ShowDeepSearchDialogAsync(string initialDirectory)
    {
        var rootBox = new TextBox { Header = "Suchordner", Text = initialDirectory };
        var patternBox = new TextBox { Header = "Dateiname (Wildcard *.cs;*.txt oder RegEx)", PlaceholderText = "*.*" };
        var contentBox = new TextBox { Header = "Text in Datei (Volltextsuche)", PlaceholderText = "Optionaler Suchbegriff im Dateiinhalt…" };

        var regexCheck = new CheckBox { Content = "RegEx für Dateiname" };
        var caseContentCheck = new CheckBox { Content = "Groß/Klein beim Inhalt beachten" };
        var hiddenCheck = new CheckBox { Content = "Versteckte einbeziehen" };
        var searchBtn = new Button { Content = "🔍 Suche starten", FontWeight = FontWeights.SemiBold };

        var flagsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        flagsRow.Children.Add(regexCheck);
        flagsRow.Children.Add(caseContentCheck);
        flagsRow.Children.Add(hiddenCheck);
        flagsRow.Children.Add(searchBtn);

        var statusText = new TextBlock { Opacity = 0.8, FontSize = 12, Text = "Bereit." };
        var resultsList = new ListView { Height = 260, SelectionMode = ListViewSelectionMode.Single };

        var foundEntries = new List<FileEntry>();

        async Task ExecuteSearchAsync()
        {
            foundEntries.Clear();
            resultsList.ItemsSource = null;
            string dir = rootBox.Text.Trim();
            if (!Directory.Exists(dir))
            {
                statusText.Text = "⚠ Suchordner existiert nicht.";
                return;
            }

            statusText.Text = "Suche läuft…";
            var opts = new DeepSearchQuery
            {
                RootPath = dir,
                NamePattern = string.IsNullOrWhiteSpace(patternBox.Text) ? "*" : patternBox.Text.Trim(),
                UseRegex = regexCheck.IsChecked == true,
                ContentText = string.IsNullOrWhiteSpace(contentBox.Text) ? "" : contentBox.Text,
                ContentCaseSensitive = caseContentCheck.IsChecked == true,
                IncludeHidden = hiddenCheck.IsChecked == true,
                IncludeSubdirectories = true,
                MaxResults = 2000,
            };

            var displayItems = new List<string>();
            var matches = await DeepSearchService.SearchAsync(opts);
            foreach (var entry in matches)
            {
                foundEntries.Add(entry);
                displayItems.Add($"{entry.Name}   —   {entry.RelativeSubPath ?? entry.FullPath}   ({(entry.IsDirectory ? "<DIR>" : Format.Size(entry.Size))})");
            }

            resultsList.ItemsSource = displayItems;
            if (displayItems.Count > 0) resultsList.SelectedIndex = 0;
            statusText.Text = $"{foundEntries.Count} Treffer gefunden.";
        }

        searchBtn.Click += async (_, _) => await ExecuteSearchAsync();

        var panel = new StackPanel { Spacing = 10, MinWidth = 700 };
        panel.Children.Add(rootBox);
        panel.Children.Add(patternBox);
        panel.Children.Add(contentBox);
        panel.Children.Add(flagsRow);
        panel.Children.Add(statusText);
        panel.Children.Add(resultsList);

        var dlg = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = "🔍 Rekursive Tiefensuche (Alt+F7)",
            Content = panel,
            PrimaryButtonText = "Als Tab öffnen",
            SecondaryButtonText = "Zum Treffer springen",
            CloseButtonText = "Schließen",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (foundEntries.Count == 0)
                await ExecuteSearchAsync();

            if (foundEntries.Count > 0 && owner.ViewModel.ActivePanel.ActiveTab is { } tab)
            {
                string label = string.IsNullOrWhiteSpace(patternBox.Text) ? "Suchergebnisse" : patternBox.Text.Trim();
                await tab.LoadSearchResultsAsync(label, foundEntries);
            }
        }
        else if (result == ContentDialogResult.Secondary &&
                 resultsList.SelectedIndex >= 0 &&
                 resultsList.SelectedIndex < foundEntries.Count &&
                 owner.ViewModel.ActivePanel.ActiveTab is { } activeTab)
        {
            var chosen = foundEntries[resultsList.SelectedIndex];
            var dir = chosen.IsDirectory ? chosen.FullPath : Path.GetDirectoryName(chosen.FullPath);
            if (!string.IsNullOrEmpty(dir))
                await activeTab.NavigateAsync(dir, selectName: chosen.Name);
        }
    }

    public async Task<bool> ShowDuplicateFinderDialogAsync(string initialDirectory)
    {
        var rootBox = new TextBox { Header = "Zu prüfendes Verzeichnis", Text = initialDirectory };
        var scanBtn = new Button { Content = "🔍 Nach Duplikaten scannen", FontWeight = FontWeights.SemiBold };
        var selectDupesBtn = new Button { Content = "☑ Alle Duplikate (außer Original je Gruppe) markieren", IsEnabled = false };
        var statusText = new TextBlock { Opacity = 0.85, FontSize = 12.5, Text = "Klicken Sie auf „Nach Duplikaten scannen“." };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        btnRow.Children.Add(scanBtn);
        btnRow.Children.Add(selectDupesBtn);

        var dupeList = new ListView
        {
            Height = 280,
            SelectionMode = ListViewSelectionMode.Multiple,
        };

        var flatCandidateFiles = new List<(FileEntry File, bool IsFirstInGroup)>();

        scanBtn.Click += async (_, _) =>
        {
            string dir = rootBox.Text.Trim();
            if (!Directory.Exists(dir))
            {
                statusText.Text = "⚠ Verzeichnis existiert nicht.";
                return;
            }

            flatCandidateFiles.Clear();
            dupeList.ItemsSource = null;
            statusText.Text = "Scanne nach identischen Dateien (Größe → Teil-Hash → SHA-256)…";

            var progress = new Progress<string>(msg =>
            {
                owner.DispatcherQueue.TryEnqueue(() =>
                {
                    statusText.Text = msg;
                });
            });

            var scanRes = await DuplicateFinderService.FindDuplicatesAsync(dir, includeSubdirectories: true, minFileSizeBytes: 1, progress: progress);
            var displayRows = new List<string>();
            int groupNum = 1;
            foreach (var g in scanRes.Groups)
            {
                bool first = true;
                foreach (var f in g.Files)
                {
                    flatCandidateFiles.Add((f, first));
                    string tag = first ? "[Original behalten]" : "[Duplikat]";
                    displayRows.Add($"Gruppe #{groupNum} ({Format.Size(g.FileSizeBytes)}) {tag}  {f.RelativeSubPath ?? f.FullPath}");
                    first = false;
                }
                groupNum++;
            }

            dupeList.ItemsSource = displayRows;
            selectDupesBtn.IsEnabled = flatCandidateFiles.Count > 0;
            statusText.Text = $"{scanRes.Groups.Count} Duplikat-Gruppe(n) gefunden · Einsparpotenzial: {Format.Size(scanRes.TotalWastedBytes)}";
        };

        selectDupesBtn.Click += (_, _) =>
        {
            dupeList.SelectedItems.Clear();
            for (int i = 0; i < flatCandidateFiles.Count; i++)
            {
                if (!flatCandidateFiles[i].IsFirstInGroup)
                    dupeList.SelectRange(new Microsoft.UI.Xaml.Data.ItemIndexRange(i, 1));
            }
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 700 };
        panel.Children.Add(rootBox);
        panel.Children.Add(btnRow);
        panel.Children.Add(statusText);
        panel.Children.Add(dupeList);

        var dlg = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = "🧹 Duplikat-Finder (SHA-256)",
            Content = panel,
            PrimaryButtonText = "Markierte Duplikate löschen",
            CloseButtonText = "Schließen",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return false;

        var selectedIndices = dupeList.SelectedRanges
            .SelectMany(r => Enumerable.Range(r.FirstIndex, (int)r.Length))
            .Distinct()
            .Where(i => i >= 0 && i < flatCandidateFiles.Count)
            .ToList();

        if (selectedIndices.Count == 0)
            return false;

        int deleted = 0;
        foreach (int idx in selectedIndices)
        {
            try
            {
                File.Delete(flatCandidateFiles[idx].File.FullPath);
                deleted++;
            }
            catch { }
        }

        owner.ViewModel.StatusNotification = $"{deleted} Duplikat(e) gelöscht";
        return deleted > 0;
    }

    public async Task<string?> ShowRemoteConnectionManagerDialogAsync()
    {
        var store = RemoteProfileStore.Shared;
        var profileCombo = new ComboBox
        {
            Header = "Gespeicherte Server-Profile",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var nameBox = new TextBox { Header = "Profilname", PlaceholderText = "Mein SFTP-Server" };
        var protoCombo = new ComboBox
        {
            Header = "Protokoll",
            ItemsSource = new[] { "SFTP (SSH)", "FTPS (Explicit TLS)", "FTP (Standard)" },
            SelectedIndex = 0,
            Width = 190,
        };
        var hostBox = new TextBox { Header = "Host / IP-Adresse", PlaceholderText = "example.com" };
        var portNum = new NumberBox { Header = "Port", Value = 22, Minimum = 1, Maximum = 65535, Width = 110 };

        var hostRow = new Grid { ColumnSpacing = 8 };
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(protoCombo, 0);
        Grid.SetColumn(hostBox, 1);
        Grid.SetColumn(portNum, 2);
        hostRow.Children.Add(protoCombo);
        hostRow.Children.Add(hostBox);
        hostRow.Children.Add(portNum);

        var userBox = new TextBox { Header = "Benutzername", PlaceholderText = "root / ubuntu" };
        var passBox = new PasswordBox { Header = "Passwort / Key-Passphrase (wird verschlüsselt gespeichert)" };
        var keyPathBox = new TextBox { Header = "Optionaler SSH Private-Key Pfad (nur SFTP)", PlaceholderText = @"C:\Users\...\.ssh\id_ed25519" };
        var remoteDirBox = new TextBox { Header = "Startverzeichnis auf dem Server", Text = "/" };

        var userRow = new Grid { ColumnSpacing = 8 };
        userRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        userRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(userBox, 0);
        Grid.SetColumn(passBox, 1);
        userRow.Children.Add(userBox);
        userRow.Children.Add(passBox);

        Guid currentProfileId = Guid.NewGuid();

        void RefreshProfileCombo()
        {
            var items = store.Profiles.Select(p => $"{p.Name} ({p.Protocol.ToString().ToUpperInvariant()}://{p.Username}@{p.Host}:{p.Port})").ToList();
            items.Insert(0, "➕ Neues Server-Profil anlegen…");
            profileCombo.ItemsSource = items;
            profileCombo.SelectedIndex = 0;
        }

        protoCombo.SelectionChanged += (_, _) =>
        {
            if (portNum.Value is 22 or 21 or 990)
                portNum.Value = protoCombo.SelectedIndex == 0 ? 22 : 21;
        };

        profileCombo.SelectionChanged += (_, _) =>
        {
            int idx = profileCombo.SelectedIndex - 1;
            if (idx >= 0 && idx < store.Profiles.Count)
            {
                var p = store.Profiles[idx];
                currentProfileId = p.Id;
                nameBox.Text = p.Name;
                protoCombo.SelectedIndex = p.Protocol switch
                {
                    RemoteProtocol.Ftps => 1,
                    RemoteProtocol.Ftp => 2,
                    _ => 0,
                };
                hostBox.Text = p.Host;
                portNum.Value = p.Port;
                userBox.Text = p.Username;
                passBox.Password = RemoteCredentialProtector.Unprotect(p.EncryptedPassword);
                keyPathBox.Text = p.PrivateKeyPath ?? "";
                remoteDirBox.Text = string.IsNullOrWhiteSpace(p.InitialRemotePath) ? "/" : p.InitialRemotePath;
            }
            else
            {
                currentProfileId = Guid.NewGuid();
                nameBox.Text = "";
                hostBox.Text = "";
                portNum.Value = protoCombo.SelectedIndex == 0 ? 22 : 21;
                userBox.Text = "";
                passBox.Password = "";
                keyPathBox.Text = "";
                remoteDirBox.Text = "/";
            }
        };

        var saveBtn = new Button { Content = "💾 Profil speichern" };
        var deleteBtn = new Button { Content = "🗑 Profil löschen" };
        var btnBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        btnBar.Children.Add(saveBtn);
        btnBar.Children.Add(deleteBtn);

        RemoteConnectionProfile BuildCurrentProfile()
        {
            var proto = protoCombo.SelectedIndex switch
            {
                1 => RemoteProtocol.Ftps,
                2 => RemoteProtocol.Ftp,
                _ => RemoteProtocol.Sftp,
            };
            int port = (int)(double.IsNaN(portNum.Value) ? (proto == RemoteProtocol.Sftp ? 22 : 21) : portNum.Value);
            string host = hostBox.Text.Trim();
            string name = string.IsNullOrWhiteSpace(nameBox.Text) ? host : nameBox.Text.Trim();
            return new RemoteConnectionProfile
            {
                Id = currentProfileId,
                Name = name,
                Protocol = proto,
                Host = host,
                Port = port,
                Username = userBox.Text.Trim(),
                EncryptedPassword = RemoteCredentialProtector.Protect(passBox.Password),
                PrivateKeyPath = string.IsNullOrWhiteSpace(keyPathBox.Text) ? "" : keyPathBox.Text.Trim(),
                InitialRemotePath = string.IsNullOrWhiteSpace(remoteDirBox.Text) ? "/" : remoteDirBox.Text.Trim(),
            };
        }

        saveBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(hostBox.Text)) return;
            var p = BuildCurrentProfile();
            store.AddOrUpdate(p);
            RefreshProfileCombo();
        };

        deleteBtn.Click += (_, _) =>
        {
            store.Remove(currentProfileId);
            RefreshProfileCombo();
        };

        RefreshProfileCombo();

        var panel = new StackPanel { Spacing = 10, MinWidth = 560 };
        panel.Children.Add(profileCombo);
        panel.Children.Add(nameBox);
        panel.Children.Add(hostRow);
        panel.Children.Add(userRow);
        panel.Children.Add(keyPathBox);
        panel.Children.Add(remoteDirBox);
        panel.Children.Add(btnBar);

        var dlg = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = "🌐 FTP / FTPS / SFTP Verbindungsmanager",
            Content = panel,
            PrimaryButtonText = "Verbinden",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return null;

        if (string.IsNullOrWhiteSpace(hostBox.Text))
            return null;

        var profile = BuildCurrentProfile();
        store.AddOrUpdate(profile);
        return profile.BuildRootUri();
    }

    public async Task<string?> ShowCreateArchiveDialogAsync(IReadOnlyList<string> sourcePaths, string defaultOutputDirectory)
    {
        string baseStem = sourcePaths.Count == 1
            ? Path.GetFileNameWithoutExtension(sourcePaths[0].TrimEnd('\\', '/'))
            : "Archiv";

        var formatCombo = new ComboBox
        {
            Header = "Archiv-Format",
            ItemsSource = new[]
            {
                "ZIP-Archiv (.zip)",
                "TAR.GZ-Archiv (.tar.gz)",
                "7-Zip-Archiv (.7z)"
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var destBox = new TextBox
        {
            Header = "Zieldatei",
            Text = Path.Combine(defaultOutputDirectory, baseStem + ".zip"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        formatCombo.SelectionChanged += (_, _) =>
        {
            string ext = formatCombo.SelectedIndex switch
            {
                1 => ".tar.gz",
                2 => ".7z",
                _ => ".zip"
            };
            string dir = Path.GetDirectoryName(destBox.Text) ?? defaultOutputDirectory;
            destBox.Text = Path.Combine(dir, baseStem + ext);
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 480 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{sourcePaths.Count} Element(e) ausgewählt.",
            Opacity = 0.8,
            FontSize = 12.5,
        });
        panel.Children.Add(formatCombo);
        panel.Children.Add(destBox);

        var dlg = new ContentDialog
        {
            XamlRoot = Root,
            RequestedTheme = owner.RootElement.RequestedTheme,
            Title = "📦 In Archiv packen (Alt+F5)",
            Content = panel,
            PrimaryButtonText = "Archiv erstellen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return null;

        string targetArchive = destBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetArchive))
            return null;

        var format = formatCombo.SelectedIndex switch
        {
            1 => ArchiveFormat.TarGz,
            2 => ArchiveFormat.SevenZip,
            _ => ArchiveFormat.Zip,
        };

        try
        {
            owner.ViewModel.StatusNotification = $"Erstelle {Path.GetFileName(targetArchive)}…";
            await ArchiveService.CreateArchiveAsync(sourcePaths, targetArchive, format);
            return targetArchive;
        }
        catch (Exception ex)
        {
            ShowMessage("Archiv erstellen fehlgeschlagen", ex.Message);
            return null;
        }
    }
}
