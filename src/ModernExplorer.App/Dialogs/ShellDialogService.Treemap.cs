using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.ViewModels;
using Windows.Foundation;
using Windows.UI;

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
        string? highlightedExtension = null;
        bool suppressHierarchySelection = false;
        bool suppressExtensionSelection = false;

        var rootBox = new TextBox
        {
            PlaceholderText = "Zu analysierendes Laufwerk oder Verzeichnis (z. B. C:\\ oder Projektordner)…",
            Text = initialDirectory,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var scanBtn = new Button
        {
            Content = "🔍 Scannen",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 6, 16, 6),
        };

        var cancelScanBtn = new Button
        {
            Content = "⏹ Stopp",
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(12, 6, 12, 6),
        };

        var zoomUpBtn = new Button
        {
            Content = "⬆ Ebene höher",
            IsEnabled = false,
            Padding = new Thickness(12, 6, 12, 6),
        };

        var zoomRootBtn = new Button
        {
            Content = "🏠 Zurück zur Wurzel",
            IsEnabled = false,
            Padding = new Thickness(12, 6, 12, 6),
        };

        var topBarGrid = new Grid { ColumnSpacing = 8 };
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(rootBox, 0);
        Grid.SetColumn(scanBtn, 1);
        Grid.SetColumn(cancelScanBtn, 2);
        Grid.SetColumn(zoomUpBtn, 3);
        Grid.SetColumn(zoomRootBtn, 4);
        topBarGrid.Children.Add(rootBox);
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
            Text = "Bereit für Speicherplatz-Analyse.",
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
        // UPPER SPLIT PANE:
        // Left:  2. Ordner-Hierarchie & Top-Speicherfresser
        // Right: 3. Dateiendungs-Statistik & Farb-Legende
        // ====================================================================

        var hierarchyHeaderText = new TextBlock
        {
            Text = "📂 Ordner-Hierarchie & Top-Speicherfresser (Doppelklick = Hineinzoomen)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var hierarchyList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
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
            Text = "🎨 Dateiendungs-Statistik & Farb-Legende (Klick = Typ hervorheben)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var clearExtFilterBtn = new Button
        {
            Content = "✕ Filter aufheben",
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

        var upperSplitGrid = new Grid
        {
            ColumnSpacing = 12,
            Height = 220,
        };
        upperSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        upperSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
        Grid.SetColumn(hierarchyPane, 0);
        Grid.SetColumn(extPane, 1);
        upperSplitGrid.Children.Add(hierarchyPane);
        upperSplitGrid.Children.Add(extPane);

        // ====================================================================
        // LOWER MAIN PANE:
        // 1. Interaktive Squarified Treemap (Kachel-Ansicht)
        // ====================================================================

        var treemapTitleText = new TextBlock
        {
            Text = "🗺 Interaktive Squarified Treemap (Klick = Auswählen · Doppelklick = In Unterordner zoomen)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var categoryLegendStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };

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
                Text = StorageTreemapService.GetCategoryDisplayName(cat).Split(' ')[0],
                FontSize = 11,
                Opacity = 0.82,
                VerticalAlignment = VerticalAlignment.Center,
            });
            categoryLegendStack.Children.Add(pill);
        }

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
        // 4. Direkte Explorer-Aktionen aus dem Analyse-Fenster
        // ====================================================================

        var selectedInfoText = new TextBlock
        {
            Text = "Kein Element ausgewählt – klicken Sie auf eine Kachel oder einen Listeneintrag.",
            FontSize = 12.5,
            Opacity = 0.9,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var showInTabBtn = new Button
        {
            Content = "🎯 Im Explorer-Tab zeigen",
            IsEnabled = false,
            Padding = new Thickness(12, 7, 12, 7),
        };

        var openFolderTabBtn = new Button
        {
            Content = "📂 Ordner als neuen Tab öffnen",
            IsEnabled = false,
            Padding = new Thickness(12, 7, 12, 7),
        };

        var deleteSelectedBtn = new Button
        {
            Content = "🗑 Markierte Datei(en) löschen",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 7, 14, 7),
        };

        var confirmDeleteBtn = new Button
        {
            Content = "⚠ Ja, jetzt löschen!",
            Visibility = Visibility.Collapsed,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Color.FromArgb(255, 220, 38, 38)),
            Padding = new Thickness(14, 7, 14, 7),
        };

        var cancelDeleteConfirmBtn = new Button
        {
            Content = "Abbrechen",
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(10, 7, 10, 7),
        };

        var closeBtn = new Button
        {
            Content = "Schließen",
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

        var bodyGrid = new Grid
        {
            Padding = new Thickness(16, 12, 16, 14),
            RowSpacing = 10,
        };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(topHeaderPanel, 0);
        Grid.SetRow(upperSplitGrid, 1);
        Grid.SetRow(treemapSectionGrid, 2);
        Grid.SetRow(footerGrid, 3);
        bodyGrid.Children.Add(topHeaderPanel);
        bodyGrid.Children.Add(upperSplitGrid);
        bodyGrid.Children.Add(treemapSectionGrid);
        bodyGrid.Children.Add(footerGrid);

        var currentHierarchyNodes = new List<StorageTreemapNode>();
        var currentExtStats = new List<StorageExtensionStat>();

        void ResetDeleteConfirmState()
        {
            confirmDeleteBtn.Visibility = Visibility.Collapsed;
            cancelDeleteConfirmBtn.Visibility = Visibility.Collapsed;
            deleteSelectedBtn.Visibility = Visibility.Visible;
        }

        void UpdateSelectedNodeUi()
        {
            ResetDeleteConfirmState();
            bool hasSelection = selectedNode is not null;
            bool hasRoot = currentRoot is not null;

            showInTabBtn.IsEnabled = hasSelection || hasRoot;
            openFolderTabBtn.IsEnabled = hasSelection || hasRoot;
            deleteSelectedBtn.IsEnabled = hasSelection && selectedNode != scannedRoot;

            if (selectedNode is null)
            {
                if (currentRoot is not null)
                {
                    selectedInfoText.Text = $"Aktuelle Ansicht: {currentRoot.FullPath} ({Format.Size(currentRoot.Size)})";
                }
                else
                {
                    selectedInfoText.Text = "Kein Element ausgewählt.";
                }
                return;
            }

            double pctView = currentRoot is { Size: > 0 } ? (double)selectedNode.Size * 100.0 / currentRoot.Size : 100.0;
            double pctTotal = scannedRoot is { Size: > 0 } ? (double)selectedNode.Size * 100.0 / scannedRoot.Size : pctView;
            string kind = selectedNode.IsDirectory
                ? $"Ordner ({selectedNode.FileCount:N0} Dateien, {selectedNode.FolderCount:N0} Unterordner)"
                : $"Datei ({StorageTreemapService.GetCategoryDisplayName(selectedNode.Category)})";

            selectedInfoText.Text =
                $"Ausgewählt: {selectedNode.Name}  ·  {Format.Size(selectedNode.Size)} ({pctView:0.0}% Ansicht / {pctTotal:0.0}% Gesamt)  ·  {kind}";
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
                        Text = "Dieser Ordner ist leer (0 Bytes).",
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
                if (r.IsGroupContainer)
                {
                    var groupBorder = new Border
                    {
                        Width = Math.Max(1, r.Width - 1),
                        Height = Math.Max(1, r.Height - 1),
                        Background = new SolidColorBrush(Color.FromArgb(255, 28, 32, 42)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(140, 100, 116, 139)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                    };

                    if (r.Width >= 55 && r.Height >= 22)
                    {
                        double pct = currentRoot.Size > 0 ? (double)r.Node.Size * 100.0 / currentRoot.Size : 0;
                        groupBorder.Child = new TextBlock
                        {
                            Text = $"📁 {r.Node.Name} ({Format.Size(r.Node.Size)} · {pct:0.0}%)",
                            FontSize = 10.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Color.FromArgb(235, 226, 232, 240)),
                            Margin = new Thickness(5, 1, 5, 0),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Top,
                        };
                    }

                    var capturedGroupNode = r.Node;
                    groupBorder.PointerPressed += (_, e) =>
                    {
                        e.Handled = true;
                        selectedNode = capturedGroupNode;
                        SyncSelectionToHierarchyList(capturedGroupNode);
                        UpdateSelectedNodeUi();
                        RenderTreemapCanvas();
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
                bool isSelected = ReferenceEquals(selectedNode, r.Node) ||
                                  (selectedNode is not null && IsNodeOrDescendantOf(r.Node, selectedNode));
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
                    BorderThickness = new Thickness(isSelected ? 2.5 : (matchesExt ? 2.0 : 0.75)),
                    BorderBrush = isSelected
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

                    labelStack.Children.Add(new TextBlock
                    {
                        Text = r.Node.IsDirectory ? $"📁 {r.Node.Name}" : r.Node.Name,
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
                tileBorder.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    selectedNode = capturedNode;
                    SyncSelectionToHierarchyList(capturedNode);
                    UpdateSelectedNodeUi();
                    RenderTreemapCanvas();
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

        void SyncSelectionToHierarchyList(StorageTreemapNode node)
        {
            if (currentRoot is null) return;
            var directChild = FindDirectChildUnderRoot(node, currentRoot);
            if (directChild is null) return;

            int idx = currentHierarchyNodes.IndexOf(directChild);
            if (idx >= 0 && hierarchyList.SelectedIndex != idx)
            {
                suppressHierarchySelection = true;
                hierarchyList.SelectedIndex = idx;
                hierarchyList.ScrollIntoView(hierarchyList.SelectedItem);
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
                ? $"Wurzel: {currentRoot.FullPath}"
                : $"Zoom: {currentRoot.FullPath}";

            scanStatusText.Text =
                $"Analysiert: {Format.Size(currentRoot.Size)} in {currentRoot.FileCount:N0} Dateien und {currentRoot.FolderCount:N0} Ordnern";

            // 2. Top-left: Ordner-Hierarchie & Top-Speicherfresser
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
                        ? $"{child.FileCount:N0} Dat."
                        : StorageTreemapService.GetCategoryDisplayName(child.Category).Split(' ')[0],
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

            // 3. Top-right: Dateiendungs-Statistik & Farb-Legende
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

            UpdateSelectedNodeUi();
            RenderTreemapCanvas();
        }

        void ZoomToNode(StorageTreemapNode target)
        {
            if (!target.IsDirectory) return;
            currentRoot = target;
            selectedNode = target;
            PopulateUpperListsAndTreemap();
        }

        hierarchyList.SelectionChanged += (_, _) =>
        {
            if (suppressHierarchySelection) return;
            int idx = hierarchyList.SelectedIndex;
            if (idx >= 0 && idx < currentHierarchyNodes.Count)
            {
                selectedNode = currentHierarchyNodes[idx];
                UpdateSelectedNodeUi();
                RenderTreemapCanvas();
            }
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
                scanStatusText.Text = $"⚠ Verzeichnis existiert nicht: {targetDir}";
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
            scanStatusText.Text = $"Scanne {targetDir} …";

            var progress = new Progress<StorageScanProgress>(p =>
            {
                scanStatusText.Text =
                    $"Scanne: {p.ScannedFiles:N0} Dateien · {p.ScannedDirectories:N0} Ordner · {Format.Size(p.TotalBytes)}  —  {p.CurrentDirectory}";
            });

            try
            {
                var tree = await StorageTreemapService.ScanDirectoryTreeAsync(targetDir, progress, ct);
                scannedRoot = tree;
                currentRoot = tree;
                selectedNode = tree;
                highlightedExtension = null;
                clearExtFilterBtn.Visibility = Visibility.Collapsed;
                PopulateUpperListsAndTreemap();
            }
            catch (OperationCanceledException)
            {
                scanStatusText.Text = "Scan abgebrochen.";
            }
            catch (Exception ex)
            {
                scanStatusText.Text = $"Fehler beim Scannen: {ex.Message}";
            }
            finally
            {
                scanProgressBar.Visibility = Visibility.Collapsed;
                cancelScanBtn.Visibility = Visibility.Collapsed;
                scanBtn.IsEnabled = true;
            }
        }

        scanBtn.Click += async (_, _) => await RunScanAsync();
        cancelScanBtn.Click += (_, _) => scanCts?.Cancel();

        // 4. Direkte Explorer-Aktionen aus dem Analyse-Fenster
        showInTabBtn.Click += async (_, _) =>
        {
            var target = selectedNode ?? currentRoot;
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
        };

        openFolderTabBtn.Click += async (_, _) =>
        {
            var target = selectedNode ?? currentRoot;
            if (target is null) return;

            string folderPath = target.IsDirectory
                ? target.FullPath
                : (Path.GetDirectoryName(target.FullPath) ?? currentRoot?.FullPath ?? "");

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            await owner.ViewModel.ActivePanel.CreateTabAsync(folderPath);
            selectedInfoText.Text = $"✓ Neuer Tab geöffnet: {folderPath}";
        };

        deleteSelectedBtn.Click += (_, _) =>
        {
            if (selectedNode is null || ReferenceEquals(selectedNode, scannedRoot))
            {
                selectedInfoText.Text = "⚠ Bitte wählen Sie zuerst eine Datei oder einen Unterordner zum Löschen aus.";
                return;
            }

            deleteSelectedBtn.Visibility = Visibility.Collapsed;
            confirmDeleteBtn.Visibility = Visibility.Visible;
            cancelDeleteConfirmBtn.Visibility = Visibility.Visible;
            selectedInfoText.Text = $"⚠ Wirklich löschen: „{selectedNode.Name}“ ({Format.Size(selectedNode.Size)})?";
        };

        cancelDeleteConfirmBtn.Click += (_, _) => UpdateSelectedNodeUi();

        confirmDeleteBtn.Click += async (_, _) =>
        {
            ResetDeleteConfirmState();
            var nodeToDelete = selectedNode;
            if (nodeToDelete is null || ReferenceEquals(nodeToDelete, scannedRoot))
                return;

            try
            {
                long freedBytes = nodeToDelete.Size;
                string deletedName = nodeToDelete.Name;

                if (nodeToDelete.IsDirectory)
                {
                    if (Directory.Exists(nodeToDelete.FullPath))
                        await Task.Run(() => Directory.Delete(nodeToDelete.FullPath, recursive: true));
                }
                else
                {
                    await Task.Run(() => DuplicateFinderService.DeleteDuplicateFiles([nodeToDelete.FullPath]));
                }

                var fallbackParent = nodeToDelete.Parent ?? scannedRoot;
                if (ReferenceEquals(currentRoot, nodeToDelete))
                    currentRoot = fallbackParent;

                StorageTreemapService.RemoveNodeAndRecalculate(nodeToDelete);
                selectedNode = fallbackParent;
                PopulateUpperListsAndTreemap();

                selectedInfoText.Text = $"✓ „{deletedName}“ gelöscht ({Format.Size(freedBytes)} freigegeben)";
                owner.ViewModel.StatusNotification = $"Speicherplatz-Analysator: „{deletedName}“ gelöscht ({Format.Size(freedBytes)} frei)";

                if (owner.ViewModel.Left.ActiveTab is not null)
                    await owner.ViewModel.Left.ActiveTab.ReloadAsync();
                if (owner.ViewModel.Right.ActiveTab is not null)
                    await owner.ViewModel.Right.ActiveTab.ReloadAsync();
            }
            catch (Exception ex)
            {
                selectedInfoText.Text = $"⚠ Löschen fehlgeschlagen: {ex.Message}";
            }
        };

        var win = CreateResizableChildWindow(
            "Speicherplatz-Analysator & Grafische Treemap (WinDirStat / Strg+Shift+W)",
            "🗺",
            bodyGrid,
            defaultWidth: 1220,
            defaultHeight: 820);

        closeBtn.Click += (_, _) =>
        {
            scanCts?.Cancel();
            win.Close();
        };

        win.Closed += (_, _) =>
        {
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
        double pctView = currentRoot is { Size: > 0 } ? (double)node.Size * 100.0 / currentRoot.Size : 100.0;
        double pctTotal = scannedRoot is { Size: > 0 } ? (double)node.Size * 100.0 / scannedRoot.Size : pctView;
        string relPath = currentRoot is not null
            ? Path.GetRelativePath(currentRoot.FullPath, node.FullPath)
            : node.Name;

        string typeInfo = node.IsDirectory
            ? $"Ordner ({node.FileCount:N0} Dateien, {node.FolderCount:N0} Unterordner)"
            : $"Datei ({node.Extension}) · {StorageTreemapService.GetCategoryDisplayName(node.Category)}";

        return $"{(node.IsDirectory ? "📁" : "📄")} {node.Name}\n" +
               $"Relativer Pfad: {relPath}\n" +
               $"Vollständiger Pfad: {node.FullPath}\n" +
               $"Größe: {Format.Size(node.Size)} ({node.Size:N0} Bytes)\n" +
               $"Anteil: {pctView:0.00} % der Ansicht ({pctTotal:0.00} % der Wurzel)\n" +
               $"Typ: {typeInfo}\n" +
               $"Geändert: {node.ModifiedLocal:yyyy-MM-dd HH:mm:ss}";
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

