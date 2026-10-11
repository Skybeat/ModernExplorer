using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModernExplorer.Core.Archive;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Localization;
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
            Header = LocalizationService.T("BatchRename.NameMaskHeader"),
            Text = "[N]",
        };
        var extMaskBox = new TextBox
        {
            Header = LocalizationService.T("BatchRename.ExtMaskHeader"),
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

        var findBox = new TextBox
        {
            Header = LocalizationService.T("BatchRename.FindHeader"),
            PlaceholderText = LocalizationService.T("BatchRename.FindPlaceholder"),
        };
        var replaceBox = new TextBox
        {
            Header = LocalizationService.T("BatchRename.ReplaceHeader"),
            PlaceholderText = LocalizationService.T("BatchRename.ReplacePlaceholder"),
        };
        var regexCheck = new CheckBox { Content = "RegEx", VerticalAlignment = VerticalAlignment.Bottom };
        var caseCheck = new CheckBox { Content = LocalizationService.T("BatchRename.CaseSensitive"), VerticalAlignment = VerticalAlignment.Bottom };

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
            Header = LocalizationService.T("BatchRename.CaseHeader"),
            ItemsSource = new[]
            {
                LocalizationService.T("BatchRename.CaseNone"),
                LocalizationService.T("BatchRename.CaseLower"),
                LocalizationService.T("BatchRename.CaseUpper"),
                LocalizationService.T("BatchRename.CaseTitle"),
                LocalizationService.T("BatchRename.CaseSentence"),
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 260,
        };

        var startNum = new NumberBox { Header = LocalizationService.T("BatchRename.CounterStart"), Value = 1, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 130 };
        var stepNum = new NumberBox { Header = LocalizationService.T("BatchRename.CounterStep"), Value = 1, Minimum = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 130 };
        var digitsNum = new NumberBox { Header = LocalizationService.T("BatchRename.CounterDigits"), Value = 3, Minimum = 1, Maximum = 10, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 130 };

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
            statusText.Text = LocalizationService.Format("BatchRename.StatusFormat", entries.Count, changes, conflicts);
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

        var livePreviewHeader = new TextBlock { Text = LocalizationService.T("BatchRename.LivePreview"), FontWeight = FontWeights.SemiBold, FontSize = 13 };
        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(maskRow);
        topPanel.Children.Add(searchRow);
        topPanel.Children.Add(counterRow);
        topPanel.Children.Add(livePreviewHeader);

        var renameBtn = new Button
        {
            Content = LocalizationService.T("BatchRename.RenameNow"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };
        var cancelBtn = new Button
        {
            Content = LocalizationService.T("Common.Cancel"),
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
            LocalizationService.T("BatchRename.WindowTitle"),
            "✏️",
            bodyGrid,
            defaultWidth: 1060,
            defaultHeight: 720,
            fontTarget: ChildWindowFontTarget.BatchRename,
            titleProvider: () => LocalizationService.T("BatchRename.WindowTitle"));

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
            owner.ViewModel.StatusNotification = LocalizationService.Format("BatchRename.RenamedNotification", res.RenamedCount);
        }

        if (res.Errors.Count > 0)
            ShowMessage(LocalizationService.T("BatchRename.NotesTitle"), string.Join(Environment.NewLine, res.Errors.Take(10)));

        return res.RenamedCount > 0;
    }

    public async Task<bool> ShowDirectorySyncDialogAsync(string leftDir, string rightDir)
    {
        var tcs = new TaskCompletionSource<bool>();

        var leftBox = new TextBox { Header = LocalizationService.T("DirSync.LeftDir"), Text = leftDir };
        var rightBox = new TextBox { Header = LocalizationService.T("DirSync.RightDir"), Text = rightDir };

        var dirGrid = new Grid { ColumnSpacing = 10 };
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftBox, 0);
        Grid.SetColumn(rightBox, 1);
        dirGrid.Children.Add(leftBox);
        dirGrid.Children.Add(rightBox);

        var modeCombo = new ComboBox
        {
            Header = LocalizationService.T("DirSync.CompareMode"),
            ItemsSource = new[]
            {
                LocalizationService.T("DirSync.ModeFast"),
                LocalizationService.T("DirSync.ModeSize"),
                LocalizationService.T("DirSync.ModeSha256"),
            },
            SelectedIndex = 0,
            MinWidth = 260,
        };

        var dirCombo = new ComboBox
        {
            Header = LocalizationService.T("DirSync.Direction"),
            ItemsSource = new[]
            {
                LocalizationService.T("DirSync.DirBi"),
                LocalizationService.T("DirSync.DirL2R"),
                LocalizationService.T("DirSync.DirR2L"),
            },
            SelectedIndex = 0,
            MinWidth = 310,
        };

        var deleteOrphansCheck = new CheckBox { Content = LocalizationService.T("DirSync.DeleteOrphans"), VerticalAlignment = VerticalAlignment.Bottom };
        var compareBtn = new Button
        {
            Content = LocalizationService.T("DirSync.CompareNow"),
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
            Text = LocalizationService.T("DirSync.InitialHint"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        IReadOnlyList<SyncComparisonItem> currentDiffs = [];

        async Task RunCompareAsync()
        {
            string l = leftBox.Text.Trim();
            string r = rightBox.Text.Trim();
            if (!Directory.Exists(l) || !Directory.Exists(r))
            {
                summaryText.Text = LocalizationService.T("DirSync.BothMustExist");
                return;
            }

            compareBtn.IsEnabled = false;
            syncProgressBar.Visibility = Visibility.Visible;
            summaryText.Text = LocalizationService.T("DirSync.Comparing");
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
                        SyncItemStatus.LeftOnly => LocalizationService.T("DirSync.BadgeLeftOnly"),
                        SyncItemStatus.RightOnly => LocalizationService.T("DirSync.BadgeRightOnly"),
                        SyncItemStatus.LeftNewer => LocalizationService.T("DirSync.BadgeLeftNewer"),
                        SyncItemStatus.RightNewer => LocalizationService.T("DirSync.BadgeRightNewer"),
                        SyncItemStatus.DifferentContent => LocalizationService.T("DirSync.BadgeDifferent"),
                        _ => "[=]"
                    };
                    return $"{badge,-18}  {d.RelativePath}   (L: {(d.LeftSize.HasValue ? Format.Size(d.LeftSize.Value) : "-")} | R: {(d.RightSize.HasValue ? Format.Size(d.RightSize.Value) : "-")})";
                }).ToList();

                summaryText.Text = LocalizationService.Format(
                    "DirSync.SummaryFormat",
                    currentDiffs.Count,
                    nonIdentical.Count,
                    currentDiffs.Count - nonIdentical.Count);
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
            Content = LocalizationService.T("DirSync.SyncNow"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };
        var closeBtn = new Button
        {
            Content = LocalizationService.T("Common.Close"),
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
            LocalizationService.T("DirSync.WindowTitle"),
            "⇄",
            bodyGrid,
            defaultWidth: 1080,
            defaultHeight: 740,
            fontTarget: ChildWindowFontTarget.DirectorySync,
            titleProvider: () => LocalizationService.T("DirSync.WindowTitle"));

        bool syncConfirmed = false;
        int capturedDirIndex = 0;
        string capturedLeftDir = leftDir;
        string capturedRightDir = rightDir;
        bool capturedDeleteOrphans = false;

        syncNowBtn.Click += (_, _) =>
        {
            capturedDirIndex = dirCombo.SelectedIndex;
            capturedLeftDir = leftBox.Text.Trim();
            capturedRightDir = rightBox.Text.Trim();
            capturedDeleteOrphans = deleteOrphansCheck.IsChecked == true;
            syncConfirmed = true;
            win.Close();
        };
        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(syncConfirmed);
        win.Activate();

        _ = RunCompareAsync();

        if (!await tcs.Task)
            return false;

        if (currentDiffs.Count == 0 && Directory.Exists(capturedLeftDir) && Directory.Exists(capturedRightDir))
            currentDiffs = await DirectorySyncService.CompareDirectoriesAsync(capturedLeftDir, capturedRightDir, SyncCompareMode.SizeAndTimestamp, includeSubdirectories: true);

        var direction = capturedDirIndex switch
        {
            1 => SyncDirectionMode.LeftToRightMirror,
            2 => SyncDirectionMode.RightToLeftMirror,
            _ => SyncDirectionMode.BidirectionalMerge,
        };

        var syncRes = await DirectorySyncService.SynchronizeAsync(
            capturedLeftDir,
            capturedRightDir,
            currentDiffs,
            direction,
            deleteOrphans: capturedDeleteOrphans);

        owner.ViewModel.StatusNotification = LocalizationService.Format(
            "DirSync.DoneNotification",
            syncRes.CopiedLeftToRight,
            syncRes.CopiedRightToLeft,
            syncRes.DeletedOrphans);
        if (syncRes.Errors.Count > 0)
            ShowMessage(LocalizationService.T("DirSync.NotesTitle"), string.Join(Environment.NewLine, syncRes.Errors.Take(10)));

        return true;
    }

    public async Task ShowDeepSearchDialogAsync(string initialDirectory)
    {
        var tcs = new TaskCompletionSource<int>(); // 0 = close, 1 = open as tab, 2 = jump to hit

        var rootBox = new TextBox { Header = LocalizationService.T("DeepSearch.RootFolder"), Text = initialDirectory };
        var patternBox = new TextBox { Header = LocalizationService.T("DeepSearch.FileNameHeader"), PlaceholderText = "*.*" };
        var contentBox = new TextBox
        {
            Header = LocalizationService.T("DeepSearch.ContentHeader"),
            PlaceholderText = LocalizationService.T("DeepSearch.ContentPlaceholder"),
        };

        var filterGrid = new Grid { ColumnSpacing = 10 };
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(patternBox, 0);
        Grid.SetColumn(contentBox, 1);
        filterGrid.Children.Add(patternBox);
        filterGrid.Children.Add(contentBox);

        var regexCheck = new CheckBox { Content = LocalizationService.T("DeepSearch.RegexName") };
        var caseContentCheck = new CheckBox { Content = LocalizationService.T("DeepSearch.CaseContent") };
        var hiddenCheck = new CheckBox { Content = LocalizationService.T("DeepSearch.IncludeHidden") };
        var searchBtn = new Button
        {
            Content = LocalizationService.T("DeepSearch.StartSearch"),
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
            Text = LocalizationService.T("DeepSearch.Ready"),
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
                statusText.Text = LocalizationService.T("DeepSearch.FolderMissing");
                return;
            }

            searchBtn.IsEnabled = false;
            searchProgressBar.Visibility = Visibility.Visible;
            statusText.Text = LocalizationService.T("DeepSearch.Searching");
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
                statusText.Text = LocalizationService.Format("DeepSearch.FoundFormat", foundEntries.Count);
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
            Content = LocalizationService.T("DeepSearch.OpenAsTab"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };
        var jumpBtn = new Button
        {
            Content = LocalizationService.T("DeepSearch.JumpToHit"),
            Padding = new Thickness(14, 7, 14, 7),
        };
        var closeBtn = new Button
        {
            Content = LocalizationService.T("Common.Close"),
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
            LocalizationService.T("DeepSearch.WindowTitle"),
            "🔍",
            bodyGrid,
            defaultWidth: 1080,
            defaultHeight: 740,
            fontTarget: ChildWindowFontTarget.DeepSearch,
            titleProvider: () => LocalizationService.T("DeepSearch.WindowTitle"));

        int actionChoice = 0;
        int capturedSelectedIndex = -1;
        string capturedPatternText = "";

        openTabBtn.Click += async (_, _) =>
        {
            if (foundEntries.Count == 0)
                await ExecuteSearchAsync();
            capturedPatternText = patternBox.Text.Trim();
            actionChoice = 1;
            win.Close();
        };
        jumpBtn.Click += (_, _) =>
        {
            capturedSelectedIndex = resultsList.SelectedIndex;
            actionChoice = 2;
            win.Close();
        };
        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(actionChoice);
        win.Activate();

        int result = await tcs.Task;
        if (result == 1)
        {
            if (foundEntries.Count > 0 && owner.ViewModel.ActivePanel.ActiveTab is { } tab)
            {
                string label = string.IsNullOrWhiteSpace(capturedPatternText)
                    ? LocalizationService.T("DeepSearch.ResultsTabDefault")
                    : capturedPatternText;
                await tab.LoadSearchResultsAsync(label, foundEntries);
            }
        }
        else if (result == 2 &&
                 capturedSelectedIndex >= 0 &&
                 capturedSelectedIndex < foundEntries.Count &&
                 owner.ViewModel.ActivePanel.ActiveTab is { } activeTab)
        {
            var chosen = foundEntries[capturedSelectedIndex];
            var dir = chosen.IsDirectory ? chosen.FullPath : Path.GetDirectoryName(chosen.FullPath);
            if (!string.IsNullOrEmpty(dir))
                await activeTab.NavigateAsync(dir, selectName: chosen.Name);
        }
    }

    public async Task<bool> ShowDuplicateFinderDialogAsync(string initialDirectory)
    {
        var tcs = new TaskCompletionSource<bool>();

        var rootBox = new TextBox { Header = LocalizationService.T("Duplicates.RootFolder"), Text = initialDirectory };
        var scanBtn = new Button
        {
            Content = LocalizationService.T("Duplicates.ScanBtn"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 6, 16, 6),
        };
        var selectDupesBtn = new Button
        {
            Content = LocalizationService.T("Duplicates.SelectAllBtn"),
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
            Text = LocalizationService.T("Duplicates.InitialHint"),
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

        var deleteBtn = new Button
        {
            Content = LocalizationService.T("Duplicates.DeleteSelectedBtn"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
        };
        var closeBtn = new Button
        {
            Content = LocalizationService.T("Common.Close"),
            Padding = new Thickness(14, 7, 14, 7),
        };

        var currentGroups = new List<DuplicateGroup>();
        var flatCandidateFiles = new List<(FileEntry File, bool IsFirstInGroup)>();
        var displayRows = new List<string>();
        var trackedSelectedIndices = new HashSet<int>();
        bool suppressSelectionSync = false;
        bool anyDeletedInSession = false;

        HashSet<int> GetCurrentlySelectedIndices()
        {
            var set = new HashSet<int>(trackedSelectedIndices);
            foreach (var r in dupeList.SelectedRanges)
            {
                for (int i = r.FirstIndex; i < r.FirstIndex + (int)r.Length; i++)
                {
                    if (i >= 0 && i < flatCandidateFiles.Count)
                        set.Add(i);
                }
            }
            foreach (var itemObj in dupeList.SelectedItems)
            {
                if (itemObj is string rowText)
                {
                    int idx = displayRows.IndexOf(rowText);
                    if (idx >= 0 && idx < flatCandidateFiles.Count)
                        set.Add(idx);
                }
            }
            return set;
        }

        void UpdateDeleteButtonLabel()
        {
            int count = GetCurrentlySelectedIndices().Count;
            deleteBtn.Content = count > 0
                ? LocalizationService.Format("Duplicates.DeleteSelectedCountBtn", count)
                : LocalizationService.T("Duplicates.DeleteSelectedBtn");
        }

        void RebuildCandidateListFromGroups()
        {
            suppressSelectionSync = true;
            try
            {
                flatCandidateFiles.Clear();
                displayRows = [];
                trackedSelectedIndices.Clear();

                int groupNum = 1;
                foreach (var g in currentGroups)
                {
                    bool first = true;
                    foreach (var f in g.Files)
                    {
                        flatCandidateFiles.Add((f, first));
                        string tag = first
                            ? LocalizationService.T("Duplicates.KeepOriginalTag")
                            : LocalizationService.T("Duplicates.DuplicateTag");
                        displayRows.Add($"{LocalizationService.Format("Duplicates.GroupPrefix", groupNum)} ({Format.Size(g.FileSizeBytes)}) {tag}  {f.RelativeSubPath ?? f.FullPath}");
                        first = false;
                    }
                    groupNum++;
                }

                dupeList.ItemsSource = displayRows;
                selectDupesBtn.IsEnabled = flatCandidateFiles.Any(c => !c.IsFirstInGroup);
            }
            finally
            {
                suppressSelectionSync = false;
                UpdateDeleteButtonLabel();
            }
        }

        dupeList.SelectionChanged += (_, e) =>
        {
            if (suppressSelectionSync)
                return;

            foreach (var added in e.AddedItems)
            {
                if (added is string s)
                {
                    int idx = displayRows.IndexOf(s);
                    if (idx >= 0) trackedSelectedIndices.Add(idx);
                }
            }
            foreach (var removed in e.RemovedItems)
            {
                if (removed is string s)
                {
                    int idx = displayRows.IndexOf(s);
                    if (idx >= 0) trackedSelectedIndices.Remove(idx);
                }
            }

            UpdateDeleteButtonLabel();
        };

        scanBtn.Click += async (_, _) =>
        {
            string dir = rootBox.Text.Trim();
            if (!Directory.Exists(dir))
            {
                statusText.Text = LocalizationService.T("Duplicates.FolderMissing");
                return;
            }

            scanBtn.IsEnabled = false;
            selectDupesBtn.IsEnabled = false;
            deleteBtn.IsEnabled = false;
            currentGroups.Clear();
            flatCandidateFiles.Clear();
            displayRows = [];
            trackedSelectedIndices.Clear();
            dupeList.ItemsSource = null;
            UpdateDeleteButtonLabel();

            dupeProgressBar.Visibility = Visibility.Visible;
            dupeProgressBar.IsIndeterminate = true;
            dupeProgressBar.Value = 0;
            progressPercentText.Text = LocalizationService.T("Duplicates.Scanning");
            statusText.Text = LocalizationService.T("Duplicates.Stage1");

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
                            ? $"{info.ProcessedFiles}"
                            : LocalizationService.T("Duplicates.Scanning");
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

                currentGroups = scanRes.Groups.ToList();
                RebuildCandidateListFromGroups();

                dupeProgressBar.IsIndeterminate = false;
                dupeProgressBar.Value = 100;
                progressPercentText.Text = "100%";
                statusText.Text = LocalizationService.Format(
                    "Duplicates.SummaryFormat",
                    scanRes.Groups.Count,
                    Format.Size(scanRes.TotalWastedBytes));
            }
            catch (Exception ex)
            {
                dupeProgressBar.IsIndeterminate = false;
                dupeProgressBar.Visibility = Visibility.Collapsed;
                progressPercentText.Text = "";
                statusText.Text = $"⚠ {ex.Message}";
            }
            finally
            {
                scanBtn.IsEnabled = true;
                deleteBtn.IsEnabled = true;
            }
        };

        selectDupesBtn.Click += (_, _) =>
        {
            suppressSelectionSync = true;
            try
            {
                trackedSelectedIndices.Clear();
                dupeList.SelectedItems.Clear();
                for (int i = 0; i < flatCandidateFiles.Count; i++)
                {
                    if (!flatCandidateFiles[i].IsFirstInGroup)
                    {
                        trackedSelectedIndices.Add(i);
                        try { dupeList.SelectRange(new Microsoft.UI.Xaml.Data.ItemIndexRange(i, 1)); } catch { }
                        if (i < displayRows.Count && !dupeList.SelectedItems.Contains(displayRows[i]))
                        {
                            try { dupeList.SelectedItems.Add(displayRows[i]); } catch { }
                        }
                    }
                }
            }
            finally
            {
                suppressSelectionSync = false;
                UpdateDeleteButtonLabel();
            }

            statusText.Text = LocalizationService.Instance.EffectiveLanguage == "de"
                ? $"{trackedSelectedIndices.Count} Duplikat(e) zur Löschung markiert. Klicken Sie auf „Markierte Duplikate löschen“."
                : $"{trackedSelectedIndices.Count} duplicate(s) selected for deletion. Click \"Delete Selected Duplicates\".";
        };

        var topPanel = new StackPanel { Spacing = 10 };
        topPanel.Children.Add(rootBox);
        topPanel.Children.Add(btnRow);
        topPanel.Children.Add(dupeProgressBar);
        topPanel.Children.Add(statusRow);

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
            LocalizationService.T("Duplicates.WindowTitle"),
            "🧹",
            bodyGrid,
            defaultWidth: 1080,
            defaultHeight: 740,
            fontTarget: ChildWindowFontTarget.DuplicateFinder,
            titleProvider: () => LocalizationService.T("Duplicates.WindowTitle"));

        deleteBtn.Click += async (_, _) =>
        {
            var selectedIndices = GetCurrentlySelectedIndices()
                .Where(i => i >= 0 && i < flatCandidateFiles.Count)
                .OrderBy(i => i)
                .ToList();

            if (selectedIndices.Count == 0)
            {
                statusText.Text = LocalizationService.T("Duplicates.NoSelectionWarning");
                return;
            }

            var pathsToDelete = selectedIndices
                .Select(i => flatCandidateFiles[i].File.FullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            deleteBtn.IsEnabled = false;
            scanBtn.IsEnabled = false;
            selectDupesBtn.IsEnabled = false;
            statusText.Text = LocalizationService.Format("Duplicates.DeletingFormat", pathsToDelete.Count);

            try
            {
                var delResult = await Task.Run(() => DuplicateFinderService.DeleteDuplicateFiles(pathsToDelete));

                if (delResult.DeletedCount > 0)
                {
                    anyDeletedInSession = true;
                    var deletedSet = new HashSet<string>(delResult.DeletedPaths, StringComparer.OrdinalIgnoreCase);

                    // Update groups by removing deleted files and dropping groups that now have < 2 files
                    var updatedGroups = new List<DuplicateGroup>();
                    foreach (var g in currentGroups)
                    {
                        var remainingFiles = g.Files.Where(f => !deletedSet.Contains(f.FullPath) && File.Exists(f.FullPath)).ToList();
                        if (remainingFiles.Count > 1)
                            updatedGroups.Add(new DuplicateGroup(g.Sha256Hash, g.FileSizeBytes, remainingFiles));
                    }

                    currentGroups = updatedGroups;
                    RebuildCandidateListFromGroups();

                    string summary = LocalizationService.Format(
                        "Duplicates.DeletedNotification",
                        delResult.DeletedCount,
                        Format.Size(delResult.FreedBytes));
                    owner.ViewModel.StatusNotification = summary;
                    statusText.Text = $"✓ {summary} · {currentGroups.Count}";

                    if (owner.ViewModel.Left.ActiveTab is { } lt)
                        await lt.ReloadAsync();
                    if (owner.ViewModel.Right.ActiveTab is { } rt)
                        await rt.ReloadAsync();
                }
                else
                {
                    statusText.Text = LocalizationService.Instance.EffectiveLanguage == "de"
                        ? "⚠ Es konnten keine Dateien gelöscht werden."
                        : "⚠ No files could be deleted.";
                }

                if (delResult.Errors.Count > 0)
                {
                    ShowMessage(LocalizationService.T("Duplicates.DeleteNotesTitle"), string.Join(Environment.NewLine, delResult.Errors.Take(10)));
                }
            }
            finally
            {
                scanBtn.IsEnabled = true;
                deleteBtn.IsEnabled = true;
                selectDupesBtn.IsEnabled = flatCandidateFiles.Any(c => !c.IsFirstInGroup);
            }
        };

        closeBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(anyDeletedInSession);
        win.Activate();

        return await tcs.Task;
    }

    public async Task<string?> ShowRemoteConnectionManagerDialogAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        var store = RemoteProfileStore.Shared;
        var profileCombo = new ComboBox
        {
            Header = LocalizationService.T("Remote.SavedProfiles"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var nameBox = new TextBox
        {
            Header = LocalizationService.T("Remote.ProfileName"),
            PlaceholderText = LocalizationService.T("Remote.ProfileNamePlaceholder"),
        };
        var protoCombo = new ComboBox
        {
            Header = LocalizationService.T("Remote.Protocol"),
            ItemsSource = new[] { "SFTP (SSH)", "FTPS (Explicit TLS)", "FTP (Standard)" },
            SelectedIndex = 0,
            Width = 210,
        };
        var hostBox = new TextBox { Header = LocalizationService.T("Remote.Host"), PlaceholderText = "example.com" };
        var portNum = new NumberBox { Header = LocalizationService.T("Remote.Port"), Value = 22, Minimum = 1, Maximum = 65535, Width = 120 };

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

        var userBox = new TextBox { Header = LocalizationService.T("Remote.Username"), PlaceholderText = "root / ubuntu" };
        var passBox = new PasswordBox { Header = LocalizationService.T("Remote.Password") };
        var keyPathBox = new TextBox { Header = LocalizationService.T("Remote.SshKeyPath"), PlaceholderText = @"C:\Users\...\.ssh\id_ed25519" };
        var remoteDirBox = new TextBox { Header = LocalizationService.T("Remote.InitialDir"), Text = "/" };

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
            items.Insert(0, LocalizationService.T("Remote.NewProfileItem"));
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

        var saveBtn = new Button { Content = LocalizationService.T("Remote.SaveProfileBtn"), Padding = new Thickness(14, 6, 14, 6) };
        var deleteBtn = new Button { Content = LocalizationService.T("Remote.DeleteProfileBtn"), Padding = new Thickness(14, 6, 14, 6) };
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
            Content = LocalizationService.T("Remote.ConnectBtn"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 7, 18, 7),
        };
        var cancelBtn = new Button
        {
            Content = LocalizationService.T("Common.Cancel"),
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
            LocalizationService.T("Remote.WindowTitle"),
            "🌐",
            bodyGrid,
            defaultWidth: 880,
            defaultHeight: 650,
            fontTarget: ChildWindowFontTarget.FtpManager,
            titleProvider: () => LocalizationService.T("Remote.WindowTitle"));

        bool connectConfirmed = false;
        RemoteConnectionProfile? capturedProfile = null;
        connectBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(hostBox.Text))
            {
                capturedProfile = BuildCurrentProfile();
                connectConfirmed = true;
            }
            win.Close();
        };
        cancelBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(connectConfirmed);
        win.Activate();

        if (!await tcs.Task || capturedProfile is null)
            return null;

        store.AddOrUpdate(capturedProfile);
        return capturedProfile.BuildRootUri();
    }

    public async Task<string?> ShowCreateArchiveDialogAsync(IReadOnlyList<string> sourcePaths, string defaultOutputDirectory)
    {
        var tcs = new TaskCompletionSource<bool>();

        string singleTrimmed = sourcePaths.Count == 1 ? sourcePaths[0].TrimEnd('\\', '/') : "";
        string defaultStem = LocalizationService.Instance.EffectiveLanguage == "de" ? "Archiv" : "Archive";
        string baseStem = sourcePaths.Count == 1
            ? (Directory.Exists(singleTrimmed) ? Path.GetFileName(singleTrimmed) : Path.GetFileNameWithoutExtension(singleTrimmed))
            : defaultStem;
        if (string.IsNullOrWhiteSpace(baseStem))
            baseStem = defaultStem;

        var formatCombo = new ComboBox
        {
            Header = LocalizationService.T("Archive.FormatHeader"),
            ItemsSource = new[]
            {
                LocalizationService.T("Archive.FormatZip"),
                LocalizationService.T("Archive.FormatTarGz"),
                LocalizationService.T("Archive.Format7z"),
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var destBox = new TextBox
        {
            Header = LocalizationService.T("Archive.DestFileHeader"),
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
            Text = LocalizationService.Format("Archive.SelectedSourcesFormat", sourcePaths.Count),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
        });

        var createBtn = new Button
        {
            Content = LocalizationService.T("Archive.CreateBtn"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 7, 18, 7),
        };
        var cancelBtn = new Button
        {
            Content = LocalizationService.T("Common.Cancel"),
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
            LocalizationService.T("Archive.WindowTitle"),
            "📦",
            bodyGrid,
            defaultWidth: 880,
            defaultHeight: 620,
            fontTarget: ChildWindowFontTarget.ArchiveDialog,
            titleProvider: () => LocalizationService.T("Archive.WindowTitle"));

        bool createConfirmed = false;
        string capturedTargetArchive = "";
        int capturedFormatIndex = 0;

        createBtn.Click += (_, _) =>
        {
            capturedTargetArchive = destBox.Text.Trim();
            capturedFormatIndex = formatCombo.SelectedIndex;
            createConfirmed = true;
            win.Close();
        };
        cancelBtn.Click += (_, _) => win.Close();
        win.Closed += (_, _) => tcs.TrySetResult(createConfirmed);
        win.Activate();

        if (!await tcs.Task)
            return null;

        if (string.IsNullOrWhiteSpace(capturedTargetArchive))
            return null;

        var format = capturedFormatIndex switch
        {
            1 => ArchiveFormat.TarGz,
            2 => ArchiveFormat.SevenZip,
            _ => ArchiveFormat.Zip,
        };

        try
        {
            owner.ViewModel.StatusNotification = LocalizationService.Format("Archive.CreatingNotification", Path.GetFileName(capturedTargetArchive));
            await ArchiveService.CreateArchiveAsync(sourcePaths, capturedTargetArchive, format);
            return capturedTargetArchive;
        }
        catch (Exception ex)
        {
            ShowMessage(LocalizationService.T("Archive.FailedTitle"), ex.Message);
            return null;
        }
    }
}
