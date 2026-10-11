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
        var tcs = new TaskCompletionSource<bool>();

        var nameMaskBox = new TextBox
        {
            Header = "Dateinamen-Maske ([N]=Name, [N1-5]=Teil, [C]=Zähler, [Y][M][D]=Datum, [P]=Ordner)",
            Text = "[N]",
        };
        var extMaskBox = new TextBox
        {
            Header = "Erweiterungs-Maske ([E]=Original)",
            Text = "[E]",
            Width = 200,
        };

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

        var searchRow = new Grid { ColumnSpacing = 10 };
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
            MinWidth = 260,
        };

        var startNum = new NumberBox { Header = "Zähler Start", Value = 1, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 130 };
        var stepNum = new NumberBox { Header = "Schrittweite", Value = 1, Minimum = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 130 };
        var digitsNum = new NumberBox { Header = "Stellen ([C])", Value = 3, Minimum = 1, Maximum = 10, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 130 };

        var counterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        counterRow.Children.Add(caseCombo);
        counterRow.Children.Add(startNum);
        counterRow.Children.Add(stepNum);
        counterRow.Children.Add(digitsNum);

        var previewList = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = previewList,
        };

        var statusText = new TextBlock
        {
            Opacity = 0.85,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

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

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(maskRow);
        topPanel.Children.Add(searchRow);
        topPanel.Children.Add(counterRow);
        topPanel.Children.Add(new TextBlock { Text = "Live-Vorschau:", FontWeight = FontWeights.SemiBold, FontSize = 13 });

        var renameBtn = new Button
        {
            Content = "✓ Jetzt umbenennen",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };
        var cancelBtn = new Button
        {
            Content = "Abbrechen",
            Padding = new Thickness(14, 7, 14, 7),
        };

        var footerBar = new Grid { ColumnSpacing = 10 };
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(statusText, 0);
        Grid.SetColumn(renameBtn, 1);
        Grid.SetColumn(cancelBtn, 2);
        footerBar.Children.Add(statusText);
        footerBar.Children.Add(renameBtn);
        footerBar.Children.Add(cancelBtn);

        var bodyGrid = new Grid
        {
            Padding = new Thickness(18, 14, 18, 16),
            RowSpacing = 12,
        };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(footerBar, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(footerBar);

        var win = CreateResizableChildWindow(
            "Mehrfach-Umbenennungstool (Strg+M)",
            "✏️",
            bodyGrid,
            defaultWidth: 1060,
            defaultHeight: 720);

        bool renameConfirmed = false;
        renameBtn.Click += (_, _) =>
        {
            renameConfirmed = true;
            win.Close();
        };
        cancelBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(renameConfirmed);
        win.Activate();

        if (!await tcs.Task)
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
        var tcs = new TaskCompletionSource<bool>();

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
            MinWidth = 260,
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
            MinWidth = 310,
        };

        var deleteOrphansCheck = new CheckBox { Content = "Verwaiste Dateien im Ziel löschen", VerticalAlignment = VerticalAlignment.Bottom };
        var compareBtn = new Button
        {
            Content = "🔍 Jetzt vergleichen",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(14, 6, 14, 6),
        };

        var optsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        optsRow.Children.Add(modeCombo);
        optsRow.Children.Add(dirCombo);
        optsRow.Children.Add(deleteOrphansCheck);
        optsRow.Children.Add(compareBtn);

        var syncProgressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var diffList = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = diffList,
        };

        var summaryText = new TextBlock
        {
            Opacity = 0.85,
            FontSize = 12.5,
            Text = "Klicken Sie auf „Jetzt vergleichen“, um Unterschiede zu analysieren.",
            VerticalAlignment = VerticalAlignment.Center,
        };

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

            compareBtn.IsEnabled = false;
            syncProgressBar.Visibility = Visibility.Visible;
            summaryText.Text = "Vergleiche Verzeichnisse…";
            try
            {
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
            finally
            {
                syncProgressBar.Visibility = Visibility.Collapsed;
                compareBtn.IsEnabled = true;
            }
        }

        compareBtn.Click += async (_, _) => await RunCompareAsync();

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(dirGrid);
        topPanel.Children.Add(optsRow);
        topPanel.Children.Add(syncProgressBar);

        var syncNowBtn = new Button
        {
            Content = "⇄ Jetzt synchronisieren",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };
        var closeBtn = new Button
        {
            Content = "Schließen",
            Padding = new Thickness(14, 7, 14, 7),
        };

        var footerBar = new Grid { ColumnSpacing = 10 };
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(summaryText, 0);
        Grid.SetColumn(syncNowBtn, 1);
        Grid.SetColumn(closeBtn, 2);
        footerBar.Children.Add(summaryText);
        footerBar.Children.Add(syncNowBtn);
        footerBar.Children.Add(closeBtn);

        var bodyGrid = new Grid
        {
            Padding = new Thickness(18, 14, 18, 16),
            RowSpacing = 12,
        };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(footerBar, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(footerBar);

        var win = CreateResizableChildWindow(
            "Verzeichnisse vergleichen & synchronisieren (Strg+Shift+S)",
            "⇄",
            bodyGrid,
            defaultWidth: 1080,
            defaultHeight: 740);

        bool syncConfirmed = false;
        syncNowBtn.Click += (_, _) =>
        {
            syncConfirmed = true;
            win.Close();
        };
        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(syncConfirmed);
        win.Activate();

        _ = RunCompareAsync();

        if (!await tcs.Task)
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
        var tcs = new TaskCompletionSource<int>(); // 0 = close, 1 = open as tab, 2 = jump to hit

        var rootBox = new TextBox { Header = "Suchordner", Text = initialDirectory };
        var patternBox = new TextBox { Header = "Dateiname (Wildcard *.cs;*.txt oder RegEx)", PlaceholderText = "*.*" };
        var contentBox = new TextBox { Header = "Text in Datei (Volltextsuche)", PlaceholderText = "Optionaler Suchbegriff im Dateiinhalt…" };

        var filterGrid = new Grid { ColumnSpacing = 10 };
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(patternBox, 0);
        Grid.SetColumn(contentBox, 1);
        filterGrid.Children.Add(patternBox);
        filterGrid.Children.Add(contentBox);

        var regexCheck = new CheckBox { Content = "RegEx für Dateiname" };
        var caseContentCheck = new CheckBox { Content = "Groß/Klein beim Inhalt beachten" };
        var hiddenCheck = new CheckBox { Content = "Versteckte einbeziehen" };
        var searchBtn = new Button
        {
            Content = "🔍 Suche starten",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 6, 16, 6),
        };

        var flagsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        flagsRow.Children.Add(regexCheck);
        flagsRow.Children.Add(caseContentCheck);
        flagsRow.Children.Add(hiddenCheck);
        flagsRow.Children.Add(searchBtn);

        var searchProgressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var statusText = new TextBlock
        {
            Opacity = 0.85,
            FontSize = 12.5,
            Text = "Bereit.",
            VerticalAlignment = VerticalAlignment.Center,
        };
        var resultsList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = resultsList,
        };

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

            searchBtn.IsEnabled = false;
            searchProgressBar.Visibility = Visibility.Visible;
            statusText.Text = "Suche läuft…";
            try
            {
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
            finally
            {
                searchProgressBar.Visibility = Visibility.Collapsed;
                searchBtn.IsEnabled = true;
            }
        }

        searchBtn.Click += async (_, _) => await ExecuteSearchAsync();

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(rootBox);
        topPanel.Children.Add(filterGrid);
        topPanel.Children.Add(flagsRow);
        topPanel.Children.Add(searchProgressBar);

        var openTabBtn = new Button
        {
            Content = "📂 Als Tab öffnen",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };
        var jumpBtn = new Button
        {
            Content = "🎯 Zum Treffer springen",
            Padding = new Thickness(14, 7, 14, 7),
        };
        var closeBtn = new Button
        {
            Content = "Schließen",
            Padding = new Thickness(14, 7, 14, 7),
        };

        var footerBar = new Grid { ColumnSpacing = 10 };
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(statusText, 0);
        Grid.SetColumn(openTabBtn, 1);
        Grid.SetColumn(jumpBtn, 2);
        Grid.SetColumn(closeBtn, 3);
        footerBar.Children.Add(statusText);
        footerBar.Children.Add(openTabBtn);
        footerBar.Children.Add(jumpBtn);
        footerBar.Children.Add(closeBtn);

        var bodyGrid = new Grid
        {
            Padding = new Thickness(18, 14, 18, 16),
            RowSpacing = 12,
        };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(footerBar, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(footerBar);

        var win = CreateResizableChildWindow(
            "Rekursive Tiefensuche (Alt+F7)",
            "🔍",
            bodyGrid,
            defaultWidth: 1080,
            defaultHeight: 740);

        int actionChoice = 0;
        openTabBtn.Click += (_, _) =>
        {
            actionChoice = 1;
            win.Close();
        };
        jumpBtn.Click += (_, _) =>
        {
            actionChoice = 2;
            win.Close();
        };
        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(actionChoice);
        win.Activate();

        int result = await tcs.Task;
        if (result == 1)
        {
            if (foundEntries.Count == 0)
                await ExecuteSearchAsync();

            if (foundEntries.Count > 0 && owner.ViewModel.ActivePanel.ActiveTab is { } tab)
            {
                string label = string.IsNullOrWhiteSpace(patternBox.Text) ? "Suchergebnisse" : patternBox.Text.Trim();
                await tab.LoadSearchResultsAsync(label, foundEntries);
            }
        }
        else if (result == 2 &&
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
        var tcs = new TaskCompletionSource<bool>();

        var rootBox = new TextBox { Header = "Zu prüfendes Verzeichnis", Text = initialDirectory };
        var scanBtn = new Button
        {
            Content = "🔍 Nach Duplikaten scannen",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 6, 16, 6),
        };
        var selectDupesBtn = new Button
        {
            Content = "☑ Alle Duplikate (außer Original je Gruppe) markieren",
            IsEnabled = false,
            Padding = new Thickness(14, 6, 14, 6),
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        btnRow.Children.Add(scanBtn);
        btnRow.Children.Add(selectDupesBtn);

        var dupeProgressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            IsIndeterminate = false,
            Height = 8,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var statusText = new TextBlock
        {
            Opacity = 0.9,
            FontSize = 12.5,
            Text = "Klicken Sie auf „Nach Duplikaten scannen“, um die mehrstufige Prüfung zu starten.",
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var progressPercentText = new TextBlock
        {
            Opacity = 0.85,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Text = "",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var statusRow = new Grid { ColumnSpacing = 12 };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(statusText, 0);
        Grid.SetColumn(progressPercentText, 1);
        statusRow.Children.Add(statusText);
        statusRow.Children.Add(progressPercentText);

        var dupeList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = dupeList,
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

            scanBtn.IsEnabled = false;
            selectDupesBtn.IsEnabled = false;
            flatCandidateFiles.Clear();
            dupeList.ItemsSource = null;

            dupeProgressBar.Visibility = Visibility.Visible;
            dupeProgressBar.IsIndeterminate = true;
            dupeProgressBar.Value = 0;
            progressPercentText.Text = "Scanne…";
            statusText.Text = "Stufe 1/3: Durchsuche Verzeichnisbaum…";

            var detailedProgress = new Progress<DuplicateScanProgress>(info =>
            {
                owner.DispatcherQueue.TryEnqueue(() =>
                {
                    statusText.Text = info.StageMessage;
                    dupeProgressBar.IsIndeterminate = info.IsIndeterminate;
                    if (!info.IsIndeterminate)
                    {
                        dupeProgressBar.Value = Math.Clamp(info.Percentage, 0, 100);
                        progressPercentText.Text = info.TotalFiles > 0
                            ? $"{info.Percentage:0}% ({info.ProcessedFiles}/{info.TotalFiles})"
                            : $"{info.Percentage:0}%";
                    }
                    else
                    {
                        progressPercentText.Text = info.ProcessedFiles > 0
                            ? $"{info.ProcessedFiles} Dateien erfasst…"
                            : "Scanne…";
                    }
                });
            });

            try
            {
                var scanRes = await DuplicateFinderService.FindDuplicatesAsync(
                    dir,
                    includeSubdirectories: true,
                    minFileSizeBytes: 1,
                    progress: null,
                    detailedProgress: detailedProgress);

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
                dupeProgressBar.IsIndeterminate = false;
                dupeProgressBar.Value = 100;
                progressPercentText.Text = "100%";
                statusText.Text = $"{scanRes.Groups.Count} Duplikat-Gruppe(n) gefunden · Einsparpotenzial: {Format.Size(scanRes.TotalWastedBytes)}";
            }
            catch (Exception ex)
            {
                dupeProgressBar.IsIndeterminate = false;
                dupeProgressBar.Visibility = Visibility.Collapsed;
                progressPercentText.Text = "";
                statusText.Text = $"⚠ Fehler beim Duplikat-Scan: {ex.Message}";
            }
            finally
            {
                scanBtn.IsEnabled = true;
            }
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

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(rootBox);
        topPanel.Children.Add(btnRow);
        topPanel.Children.Add(dupeProgressBar);
        topPanel.Children.Add(statusRow);

        var deleteBtn = new Button
        {
            Content = "🗑 Markierte Duplikate löschen",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };
        var closeBtn = new Button
        {
            Content = "Schließen",
            Padding = new Thickness(14, 7, 14, 7),
        };

        var footerBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
        };
        footerBar.Children.Add(deleteBtn);
        footerBar.Children.Add(closeBtn);

        var bodyGrid = new Grid
        {
            Padding = new Thickness(18, 14, 18, 16),
            RowSpacing = 12,
        };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(footerBar, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(footerBar);

        var win = CreateResizableChildWindow(
            "Duplikat-Finder (SHA-256)",
            "🧹",
            bodyGrid,
            defaultWidth: 1080,
            defaultHeight: 740);

        bool deleteConfirmed = false;
        deleteBtn.Click += (_, _) =>
        {
            deleteConfirmed = true;
            win.Close();
        };
        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(deleteConfirmed);
        win.Activate();

        if (!await tcs.Task)
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
        var tcs = new TaskCompletionSource<bool>();

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
            Width = 210,
        };
        var hostBox = new TextBox { Header = "Host / IP-Adresse", PlaceholderText = "example.com" };
        var portNum = new NumberBox { Header = "Port", Value = 22, Minimum = 1, Maximum = 65535, Width = 120 };

        var hostRow = new Grid { ColumnSpacing = 10 };
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

        var userRow = new Grid { ColumnSpacing = 10 };
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

        var saveBtn = new Button { Content = "💾 Profil speichern", Padding = new Thickness(14, 6, 14, 6) };
        var deleteBtn = new Button { Content = "🗑 Profil löschen", Padding = new Thickness(14, 6, 14, 6) };
        var btnBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
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

        var formStack = new StackPanel { Spacing = 12 };
        formStack.Children.Add(profileCombo);
        formStack.Children.Add(nameBox);
        formStack.Children.Add(hostRow);
        formStack.Children.Add(userRow);
        formStack.Children.Add(keyPathBox);
        formStack.Children.Add(remoteDirBox);
        formStack.Children.Add(btnBar);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = formStack,
        };

        var connectBtn = new Button
        {
            Content = "🌐 Verbinden",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 7, 18, 7),
        };
        var cancelBtn = new Button
        {
            Content = "Abbrechen",
            Padding = new Thickness(14, 7, 14, 7),
        };

        var footerBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
        };
        footerBar.Children.Add(connectBtn);
        footerBar.Children.Add(cancelBtn);

        var bodyGrid = new Grid
        {
            Padding = new Thickness(18, 14, 18, 16),
            RowSpacing = 14,
        };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scrollViewer, 0);
        Grid.SetRow(footerBar, 1);
        bodyGrid.Children.Add(scrollViewer);
        bodyGrid.Children.Add(footerBar);

        var win = CreateResizableChildWindow(
            "FTP / FTPS / SFTP Verbindungsmanager",
            "🌐",
            bodyGrid,
            defaultWidth: 880,
            defaultHeight: 650);

        bool connectConfirmed = false;
        connectBtn.Click += (_, _) =>
        {
            connectConfirmed = true;
            win.Close();
        };
        cancelBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(connectConfirmed);
        win.Activate();

        if (!await tcs.Task)
            return null;

        if (string.IsNullOrWhiteSpace(hostBox.Text))
            return null;

        var profile = BuildCurrentProfile();
        store.AddOrUpdate(profile);
        return profile.BuildRootUri();
    }

    public async Task<string?> ShowCreateArchiveDialogAsync(IReadOnlyList<string> sourcePaths, string defaultOutputDirectory)
    {
        var tcs = new TaskCompletionSource<bool>();

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

        var sourceList = new ListView
        {
            ItemsSource = sourcePaths.ToList(),
            SelectionMode = ListViewSelectionMode.None,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = sourceList,
        };

        var topPanel = new StackPanel { Spacing = 12 };
        topPanel.Children.Add(formatCombo);
        topPanel.Children.Add(destBox);
        topPanel.Children.Add(new TextBlock
        {
            Text = $"Ausgewählte Quellelemente ({sourcePaths.Count}):",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
        });

        var createBtn = new Button
        {
            Content = "📦 Archiv erstellen",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 7, 18, 7),
        };
        var cancelBtn = new Button
        {
            Content = "Abbrechen",
            Padding = new Thickness(14, 7, 14, 7),
        };

        var footerBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
        };
        footerBar.Children.Add(createBtn);
        footerBar.Children.Add(cancelBtn);

        var bodyGrid = new Grid
        {
            Padding = new Thickness(18, 14, 18, 16),
            RowSpacing = 12,
        };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(footerBar, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(footerBar);

        var win = CreateResizableChildWindow(
            "In Archiv packen (Alt+F5)",
            "📦",
            bodyGrid,
            defaultWidth: 880,
            defaultHeight: 620);

        bool createConfirmed = false;
        createBtn.Click += (_, _) =>
        {
            createConfirmed = true;
            win.Close();
        };
        cancelBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(createConfirmed);
        win.Activate();

        if (!await tcs.Task)
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
