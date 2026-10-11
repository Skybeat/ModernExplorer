using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace ModernExplorer.App.Dialogs;

public sealed partial class ShellDialogService
{
    public async Task ShowStorageTreemapDialogAsync(string initialDirectory)
    {
        var tcs = new TaskCompletionSource<bool>();
        CancellationTokenSource? scanCts = null;

        StorageTreemapNode? scannedRoot = null;
        StorageTreemapNode? currentRoot = null;
        StorageTreemapNode? selectedNode = null;
        var selectedNodes = new List<StorageTreemapNode>();
        string? highlightedExtension = null;
        bool suppressHierarchySelection = false;
        bool suppressExtensionSelection = false;

        static bool IsDe() => LocalizationService.Instance.EffectiveLanguage == "de";

        static bool IsCtrlKeyDown()
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        var rootBox = new TextBox
        {
            PlaceholderText = LocalizationService.T("Treemap.RootPlaceholder"),
            Text = initialDirectory,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // ====================================================================
        // EXPLORER-TREE DROPDOWN FLYOUT FOR DRIVE / FOLDER SELECTION
        // ====================================================================
        var treeRootStack = new StackPanel { Spacing = 2, Padding = new Thickness(4) };
        var treeScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            Content = treeRootStack,
        };

        var treeFlyoutHeader = new TextBlock
        {
            Text = IsDe()
                ? "🌳 Explorer-Baum: Laufwerk oder Ordner direkt auswählen (▶ = Unterordner aufklappen)"
                : "🌳 Explorer Tree: Select drive or folder directly (▶ = expand subfolders)",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.9,
            Margin = new Thickness(4, 2, 4, 6),
        };

        var treeFlyoutContainer = new Grid
        {
            Width = 540,
            Height = 440,
            RowSpacing = 4,
        };
        treeFlyoutContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        treeFlyoutContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(treeFlyoutHeader, 0);
        Grid.SetRow(treeScrollViewer, 1);
        treeFlyoutContainer.Children.Add(treeFlyoutHeader);
        treeFlyoutContainer.Children.Add(treeScrollViewer);

        var explorerTreeFlyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            Content = treeFlyoutContainer,
        };

        var treeDropdownBtn = new Button
        {
            Content = IsDe() ? "🌳 Explorer-Baum ▾" : "🌳 Explorer Tree ▾",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = explorerTreeFlyout,
        };
        ToolTipService.SetToolTip(
            treeDropdownBtn,
            IsDe()
                ? "Öffnet den hierarchischen Explorer-Verzeichnisbaum zur direkten Auswahl eines Laufwerks oder Ordners"
                : "Opens the hierarchical Explorer directory tree to select a drive or folder directly");

        var pathAndTreeGrid = new Grid { ColumnSpacing = 6 };
        pathAndTreeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathAndTreeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(rootBox, 0);
        Grid.SetColumn(treeDropdownBtn, 1);
        pathAndTreeGrid.Children.Add(rootBox);
        pathAndTreeGrid.Children.Add(treeDropdownBtn);

        var scanBtn = new Button
        {
            Content = LocalizationService.T("Treemap.ScanButton"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 6, 16, 6),
        };

        var cancelScanBtn = new Button
        {
            Content = LocalizationService.T("Treemap.CancelButton"),
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(12, 6, 12, 6),
        };

        var zoomUpBtn = new Button
        {
            Content = LocalizationService.T("Treemap.ZoomUpButton"),
            IsEnabled = false,
            Padding = new Thickness(12, 6, 12, 6),
        };

        var zoomRootBtn = new Button
        {
            Content = LocalizationService.T("Treemap.ZoomRootButton"),
            IsEnabled = false,
            Padding = new Thickness(12, 6, 12, 6),
        };

        var topBarGrid = new Grid { ColumnSpacing = 8 };
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pathAndTreeGrid, 0);
        Grid.SetColumn(scanBtn, 1);
        Grid.SetColumn(cancelScanBtn, 2);
        Grid.SetColumn(zoomUpBtn, 3);
        Grid.SetColumn(zoomRootBtn, 4);
        topBarGrid.Children.Add(pathAndTreeGrid);
        topBarGrid.Children.Add(scanBtn);
        topBarGrid.Children.Add(cancelScanBtn);
        topBarGrid.Children.Add(zoomUpBtn);
        topBarGrid.Children.Add(zoomRootBtn);

        var scanProgressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            IsIndeterminate = true,
            Height = 8,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var scanStatusText = new TextBlock
        {
            Text = LocalizationService.T("Treemap.ScanningInit"),
            FontSize = 12,
            Opacity = 0.88,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var zoomPathBadge = new TextBlock
        {
            Text = "",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 56, 189, 248)),
            HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var statusRow = new Grid { ColumnSpacing = 12 };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(scanStatusText, 0);
        Grid.SetColumn(zoomPathBadge, 1);
        statusRow.Children.Add(scanStatusText);
        statusRow.Children.Add(zoomPathBadge);

        var topHeaderPanel = new StackPanel { Spacing = 8 };
        topHeaderPanel.Children.Add(topBarGrid);
        topHeaderPanel.Children.Add(scanProgressBar);
        topHeaderPanel.Children.Add(statusRow);

        // ====================================================================
        // UPPER SPLIT PANE (WITH HORIZONTAL DRAGGABLE SPLITTER):
        // Left:  2. Folder Hierarchy & Top Space Consumers
        // Right: 3. File Extension Statistics & Color Legend
        // ====================================================================

        var hierarchyHeaderText = new TextBlock
        {
            Text = LocalizationService.T("Treemap.HierarchyHeader"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var hierarchyList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Extended,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var hierarchyPane = new Grid { RowSpacing = 6 };
        hierarchyPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hierarchyPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(hierarchyHeaderText, 0);

        var hierarchyBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = hierarchyList,
        };
        Grid.SetRow(hierarchyBorder, 1);
        hierarchyPane.Children.Add(hierarchyHeaderText);
        hierarchyPane.Children.Add(hierarchyBorder);

        var extHeaderText = new TextBlock
        {
            Text = LocalizationService.T("Treemap.ExtHeader"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var clearExtFilterBtn = new Button
        {
            Content = LocalizationService.T("Treemap.ShowAllTypesButton"),
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var extHeaderGrid = new Grid { ColumnSpacing = 8 };
        extHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        extHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(extHeaderText, 0);
        Grid.SetColumn(clearExtFilterBtn, 1);
        extHeaderGrid.Children.Add(extHeaderText);
        extHeaderGrid.Children.Add(clearExtFilterBtn);

        var extensionList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var extBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 128, 128, 145)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = extensionList,
        };

        var extPane = new Grid { RowSpacing = 6 };
        extPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        extPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(extHeaderGrid, 0);
        Grid.SetRow(extBorder, 1);
        extPane.Children.Add(extHeaderGrid);
        extPane.Children.Add(extBorder);

        var leftUpperCol = new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star), MinWidth = 200 };
        var splitterUpperCol = new ColumnDefinition { Width = new GridLength(12) };
        var rightUpperCol = new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star), MinWidth = 200 };

        var upperColGripPill = new Border
        {
            Width = 4,
            Height = 38,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(110, 148, 163, 184)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var upperColSplitter = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ManipulationMode = ManipulationModes.TranslateX,
            Child = upperColGripPill,
        };
        ToolTipService.SetToolTip(upperColSplitter, LocalizationService.T("Treemap.SplitterHorizontalTip"));

        upperColSplitter.PointerEntered += (_, _) =>
        {
            upperColGripPill.Background = new SolidColorBrush(Color.FromArgb(230, 56, 189, 248));
            upperColSplitter.Background = new SolidColorBrush(Color.FromArgb(30, 56, 189, 248));
        };
        upperColSplitter.PointerExited += (_, _) =>
        {
            upperColGripPill.Background = new SolidColorBrush(Color.FromArgb(110, 148, 163, 184));
            upperColSplitter.Background = new SolidColorBrush(Colors.Transparent);
        };
        upperColSplitter.ManipulationDelta += (_, e) =>
        {
            double totalW = hierarchyPane.ActualWidth + extPane.ActualWidth;
            if (totalW <= 120) return;
            double newLeftW = Math.Clamp(hierarchyPane.ActualWidth + e.Delta.Translation.X, 200, Math.Max(200, totalW - 200));
            double newRightW = Math.Max(200, totalW - newLeftW);
            leftUpperCol.Width = new GridLength(newLeftW, GridUnitType.Star);
            rightUpperCol.Width = new GridLength(newRightW, GridUnitType.Star);
        };

        var upperSplitGrid = new Grid
        {
            ColumnSpacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        upperSplitGrid.ColumnDefinitions.Add(leftUpperCol);
        upperSplitGrid.ColumnDefinitions.Add(splitterUpperCol);
        upperSplitGrid.ColumnDefinitions.Add(rightUpperCol);
        Grid.SetColumn(hierarchyPane, 0);
        Grid.SetColumn(upperColSplitter, 1);
        Grid.SetColumn(extPane, 2);
        upperSplitGrid.Children.Add(hierarchyPane);
        upperSplitGrid.Children.Add(upperColSplitter);
        upperSplitGrid.Children.Add(extPane);

        // ====================================================================
        // VERTICAL DRAGGABLE SPLITTER BETWEEN TEXTUAL LISTS AND GRAPHICAL TREEMAP
        // ====================================================================
        var verticalGripPill = new Border
        {
            Width = 52,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(120, 148, 163, 184)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var verticalPaneSplitter = new Border
        {
            Height = 12,
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            ManipulationMode = ManipulationModes.TranslateY,
            Child = verticalGripPill,
        };
        ToolTipService.SetToolTip(verticalPaneSplitter, LocalizationService.T("Treemap.SplitterVerticalTip"));

        verticalPaneSplitter.PointerEntered += (_, _) =>
        {
            verticalGripPill.Background = new SolidColorBrush(Color.FromArgb(235, 56, 189, 248));
            verticalPaneSplitter.Background = new SolidColorBrush(Color.FromArgb(30, 56, 189, 248));
        };
        verticalPaneSplitter.PointerExited += (_, _) =>
        {
            verticalGripPill.Background = new SolidColorBrush(Color.FromArgb(120, 148, 163, 184));
            verticalPaneSplitter.Background = new SolidColorBrush(Colors.Transparent);
        };

        // ====================================================================
        // LOWER MAIN PANE:
        // 1. Interactive Squarified Treemap (Tile View)
        // ====================================================================

        var treemapTitleText = new TextBlock
        {
            Text = $"{LocalizationService.T("Treemap.CanvasTitle")}  —  {LocalizationService.T("Treemap.HoverHint")}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var categoryLegendStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };

        void RebuildCategoryLegendPills()
        {
            categoryLegendStack.Children.Clear();
            foreach (var cat in new[]
            {
                StorageCategory.VideoAudio,
                StorageCategory.ArchiveDiskImage,
                StorageCategory.VirtualDiskDatabaseSystem,
                StorageCategory.CodeTextDocument,
                StorageCategory.ImageGraphics,
                StorageCategory.ExecutableBinary,
                StorageCategory.Other,
            })
            {
                var pill = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                pill.Children.Add(new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(ParseColorHex(StorageTreemapService.GetCategoryColorHex(cat))),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                pill.Children.Add(new TextBlock
                {
                    Text = StorageTreemapService.GetCategoryDisplayName(cat).Split(' ')[0].TrimEnd(','),
                    FontSize = 11,
                    Opacity = 0.82,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                categoryLegendStack.Children.Add(pill);
            }
        }

        RebuildCategoryLegendPills();

        var treemapHeaderRow = new Grid { ColumnSpacing = 10 };
        treemapHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        treemapHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(treemapTitleText, 0);
        Grid.SetColumn(categoryLegendStack, 1);
        treemapHeaderRow.Children.Add(treemapTitleText);
        treemapHeaderRow.Children.Add(categoryLegendStack);

        var treemapCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var treemapHostBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 145)),
            Background = new SolidColorBrush(Color.FromArgb(255, 18, 20, 26)),
            CornerRadius = new CornerRadius(6),
            Child = treemapCanvas,
        };

        var treemapSectionGrid = new Grid { RowSpacing = 6 };
        treemapSectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        treemapSectionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(treemapHeaderRow, 0);
        Grid.SetRow(treemapHostBorder, 1);
        treemapSectionGrid.Children.Add(treemapHeaderRow);
        treemapSectionGrid.Children.Add(treemapHostBorder);

        // ====================================================================
        // FOOTER BAR:
        // 4. Direct Explorer Actions from the Analyzer Window
        // ====================================================================

        var selectedInfoText = new TextBlock
        {
            Text = LocalizationService.T("Treemap.HoverHint"),
            FontSize = 12.5,
            Opacity = 0.9,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var showInTabBtn = new Button
        {
            Content = IsDe() ? "🎯 Im Explorer-Tab zeigen" : "🎯 Show in Explorer Tab",
            IsEnabled = false,
            Padding = new Thickness(12, 7, 12, 7),
        };

        var openFolderTabBtn = new Button
        {
            Content = LocalizationService.T("Treemap.OpenExplorerButton"),
            IsEnabled = false,
            Padding = new Thickness(12, 7, 12, 7),
        };

        var deleteSelectedBtn = new Button
        {
            Content = IsDe() ? "🗑 Markierte Datei(en) löschen (Entf)" : "🗑 Delete Selected Item(s) (Del)",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 7, 14, 7),
        };

        var confirmDeleteBtn = new Button
        {
            Content = IsDe() ? "⚠ Ja, jetzt löschen!" : "⚠ Yes, delete now!",
            Visibility = Visibility.Collapsed,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Color.FromArgb(255, 220, 38, 38)),
            Padding = new Thickness(14, 7, 14, 7),
        };

        var cancelDeleteConfirmBtn = new Button
        {
            Content = LocalizationService.T("Dialog.Cancel"),
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(10, 7, 10, 7),
        };

        var closeBtn = new Button
        {
            Content = LocalizationService.T("Dialog.Close"),
            Padding = new Thickness(14, 7, 14, 7),
        };

        var footerButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        footerButtonsPanel.Children.Add(showInTabBtn);
        footerButtonsPanel.Children.Add(openFolderTabBtn);
        footerButtonsPanel.Children.Add(deleteSelectedBtn);
        footerButtonsPanel.Children.Add(confirmDeleteBtn);
        footerButtonsPanel.Children.Add(cancelDeleteConfirmBtn);
        footerButtonsPanel.Children.Add(closeBtn);

        var footerGrid = new Grid { ColumnSpacing = 12 };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(selectedInfoText, 0);
        Grid.SetColumn(footerButtonsPanel, 1);
        footerGrid.Children.Add(selectedInfoText);
        footerGrid.Children.Add(footerButtonsPanel);

        var upperRowDef = new RowDefinition { Height = new GridLength(225, GridUnitType.Pixel), MinHeight = 95 };
        var lowerRowDef = new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 };

        var bodyGrid = new Grid
        {
            Padding = new Thickness(16, 12, 16, 14),
            RowSpacing = 6,
        };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(upperRowDef);
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(lowerRowDef);
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topHeaderPanel, 0);
        Grid.SetRow(upperSplitGrid, 1);
        Grid.SetRow(verticalPaneSplitter, 2);
        Grid.SetRow(treemapSectionGrid, 3);
        Grid.SetRow(footerGrid, 4);
        bodyGrid.Children.Add(topHeaderPanel);
        bodyGrid.Children.Add(upperSplitGrid);
        bodyGrid.Children.Add(verticalPaneSplitter);
        bodyGrid.Children.Add(treemapSectionGrid);
        bodyGrid.Children.Add(footerGrid);

        verticalPaneSplitter.ManipulationDelta += (_, e) =>
        {
            double currentUpperH = upperSplitGrid.ActualHeight > 0 ? upperSplitGrid.ActualHeight : upperRowDef.Height.Value;
            double maxUpperH = Math.Max(140, bodyGrid.ActualHeight - topHeaderPanel.ActualHeight - footerGrid.ActualHeight - 150);
            double newUpperH = Math.Clamp(currentUpperH + e.Delta.Translation.Y, 95, maxUpperH);
            upperRowDef.Height = new GridLength(newUpperH, GridUnitType.Pixel);
        };

        var currentHierarchyNodes = new List<StorageTreemapNode>();
        var currentExtStats = new List<StorageExtensionStat>();

        List<StorageTreemapNode> GetDeletableSelectedNodes()
        {
            var raw = selectedNodes
                .Where(n => n is not null && !ReferenceEquals(n, scannedRoot))
                .Distinct()
                .ToList();

            if (raw.Count == 0 && selectedNode is not null && !ReferenceEquals(selectedNode, scannedRoot))
                raw.Add(selectedNode);

            // Filter out nodes whose ancestor is already in the deletion list
            return raw
                .Where(candidate => !raw.Any(other => !ReferenceEquals(other, candidate) && IsNodeOrDescendantOf(candidate, other)))
                .ToList();
        }

        void ResetDeleteConfirmState()
        {
            confirmDeleteBtn.Visibility = Visibility.Collapsed;
            cancelDeleteConfirmBtn.Visibility = Visibility.Collapsed;
            deleteSelectedBtn.Visibility = Visibility.Visible;
        }

        void UpdateSelectedNodeUi()
        {
            ResetDeleteConfirmState();
            var deletable = GetDeletableSelectedNodes();
            bool hasSelection = selectedNode is not null || deletable.Count > 0;
            bool hasRoot = currentRoot is not null;

            showInTabBtn.IsEnabled = hasSelection || hasRoot;
            openFolderTabBtn.IsEnabled = hasSelection || hasRoot;
            deleteSelectedBtn.IsEnabled = deletable.Count > 0;

            deleteSelectedBtn.Content = deletable.Count > 1
                ? (IsDe() ? $"🗑 {deletable.Count} markierte löschen (Entf)" : $"🗑 Delete {deletable.Count} selected (Del)")
                : (IsDe() ? "🗑 Markierte Datei(en) löschen (Entf)" : "🗑 Delete Selected Item(s) (Del)");

            if (deletable.Count > 1)
            {
                long totalSelectedBytes = deletable.Sum(n => n.Size);
                double pctTotal = scannedRoot is { Size: > 0 } ? (double)totalSelectedBytes * 100.0 / scannedRoot.Size : 0.0;
                selectedInfoText.Text = IsDe()
                    ? $"Mehrfachauswahl: {deletable.Count} Elemente markiert  ·  Gesamtgröße: {Format.Size(totalSelectedBytes)} ({pctTotal:0.0}% Gesamt)  ·  Rechtsklick oder [Entf] zum Löschen"
                    : $"Multi-select: {deletable.Count} items selected  ·  Total size: {Format.Size(totalSelectedBytes)} ({pctTotal:0.0}% total)  ·  Right-click or [Del] to delete";
                return;
            }

            var active = deletable.FirstOrDefault() ?? selectedNode;
            if (active is null)
            {
                if (currentRoot is not null)
                {
                    selectedInfoText.Text = IsDe()
                        ? $"Aktuelle Ansicht: {currentRoot.FullPath} ({Format.Size(currentRoot.Size)})"
                        : $"Current view: {currentRoot.FullPath} ({Format.Size(currentRoot.Size)})";
                }
                else
                {
                    selectedInfoText.Text = LocalizationService.T("Treemap.HoverHint");
                }
                return;
            }

            double pctView = currentRoot is { Size: > 0 } ? (double)active.Size * 100.0 / currentRoot.Size : 100.0;
            double pctRoot = scannedRoot is { Size: > 0 } ? (double)active.Size * 100.0 / scannedRoot.Size : pctView;
            string kind = active.IsDirectory
                ? (IsDe()
                    ? $"Ordner ({active.FileCount:N0} Dateien, {active.FolderCount:N0} Unterordner)"
                    : $"Folder ({active.FileCount:N0} files, {active.FolderCount:N0} subfolders)")
                : $"{LocalizationService.T("File.FileType")} ({StorageTreemapService.GetCategoryDisplayName(active.Category)})";

            selectedInfoText.Text = IsDe()
                ? $"Ausgewählt: {active.Name}  ·  {Format.Size(active.Size)} ({pctView:0.0}% Ansicht / {pctRoot:0.0}% Gesamt)  ·  {kind}"
                : $"Selected: {active.Name}  ·  {Format.Size(active.Size)} ({pctView:0.0}% view / {pctRoot:0.0}% total)  ·  {kind}";
        }

        void HandleNodeSelectionClick(StorageTreemapNode clickedNode, bool isCtrlHeld)
        {
            if (isCtrlHeld)
            {
                int existingIdx = selectedNodes.FindIndex(n => ReferenceEquals(n, clickedNode));
                if (existingIdx >= 0)
                {
                    selectedNodes.RemoveAt(existingIdx);
                    selectedNode = selectedNodes.LastOrDefault();
                }
                else
                {
                    selectedNodes.Add(clickedNode);
                    selectedNode = clickedNode;
                }
            }
            else
            {
                selectedNodes.Clear();
                selectedNodes.Add(clickedNode);
                selectedNode = clickedNode;
            }

            SyncSelectionToHierarchyList();
            UpdateSelectedNodeUi();
            RenderTreemapCanvas();
        }

        async Task ExecuteDeleteNodesAsync(IReadOnlyList<StorageTreemapNode> nodesToDelete)
        {
            ResetDeleteConfirmState();
            if (nodesToDelete.Count == 0)
                return;

            try
            {
                long freedBytes = 0;
                int deletedCount = 0;
                string firstDeletedName = nodesToDelete[0].Name;

                var fileNodes = nodesToDelete.Where(n => !n.IsDirectory).ToList();
                var dirNodes = nodesToDelete.Where(n => n.IsDirectory).ToList();

                if (fileNodes.Count > 0)
                {
                    var filePaths = fileNodes.Select(n => n.FullPath).ToList();
                    var delRes = await Task.Run(() => DuplicateFinderService.DeleteDuplicateFiles(filePaths));
                    var deletedPathSet = new HashSet<string>(delRes.DeletedPaths, StringComparer.OrdinalIgnoreCase);

                    foreach (var fn in fileNodes)
                    {
                        if (deletedPathSet.Contains(fn.FullPath) || !File.Exists(fn.FullPath))
                        {
                            freedBytes += fn.Size;
                            deletedCount++;
                            StorageTreemapService.RemoveNodeAndRecalculate(fn);
                        }
                    }
                }

                foreach (var dn in dirNodes)
                {
                    if (Directory.Exists(dn.FullPath))
                    {
                        await Task.Run(() => Directory.Delete(dn.FullPath, recursive: true));
                    }
                    if (!Directory.Exists(dn.FullPath))
                    {
                        freedBytes += dn.Size;
                        deletedCount++;
                        var fallbackParent = dn.Parent ?? scannedRoot;
                        if (currentRoot is not null && IsNodeOrDescendantOf(currentRoot, dn))
                            currentRoot = fallbackParent;
                        StorageTreemapService.RemoveNodeAndRecalculate(dn);
                    }
                }

                selectedNodes.Clear();
                selectedNode = currentRoot ?? scannedRoot;
                PopulateUpperListsAndTreemap();

                if (deletedCount > 0)
                {
                    string label = deletedCount == 1
                        ? (IsDe() ? $"„{firstDeletedName}“ gelöscht" : $"\"{firstDeletedName}\" deleted")
                        : (IsDe() ? $"{deletedCount} Elemente gelöscht" : $"{deletedCount} items deleted");
                    selectedInfoText.Text = LocalizationService.Format("Treemap.DeletedStatus", label, Format.Size(freedBytes));
                    owner.ViewModel.StatusNotification = LocalizationService.Format("Treemap.DeletedStatus", label, Format.Size(freedBytes));

                    if (owner.ViewModel.Left.ActiveTab is not null)
                        await owner.ViewModel.Left.ActiveTab.ReloadAsync();
                    if (owner.ViewModel.Right.ActiveTab is not null)
                        await owner.ViewModel.Right.ActiveTab.ReloadAsync();
                }
                else
                {
                    selectedInfoText.Text = IsDe()
                        ? "⚠ Es konnten keine Elemente gelöscht werden."
                        : "⚠ No items could be deleted.";
                }
            }
            catch (Exception ex)
            {
                selectedInfoText.Text = LocalizationService.Format("Treemap.DeleteErrorStatus", ex.Message);
            }
        }

        async Task NavigateToNodeInExplorerTabAsync(StorageTreemapNode? targetNode)
        {
            var target = targetNode ?? selectedNode ?? currentRoot;
            if (target is null) return;

            var activeTab = owner.ViewModel.ActivePanel.ActiveTab;
            if (activeTab is null) return;

            string parentDir = Path.GetDirectoryName(target.FullPath.TrimEnd('\\', '/')) ?? "";
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                await activeTab.NavigateAsync(parentDir, selectName: target.Name);
            }
            else if (Directory.Exists(target.FullPath))
            {
                await activeTab.NavigateAsync(target.FullPath);
            }

            WindowChromeHelper.BringWindowToForeground(owner);
        }

        async Task OpenNodeFolderAsNewTabAsync(StorageTreemapNode? targetNode)
        {
            var target = targetNode ?? selectedNode ?? currentRoot;
            if (target is null) return;

            string folderPath = target.IsDirectory
                ? target.FullPath
                : (Path.GetDirectoryName(target.FullPath) ?? currentRoot?.FullPath ?? "");

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            await owner.ViewModel.ActivePanel.CreateTabAsync(folderPath);
            selectedInfoText.Text = IsDe()
                ? $"✓ Neuer Tab geöffnet: {folderPath}"
                : $"✓ Opened in new tab: {folderPath}";
        }

        MenuFlyout BuildNodeContextMenu(StorageTreemapNode contextNode)
        {
            var menu = new MenuFlyout();

            menu.Opening += (_, _) =>
            {
                // Ensure the right-clicked node is part of the selection if not already selected
                bool alreadySelected = selectedNodes.Any(n => ReferenceEquals(n, contextNode) || IsNodeOrDescendantOf(contextNode, n));
                if (!alreadySelected)
                {
                    selectedNodes.Clear();
                    selectedNodes.Add(contextNode);
                    selectedNode = contextNode;
                    SyncSelectionToHierarchyList();
                    UpdateSelectedNodeUi();
                    RenderTreemapCanvas();
                }

                menu.Items.Clear();
                var deletable = GetDeletableSelectedNodes();
                long totalDelSize = deletable.Sum(n => n.Size);

                var deleteItem = new MenuFlyoutItem
                {
                    Text = deletable.Count > 1
                        ? (IsDe()
                            ? $"🗑 {deletable.Count} ausgewählte Elemente löschen ({Format.Size(totalDelSize)})   [Entf]"
                            : $"🗑 Delete {deletable.Count} selected items ({Format.Size(totalDelSize)})   [Del]")
                        : (IsDe()
                            ? $"🗑 „{contextNode.Name}“ löschen ({Format.Size(contextNode.Size)})   [Entf]"
                            : $"🗑 Delete \"{contextNode.Name}\" ({Format.Size(contextNode.Size)})   [Del]"),
                    IsEnabled = deletable.Count > 0,
                };
                deleteItem.Click += async (_, _) =>
                {
                    var targets = GetDeletableSelectedNodes();
                    await ExecuteDeleteNodesAsync(targets);
                };
                menu.Items.Add(deleteItem);

                menu.Items.Add(new MenuFlyoutSeparator());

                if (contextNode.IsDirectory)
                {
                    var zoomItem = new MenuFlyoutItem
                    {
                        Text = IsDe()
                            ? $"🔍 In Ordner „{contextNode.Name}“ hineinzoomen"
                            : $"🔍 Zoom into folder \"{contextNode.Name}\"",
                    };
                    zoomItem.Click += (_, _) => ZoomToNode(contextNode);
                    menu.Items.Add(zoomItem);
                }

                var showInExplorerItem = new MenuFlyoutItem
                {
                    Text = IsDe() ? "🎯 Im aktiven Explorer-Tab zeigen" : "🎯 Show in active Explorer Tab",
                };
                showInExplorerItem.Click += async (_, _) => await NavigateToNodeInExplorerTabAsync(contextNode);
                menu.Items.Add(showInExplorerItem);

                var openNewTabItem = new MenuFlyoutItem
                {
                    Text = LocalizationService.T("Treemap.MenuOpenTab"),
                };
                openNewTabItem.Click += async (_, _) => await OpenNodeFolderAsNewTabAsync(contextNode);
                menu.Items.Add(openNewTabItem);

                menu.Items.Add(new MenuFlyoutSeparator());

                var copyPathItem = new MenuFlyoutItem
                {
                    Text = deletable.Count > 1
                        ? (IsDe()
                            ? $"📋 {deletable.Count} Pfade in Zwischenablage kopieren"
                            : $"📋 Copy {deletable.Count} paths to clipboard")
                        : LocalizationService.T("Treemap.MenuCopyPath"),
                };
                copyPathItem.Click += (_, _) =>
                {
                    try
                    {
                        var pkg = new DataPackage();
                        var paths = deletable.Count > 1
                            ? string.Join(Environment.NewLine, deletable.Select(n => n.FullPath))
                            : contextNode.FullPath;
                        pkg.SetText(paths);
                        Clipboard.SetContent(pkg);
                        selectedInfoText.Text = LocalizationService.Format("Treemap.MenuPathCopiedStatus", paths);
                    }
                    catch { }
                };
                menu.Items.Add(copyPathItem);
            };

            return menu;
        }

        void RenderTreemapCanvas()
        {
            treemapCanvas.Children.Clear();
            double width = treemapHostBorder.ActualWidth - 4;
            double height = treemapHostBorder.ActualHeight - 4;
            if (width < 40 || height < 40 || currentRoot is null || currentRoot.Size <= 0)
            {
                if (currentRoot is { Size: 0 })
                {
                    var emptyTb = new TextBlock
                    {
                        Text = LocalizationService.T("Treemap.FolderEmpty"),
                        Opacity = 0.65,
                        FontSize = 13,
                    };
                    Canvas.SetLeft(emptyTb, 20);
                    Canvas.SetTop(emptyTb, 20);
                    treemapCanvas.Children.Add(emptyTb);
                }
                return;
            }

            var rects = StorageTreemapService.BuildSquarifiedTreemapLayout(
                currentRoot,
                width,
                height,
                maxDepth: 2,
                minTileSize: 4.0);

            bool showTips = owner.ViewModel.Settings.ShowToolTips;
            bool hasExtFilter = !string.IsNullOrWhiteSpace(highlightedExtension);

            foreach (var r in rects)
            {
                bool isNodeSelected = selectedNodes.Any(sel => ReferenceEquals(sel, r.Node) || IsNodeOrDescendantOf(r.Node, sel)) ||
                                      ReferenceEquals(selectedNode, r.Node) ||
                                      (selectedNode is not null && IsNodeOrDescendantOf(r.Node, selectedNode));
                bool isDirectlyInMultiSelect = selectedNodes.Count > 1 && selectedNodes.Any(sel => ReferenceEquals(sel, r.Node));

                if (r.IsGroupContainer)
                {
                    var groupBorder = new Border
                    {
                        Width = Math.Max(1, r.Width - 1),
                        Height = Math.Max(1, r.Height - 1),
                        Background = new SolidColorBrush(Color.FromArgb(255, 28, 32, 42)),
                        BorderBrush = isNodeSelected
                            ? new SolidColorBrush(Color.FromArgb(255, 56, 189, 248))
                            : new SolidColorBrush(Color.FromArgb(140, 100, 116, 139)),
                        BorderThickness = new Thickness(isNodeSelected ? 2.0 : 1.0),
                        CornerRadius = new CornerRadius(3),
                    };

                    if (r.Width >= 55 && r.Height >= 22)
                    {
                        double pct = currentRoot.Size > 0 ? (double)r.Node.Size * 100.0 / currentRoot.Size : 0;
                        string prefix = isDirectlyInMultiSelect ? "☑ 📁 " : "📁 ";
                        groupBorder.Child = new TextBlock
                        {
                            Text = $"{prefix}{r.Node.Name} ({Format.Size(r.Node.Size)} · {pct:0.0}%)",
                            FontSize = 10.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Color.FromArgb(235, 226, 232, 240)),
                            Margin = new Thickness(5, 1, 5, 0),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Top,
                        };
                    }

                    var capturedGroupNode = r.Node;
                    groupBorder.ContextFlyout = BuildNodeContextMenu(capturedGroupNode);
                    groupBorder.PointerPressed += (_, e) =>
                    {
                        var props = e.GetCurrentPoint(groupBorder).Properties;
                        if (props.IsRightButtonPressed)
                            return;
                        e.Handled = true;
                        HandleNodeSelectionClick(capturedGroupNode, IsCtrlKeyDown());
                    };
                    groupBorder.DoubleTapped += (_, e) =>
                    {
                        e.Handled = true;
                        ZoomToNode(capturedGroupNode);
                    };

                    if (showTips)
                        ToolTipService.SetToolTip(groupBorder, BuildNodeToolTipText(capturedGroupNode, currentRoot, scannedRoot));

                    Canvas.SetLeft(groupBorder, r.X);
                    Canvas.SetTop(groupBorder, r.Y);
                    treemapCanvas.Children.Add(groupBorder);
                    continue;
                }

                Color baseColor = ParseColorHex(r.Node.ColorHex);
                bool matchesExt = hasExtFilter && MatchesExtensionFilter(r.Node, highlightedExtension!);

                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                };
                gradient.GradientStops.Add(new GradientStop
                {
                    Color = LightenColor(baseColor, 0.18),
                    Offset = 0.0,
                });
                gradient.GradientStops.Add(new GradientStop
                {
                    Color = baseColor,
                    Offset = 0.55,
                });
                gradient.GradientStops.Add(new GradientStop
                {
                    Color = DarkenColor(baseColor, 0.28),
                    Offset = 1.0,
                });

                double tileW = Math.Max(1.0, r.Width - 1.0);
                double tileH = Math.Max(1.0, r.Height - 1.0);

                var tileBorder = new Border
                {
                    Width = tileW,
                    Height = tileH,
                    Background = gradient,
                    CornerRadius = new CornerRadius(2),
                    BorderThickness = new Thickness(isNodeSelected ? 2.5 : (matchesExt ? 2.0 : 0.75)),
                    BorderBrush = isNodeSelected
                        ? new SolidColorBrush(Colors.White)
                        : (matchesExt
                            ? new SolidColorBrush(Color.FromArgb(255, 250, 204, 21))
                            : new SolidColorBrush(Color.FromArgb(95, 10, 12, 18))),
                    Opacity = hasExtFilter ? (matchesExt ? 1.0 : 0.22) : 1.0,
                };

                if (tileW >= 42 && tileH >= 18)
                {
                    var labelStack = new StackPanel
                    {
                        Margin = new Thickness(4, 2, 4, 2),
                        Spacing = 0,
                        IsHitTestVisible = false,
                    };

                    string checkPrefix = isNodeSelected && selectedNodes.Count > 1 ? "✓ " : "";
                    labelStack.Children.Add(new TextBlock
                    {
                        Text = r.Node.IsDirectory ? $"{checkPrefix}📁 {r.Node.Name}" : $"{checkPrefix}{r.Node.Name}",
                        FontSize = tileW >= 90 && tileH >= 34 ? 11.5 : 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Colors.White),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });

                    if (tileH >= 34 && tileW >= 54)
                    {
                        double pct = currentRoot.Size > 0 ? (double)r.Node.Size * 100.0 / currentRoot.Size : 0;
                        labelStack.Children.Add(new TextBlock
                        {
                            Text = $"{Format.Size(r.Node.Size)} ({pct:0.0}%)",
                            FontSize = 9.5,
                            Foreground = new SolidColorBrush(Color.FromArgb(230, 248, 250, 252)),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        });
                    }

                    tileBorder.Child = labelStack;
                }

                var capturedNode = r.Node;
                tileBorder.ContextFlyout = BuildNodeContextMenu(capturedNode);
                tileBorder.PointerPressed += (_, e) =>
                {
                    var props = e.GetCurrentPoint(tileBorder).Properties;
                    if (props.IsRightButtonPressed)
                        return;
                    e.Handled = true;
                    HandleNodeSelectionClick(capturedNode, IsCtrlKeyDown());
                };

                tileBorder.DoubleTapped += (_, e) =>
                {
                    e.Handled = true;
                    if (capturedNode.IsDirectory)
                    {
                        ZoomToNode(capturedNode);
                    }
                    else if (capturedNode.Parent is not null && !ReferenceEquals(capturedNode.Parent, currentRoot))
                    {
                        ZoomToNode(capturedNode.Parent);
                    }
                };

                if (showTips)
                    ToolTipService.SetToolTip(tileBorder, BuildNodeToolTipText(capturedNode, currentRoot, scannedRoot));

                Canvas.SetLeft(tileBorder, r.X);
                Canvas.SetTop(tileBorder, r.Y);
                treemapCanvas.Children.Add(tileBorder);
            }
        }

        void SyncSelectionToHierarchyList()
        {
            if (currentRoot is null) return;
            suppressHierarchySelection = true;
            try
            {
                hierarchyList.SelectedItems.Clear();
                var targetNodes = selectedNodes.Count > 0
                    ? selectedNodes
                    : (selectedNode is not null ? [selectedNode] : []);

                object? lastMatchedItem = null;
                foreach (var n in targetNodes)
                {
                    var directChild = FindDirectChildUnderRoot(n, currentRoot);
                    if (directChild is null) continue;
                    int idx = currentHierarchyNodes.IndexOf(directChild);
                    if (idx >= 0 && idx < hierarchyList.Items.Count)
                    {
                        var item = hierarchyList.Items[idx];
                        if (!hierarchyList.SelectedItems.Contains(item))
                            hierarchyList.SelectedItems.Add(item);
                        lastMatchedItem = item;
                    }
                }

                if (lastMatchedItem is not null)
                    hierarchyList.ScrollIntoView(lastMatchedItem);
            }
            finally
            {
                suppressHierarchySelection = false;
            }
        }

        void PopulateUpperListsAndTreemap()
        {
            if (currentRoot is null)
            {
                hierarchyList.ItemsSource = null;
                extensionList.ItemsSource = null;
                treemapCanvas.Children.Clear();
                zoomUpBtn.IsEnabled = false;
                zoomRootBtn.IsEnabled = false;
                zoomPathBadge.Text = "";
                UpdateSelectedNodeUi();
                return;
            }

            zoomUpBtn.IsEnabled = currentRoot.Parent is not null && !ReferenceEquals(currentRoot, scannedRoot);
            zoomRootBtn.IsEnabled = scannedRoot is not null && !ReferenceEquals(currentRoot, scannedRoot);
            zoomPathBadge.Text = ReferenceEquals(currentRoot, scannedRoot)
                ? (IsDe() ? $"Wurzel: {currentRoot.FullPath}" : $"Root: {currentRoot.FullPath}")
                : $"Zoom: {currentRoot.FullPath}";

            scanStatusText.Text = LocalizationService.Format(
                "Treemap.SubtreeStats",
                currentRoot.Name,
                Format.Size(currentRoot.Size),
                currentRoot.FileCount,
                currentRoot.FolderCount);

            // 2. Top-left: Folder Hierarchy & Top Space Consumers
            currentHierarchyNodes = currentRoot.Children
                .OrderByDescending(c => c.Size)
                .Take(250)
                .ToList();

            var hierarchyItems = new List<Grid>(currentHierarchyNodes.Count);
            foreach (var child in currentHierarchyNodes)
            {
                double pct = currentRoot.Size > 0 ? (double)child.Size * 100.0 / currentRoot.Size : 0.0;
                Color barColor = ParseColorHex(child.ColorHex);

                var rowGrid = new Grid
                {
                    ColumnSpacing = 8,
                    Padding = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

                var nameTb = new TextBlock
                {
                    Text = $"{(child.IsDirectory ? "📁" : "📄")} {child.Name}",
                    FontWeight = child.IsDirectory ? FontWeights.SemiBold : FontWeights.Normal,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                var barHost = new Grid
                {
                    Height = 14,
                    Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 145)),
                    CornerRadius = new CornerRadius(3),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                barHost.Children.Add(new Border
                {
                    Width = Math.Clamp(pct * 1.1, 2, 110),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(barColor),
                    CornerRadius = new CornerRadius(3),
                });
                barHost.Children.Add(new TextBlock
                {
                    Text = $"{pct:0.0} %",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });

                var sizeTb = new TextBlock
                {
                    Text = Format.Size(child.Size),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var countTb = new TextBlock
                {
                    Text = child.IsDirectory
                        ? LocalizationService.Format("Treemap.FilesShort", child.FileCount)
                        : StorageTreemapService.GetCategoryDisplayName(child.Category).Split(' ')[0].TrimEnd(','),
                    FontSize = 11,
                    Opacity = 0.75,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                Grid.SetColumn(nameTb, 0);
                Grid.SetColumn(barHost, 1);
                Grid.SetColumn(sizeTb, 2);
                Grid.SetColumn(countTb, 3);
                rowGrid.Children.Add(nameTb);
                rowGrid.Children.Add(barHost);
                rowGrid.Children.Add(sizeTb);
                rowGrid.Children.Add(countTb);

                var capturedChild = child;
                rowGrid.ContextFlyout = BuildNodeContextMenu(capturedChild);
                rowGrid.DoubleTapped += (_, e) =>
                {
                    e.Handled = true;
                    if (capturedChild.IsDirectory)
                        ZoomToNode(capturedChild);
                };

                if (owner.ViewModel.Settings.ShowToolTips)
                    ToolTipService.SetToolTip(rowGrid, BuildNodeToolTipText(capturedChild, currentRoot, scannedRoot));

                hierarchyItems.Add(rowGrid);
            }

            suppressHierarchySelection = true;
            hierarchyList.ItemsSource = hierarchyItems;
            suppressHierarchySelection = false;

            // 3. Top-right: File Extension Statistics & Color Legend
            currentExtStats = StorageTreemapService.ComputeExtensionStats(currentRoot, topCount: 60).ToList();
            var extItems = new List<Grid>(currentExtStats.Count);
            foreach (var stat in currentExtStats)
            {
                var rowGrid = new Grid
                {
                    ColumnSpacing = 8,
                    Padding = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });

                var colorSwatch = new Border
                {
                    Width = 13,
                    Height = 13,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(ParseColorHex(stat.ColorHex)),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var extTb = new TextBlock
                {
                    Text = stat.Extension,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                var catTb = new TextBlock
                {
                    Text = stat.CategoryLabel,
                    FontSize = 11,
                    Opacity = 0.75,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                var sizeTb = new TextBlock
                {
                    Text = Format.Size(stat.TotalBytes),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var pctTb = new TextBlock
                {
                    Text = $"{stat.Percentage:0.0} %",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var countTb = new TextBlock
                {
                    Text = $"{stat.FileCount:N0}",
                    FontSize = 11,
                    Opacity = 0.75,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                Grid.SetColumn(colorSwatch, 0);
                Grid.SetColumn(extTb, 1);
                Grid.SetColumn(catTb, 2);
                Grid.SetColumn(sizeTb, 3);
                Grid.SetColumn(pctTb, 4);
                Grid.SetColumn(countTb, 5);
                rowGrid.Children.Add(colorSwatch);
                rowGrid.Children.Add(extTb);
                rowGrid.Children.Add(catTb);
                rowGrid.Children.Add(sizeTb);
                rowGrid.Children.Add(pctTb);
                rowGrid.Children.Add(countTb);

                extItems.Add(rowGrid);
            }

            suppressExtensionSelection = true;
            extensionList.ItemsSource = extItems;
            suppressExtensionSelection = false;

            ApplyFontScalingToTree(
                upperSplitGrid,
                owner.ViewModel.Settings.GetChildWindowFontSize(ChildWindowFontTarget.StorageAnalyzer));

            UpdateSelectedNodeUi();
            RenderTreemapCanvas();
        }

        void ZoomToNode(StorageTreemapNode target)
        {
            if (!target.IsDirectory) return;
            currentRoot = target;
            selectedNodes.Clear();
            selectedNode = target;
            PopulateUpperListsAndTreemap();
        }

        hierarchyList.SelectionChanged += (_, _) =>
        {
            if (suppressHierarchySelection) return;
            selectedNodes.Clear();
            foreach (var itemObj in hierarchyList.SelectedItems)
            {
                if (itemObj is Grid g)
                {
                    int idx = hierarchyList.Items.IndexOf(g);
                    if (idx >= 0 && idx < currentHierarchyNodes.Count)
                        selectedNodes.Add(currentHierarchyNodes[idx]);
                }
            }
            selectedNode = selectedNodes.LastOrDefault();
            UpdateSelectedNodeUi();
            RenderTreemapCanvas();
        };

        extensionList.SelectionChanged += (_, _) =>
        {
            if (suppressExtensionSelection) return;
            int idx = extensionList.SelectedIndex;
            if (idx >= 0 && idx < currentExtStats.Count)
            {
                string chosenExt = currentExtStats[idx].Extension;
                if (string.Equals(highlightedExtension, chosenExt, StringComparison.OrdinalIgnoreCase))
                {
                    highlightedExtension = null;
                    suppressExtensionSelection = true;
                    extensionList.SelectedIndex = -1;
                    suppressExtensionSelection = false;
                    clearExtFilterBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    highlightedExtension = chosenExt;
                    clearExtFilterBtn.Visibility = Visibility.Visible;
                }
                RenderTreemapCanvas();
            }
        };

        clearExtFilterBtn.Click += (_, _) =>
        {
            highlightedExtension = null;
            suppressExtensionSelection = true;
            extensionList.SelectedIndex = -1;
            suppressExtensionSelection = false;
            clearExtFilterBtn.Visibility = Visibility.Collapsed;
            RenderTreemapCanvas();
        };

        zoomUpBtn.Click += (_, _) =>
        {
            if (currentRoot?.Parent is { } parent)
                ZoomToNode(parent);
        };

        zoomRootBtn.Click += (_, _) =>
        {
            if (scannedRoot is not null)
                ZoomToNode(scannedRoot);
        };

        treemapHostBorder.SizeChanged += (_, _) =>
        {
            if (currentRoot is not null)
                RenderTreemapCanvas();
        };

        async Task RunScanAsync()
        {
            string targetDir = rootBox.Text.Trim();
            if (!Directory.Exists(targetDir))
            {
                scanStatusText.Text = LocalizationService.Format("Treemap.PathNotFoundStatus", targetDir);
                return;
            }

            scanCts?.Cancel();
            scanCts?.Dispose();
            scanCts = new CancellationTokenSource();
            var ct = scanCts.Token;

            scanBtn.IsEnabled = false;
            cancelScanBtn.Visibility = Visibility.Visible;
            scanProgressBar.Visibility = Visibility.Visible;
            scanProgressBar.IsIndeterminate = true;
            scanStatusText.Text = LocalizationService.Format("Treemap.ScanStartStatus", targetDir);

            var progress = new Progress<StorageScanProgress>(p =>
            {
                scanStatusText.Text = LocalizationService.Format(
                    "Treemap.ScanProgressStatus",
                    p.ScannedFiles,
                    p.ScannedDirectories,
                    Format.Size(p.TotalBytes),
                    p.CurrentDirectory);
            });

            try
            {
                var tree = await StorageTreemapService.ScanDirectoryTreeAsync(targetDir, progress, ct);
                scannedRoot = tree;
                currentRoot = tree;
                selectedNodes.Clear();
                selectedNode = tree;
                highlightedExtension = null;
                clearExtFilterBtn.Visibility = Visibility.Collapsed;
                PopulateUpperListsAndTreemap();
            }
            catch (OperationCanceledException)
            {
                scanStatusText.Text = LocalizationService.T("Treemap.ScanCancelledStatus");
            }
            catch (Exception ex)
            {
                scanStatusText.Text = LocalizationService.Format("Treemap.ScanErrorStatus", ex.Message);
            }
            finally
            {
                scanProgressBar.Visibility = Visibility.Collapsed;
                cancelScanBtn.Visibility = Visibility.Collapsed;
                scanBtn.IsEnabled = true;
            }
        }

        // ====================================================================
        // POPULATE INTERACTIVE EXPLORER-TREE DROPDOWN
        // ====================================================================
        UIElement CreateExplorerTreeNodeElement(string fullPath, string displayLabel, bool isDrive, int depth, string currentTargetPath)
        {
            var container = new StackPanel { Spacing = 1 };
            var childrenHost = new StackPanel { Spacing = 1, Visibility = Visibility.Collapsed };
            bool childrenLoaded = false;

            bool isCurrentMatch = string.Equals(
                fullPath.TrimEnd('\\', '/'),
                currentTargetPath.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);

            bool isAncestorOfCurrent = !isCurrentMatch &&
                currentTargetPath.StartsWith(
                    fullPath.EndsWith('\\') ? fullPath : fullPath + "\\",
                    StringComparison.OrdinalIgnoreCase);

            var rowGrid = new Grid
            {
                ColumnSpacing = 4,
                Margin = new Thickness(depth * 16, 1, 2, 1),
            };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var expandBtn = new Button
            {
                Content = "▶",
                FontSize = 10,
                Width = 24,
                Height = 26,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var selectNodeBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4, 8, 4),
                Background = isCurrentMatch
                    ? new SolidColorBrush(Color.FromArgb(65, 56, 189, 248))
                    : new SolidColorBrush(Colors.Transparent),
                BorderBrush = isCurrentMatch
                    ? new SolidColorBrush(Color.FromArgb(160, 56, 189, 248))
                    : new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(1),
                Content = new TextBlock
                {
                    Text = isDrive ? $"💾 {displayLabel}" : $"📁 {displayLabel}",
                    FontSize = 12,
                    FontWeight = isDrive || isCurrentMatch ? FontWeights.SemiBold : FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };

            void ExpandNode()
            {
                if (!childrenLoaded)
                {
                    childrenLoaded = true;
                    childrenHost.Children.Clear();
                    try
                    {
                        var subDirs = Directory.EnumerateDirectories(fullPath)
                            .Select(d => new DirectoryInfo(d))
                            .Where(di => (di.Attributes & System.IO.FileAttributes.ReparsePoint) == 0)
                            .OrderBy(di => di.Name, StringComparer.OrdinalIgnoreCase)
                            .Take(200)
                            .ToList();

                        if (subDirs.Count == 0)
                        {
                            expandBtn.Opacity = 0.25;
                            expandBtn.IsEnabled = false;
                        }
                        else
                        {
                            foreach (var sub in subDirs)
                            {
                                childrenHost.Children.Add(
                                    CreateExplorerTreeNodeElement(sub.FullName, sub.Name, isDrive: false, depth: depth + 1, currentTargetPath));
                            }
                        }
                    }
                    catch
                    {
                        expandBtn.Opacity = 0.25;
                        expandBtn.IsEnabled = false;
                    }
                }

                childrenHost.Visibility = Visibility.Visible;
                expandBtn.Content = "▼";
            }

            expandBtn.Click += (_, _) =>
            {
                if (childrenHost.Visibility == Visibility.Visible)
                {
                    childrenHost.Visibility = Visibility.Collapsed;
                    expandBtn.Content = "▶";
                }
                else
                {
                    ExpandNode();
                }
            };

            selectNodeBtn.Click += async (_, _) =>
            {
                rootBox.Text = fullPath;
                explorerTreeFlyout.Hide();
                await RunScanAsync();
            };

            Grid.SetColumn(expandBtn, 0);
            Grid.SetColumn(selectNodeBtn, 1);
            rowGrid.Children.Add(expandBtn);
            rowGrid.Children.Add(selectNodeBtn);

            container.Children.Add(rowGrid);
            container.Children.Add(childrenHost);

            if (isAncestorOfCurrent && depth < 5)
            {
                ExpandNode();
            }

            return container;
        }

        void PopulateExplorerTreeFlyout()
        {
            treeRootStack.Children.Clear();
            string currentTarget = rootBox.Text.Trim();

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? (IsDe() ? "Lokaler Datenträger" : "Local Disk")
                        : drive.VolumeLabel;
                    string sizeInfo = LocalizationService.Format(
                        "Drive.FreeOfTotal",
                        Format.Size(drive.AvailableFreeSpace),
                        Format.Size(drive.TotalSize));
                    string display = $"{drive.Name} ({label})  —  {sizeInfo}";
                    treeRootStack.Children.Add(
                        CreateExplorerTreeNodeElement(drive.RootDirectory.FullName, display, isDrive: true, depth: 0, currentTargetPath: currentTarget));
                }
            }
            catch { }

            ApplyFontScalingToTree(
                treeFlyoutContainer,
                owner.ViewModel.Settings.GetChildWindowFontSize(ChildWindowFontTarget.StorageAnalyzer));
        }

        explorerTreeFlyout.Opening += (_, _) => PopulateExplorerTreeFlyout();

        rootBox.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await RunScanAsync();
            }
        };

        scanBtn.Click += async (_, _) => await RunScanAsync();
        cancelScanBtn.Click += (_, _) => scanCts?.Cancel();

        // 4. Direct Explorer Actions from the Analyzer Window
        showInTabBtn.Click += async (_, _) => await NavigateToNodeInExplorerTabAsync(selectedNode);
        openFolderTabBtn.Click += async (_, _) => await OpenNodeFolderAsNewTabAsync(selectedNode);

        deleteSelectedBtn.Click += (_, _) =>
        {
            var deletable = GetDeletableSelectedNodes();
            if (deletable.Count == 0)
            {
                selectedInfoText.Text = IsDe()
                    ? "⚠ Bitte wählen Sie zuerst eine oder mehrere Dateien/Unterordner zum Löschen aus."
                    : "⚠ Please select one or more files/subfolders to delete first.";
                return;
            }

            deleteSelectedBtn.Visibility = Visibility.Collapsed;
            confirmDeleteBtn.Visibility = Visibility.Visible;
            cancelDeleteConfirmBtn.Visibility = Visibility.Visible;

            if (deletable.Count == 1)
            {
                selectedInfoText.Text = IsDe()
                    ? $"⚠ Wirklich löschen: „{deletable[0].Name}“ ({Format.Size(deletable[0].Size)})?"
                    : $"⚠ Really delete: \"{deletable[0].Name}\" ({Format.Size(deletable[0].Size)})?";
            }
            else
            {
                long totalBytes = deletable.Sum(n => n.Size);
                selectedInfoText.Text = IsDe()
                    ? $"⚠ Wirklich {deletable.Count} markierte Elemente löschen ({Format.Size(totalBytes)})?"
                    : $"⚠ Really delete {deletable.Count} selected items ({Format.Size(totalBytes)})?";
            }
        };

        cancelDeleteConfirmBtn.Click += (_, _) => UpdateSelectedNodeUi();

        confirmDeleteBtn.Click += async (_, _) =>
        {
            var deletable = GetDeletableSelectedNodes();
            await ExecuteDeleteNodesAsync(deletable);
        };

        // Keyboard shortcut: ENTF (Delete) deletes selected node(s)
        bodyGrid.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Delete)
            {
                var focused = bodyGrid.XamlRoot is not null
                    ? FocusManager.GetFocusedElement(bodyGrid.XamlRoot)
                    : null;
                if (focused is TextBox)
                    return;

                var deletable = GetDeletableSelectedNodes();
                if (deletable.Count > 0)
                {
                    e.Handled = true;
                    await ExecuteDeleteNodesAsync(deletable);
                }
            }
        };

        void OnTreemapColorsChanged(object? s, EventArgs e)
        {
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                StorageTreemapService.RefreshNodeColors(scannedRoot);
                RebuildCategoryLegendPills();
                PopulateUpperListsAndTreemap();
            });
        }

        var win = CreateResizableChildWindow(
            LocalizationService.T("Treemap.WindowTitle"),
            "🗺",
            bodyGrid,
            defaultWidth: 1220,
            defaultHeight: 820,
            fontTarget: ChildWindowFontTarget.StorageAnalyzer,
            titleProvider: () => LocalizationService.T("Treemap.WindowTitle"));

        void OnTreemapLanguageChanged(object? s, EventArgs e)
        {
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateChildWindowTitle(win, LocalizationService.T("Treemap.WindowTitle"));
                rootBox.PlaceholderText = LocalizationService.T("Treemap.RootPlaceholder");
                treeFlyoutHeader.Text = IsDe()
                    ? "🌳 Explorer-Baum: Laufwerk oder Ordner direkt auswählen (▶ = Unterordner aufklappen)"
                    : "🌳 Explorer Tree: Select drive or folder directly (▶ = expand subfolders)";
                treeDropdownBtn.Content = IsDe() ? "🌳 Explorer-Baum ▾" : "🌳 Explorer Tree ▾";
                ToolTipService.SetToolTip(
                    treeDropdownBtn,
                    IsDe()
                        ? "Öffnet den hierarchischen Explorer-Verzeichnisbaum zur direkten Auswahl eines Laufwerks oder Ordners"
                        : "Opens the hierarchical Explorer directory tree to select a drive or folder directly");
                scanBtn.Content = LocalizationService.T("Treemap.ScanButton");
                cancelScanBtn.Content = LocalizationService.T("Treemap.CancelButton");
                zoomUpBtn.Content = LocalizationService.T("Treemap.ZoomUpButton");
                zoomRootBtn.Content = LocalizationService.T("Treemap.ZoomRootButton");
                hierarchyHeaderText.Text = LocalizationService.T("Treemap.HierarchyHeader");
                extHeaderText.Text = LocalizationService.T("Treemap.ExtHeader");
                clearExtFilterBtn.Content = LocalizationService.T("Treemap.ShowAllTypesButton");
                ToolTipService.SetToolTip(upperColSplitter, LocalizationService.T("Treemap.SplitterHorizontalTip"));
                ToolTipService.SetToolTip(verticalPaneSplitter, LocalizationService.T("Treemap.SplitterVerticalTip"));
                treemapTitleText.Text = $"{LocalizationService.T("Treemap.CanvasTitle")}  —  {LocalizationService.T("Treemap.HoverHint")}";
                showInTabBtn.Content = IsDe() ? "🎯 Im Explorer-Tab zeigen" : "🎯 Show in Explorer Tab";
                openFolderTabBtn.Content = LocalizationService.T("Treemap.OpenExplorerButton");
                confirmDeleteBtn.Content = IsDe() ? "⚠ Ja, jetzt löschen!" : "⚠ Yes, delete now!";
                cancelDeleteConfirmBtn.Content = LocalizationService.T("Dialog.Cancel");
                closeBtn.Content = LocalizationService.T("Dialog.Close");
                RebuildCategoryLegendPills();
                PopulateUpperListsAndTreemap();
            });
        }

        owner.ViewModel.Settings.TreemapColorsChanged += OnTreemapColorsChanged;
        LocalizationService.Instance.LanguageChanged += OnTreemapLanguageChanged;

        closeBtn.Click += (_, _) =>
        {
            scanCts?.Cancel();
            win.Close();
        };

        win.Closed += (_, _) =>
        {
            owner.ViewModel.Settings.TreemapColorsChanged -= OnTreemapColorsChanged;
            LocalizationService.Instance.LanguageChanged -= OnTreemapLanguageChanged;
            scanCts?.Cancel();
            scanCts?.Dispose();
            tcs.TrySetResult(true);
        };

        win.Activate();

        // Automatically start scanning the initial directory upon opening
        _ = RunScanAsync();

        await tcs.Task;
    }

    private static string BuildNodeToolTipText(
        StorageTreemapNode node,
        StorageTreemapNode? currentRoot,
        StorageTreemapNode? scannedRoot)
    {
        bool isDe = LocalizationService.Instance.EffectiveLanguage == "de";
        double pctView = currentRoot is { Size: > 0 } ? (double)node.Size * 100.0 / currentRoot.Size : 100.0;
        double pctTotal = scannedRoot is { Size: > 0 } ? (double)node.Size * 100.0 / scannedRoot.Size : pctView;
        string relPath = currentRoot is not null
            ? Path.GetRelativePath(currentRoot.FullPath, node.FullPath)
            : node.Name;

        string typeInfo = node.IsDirectory
            ? (isDe
                ? $"Ordner ({node.FileCount:N0} Dateien, {node.FolderCount:N0} Unterordner)"
                : $"Folder ({node.FileCount:N0} files, {node.FolderCount:N0} subfolders)")
            : $"{LocalizationService.T("File.FileType")} ({node.Extension}) · {StorageTreemapService.GetCategoryDisplayName(node.Category)}";

        return isDe
            ? $"{(node.IsDirectory ? "📁" : "📄")} {node.Name}\n" +
              $"Relativer Pfad: {relPath}\n" +
              $"Vollständiger Pfad: {node.FullPath}\n" +
              $"Größe: {Format.Size(node.Size)} ({node.Size:N0} Bytes)\n" +
              $"Anteil: {pctView:0.00} % der Ansicht ({pctTotal:0.00} % der Wurzel)\n" +
              $"Typ: {typeInfo}\n" +
              $"Geändert: {node.ModifiedLocal:yyyy-MM-dd HH:mm:ss}"
            : $"{(node.IsDirectory ? "📁" : "📄")} {node.Name}\n" +
              $"Relative Path: {relPath}\n" +
              $"Full Path: {node.FullPath}\n" +
              $"Size: {Format.Size(node.Size)} ({node.Size:N0} bytes)\n" +
              $"Share: {pctView:0.00}% of view ({pctTotal:0.00}% of root)\n" +
              $"Type: {typeInfo}\n" +
              $"Modified: {node.ModifiedLocal:yyyy-MM-dd HH:mm:ss}";
    }

    private static StorageTreemapNode? FindDirectChildUnderRoot(StorageTreemapNode node, StorageTreemapNode root)
    {
        var cur = node;
        while (cur is not null)
        {
            if (ReferenceEquals(cur.Parent, root))
                return cur;
            cur = cur.Parent;
        }
        return null;
    }

    private static bool IsNodeOrDescendantOf(StorageTreemapNode candidate, StorageTreemapNode ancestor)
    {
        var cur = candidate;
        while (cur is not null)
        {
            if (ReferenceEquals(cur, ancestor))
                return true;
            cur = cur.Parent;
        }
        return false;
    }

    private static bool MatchesExtensionFilter(StorageTreemapNode node, string extFilter)
    {
        if (!node.IsDirectory)
            return string.Equals(node.Extension, extFilter, StringComparison.OrdinalIgnoreCase);

        foreach (var child in node.Children)
        {
            if (MatchesExtensionFilter(child, extFilter))
                return true;
        }
        return false;
    }

    private static Color ParseColorHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Color.FromArgb(255, 100, 116, 139);

        var s = hex.Trim().TrimStart('#');
        if (s.Length == 6 &&
            byte.TryParse(s[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
            byte.TryParse(s[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
            byte.TryParse(s[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return Color.FromArgb(255, r, g, b);
        }
        return Color.FromArgb(255, 100, 116, 139);
    }

    private static Color LightenColor(Color c, double factor)
    {
        byte r = (byte)Math.Clamp(c.R + (255 - c.R) * factor, 0, 255);
        byte g = (byte)Math.Clamp(c.G + (255 - c.G) * factor, 0, 255);
        byte b = (byte)Math.Clamp(c.B + (255 - c.B) * factor, 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }

    private static Color DarkenColor(Color c, double factor)
    {
        byte r = (byte)Math.Clamp(c.R * (1.0 - factor), 0, 255);
        byte g = (byte)Math.Clamp(c.G * (1.0 - factor), 0, 255);
        byte b = (byte)Math.Clamp(c.B * (1.0 - factor), 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }
}

