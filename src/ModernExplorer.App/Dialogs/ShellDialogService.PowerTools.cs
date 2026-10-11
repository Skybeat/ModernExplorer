using System.IO;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModernExplorer.Core.Features;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.Settings;
using ModernExplorer.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace ModernExplorer.App.Dialogs;

public sealed partial class ShellDialogService
{
    public void ToggleQuickCommandBar()
    {
        owner.SyncQuickCommandBarVisibility();
    }

    // ========================================================================================
    // 1. VISUAL SIDE-BY-SIDE FILE DIFF VIEWER
    // ========================================================================================
    public async Task<bool> ShowFileDiffDialogAsync(string leftFilePath, string rightFilePath)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool anySaved = false;
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        var leftBox = new TextBox
        {
            Header = isDe ? "Linke Datei (Quelle A)" : "Left File (Source A)",
            Text = leftFilePath ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var rightBox = new TextBox
        {
            Header = isDe ? "Rechte Datei (Quelle B)" : "Right File (Source B)",
            Text = rightFilePath ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var ignoreWsCheck = new CheckBox
        {
            Content = isDe ? "Leerzeichen ignorieren" : "Ignore whitespace",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var compareBtn = new Button
        {
            Content = isDe ? "🔍 Vergleichen" : "🔍 Compare",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var pathGrid = new Grid { ColumnSpacing = 10 };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leftBox, 0);
        Grid.SetColumn(rightBox, 1);
        Grid.SetColumn(ignoreWsCheck, 2);
        Grid.SetColumn(compareBtn, 3);
        pathGrid.Children.Add(leftBox);
        pathGrid.Children.Add(rightBox);
        pathGrid.Children.Add(ignoreWsCheck);
        pathGrid.Children.Add(compareBtn);

        var prevDiffBtn = new Button { Content = isDe ? "⬆ Vorheriger Diff" : "⬆ Prev Diff", IsEnabled = false };
        var nextDiffBtn = new Button { Content = isDe ? "⬇ Nächster Diff" : "⬇ Next Diff", IsEnabled = false };
        var mergeRightBtn = new Button { Content = isDe ? "Block → Rechts kopieren" : "Copy Block → Right", IsEnabled = false };
        var mergeLeftBtn = new Button { Content = isDe ? "← Block nach Links kopieren" : "← Copy Block Left", IsEnabled = false };
        var saveLeftBtn = new Button { Content = isDe ? "💾 Links speichern" : "💾 Save Left", IsEnabled = false };
        var saveRightBtn = new Button { Content = isDe ? "💾 Rechts speichern" : "💾 Save Right", IsEnabled = false };

        var summaryText = new TextBlock
        {
            Text = isDe ? "Bereit zum Dateivergleich." : "Ready to compare files.",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
        };

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionRow.Children.Add(prevDiffBtn);
        actionRow.Children.Add(nextDiffBtn);
        actionRow.Children.Add(mergeRightBtn);
        actionRow.Children.Add(mergeLeftBtn);
        actionRow.Children.Add(saveLeftBtn);
        actionRow.Children.Add(saveRightBtn);
        actionRow.Children.Add(summaryText);

        var diffListView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = diffListView,
        };

        var closeBtn = new Button
        {
            Content = LocalizationService.T("Dialog.Close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 7, 16, 7),
        };

        var topStack = new StackPanel { Spacing = 10 };
        topStack.Children.Add(pathGrid);
        topStack.Children.Add(actionRow);

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topStack, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topStack);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(closeBtn);

        FileDiffResult? currentDiff = null;
        int activeHunkIdx = 0;

        void RenderDiffResult(FileDiffResult diff)
        {
            currentDiff = diff;
            var rowsUi = new List<Grid>(diff.Rows.Count);
            var monoFont = new FontFamily("Consolas");

            foreach (var r in diff.Rows)
            {
                Color rowBg = r.Kind switch
                {
                    DiffLineKind.Added => Color.FromArgb(48, 16, 185, 129),
                    DiffLineKind.Removed => Color.FromArgb(48, 239, 68, 68),
                    DiffLineKind.Modified => Color.FromArgb(48, 245, 158, 11),
                    _ => Colors.Transparent,
                };

                string marker = r.Kind switch
                {
                    DiffLineKind.Added => "+",
                    DiffLineKind.Removed => "-",
                    DiffLineKind.Modified => "~",
                    _ => " ",
                };

                var g = new Grid
                {
                    Background = new SolidColorBrush(rowBg),
                    ColumnSpacing = 8,
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lNum = new TextBlock { Text = r.LeftLineNumber?.ToString() ?? "", Opacity = 0.55, FontFamily = monoFont, FontSize = 11.5 };
                var lTxt = new TextBlock { Text = r.LeftText, FontFamily = monoFont, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                var mid = new TextBlock { Text = marker, FontWeight = FontWeights.Bold, FontFamily = monoFont, HorizontalAlignment = HorizontalAlignment.Center };
                var rNum = new TextBlock { Text = r.RightLineNumber?.ToString() ?? "", Opacity = 0.55, FontFamily = monoFont, FontSize = 11.5 };
                var rTxt = new TextBlock { Text = r.RightText, FontFamily = monoFont, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };

                Grid.SetColumn(lNum, 0);
                Grid.SetColumn(lTxt, 1);
                Grid.SetColumn(mid, 2);
                Grid.SetColumn(rNum, 3);
                Grid.SetColumn(rTxt, 4);
                g.Children.Add(lNum);
                g.Children.Add(lTxt);
                g.Children.Add(mid);
                g.Children.Add(rNum);
                g.Children.Add(rTxt);
                rowsUi.Add(g);
            }

            diffListView.ItemsSource = rowsUi;
            bool hasHunks = diff.Hunks.Count > 0;
            prevDiffBtn.IsEnabled = hasHunks;
            nextDiffBtn.IsEnabled = hasHunks;
            mergeRightBtn.IsEnabled = hasHunks;
            mergeLeftBtn.IsEnabled = hasHunks;
            saveLeftBtn.IsEnabled = !string.IsNullOrWhiteSpace(diff.LeftPath);
            saveRightBtn.IsEnabled = !string.IsNullOrWhiteSpace(diff.RightPath);

            summaryText.Text = diff.AreIdentical
                ? (isDe ? "✓ Dateien sind identisch!" : "✓ Files are identical!")
                : (isDe
                    ? $"{diff.Hunks.Count} Differenz-Blöcke  ·  +{diff.AddedCount} hinzugefügt  ·  -{diff.RemovedCount} gelöscht  ·  ~{diff.ModifiedCount} geändert"
                    : $"{diff.Hunks.Count} diff blocks  ·  +{diff.AddedCount} added  ·  -{diff.RemovedCount} removed  ·  ~{diff.ModifiedCount} modified");

            if (hasHunks)
            {
                activeHunkIdx = Math.Clamp(activeHunkIdx, 0, diff.Hunks.Count - 1);
                int rowIdx = diff.Hunks[activeHunkIdx].FirstRowIndex;
                if (rowIdx >= 0 && rowIdx < diffListView.Items.Count)
                {
                    diffListView.SelectedIndex = rowIdx;
                    diffListView.ScrollIntoView(diffListView.Items[rowIdx]);
                }
            }
        }

        async Task RunCompareAsync()
        {
            var res = await FileDiffService.CompareFilesAsync(
                leftBox.Text.Trim(),
                rightBox.Text.Trim(),
                ignoreWsCheck.IsChecked == true);
            activeHunkIdx = 0;
            RenderDiffResult(res);
        }

        compareBtn.Click += async (_, _) => await RunCompareAsync();
        ignoreWsCheck.Click += async (_, _) => await RunCompareAsync();

        prevDiffBtn.Click += (_, _) =>
        {
            if (currentDiff is null || currentDiff.Hunks.Count == 0) return;
            activeHunkIdx = (activeHunkIdx - 1 + currentDiff.Hunks.Count) % currentDiff.Hunks.Count;
            int rowIdx = currentDiff.Hunks[activeHunkIdx].FirstRowIndex;
            if (rowIdx >= 0 && rowIdx < diffListView.Items.Count)
            {
                diffListView.SelectedIndex = rowIdx;
                diffListView.ScrollIntoView(diffListView.Items[rowIdx]);
            }
        };

        nextDiffBtn.Click += (_, _) =>
        {
            if (currentDiff is null || currentDiff.Hunks.Count == 0) return;
            activeHunkIdx = (activeHunkIdx + 1) % currentDiff.Hunks.Count;
            int rowIdx = currentDiff.Hunks[activeHunkIdx].FirstRowIndex;
            if (rowIdx >= 0 && rowIdx < diffListView.Items.Count)
            {
                diffListView.SelectedIndex = rowIdx;
                diffListView.ScrollIntoView(diffListView.Items[rowIdx]);
            }
        };

        diffListView.SelectionChanged += (_, _) =>
        {
            if (currentDiff is null || diffListView.SelectedIndex < 0 || diffListView.SelectedIndex >= currentDiff.Rows.Count)
                return;
            int h = currentDiff.Rows[diffListView.SelectedIndex].HunkIndex;
            if (h >= 0) activeHunkIdx = h;
        };

        mergeRightBtn.Click += (_, _) =>
        {
            if (currentDiff is null || currentDiff.Hunks.Count == 0) return;
            RenderDiffResult(FileDiffService.ApplyHunkLeftToRight(currentDiff, activeHunkIdx));
        };

        mergeLeftBtn.Click += (_, _) =>
        {
            if (currentDiff is null || currentDiff.Hunks.Count == 0) return;
            RenderDiffResult(FileDiffService.ApplyHunkRightToLeft(currentDiff, activeHunkIdx));
        };

        saveLeftBtn.Click += async (_, _) =>
        {
            if (currentDiff is null || string.IsNullOrWhiteSpace(currentDiff.LeftPath)) return;
            try
            {
                await File.WriteAllTextAsync(currentDiff.LeftPath, currentDiff.LeftRawText);
                anySaved = true;
                summaryText.Text = isDe ? $"✓ Gespeichert: {Path.GetFileName(currentDiff.LeftPath)}" : $"✓ Saved: {Path.GetFileName(currentDiff.LeftPath)}";
            }
            catch (Exception ex) { summaryText.Text = $"⚠ {ex.Message}"; }
        };

        saveRightBtn.Click += async (_, _) =>
        {
            if (currentDiff is null || string.IsNullOrWhiteSpace(currentDiff.RightPath)) return;
            try
            {
                await File.WriteAllTextAsync(currentDiff.RightPath, currentDiff.RightRawText);
                anySaved = true;
                summaryText.Text = isDe ? $"✓ Gespeichert: {Path.GetFileName(currentDiff.RightPath)}" : $"✓ Saved: {Path.GetFileName(currentDiff.RightPath)}";
            }
            catch (Exception ex) { summaryText.Text = $"⚠ {ex.Message}"; }
        };

        var win = CreateResizableChildWindow(
            isDe ? "Visueller Side-by-Side Datei-Diff-Viewer" : "Visual Side-by-Side File Diff Viewer",
            "⚖",
            bodyGrid,
            defaultWidth: 1220,
            defaultHeight: 780,
            fontTarget: ChildWindowFontTarget.DirectorySync);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(anySaved);
        win.Activate();

        if (!string.IsNullOrWhiteSpace(leftFilePath) || !string.IsNullOrWhiteSpace(rightFilePath))
            await RunCompareAsync();

        return await tcs.Task;
    }

    // ========================================================================================
    // 2. CHECKSUM GENERATOR & VERIFIER (.sha256, .md5, .sfv)
    // ========================================================================================
    public async Task ShowChecksumToolDialogAsync(IReadOnlyList<string> paths, string initialDirectory)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        var expectedHashBox = new TextBox
        {
            Header = isDe ? "Erwarteten Hash-Wert zum Sofort-Abgleich einfügen (SHA-256 / SHA-512 / MD5 / CRC32)" : "Paste expected hash for instant verification (SHA-256 / SHA-512 / MD5 / CRC32)",
            PlaceholderText = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 …",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var matchStatusBadge = new TextBlock
        {
            Text = "",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
        };

        var exportSha256Btn = new Button { Content = isDe ? "💾 Als .sha256 speichern" : "💾 Save .sha256", Padding = new Thickness(12, 6, 12, 6) };
        var exportMd5Btn = new Button { Content = isDe ? "💾 Als .md5 speichern" : "💾 Save .md5", Padding = new Thickness(12, 6, 12, 6) };
        var exportSfvBtn = new Button { Content = isDe ? "💾 Als .sfv speichern" : "💾 Save .sfv", Padding = new Thickness(12, 6, 12, 6) };
        var verifyFileBtn = new Button { Content = isDe ? "✅ Manifest-Datei prüfen" : "✅ Verify Manifest File", Padding = new Thickness(12, 6, 12, 6) };
        var copyAllBtn = new Button { Content = isDe ? "📋 Alle SHA-256 kopieren" : "📋 Copy All SHA-256", Padding = new Thickness(12, 6, 12, 6) };

        var btnBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        btnBar.Children.Add(exportSha256Btn);
        btnBar.Children.Add(exportMd5Btn);
        btnBar.Children.Add(exportSfvBtn);
        btnBar.Children.Add(verifyFileBtn);
        btnBar.Children.Add(copyAllBtn);

        var statusText = new TextBlock { Text = isDe ? "Berechne Prüfsummen …" : "Calculating checksums …", Opacity = 0.85 };

        var resultsList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Child = resultsList,
        };

        var closeBtn = new Button
        {
            Content = LocalizationService.T("Dialog.Close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 7, 16, 7),
        };

        var topPanel = new StackPanel { Spacing = 8 };
        topPanel.Children.Add(expectedHashBox);
        topPanel.Children.Add(matchStatusBadge);
        topPanel.Children.Add(btnBar);
        topPanel.Children.Add(statusText);

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(closeBtn);

        IReadOnlyList<FileChecksumEntry> computedEntries = [];

        void RefreshListDisplay()
        {
            string expected = expectedHashBox.Text.Trim();
            bool anyMatch = false;

            var items = new List<StackPanel>(computedEntries.Count);
            foreach (var e in computedEntries)
            {
                bool isMatch = !string.IsNullOrEmpty(expected) && e.MatchesExpectedHash(expected);
                if (isMatch) anyMatch = true;

                var card = new StackPanel { Spacing = 2, Padding = new Thickness(6, 4, 6, 4) };
                card.Children.Add(new TextBlock
                {
                    Text = $"{(isMatch ? "✅ MATCH — " : "📄 ")}{e.FileName}   ({Format.Size(e.SizeBytes)})",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isMatch ? new SolidColorBrush(Color.FromArgb(255, 16, 185, 129)) : null,
                });
                card.Children.Add(new TextBlock
                {
                    Text = $"SHA-256: {e.Sha256}   |   MD5: {e.Md5}   |   CRC32: {e.Crc32}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11.5,
                    Opacity = 0.85,
                    IsTextSelectionEnabled = true,
                });
                items.Add(card);
            }
            resultsList.ItemsSource = items;

            if (string.IsNullOrEmpty(expected))
                matchStatusBadge.Text = "";
            else if (anyMatch)
            {
                matchStatusBadge.Text = isDe ? "✓ ÜBEREINSTIMMUNG! Der eingefügte Hash stimmt exakt überein." : "✓ MATCH! The pasted hash matches.";
                matchStatusBadge.Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));
            }
            else
            {
                matchStatusBadge.Text = isDe ? "✗ KEINE ÜBEREINSTIMMUNG mit den berechneten Dateien!" : "✗ NO MATCH with the calculated files!";
                matchStatusBadge.Foreground = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
            }
        }

        expectedHashBox.TextChanged += (_, _) => RefreshListDisplay();

        async Task SaveManifestAsync(ChecksumAlgorithm algo)
        {
            if (computedEntries.Count == 0) return;
            string ext = ChecksumBatchService.GetDefaultManifestExtension(algo);
            string baseName = computedEntries.Count == 1 ? computedEntries[0].FileName : "checksums";
            string outPath = Path.Combine(initialDirectory, baseName + ext);
            string content = ChecksumBatchService.GenerateManifestContent(computedEntries, algo, initialDirectory);
            await File.WriteAllTextAsync(outPath, content);
            statusText.Text = isDe ? $"✓ Gespeichert: {outPath}" : $"✓ Saved: {outPath}";
        }

        exportSha256Btn.Click += async (_, _) => await SaveManifestAsync(ChecksumAlgorithm.Sha256);
        exportMd5Btn.Click += async (_, _) => await SaveManifestAsync(ChecksumAlgorithm.Md5);
        exportSfvBtn.Click += async (_, _) => await SaveManifestAsync(ChecksumAlgorithm.Crc32);

        copyAllBtn.Click += (_, _) =>
        {
            if (computedEntries.Count == 0) return;
            string content = ChecksumBatchService.GenerateManifestContent(computedEntries, ChecksumAlgorithm.Sha256, initialDirectory);
            SetClipboardText(content);
            statusText.Text = isDe ? "✓ SHA-256-Prüfsummen in Zwischenablage kopiert." : "✓ Copied SHA-256 checksums to clipboard.";
        };

        verifyFileBtn.Click += async (_, _) =>
        {
            var manifestCandidate = paths.FirstOrDefault(ChecksumBatchService.IsManifestFile)
                ?? Directory.EnumerateFiles(initialDirectory).FirstOrDefault(ChecksumBatchService.IsManifestFile);

            if (string.IsNullOrEmpty(manifestCandidate) || !File.Exists(manifestCandidate))
            {
                statusText.Text = isDe
                    ? "⚠ Keine .sha256 / .md5 / .sfv Datei im Ordner gefunden."
                    : "⚠ No .sha256 / .md5 / .sfv manifest file found in folder.";
                return;
            }

            var rows = await ChecksumBatchService.VerifyManifestFileAsync(manifestCandidate);
            int okCount = rows.Count(r => r.Status == ChecksumVerifyStatus.VerifiedOk);
            int failCount = rows.Count - okCount;
            statusText.Text = isDe
                ? $"Manifest-Prüfung ({Path.GetFileName(manifestCandidate)}): ✓ {okCount} OK  ·  ✗ {failCount} Fehler/Fehlend"
                : $"Manifest Check ({Path.GetFileName(manifestCandidate)}): ✓ {okCount} OK  ·  ✗ {failCount} Failed/Missing";

            resultsList.ItemsSource = rows.Select(r =>
            {
                string icon = r.Status == ChecksumVerifyStatus.VerifiedOk ? "✅ OK" : (r.Status == ChecksumVerifyStatus.FileMissing ? "❓ FEHLT" : "❌ FEHLER");
                return $"{icon}  —  {r.RelativeOrFileName}   (Erwartet: {r.ExpectedHash})";
            }).ToList();
        };

        var win = CreateResizableChildWindow(
            isDe ? "Prüfsummen-Generator & Verifizierer (SHA-256 / SHA-512 / MD5 / CRC32)" : "Checksum Generator & Verifier (SHA-256 / SHA-512 / MD5 / CRC32)",
            "🔐",
            bodyGrid,
            defaultWidth: 1020,
            defaultHeight: 680);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(true);
        win.Activate();

        // If user selected a manifest file directly, verify it immediately; otherwise compute hashes
        if (paths.Count == 1 && ChecksumBatchService.IsManifestFile(paths[0]))
        {
            var rows = await ChecksumBatchService.VerifyManifestFileAsync(paths[0]);
            int okCount = rows.Count(r => r.Status == ChecksumVerifyStatus.VerifiedOk);
            statusText.Text = $"✓ {okCount} / {rows.Count} OK ({Path.GetFileName(paths[0])})";
            resultsList.ItemsSource = rows.Select(r =>
                $"{(r.Status == ChecksumVerifyStatus.VerifiedOk ? "✅ OK" : "❌ FEHLER")}  —  {r.RelativeOrFileName}  ({r.ActualHash})").ToList();
        }
        else
        {
            computedEntries = await ChecksumBatchService.ComputeChecksumsAsync(paths);
            statusText.Text = isDe
                ? $"✓ {computedEntries.Count} Datei(en) berechnet."
                : $"✓ Calculated checksums for {computedEntries.Count} file(s).";
            RefreshListDisplay();
        }

        await tcs.Task;
    }

    // ========================================================================================
    // 3. FILE SPLITTER, JOINER (.001) & SECURE SHREDDER
    // ========================================================================================
    public async Task<bool> ShowSplitJoinShredDialogAsync(IReadOnlyList<string> paths, string initialDirectory)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool anyChanged = false;
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        string firstFile = paths.FirstOrDefault(File.Exists) ?? "";

        var sourceFileBox = new TextBox
        {
            Header = isDe ? "Quelldatei zum Aufteilen oder .001-Teildatei zum Zusammenfügen" : "Source file to split or .001 part file to join",
            Text = firstFile,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var targetDirBox = new TextBox
        {
            Header = isDe ? "Zielordner" : "Output Directory",
            Text = initialDirectory,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var partSizeCombo = new ComboBox
        {
            Header = isDe ? "Teilgröße (für Split)" : "Part Size (for Split)",
            ItemsSource = FileSplitAndShredService.PresetPartSizes.Select(p => p.Label).ToList(),
            SelectedIndex = 2, // 100 MB default
            Width = 240,
        };

        var shredPassCombo = new ComboBox
        {
            Header = isDe ? "Shredder-Sicherheitsstufe" : "Shredder Security Passes",
            ItemsSource = isDe
                ? new[] { "1 Durchgang (Schnell / Nullen)", "3 Durchgänge (DoD 5220.22-M Standard)", "7 Durchgänge (Maximale Sicherheit)" }
                : new[] { "1 Pass (Fast / Zeroes)", "3 Passes (DoD 5220.22-M Standard)", "7 Passes (Maximum Security)" },
            SelectedIndex = 1,
            Width = 280,
        };

        var splitBtn = new Button { Content = isDe ? "✂ Datei in .001-Teile splitten" : "✂ Split File into .001 Parts", FontWeight = FontWeights.SemiBold, Padding = new Thickness(14, 7, 14, 7) };
        var joinBtn = new Button { Content = isDe ? "🔗 .001-Teile zusammenfügen" : "🔗 Join .001 Parts", FontWeight = FontWeights.SemiBold, Padding = new Thickness(14, 7, 14, 7) };
        var shredBtn = new Button
        {
            Content = isDe ? $"🔥 {paths.Count} ausgewählte Element(e) sicher schreddern" : $"🔥 Securely Shred {paths.Count} Selected Item(s)",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Color.FromArgb(255, 220, 38, 38)),
            Padding = new Thickness(14, 7, 14, 7),
        };

        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 8, Visibility = Visibility.Collapsed };
        var statusText = new TextBlock { Text = isDe ? "Wählen Sie Split, Join oder Sicheres Löschen (Shredder)." : "Choose Split, Join, or Secure Shredder.", Opacity = 0.9 };

        var targetList = new ListView { ItemsSource = paths.ToList(), SelectionMode = ListViewSelectionMode.None };
        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = targetList,
        };

        var optionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        optionsRow.Children.Add(partSizeCombo);
        optionsRow.Children.Add(shredPassCombo);

        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actionsRow.Children.Add(splitBtn);
        actionsRow.Children.Add(joinBtn);
        actionsRow.Children.Add(shredBtn);

        var topStack = new StackPanel { Spacing = 10 };
        topStack.Children.Add(sourceFileBox);
        topStack.Children.Add(targetDirBox);
        topStack.Children.Add(optionsRow);
        topStack.Children.Add(actionsRow);
        topStack.Children.Add(progressBar);
        topStack.Children.Add(statusText);

        var closeBtn = new Button { Content = LocalizationService.T("Dialog.Close"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7) };

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topStack, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topStack);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(closeBtn);

        splitBtn.Click += async (_, _) =>
        {
            string src = sourceFileBox.Text.Trim();
            string outDir = targetDirBox.Text.Trim();
            if (!File.Exists(src))
            {
                statusText.Text = isDe ? "⚠ Bitte gültige Quelldatei auswählen." : "⚠ Please select a valid source file.";
                return;
            }
            int sizeIdx = Math.Clamp(partSizeCombo.SelectedIndex, 0, FileSplitAndShredService.PresetPartSizes.Count - 1);
            long partBytes = FileSplitAndShredService.PresetPartSizes[sizeIdx].Bytes;

            progressBar.Visibility = Visibility.Visible;
            var prog = new Progress<(int Part, int Total, double Pct)>(p =>
            {
                progressBar.Value = p.Pct;
                statusText.Text = $"Teil {p.Part}/{p.Total} ({p.Pct:0}%) …";
            });

            try
            {
                var res = await FileSplitAndShredService.SplitFileAsync(src, outDir, partBytes, true, prog);
                anyChanged = true;
                statusText.Text = isDe
                    ? $"✓ Datei in {res.CreatedPartPaths.Count} Teile aufgeteilt (+ .sha256 Manifest erstellt)."
                    : $"✓ Split file into {res.CreatedPartPaths.Count} parts (+ .sha256 manifest).";
            }
            catch (Exception ex) { statusText.Text = $"⚠ {ex.Message}"; }
        };

        joinBtn.Click += async (_, _) =>
        {
            string src = sourceFileBox.Text.Trim();
            if (!File.Exists(src))
            {
                statusText.Text = isDe ? "⚠ Bitte eine .001-Teildatei auswählen." : "⚠ Please select a .001 part file.";
                return;
            }
            progressBar.Visibility = Visibility.Visible;
            var prog = new Progress<(int Part, int Total, double Pct)>(p =>
            {
                progressBar.Value = p.Pct;
                statusText.Text = $"Füge Teil {p.Part}/{p.Total} zusammen ({p.Pct:0}%) …";
            });

            try
            {
                var res = await FileSplitAndShredService.JoinFilePartsAsync(src, null, prog);
                anyChanged = true;
                statusText.Text = isDe
                    ? $"✓ {res.JoinedPartCount} Teile erfolgreich zusammengefügt: {res.OutputFilePath} ({Format.Size(res.TotalBytes)})"
                    : $"✓ Joined {res.JoinedPartCount} parts into {res.OutputFilePath} ({Format.Size(res.TotalBytes)})";
            }
            catch (Exception ex) { statusText.Text = $"⚠ {ex.Message}"; }
        };

        shredBtn.Click += async (_, _) =>
        {
            if (paths.Count == 0) return;
            int passes = shredPassCombo.SelectedIndex switch { 0 => 1, 2 => 7, _ => 3 };
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;

            var prog = new Progress<(string File, int Pass, int TotalPasses)>(p =>
            {
                statusText.Text = $"🔥 Schreddere {p.File} (Durchgang {p.Pass}/{p.TotalPasses}) …";
            });

            try
            {
                var res = await FileSplitAndShredService.SecureShredAsync(paths, passes, prog);
                anyChanged = res.ShreddedFileCount > 0;
                progressBar.IsIndeterminate = false;
                progressBar.Value = 100;
                statusText.Text = isDe
                    ? $"✓ {res.ShreddedFileCount} Datei(en) ({Format.Size(res.ShreddedBytes)}) mit {passes} Durchgängen sicher überschrieben & vernichtet."
                    : $"✓ Securely shredded {res.ShreddedFileCount} file(s) ({Format.Size(res.ShreddedBytes)}) with {passes} passes.";
            }
            catch (Exception ex) { statusText.Text = $"⚠ {ex.Message}"; }
        };

        var win = CreateResizableChildWindow(
            isDe ? "Datei-Splitter (.001), Joiner & Sicheres Löschen (Shredder)" : "File Splitter (.001), Joiner & Secure Shredder",
            "✂",
            bodyGrid,
            defaultWidth: 960,
            defaultHeight: 640);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(anyChanged);
        win.Activate();

        return await tcs.Task;
    }

    // ========================================================================================
    // 4. COLOR FILE TAGS / LABELS & NOTES
    // ========================================================================================
    public async Task<bool> ShowFileTagDialogAsync(IReadOnlyList<string> paths)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool anyModified = false;
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        var existingTag = paths.Count > 0 ? FileTagService.GetTag(paths[0]) : null;
        FileTagColor selectedColor = existingTag?.Color ?? FileTagColor.Red;

        var noteBox = new TextBox
        {
            Header = isDe ? "Notiz / Kommentar für ausgewählte Datei(en)" : "Note / Comment for selected file(s)",
            PlaceholderText = isDe ? "Optionale Kurznotiz eingeben (filterbar über note:text) …" : "Enter optional short note (filterable via note:text) …",
            Text = existingTag?.Note ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var selectedColorLabel = new TextBlock
        {
            Text = $"{FileTagService.GetColorBadgeEmoji(selectedColor)} {FileTagService.GetColorDisplayName(selectedColor)}",
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var col in new[] { FileTagColor.Red, FileTagColor.Orange, FileTagColor.Yellow, FileTagColor.Green, FileTagColor.Blue, FileTagColor.Purple, FileTagColor.None })
        {
            var captured = col;
            var btn = new Button
            {
                Content = $"{FileTagService.GetColorBadgeEmoji(col)} {FileTagService.GetColorDisplayName(col)}".Trim(),
                Padding = new Thickness(12, 6, 12, 6),
            };
            btn.Click += (_, _) =>
            {
                selectedColor = captured;
                selectedColorLabel.Text = $"{FileTagService.GetColorBadgeEmoji(selectedColor)} {FileTagService.GetColorDisplayName(selectedColor)}";
                FileTagService.SetTag(paths, selectedColor, noteBox.Text);
                anyModified = true;
            };
            colorRow.Children.Add(btn);
        }

        var saveBtn = new Button
        {
            Content = isDe ? "💾 Tag & Notiz speichern" : "💾 Save Tag & Note",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };

        var allTagsList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        void RefreshAllTagsList()
        {
            var all = FileTagService.GetAllTags();
            allTagsList.ItemsSource = all.Select(t =>
                $"{FileTagService.GetColorBadgeEmoji(t.Color)} {FileTagService.GetColorDisplayName(t.Color)}  —  {t.FullPath} {(string.IsNullOrEmpty(t.Note) ? "" : $"  [📝 {t.Note}]")}").ToList();
        }
        RefreshAllTagsList();

        saveBtn.Click += (_, _) =>
        {
            FileTagService.SetTag(paths, selectedColor, noteBox.Text);
            anyModified = true;
            RefreshAllTagsList();
        };

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(new TextBlock
        {
            Text = isDe
                ? $"🏷 Farb-Label & Notiz für {paths.Count} ausgewählte Element(e) (Tipp: Im Filter 'tag:rot', 'tag:grün' oder 'note:...' eingeben)"
                : $"🏷 Color Tag & Note for {paths.Count} selected item(s) (Tip: Type 'tag:red', 'tag:green' or 'note:...' in Filter)",
            FontWeight = FontWeights.SemiBold,
        });
        topPanel.Children.Add(colorRow);
        topPanel.Children.Add(selectedColorLabel);
        topPanel.Children.Add(noteBox);
        topPanel.Children.Add(saveBtn);

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = allTagsList,
        };

        var closeBtn = new Button { Content = LocalizationService.T("Dialog.Close"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7) };

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(closeBtn);

        var win = CreateResizableChildWindow(
            isDe ? "Farbige Datei-Tags / Labels & Notizen" : "Color File Tags / Labels & Notes",
            "🏷",
            bodyGrid,
            defaultWidth: 940,
            defaultHeight: 620);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(anyModified);
        win.Activate();

        return await tcs.Task;
    }

    // ========================================================================================
    // 5. WORKSPACE PROFILES (NAMED DUAL-PANE SESSIONS)
    // ========================================================================================
    public async Task ShowWorkspaceManagerDialogAsync(MainViewModel mainViewModel)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        var nameBox = new TextBox
        {
            Header = isDe ? "Name des Arbeitsbereichs (Workspace)" : "Workspace Profile Name",
            PlaceholderText = isDe ? "z.B. Web-Deployment, Foto-Sortierung, Steuer 2026 …" : "e.g. Web Deployment, Photo Sorting …",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var descBox = new TextBox
        {
            Header = isDe ? "Beschreibung (optional)" : "Description (optional)",
            PlaceholderText = isDe ? "Speichert alle offenen Tabs links & rechts inkl. Sperrstatus und Ansicht" : "Saves all open left & right tabs including lock state and view mode",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var saveCurrentBtn = new Button
        {
            Content = isDe ? "💾 Aktuelles Fenster-Layout als Workspace speichern" : "💾 Save Current Layout as Workspace",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 7, 14, 7),
        };

        var loadSelectedBtn = new Button
        {
            Content = isDe ? "🚀 Ausgewählten Workspace laden" : "🚀 Load Selected Workspace",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 7, 14, 7),
        };

        var deleteSelectedBtn = new Button
        {
            Content = isDe ? "🗑 Löschen" : "🗑 Delete",
            Padding = new Thickness(14, 7, 14, 7),
        };

        var statusText = new TextBlock { Text = "", Opacity = 0.85 };

        var profileList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        IReadOnlyList<WorkspaceProfile> currentProfiles = [];

        void RefreshList()
        {
            currentProfiles = WorkspaceProfileService.GetProfiles();
            profileList.ItemsSource = currentProfiles.Select(p =>
                $"🗂 {p.Name}   —   {p.SummaryText}   {(string.IsNullOrEmpty(p.Description) ? "" : $"({p.Description})")}").ToList();
            if (currentProfiles.Count > 0)
                profileList.SelectedIndex = 0;
        }
        RefreshList();

        saveCurrentBtn.Click += (_, _) =>
        {
            var prof = WorkspaceProfileService.CaptureProfile(
                nameBox.Text,
                descBox.Text,
                mainViewModel.IsDualMode,
                mainViewModel.SplitRatio,
                mainViewModel.ActiveSide,
                mainViewModel.Left.ToState(),
                mainViewModel.Right.ToState());
            statusText.Text = isDe ? $"✓ Workspace „{prof.Name}“ gespeichert." : $"✓ Saved workspace \"{prof.Name}\".";
            RefreshList();
        };

        loadSelectedBtn.Click += async (_, _) =>
        {
            int idx = profileList.SelectedIndex;
            if (idx < 0 || idx >= currentProfiles.Count) return;
            var prof = currentProfiles[idx];
            await WorkspaceProfileService.ApplyWorkspaceAsync(mainViewModel, prof);
            statusText.Text = isDe ? $"✓ Workspace „{prof.Name}“ wiederhergestellt." : $"✓ Loaded workspace \"{prof.Name}\".";
        };

        deleteSelectedBtn.Click += (_, _) =>
        {
            int idx = profileList.SelectedIndex;
            if (idx < 0 || idx >= currentProfiles.Count) return;
            WorkspaceProfileService.DeleteProfile(currentProfiles[idx].Id);
            RefreshList();
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        btnRow.Children.Add(saveCurrentBtn);
        btnRow.Children.Add(loadSelectedBtn);
        btnRow.Children.Add(deleteSelectedBtn);

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(nameBox);
        topPanel.Children.Add(descBox);
        topPanel.Children.Add(btnRow);
        topPanel.Children.Add(statusText);

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = profileList,
        };

        var closeBtn = new Button { Content = LocalizationService.T("Dialog.Close"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7) };

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(closeBtn);

        var win = CreateResizableChildWindow(
            isDe ? "Arbeitsbereiche / Ordner-Paar-Profile (Workspaces)" : "Workspace Profiles (Dual-Pane Sessions)",
            "🗂",
            bodyGrid,
            defaultWidth: 920,
            defaultHeight: 620);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(true);
        win.Activate();

        await tcs.Task;
    }

    // ========================================================================================
    // 6. SMART COPY / PATH & LIST EXPORT TOOLS
    // ========================================================================================
    public async Task ShowSmartExportDialogAsync(IReadOnlyList<FileEntry> entries, string baseDirectory)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        var formatCombo = new ComboBox
        {
            Header = isDe ? "Export-Format" : "Export Format",
            ItemsSource = isDe
                ? new[]
                {
                    "Vollständige Pfade (C:\\Pfad\\Datei.ext)",
                    "Nur Dateinamen (Datei.ext)",
                    "Relative Pfade",
                    "Slash-Pfade (C:/Pfad/Datei.ext)",
                    "Markdown file://-Links ([Name](file:///...))",
                    "CSV-Tabelle (Name, Typ, Größe, Datum, Pfad)",
                    "Markdown-Tabelle (| Name | Typ | Größe | ... |)",
                    "JSON-Array",
                    "HTML-Tabelle",
                }
                : new[]
                {
                    "Full Paths (C:\\Path\\File.ext)",
                    "File Names Only (File.ext)",
                    "Relative Paths",
                    "Forward-Slash Paths (C:/Path/File.ext)",
                    "Markdown file:// Links ([Name](file:///...))",
                    "CSV Table (Name, Type, Size, Modified, Path)",
                    "Markdown Table (| Name | Type | Size | ... |)",
                    "JSON Array",
                    "HTML Table",
                },
            SelectedIndex = 6, // Markdown table default
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var previewBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(previewBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(previewBox, ScrollBarVisibility.Auto);

        var copyBtn = new Button { Content = isDe ? "📋 In Zwischenablage kopieren" : "📋 Copy to Clipboard", FontWeight = FontWeights.SemiBold, Padding = new Thickness(16, 7, 16, 7) };
        var saveFileBtn = new Button { Content = isDe ? "💾 Als Datei speichern" : "💾 Save to File", Padding = new Thickness(14, 7, 14, 7) };
        var statusText = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center };

        void UpdatePreview()
        {
            var fmt = (SmartExportFormat)Math.Clamp(formatCombo.SelectedIndex, 0, 8);
            previewBox.Text = SmartExportService.FormatEntries(entries, fmt, baseDirectory);
        }

        formatCombo.SelectionChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        copyBtn.Click += (_, _) =>
        {
            SetClipboardText(previewBox.Text);
            statusText.Text = isDe ? "✓ In die Zwischenablage kopiert!" : "✓ Copied to clipboard!";
        };

        saveFileBtn.Click += async (_, _) =>
        {
            var fmt = (SmartExportFormat)Math.Clamp(formatCombo.SelectedIndex, 0, 8);
            string ext = SmartExportService.GetRecommendedFileExtension(fmt);
            string outDir = Directory.Exists(baseDirectory) ? baseDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string outPath = Path.Combine(outDir, $"FileList_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
            try
            {
                await File.WriteAllTextAsync(outPath, previewBox.Text);
                statusText.Text = isDe ? $"✓ Gespeichert: {outPath}" : $"✓ Saved: {outPath}";
            }
            catch (Exception ex) { statusText.Text = $"⚠ {ex.Message}"; }
        };

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actionRow.Children.Add(copyBtn);
        actionRow.Children.Add(saveFileBtn);
        actionRow.Children.Add(statusText);

        var topStack = new StackPanel { Spacing = 10 };
        topStack.Children.Add(formatCombo);
        topStack.Children.Add(actionRow);

        var closeBtn = new Button { Content = LocalizationService.T("Dialog.Close"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7) };

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topStack, 0);
        Grid.SetRow(previewBox, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topStack);
        bodyGrid.Children.Add(previewBox);
        bodyGrid.Children.Add(closeBtn);

        var win = CreateResizableChildWindow(
            isDe ? $"Smart Copy & Dateilisten-Export ({entries.Count} Elemente)" : $"Smart Copy & File List Export ({entries.Count} items)",
            "📋",
            bodyGrid,
            defaultWidth: 980,
            defaultHeight: 650);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(true);
        win.Activate();

        await tcs.Task;
    }

    // ========================================================================================
    // 7. FILE BASKET / DROP-BASKET (SAMMELKORB)
    // ========================================================================================
    public async Task ShowFileBasketDialogAsync(MainViewModel mainViewModel)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        var summaryText = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13 };

        var addCurrentBtn = new Button { Content = isDe ? "+ Aktuelle Auswahl hinzufügen" : "+ Add Current Selection", Padding = new Thickness(12, 6, 12, 6) };
        var copyHereBtn = new Button { Content = isDe ? "📋 Alle in aktiven Ordner kopieren" : "📋 Copy All to Active Folder", FontWeight = FontWeights.SemiBold, Padding = new Thickness(12, 6, 12, 6) };
        var moveHereBtn = new Button { Content = isDe ? "✂ Alle in aktiven Ordner verschieben" : "✂ Move All to Active Folder", Padding = new Thickness(12, 6, 12, 6) };
        var packBtn = new Button { Content = isDe ? "📦 Als Archiv packen" : "📦 Pack into Archive", Padding = new Thickness(12, 6, 12, 6) };
        var batchRenameBtn = new Button { Content = isDe ? "✏ Batch-Umbenennen" : "✏ Batch Rename", Padding = new Thickness(12, 6, 12, 6) };
        var clearBtn = new Button { Content = isDe ? "🧹 Sammelkorb leeren" : "🧹 Clear Basket", Padding = new Thickness(12, 6, 12, 6) };

        var basketList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        void RefreshBasketUi()
        {
            var items = FileBasketService.GetItems();
            summaryText.Text = isDe
                ? $"🧺 Sammelkorb enthält {items.Count} Element(e)  ·  Gesamtgröße: {Format.Size(FileBasketService.TotalFileBytes)}"
                : $"🧺 File Basket contains {items.Count} item(s)  ·  Total size: {Format.Size(FileBasketService.TotalFileBytes)}";
            basketList.ItemsSource = items.Select(i =>
                $"{(i.IsDirectory ? "📁" : "📄")} {i.Name}   ({(i.IsDirectory ? "<DIR>" : Format.Size(i.Size))})   —   {i.FullPath}").ToList();
        }
        RefreshBasketUi();

        addCurrentBtn.Click += (_, _) =>
        {
            mainViewModel.AddSelectedToBasket();
            RefreshBasketUi();
        };

        copyHereBtn.Click += async (_, _) =>
        {
            await mainViewModel.CopyOrMoveBasketToActiveFolderAsync(OpKind.Copy);
            summaryText.Text = isDe ? "✓ Kopierauftrag für Sammelkorb gestartet." : "✓ Started copying Basket items.";
        };

        moveHereBtn.Click += async (_, _) =>
        {
            await mainViewModel.CopyOrMoveBasketToActiveFolderAsync(OpKind.Move);
            FileBasketService.Clear();
            RefreshBasketUi();
        };

        packBtn.Click += async (_, _) =>
        {
            await mainViewModel.PackBasketToArchiveAsync();
        };

        batchRenameBtn.Click += async (_, _) =>
        {
            await mainViewModel.BatchRenameBasketAsync();
            RefreshBasketUi();
        };

        clearBtn.Click += (_, _) =>
        {
            FileBasketService.Clear();
            RefreshBasketUi();
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        btnRow.Children.Add(addCurrentBtn);
        btnRow.Children.Add(copyHereBtn);
        btnRow.Children.Add(moveHereBtn);
        btnRow.Children.Add(packBtn);
        btnRow.Children.Add(batchRenameBtn);
        btnRow.Children.Add(clearBtn);

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(summaryText);
        topPanel.Children.Add(btnRow);

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = basketList,
        };

        var closeBtn = new Button { Content = LocalizationService.T("Dialog.Close"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7) };

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(closeBtn);

        var win = CreateResizableChildWindow(
            isDe ? "Sammelkorb / Drop-Basket (Dateien aus verschiedenen Ordnern sammeln)" : "File Basket / Drop-Basket (Stage files across folders)",
            "🧺",
            bodyGrid,
            defaultWidth: 1040,
            defaultHeight: 620);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(true);
        win.Activate();

        await tcs.Task;
    }

    // ========================================================================================
    // 8. EXIF / MEDIA METADATA INSPECTOR & TIMESTAMP / ATTRIBUTE EDITOR
    // ========================================================================================
    public async Task<bool> ShowAttributeAndExifDialogAsync(IReadOnlyList<string> paths)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool anyModified = false;
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        DateTime initialDt = DateTime.Now;
        FileAttributes initialAttrs = FileAttributes.Normal;
        if (paths.Count > 0 && (File.Exists(paths[0]) || Directory.Exists(paths[0])))
        {
            try
            {
                initialDt = File.GetLastWriteTime(paths[0]);
                initialAttrs = File.GetAttributes(paths[0]);
            }
            catch { }
        }

        var setModifiedCheck = new CheckBox { Content = isDe ? "Geändert-Datum setzen (ISO):" : "Set Modified Date (ISO):", IsChecked = false };
        var modifiedBox = new TextBox { Text = initialDt.ToString("yyyy-MM-dd HH:mm:ss"), Width = 200 };

        var setCreatedCheck = new CheckBox { Content = isDe ? "Erstellt-Datum setzen (ISO):" : "Set Created Date (ISO):", IsChecked = false };
        var createdBox = new TextBox { Text = initialDt.ToString("yyyy-MM-dd HH:mm:ss"), Width = 200 };

        var shiftHoursNum = new NumberBox { Header = isDe ? "Zeitstempel verschieben (+/- Stunden)" : "Shift Timestamps (+/- Hours)", Value = 0, Minimum = -8760, Maximum = 8760, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 240 };
        var copyExifDateCheck = new CheckBox { Content = isDe ? "📷 EXIF-Aufnahmedatum in Datei-Zeitstempel übernehmen" : "📷 Copy EXIF DateTaken into file timestamps", IsChecked = false };

        var modifyAttrCheck = new CheckBox { Content = isDe ? "Dateiattribute ändern:" : "Modify File Attributes:", IsChecked = false };
        var attrReadOnly = new CheckBox { Content = "ReadOnly", IsChecked = (initialAttrs & FileAttributes.ReadOnly) != 0 };
        var attrHidden = new CheckBox { Content = "Hidden", IsChecked = (initialAttrs & FileAttributes.Hidden) != 0 };
        var attrArchive = new CheckBox { Content = "Archive", IsChecked = (initialAttrs & FileAttributes.Archive) != 0 };
        var attrSystem = new CheckBox { Content = "System", IsChecked = (initialAttrs & FileAttributes.System) != 0 };
        var recurseCheck = new CheckBox { Content = isDe ? "Unterordner rekursiv einbeziehen" : "Include subfolders recursively", IsChecked = false };

        var touchNowBtn = new Button { Content = isDe ? "🕒 Auf aktuelle Uhrzeit (Jetzt) setzen" : "🕒 Touch (Set to Now)", Padding = new Thickness(12, 6, 12, 6) };
        var applyBtn = new Button { Content = isDe ? "✓ Änderungen auf Auswahl anwenden" : "✓ Apply Changes to Selection", FontWeight = FontWeights.SemiBold, Padding = new Thickness(16, 7, 16, 7) };
        var statusText = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center };

        var metadataList = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        void RefreshInspectionList()
        {
            var rows = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    var meta = MetadataAndAttributeService.InspectMediaMetadata(p);
                    var attrs = File.Exists(p) || Directory.Exists(p) ? File.GetAttributes(p).ToString() : "-";
                    var mod = File.Exists(p) || Directory.Exists(p) ? File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss") : "-";
                    string exifPart = meta.HasAnyMetadata
                        ? $"  |  🖼 {meta.ResolutionText}  📷 {meta.CameraMake} {meta.CameraModel}  📅 EXIF: {meta.DateTaken:yyyy-MM-dd HH:mm:ss}  ISO: {meta.IsoSpeed}"
                        : "";
                    rows.Add($"{Path.GetFileName(p)}   —   Geändert: {mod}   |   Attr: [{attrs}]{exifPart}");
                }
                catch { }
            }
            metadataList.ItemsSource = rows;
        }
        RefreshInspectionList();

        touchNowBtn.Click += (_, _) =>
        {
            string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            modifiedBox.Text = nowStr;
            createdBox.Text = nowStr;
            setModifiedCheck.IsChecked = true;
            setCreatedCheck.IsChecked = true;
        };

        applyBtn.Click += (_, _) =>
        {
            DateTime.TryParse(createdBox.Text, out var parsedCreated);
            DateTime.TryParse(modifiedBox.Text, out var parsedModified);
            double shiftHrs = double.IsNaN(shiftHoursNum.Value) ? 0 : shiftHoursNum.Value;

            var opts = new BatchAttributeTimestampOptions
            {
                SetCreationTime = setCreatedCheck.IsChecked == true && parsedCreated != default,
                CreationTimeLocal = parsedCreated == default ? DateTime.Now : parsedCreated,
                SetLastWriteTime = setModifiedCheck.IsChecked == true && parsedModified != default,
                LastWriteTimeLocal = parsedModified == default ? DateTime.Now : parsedModified,
                CopyExifDateTakenToTimestamps = copyExifDateCheck.IsChecked == true,
                ShiftTimestamps = Math.Abs(shiftHrs) > 0.001,
                ShiftOffset = TimeSpan.FromHours(shiftHrs),
                ModifyAttributes = modifyAttrCheck.IsChecked == true,
                ReadOnly = attrReadOnly.IsChecked,
                Hidden = attrHidden.IsChecked,
                Archive = attrArchive.IsChecked,
                System = attrSystem.IsChecked,
                IncludeSubdirectories = recurseCheck.IsChecked == true,
            };

            var res = MetadataAndAttributeService.ApplyBatchChanges(paths, opts);
            anyModified = res.ModifiedCount > 0;
            statusText.Text = isDe
                ? $"✓ {res.ModifiedCount} Element(e) aktualisiert ({res.Errors.Count} Fehler)."
                : $"✓ Updated {res.ModifiedCount} item(s) ({res.Errors.Count} errors).";
            RefreshInspectionList();
        };

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row1.Children.Add(setModifiedCheck);
        row1.Children.Add(modifiedBox);
        row1.Children.Add(setCreatedCheck);
        row1.Children.Add(createdBox);
        row1.Children.Add(touchNowBtn);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        row2.Children.Add(shiftHoursNum);
        row2.Children.Add(copyExifDateCheck);
        row2.Children.Add(recurseCheck);

        var row3 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row3.Children.Add(modifyAttrCheck);
        row3.Children.Add(attrReadOnly);
        row3.Children.Add(attrHidden);
        row3.Children.Add(attrArchive);
        row3.Children.Add(attrSystem);

        var row4 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row4.Children.Add(applyBtn);
        row4.Children.Add(statusText);

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(row1);
        topPanel.Children.Add(row2);
        topPanel.Children.Add(row3);
        topPanel.Children.Add(row4);

        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = metadataList,
        };

        var closeBtn = new Button { Content = LocalizationService.T("Dialog.Close"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7) };

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topPanel, 0);
        Grid.SetRow(listBorder, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topPanel);
        bodyGrid.Children.Add(listBorder);
        bodyGrid.Children.Add(closeBtn);

        var win = CreateResizableChildWindow(
            isDe ? "Zeitstempel-, Dateiattribut- & EXIF-Metadaten-Editor" : "Timestamp, File Attribute & EXIF Metadata Editor",
            "🕒",
            bodyGrid,
            defaultWidth: 1020,
            defaultHeight: 660);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(anyModified);
        win.Activate();

        return await tcs.Task;
    }

    // ========================================================================================
    // 10. EXTENDED GIT REPOSITORY PANEL
    // ========================================================================================
    public async Task<bool> ShowGitPanelDialogAsync(string directoryPath)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool anyRepoAction = false;
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";

        var overview = await GitIntegrationService.InspectRepositoryAsync(directoryPath);
        if (!overview.IsGitRepository)
        {
            ShowMessage(
                isDe ? "Kein Git-Repository" : "Not a Git Repository",
                isDe ? $"Der Ordner „{directoryPath}“ befindet sich in keinem Git-Repository." : $"The folder \"{directoryPath}\" is not inside a Git repository.");
            return false;
        }

        var branchCombo = new ComboBox
        {
            Header = isDe ? "Aktiver Git-Branch (Auswahl wechselt Branch)" : "Active Git Branch (Select to checkout)",
            ItemsSource = overview.LocalBranches,
            SelectedItem = overview.CurrentBranch,
            Width = 280,
        };

        var diffSelectedBtn = new Button
        {
            Content = isDe ? "⚖ Visueller Diff gegen HEAD" : "⚖ Visual Diff vs HEAD",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 7, 14, 7),
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var stageSelectedBtn = new Button
        {
            Content = isDe ? "+ Datei stagen (git add)" : "+ Stage File (git add)",
            Padding = new Thickness(12, 7, 12, 7),
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var refreshBtn = new Button
        {
            Content = isDe ? "🔄 Aktualisieren" : "🔄 Refresh",
            Padding = new Thickness(12, 7, 12, 7),
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var statusText = new TextBlock
        {
            Text = $"⎇ {overview.RepoRootPath} ({overview.ChangedFiles.Count} geänderte Dateien, {overview.RecentCommits.Count} letzte Commits)",
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0.85,
        };

        var changedList = new ListView { SelectionMode = ListViewSelectionMode.Single };
        var commitsList = new ListView { SelectionMode = ListViewSelectionMode.Single };

        void PopulateGitLists()
        {
            changedList.ItemsSource = overview.ChangedFiles.Select(c =>
                $"[{c.StatusBadge}]  {c.RelativePath}   {(c.IsStaged ? "(Staged)" : "")}").ToList();
            commitsList.ItemsSource = overview.RecentCommits.Select(c =>
                $"{c.ShortHash}  ·  {c.RelativeDate}  ·  {c.Author}:  {c.Subject}").ToList();
        }
        PopulateGitLists();

        bool suppressBranchEvent = false;
        branchCombo.SelectionChanged += async (_, _) =>
        {
            if (suppressBranchEvent || branchCombo.SelectedItem is not string targetBranch) return;
            if (string.Equals(targetBranch, overview.CurrentBranch, StringComparison.Ordinal)) return;

            var (ok, msg) = await GitIntegrationService.CheckoutBranchAsync(overview.RepoRootPath, targetBranch);
            anyRepoAction = anyRepoAction || ok;
            statusText.Text = ok ? $"✓ Branch gewechselt auf {targetBranch}" : $"⚠ {msg}";
            overview = await GitIntegrationService.InspectRepositoryAsync(directoryPath);
            PopulateGitLists();
        };

        refreshBtn.Click += async (_, _) =>
        {
            overview = await GitIntegrationService.InspectRepositoryAsync(directoryPath);
            suppressBranchEvent = true;
            branchCombo.ItemsSource = overview.LocalBranches;
            branchCombo.SelectedItem = overview.CurrentBranch;
            suppressBranchEvent = false;
            PopulateGitLists();
        };

        stageSelectedBtn.Click += async (_, _) =>
        {
            int idx = changedList.SelectedIndex;
            if (idx < 0 || idx >= overview.ChangedFiles.Count) return;
            var file = overview.ChangedFiles[idx];
            var (ok, msg) = await GitIntegrationService.StageFileAsync(overview.RepoRootPath, file.RelativePath);
            anyRepoAction = anyRepoAction || ok;
            overview = await GitIntegrationService.InspectRepositoryAsync(directoryPath);
            PopulateGitLists();
            statusText.Text = ok ? $"✓ Staged: {file.RelativePath}" : $"⚠ {msg}";
        };

        diffSelectedBtn.Click += async (_, _) =>
        {
            int idx = changedList.SelectedIndex;
            if (idx < 0 || idx >= overview.ChangedFiles.Count) return;
            var file = overview.ChangedFiles[idx];
            string headText = await GitIntegrationService.GetHeadFileContentAsync(overview.RepoRootPath, file.RelativePath);
            string tempHeadFile = Path.Combine(Path.GetTempPath(), $"HEAD_{Path.GetFileName(file.FullPath)}");
            await File.WriteAllTextAsync(tempHeadFile, headText);
            await ShowFileDiffDialogAsync(tempHeadFile, file.FullPath);
        };

        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        topRow.Children.Add(branchCombo);
        topRow.Children.Add(diffSelectedBtn);
        topRow.Children.Add(stageSelectedBtn);
        topRow.Children.Add(refreshBtn);

        var splitGrid = new Grid { ColumnSpacing = 12 };
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        var leftBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Child = changedList,
        };
        var rightBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Child = commitsList,
        };
        Grid.SetColumn(leftBorder, 0);
        Grid.SetColumn(rightBorder, 1);
        splitGrid.Children.Add(leftBorder);
        splitGrid.Children.Add(rightBorder);

        var topStack = new StackPanel { Spacing = 8 };
        topStack.Children.Add(topRow);
        topStack.Children.Add(statusText);

        var closeBtn = new Button { Content = LocalizationService.T("Dialog.Close"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7) };

        var bodyGrid = new Grid { Padding = new Thickness(18, 14, 18, 16), RowSpacing = 10 };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topStack, 0);
        Grid.SetRow(splitGrid, 1);
        Grid.SetRow(closeBtn, 2);
        bodyGrid.Children.Add(topStack);
        bodyGrid.Children.Add(splitGrid);
        bodyGrid.Children.Add(closeBtn);

        var win = CreateResizableChildWindow(
            isDe ? $"Git-Repository-Inspektor — {overview.CurrentBranch}" : $"Git Repository Inspector — {overview.CurrentBranch}",
            "⎇",
            bodyGrid,
            defaultWidth: 1120,
            defaultHeight: 680);

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(anyRepoAction);
        win.Activate();

        return await tcs.Task;
    }
}
