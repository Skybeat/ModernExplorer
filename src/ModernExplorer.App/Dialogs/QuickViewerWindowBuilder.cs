using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Viewer;
using ModernExplorer.Core.ViewModels;
using Windows.Data.Pdf;
using Windows.Media.Core;
using Windows.Storage.Streams;
using Windows.UI;

namespace ModernExplorer.App.Dialogs;

/// <summary>
/// Smart Quick Viewer (F3) that automatically renders files according to their format
/// (images with zoom &amp; background selector, PDF page renderer, audio/video player,
/// rendered Markdown, CSV/TSV tables, formatted JSON/XML, ZIP archive browser,
/// directory statistics, log viewer with live follow, plus hex dump &amp; SHA-256 file info).
/// </summary>
internal static class QuickViewerWindowBuilder
{
    public static async Task ShowAsync(MainWindow owner, ShellDialogService dialogService, string path)
    {
        LoadedDocument doc;
        try
        {
            doc = await FileContentService.LoadPreviewAsync(path);
        }
        catch (Exception ex)
        {
            dialogService.ShowMessage("Quick-Viewer Fehler", ex.Message);
            return;
        }

        var cleanupActions = new List<Action>();
        Window? win = null;

        // ====================================================================
        // HEADER SECTION: FILE ICON, FORMAT BADGE, DETAILS & VIEW SWITCHER
        // ====================================================================
        var headerCard = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1),
            Background = GetThemeBrush("ExplorerCardBackgroundBrush", Color.FromArgb(40, 128, 128, 140)),
            BorderBrush = GetThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 140)),
        };

        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var entry = FileEntry.TryFromPath(path);
        var fileIcon = new Image
        {
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Source = ShellIconConverter.ToImageSource(entry),
        };
        Grid.SetColumn(fileIcon, 0);
        headerGrid.Children.Add(fileIcon);

        var titleStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var fileNameBlock = new TextBlock
        {
            Text = Path.GetFileName(path.TrimEnd('\\', '/')),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var metaSubBlock = new TextBlock
        {
            Text = BuildDefaultMetaSummary(doc, entry),
            FontSize = 12,
            Opacity = 0.8,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleStack.Children.Add(fileNameBlock);
        titleStack.Children.Add(metaSubBlock);
        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);

        var viewSelector = new ComboBox
        {
            Width = 250,
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = new[]
            {
                $"✨ Intelligente Ansicht ({GetSmartModeLabel(doc.Mode)})",
                "📄 Text / Quellcode",
                "🔢 Hex-Dump (Binär)",
                "ℹ️ Datei-Info & SHA-256",
            },
            SelectedIndex = 0,
        };
        Grid.SetColumn(viewSelector, 2);
        headerGrid.Children.Add(viewSelector);
        headerCard.Child = headerGrid;

        // ====================================================================
        // MAIN CONTENT CONTAINER (switches depending on active view mode)
        // ====================================================================
        var contentHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        UIElement? smartViewElement = null;
        UIElement? rawTextViewElement = null;
        UIElement? hexViewElement = null;
        UIElement? infoViewElement = null;

        async Task<UIElement> GetOrCreateSmartViewAsync()
        {
            if (smartViewElement is not null) return smartViewElement;

            smartViewElement = doc.Mode switch
            {
                PreviewMode.Image => await BuildImageViewAsync(path, doc, metaSubBlock),
                PreviewMode.Pdf => await BuildPdfViewAsync(path, doc, metaSubBlock),
                PreviewMode.Media => BuildMediaView(path, doc, cleanupActions),
                PreviewMode.Markdown => BuildMarkdownView(doc),
                PreviewMode.Csv => BuildCsvTableView(doc),
                PreviewMode.Archive => BuildArchiveView(doc),
                PreviewMode.Directory => BuildDirectoryView(doc),
                PreviewMode.Json or PreviewMode.Xml => BuildTextAndLogView(path, doc, formatted: true, cleanupActions),
                PreviewMode.Log or PreviewMode.Text => BuildTextAndLogView(path, doc, formatted: false, cleanupActions),
                _ => BuildBinaryHexWithInfoView(doc, entry),
            };
            return smartViewElement;
        }

        UIElement GetOrCreateRawTextView()
        {
            rawTextViewElement ??= BuildTextAndLogView(path, doc, formatted: false, cleanupActions, forceRawText: true);
            return rawTextViewElement;
        }

        UIElement GetOrCreateHexView()
        {
            hexViewElement ??= BuildHexOnlyView(doc);
            return hexViewElement;
        }

        UIElement GetOrCreateInfoView()
        {
            infoViewElement ??= BuildFileInfoCard(doc, entry);
            return infoViewElement;
        }

        async Task UpdateActiveViewAsync()
        {
            contentHost.Children.Clear();
            UIElement target = viewSelector.SelectedIndex switch
            {
                1 => GetOrCreateRawTextView(),
                2 => GetOrCreateHexView(),
                3 => GetOrCreateInfoView(),
                _ => await GetOrCreateSmartViewAsync(),
            };
            contentHost.Children.Add(target);
        }

        viewSelector.SelectionChanged += async (_, _) => await UpdateActiveViewAsync();

        // ====================================================================
        // FOOTER BAR
        // ====================================================================
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pathLabel = new TextBlock
        {
            Text = path,
            Opacity = 0.65,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(pathLabel, 0);
        footerGrid.Children.Add(pathLabel);

        var footerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var openExternalBtn = new Button
        {
            Content = "Mit Standard-App öffnen",
            Padding = new Thickness(12, 6, 12, 6),
        };
        openExternalBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { dialogService.ShowMessage("Öffnen fehlgeschlagen", ex.Message); }
        };
        footerButtons.Children.Add(openExternalBtn);

        if (doc.Mode is not (PreviewMode.Directory or PreviewMode.Image or PreviewMode.Pdf or PreviewMode.Media or PreviewMode.Archive))
        {
            var openEditorBtn = new Button
            {
                Content = "Im Editor öffnen (F4)",
                Padding = new Thickness(12, 6, 12, 6),
            };
            openEditorBtn.Click += (_, _) =>
            {
                win?.Close();
                dialogService.OpenEditor(path);
            };
            footerButtons.Children.Add(openEditorBtn);
        }

        var closeBtn = new Button
        {
            Content = "Schließen (Esc)",
            Padding = new Thickness(14, 6, 14, 6),
        };
        closeBtn.Click += (_, _) => win?.Close();
        footerButtons.Children.Add(closeBtn);

        Grid.SetColumn(footerButtons, 1);
        footerGrid.Children.Add(footerButtons);

        // ====================================================================
        // ASSEMBLE OVERALL LAYOUT
        // ====================================================================
        var bodyGrid = new Grid { RowSpacing = 10, Padding = new Thickness(16, 12, 16, 14) };
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(headerCard, 0);
        Grid.SetRow(contentHost, 1);
        Grid.SetRow(footerGrid, 2);
        bodyGrid.Children.Add(headerCard);
        bodyGrid.Children.Add(contentHost);
        bodyGrid.Children.Add(footerGrid);

        await UpdateActiveViewAsync();

        win = dialogService.CreateResizableChildWindow(
            title: $"Quick-Viewer (F3) – {Path.GetFileName(path.TrimEnd('\\', '/'))}",
            iconGlyph: GetModeIconGlyph(doc.Mode),
            bodyContent: bodyGrid);

        win.Closed += (_, _) =>
        {
            foreach (var cleanup in cleanupActions)
            {
                try { cleanup(); } catch { }
            }
        };

        win.Activate();
    }

    // ========================================================================
    // 1. IMAGE VIEW (PNG, JPG, GIF, WEBP, BMP, ICO, TIFF, JFIF, SVG)
    // ========================================================================
    private static async Task<UIElement> BuildImageViewAsync(string path, LoadedDocument doc, TextBlock metaSubBlock)
    {
        var outerGrid = new Grid { RowSpacing = 8 };
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Image toolbar (Fit, 100%, Zoom -, Zoom +, background toggle)
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var fitBtn = new Button { Content = "↔ Einpassen", Padding = new Thickness(10, 4, 10, 4) };
        var actualSizeBtn = new Button { Content = "1:1 (100 %)", Padding = new Thickness(10, 4, 10, 4) };
        var zoomOutBtn = new Button { Content = "🔍 −", Padding = new Thickness(10, 4, 10, 4) };
        var zoomInBtn = new Button { Content = "🔍 +", Padding = new Thickness(10, 4, 10, 4) };
        var bgBtn = new Button { Content = "🎨 Hintergrund: Dunkel", Padding = new Thickness(10, 4, 10, 4) };
        var zoomLabel = new TextBlock
        {
            Text = "Modus: Automatisch einpassen",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Opacity = 0.85,
            Margin = new Thickness(6, 0, 0, 0),
        };

        toolbar.Children.Add(fitBtn);
        toolbar.Children.Add(actualSizeBtn);
        toolbar.Children.Add(zoomOutBtn);
        toolbar.Children.Add(zoomInBtn);
        toolbar.Children.Add(bgBtn);
        toolbar.Children.Add(zoomLabel);
        Grid.SetRow(toolbar, 0);
        outerGrid.Children.Add(toolbar);

        // Image stage
        var canvasBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = GetThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 140)),
            Background = new SolidColorBrush(Color.FromArgb(255, 22, 24, 30)),
            Padding = new Thickness(8),
        };

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Enabled,
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = 0.1f,
            MaxZoomFactor = 8.0f,
        };

        var imageControl = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        scrollViewer.Content = imageControl;
        canvasBorder.Child = scrollViewer;
        Grid.SetRow(canvasBorder, 1);
        outerGrid.Children.Add(canvasBorder);

        int pixelWidth = 0;
        int pixelHeight = 0;
        double currentZoom = 1.0;
        bool isFitMode = true;
        int bgMode = 0; // 0 = Dark, 1 = Light, 2 = Mid-gray contrast

        try
        {
            byte[] bytes = await Task.Run(() =>
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buf = new byte[fs.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                return buf;
            });

            using var ms = new MemoryStream(bytes, writable: false);
            using var ras = ms.AsRandomAccessStream();

            if (FileContentService.IsSvgFile(path))
            {
                var svg = new SvgImageSource();
                await svg.SetSourceAsync(ras);
                imageControl.Source = svg;
                metaSubBlock.Text = $"{doc.FileTypeDescription} · {Format.Size(doc.FileSize)}";
            }
            else
            {
                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(ras);
                pixelWidth = bmp.PixelWidth;
                pixelHeight = bmp.PixelHeight;
                imageControl.Source = bmp;

                if (pixelWidth > 0 && pixelHeight > 0)
                {
                    metaSubBlock.Text = $"{doc.FileTypeDescription} · {pixelWidth} × {pixelHeight} px · {Format.Size(doc.FileSize)}";
                }
            }
        }
        catch (Exception ex)
        {
            canvasBorder.Child = new TextBlock
            {
                Text = $"Bild konnte nicht dekodiert werden: {ex.Message}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        void ApplyZoomState()
        {
            if (isFitMode)
            {
                imageControl.Width = double.NaN;
                imageControl.Height = double.NaN;
                imageControl.Stretch = Stretch.Uniform;
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                zoomLabel.Text = pixelWidth > 0
                    ? $"Einpassen ({pixelWidth} × {pixelHeight} px)"
                    : "Automatisch einpassen";
            }
            else
            {
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                imageControl.Stretch = Stretch.Uniform;
                int baseW = pixelWidth > 0 ? pixelWidth : 800;
                int baseH = pixelHeight > 0 ? pixelHeight : 600;
                imageControl.Width = Math.Round(baseW * currentZoom);
                imageControl.Height = Math.Round(baseH * currentZoom);
                zoomLabel.Text = $"Zoom: {Math.Round(currentZoom * 100)} % ({imageControl.Width} × {imageControl.Height} px)";
            }
        }

        fitBtn.Click += (_, _) =>
        {
            isFitMode = true;
            ApplyZoomState();
        };

        actualSizeBtn.Click += (_, _) =>
        {
            isFitMode = false;
            currentZoom = 1.0;
            ApplyZoomState();
        };

        zoomOutBtn.Click += (_, _) =>
        {
            isFitMode = false;
            currentZoom = Math.Clamp(currentZoom - 0.25, 0.15, 8.0);
            ApplyZoomState();
        };

        zoomInBtn.Click += (_, _) =>
        {
            isFitMode = false;
            currentZoom = Math.Clamp(currentZoom + 0.25, 0.15, 8.0);
            ApplyZoomState();
        };

        bgBtn.Click += (_, _) =>
        {
            bgMode = (bgMode + 1) % 3;
            switch (bgMode)
            {
                case 0:
                    canvasBorder.Background = new SolidColorBrush(Color.FromArgb(255, 22, 24, 30));
                    bgBtn.Content = "🎨 Hintergrund: Dunkel";
                    break;
                case 1:
                    canvasBorder.Background = new SolidColorBrush(Color.FromArgb(255, 245, 247, 250));
                    bgBtn.Content = "🎨 Hintergrund: Hell";
                    break;
                case 2:
                    canvasBorder.Background = new SolidColorBrush(Color.FromArgb(255, 110, 116, 128));
                    bgBtn.Content = "🎨 Hintergrund: Kontrast-Grau";
                    break;
            }
        };

        ApplyZoomState();
        return outerGrid;
    }

    // ========================================================================
    // 2. PDF VIEW (Native Windows.Data.Pdf page rendering)
    // ========================================================================
    private static async Task<UIElement> BuildPdfViewAsync(string path, LoadedDocument doc, TextBlock metaSubBlock)
    {
        var outerGrid = new Grid { RowSpacing = 8 };
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var prevBtn = new Button { Content = "‹ Vorherige Seite", Padding = new Thickness(10, 4, 10, 4) };
        var nextBtn = new Button { Content = "Nächste Seite ›", Padding = new Thickness(10, 4, 10, 4) };
        var pageLabel = new TextBlock
        {
            Text = "Lade PDF …",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 8, 0),
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toolbar.Children.Add(prevBtn);
        toolbar.Children.Add(pageLabel);
        toolbar.Children.Add(nextBtn);
        Grid.SetRow(toolbar, 0);
        outerGrid.Children.Add(toolbar);

        var pageImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = 0.5f,
            MaxZoomFactor = 4.0f,
            Content = pageImage,
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = GetThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 140)),
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 32, 38)),
            Padding = new Thickness(12),
            Child = scroll,
        };
        Grid.SetRow(border, 1);
        outerGrid.Children.Add(border);

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            using var ms = new MemoryStream(bytes, writable: false);
            var pdfDoc = await PdfDocument.LoadFromStreamAsync(ms.AsRandomAccessStream());
            uint pageCount = pdfDoc.PageCount;
            uint currentPage = 0;

            metaSubBlock.Text = $"{doc.FileTypeDescription} · {pageCount} Seite(n) · {Format.Size(doc.FileSize)}";

            async Task RenderCurrentPageAsync()
            {
                if (pageCount == 0) return;
                pageLabel.Text = $"Seite {currentPage + 1} von {pageCount}";
                prevBtn.IsEnabled = currentPage > 0;
                nextBtn.IsEnabled = currentPage + 1 < pageCount;

                using var page = pdfDoc.GetPage(currentPage);
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream);
                stream.Seek(0);

                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(stream);
                pageImage.Source = bmp;
            }

            prevBtn.Click += async (_, _) =>
            {
                if (currentPage > 0)
                {
                    currentPage--;
                    await RenderCurrentPageAsync();
                }
            };

            nextBtn.Click += async (_, _) =>
            {
                if (currentPage + 1 < pageCount)
                {
                    currentPage++;
                    await RenderCurrentPageAsync();
                }
            };

            await RenderCurrentPageAsync();
        }
        catch (Exception ex)
        {
            border.Child = new TextBlock
            {
                Text = $"PDF-Vorschau konnte nicht geladen werden: {ex.Message}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        return outerGrid;
    }

    // ========================================================================
    // 3. AUDIO / VIDEO PLAYER (MP4, MKV, MOV, WEBM, MP3, WAV, FLAC, OGG)
    // ========================================================================
    private static UIElement BuildMediaView(string path, LoadedDocument doc, List<Action> cleanupActions)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = GetThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 140)),
            Background = new SolidColorBrush(Color.FromArgb(255, 16, 18, 24)),
            Padding = new Thickness(12),
        };

        try
        {
            var playerElement = new MediaPlayerElement
            {
                AreTransportControlsEnabled = true,
                AutoPlay = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Source = MediaSource.CreateFromUri(new Uri(path)),
            };

            cleanupActions.Add(() =>
            {
                try
                {
                    playerElement.MediaPlayer?.Pause();
                    playerElement.Source = null;
                }
                catch { }
            });

            border.Child = playerElement;
        }
        catch (Exception ex)
        {
            border.Child = new TextBlock
            {
                Text = $"Medienwiedergabe nicht verfügbar: {ex.Message}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        return border;
    }

    // ========================================================================
    // 4. MARKDOWN VIEW (Rendered headings, lists, code blocks & quotes)
    // ========================================================================
    private static UIElement BuildMarkdownView(LoadedDocument doc)
    {
        var stack = new StackPanel { Spacing = 8, Padding = new Thickness(18, 14, 18, 18) };
        var lines = doc.Content.Replace("\r\n", "\n").Split('\n');
        bool inCodeBlock = false;
        var codeBuffer = new StringBuilder();

        void FlushCodeBlock()
        {
            if (codeBuffer.Length == 0) return;
            var codeBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)),
                BorderBrush = GetThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 140)),
                Child = new TextBlock
                {
                    Text = codeBuffer.ToString().TrimEnd(),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12.5,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            stack.Children.Add(codeBorder);
            codeBuffer.Clear();
        }

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCodeBlock)
                {
                    FlushCodeBlock();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                codeBuffer.AppendLine(rawLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                stack.Children.Add(new Border { Height = 4 });
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                stack.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 6, 0, 6),
                    Background = GetThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(80, 128, 128, 140)),
                });
                continue;
            }

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = trimmed[2..].Trim(),
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Margin = new Thickness(0, 6, 0, 2),
                });
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = trimmed[3..].Trim(),
                    FontSize = 17.5,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Margin = new Thickness(0, 6, 0, 2),
                });
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = trimmed[4..].Trim(),
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Margin = new Thickness(0, 4, 0, 1),
                });
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                stack.Children.Add(new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 56, 189, 248)),
                    Padding = new Thickness(12, 4, 8, 4),
                    Child = new TextBlock
                    {
                        Text = trimmed[2..].Trim(),
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        Opacity = 0.9,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                    },
                });
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"  •  {trimmed[2..].Trim().Replace("**", "")}",
                    FontSize = 13.5,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                });
                continue;
            }

            stack.Children.Add(new TextBlock
            {
                Text = rawLine.Replace("**", ""),
                FontSize = 13.5,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
        }

        if (inCodeBlock)
            FlushCodeBlock();

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = GetThemeBrush("ExplorerCardBackgroundBrush", Color.FromArgb(30, 128, 128, 140)),
            BorderBrush = GetThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 140)),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = stack,
            },
        };
    }

    // ========================================================================
    // 5. CSV / TSV TABLE VIEW
    // ========================================================================
    private static UIElement BuildCsvTableView(LoadedDocument doc)
    {
        var table = doc.CsvTable ?? FileContentService.ParseCsvPreview(doc.Content);
        var outerGrid = new Grid { RowSpacing = 8 };
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var searchBox = new TextBox
        {
            PlaceholderText = "In Tabelle filtern …",
            Width = 260,
        };
        var sepName = table.Delimiter == '\t' ? "Tab (\\t)" : $"'{table.Delimiter}'";
        var infoLabel = new TextBlock
        {
            Text = $"Trennzeichen: {sepName} · {table.Headers.Count} Spalten · {table.Rows.Count} Zeilen geladen",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Opacity = 0.8,
        };

        var topBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        topBar.Children.Add(searchBox);
        topBar.Children.Add(infoLabel);
        Grid.SetRow(topBar, 0);
        outerGrid.Children.Add(topBar);

        var rowsStack = new StackPanel { Spacing = 1 };

        void RebuildTableRows(string? query)
        {
            rowsStack.Children.Clear();
            int colCount = Math.Max(1, table.Headers.Count);

            // Header row
            var headerGrid = new Grid
            {
                Padding = new Thickness(10, 6, 10, 6),
                ColumnSpacing = 12,
                Background = new SolidColorBrush(Color.FromArgb(55, 56, 189, 248)),
            };
            for (int c = 0; c < colCount; c++)
            {
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                var hText = new TextBlock
                {
                    Text = table.Headers[c],
                    FontWeight = FontWeights.Bold,
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(hText, c);
                headerGrid.Children.Add(hText);
            }
            rowsStack.Children.Add(headerGrid);

            var filtered = string.IsNullOrWhiteSpace(query)
                ? table.Rows
                : table.Rows.Where(r => r.Any(cell => cell.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

            int rowIdx = 0;
            foreach (var row in filtered)
            {
                var rGrid = new Grid
                {
                    Padding = new Thickness(10, 4, 10, 4),
                    ColumnSpacing = 12,
                    Background = rowIdx % 2 == 0
                        ? new SolidColorBrush(Colors.Transparent)
                        : new SolidColorBrush(Color.FromArgb(18, 128, 128, 140)),
                };
                for (int c = 0; c < colCount; c++)
                {
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                    var cellText = new TextBlock
                    {
                        Text = c < row.Length ? row[c] : "",
                        FontSize = 12.5,
                        FontFamily = new FontFamily("Consolas"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        IsTextSelectionEnabled = true,
                    };
                    Grid.SetColumn(cellText, c);
                    rGrid.Children.Add(cellText);
                }
                rowsStack.Children.Add(rGrid);
                rowIdx++;
            }
        }

        searchBox.TextChanged += (_, _) => RebuildTableRows(searchBox.Text);
        RebuildTableRows(null);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Enabled,
            Content = rowsStack,
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = GetThemeBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 140)),
            Background = GetThemeBrush("ExplorerCardBackgroundBrush", Color.FromArgb(30, 128, 128, 140)),
            Child = scroll,
        };
        Grid.SetRow(border, 1);
        outerGrid.Children.Add(border);

        return outerGrid;
    }

    // ========================================================================
    // 6. ARCHIVE VIEW (ZIP, NUPKG, JAR, DOCX, XLSX, PPTX)
    // ========================================================================
    private static UIElement BuildArchiveView(LoadedDocument doc)
    {
        var entries = doc.ArchiveEntries ?? [];
        var outerGrid = new Grid { RowSpacing = 8 };
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var searchBox = new TextBox
        {
            PlaceholderText = "Dateien im Archiv suchen …",
            Width = 260,
        };

        long totalUncompressed = entries.Where(e => !e.IsDirectory).Sum(e => e.UncompressedSize);
        long totalCompressed = entries.Where(e => !e.IsDirectory).Sum(e => e.CompressedSize);
        var summaryText = new TextBlock
        {
            Text = $"{entries.Count} Einträge · Entpackt: {Format.Size(totalUncompressed)} · Gepackt: {Format.Size(totalCompressed)}",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Opacity = 0.85,
        };

        var topBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        topBar.Children.Add(searchBox);
        topBar.Children.Add(summaryText);
        Grid.SetRow(topBar, 0);
        outerGrid.Children.Add(topBar);

        var textBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);

        void UpdateArchiveFilter()
        {
            var q = searchBox.Text.Trim();
            var filtered = string.IsNullOrEmpty(q)
                ? entries
                : entries.Where(e => e.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"{"Größe",12}  {"Gepackt",12}  {"Rate",7}  {"Geändert",16}  Pfad im Archiv");
            sb.AppendLine(new string('─', 96));
            foreach (var e in filtered)
                sb.AppendLine($"{e.SizeText,12}  {e.CompressedText,12}  {e.RatioText,7}  {e.ModifiedText,16}  {e.FullName}");
            textBox.Text = sb.ToString();
        }

        searchBox.TextChanged += (_, _) => UpdateArchiveFilter();
        UpdateArchiveFilter();

        Grid.SetRow(textBox, 1);
        outerGrid.Children.Add(textBox);
        return outerGrid;
    }

    // ========================================================================
    // 7. DIRECTORY VIEW (Folder statistics & contents)
    // ========================================================================
    private static UIElement BuildDirectoryView(LoadedDocument doc)
    {
        return BuildTextAndLogView(doc.FullPath, doc, formatted: false, cleanupActions: []);
    }

    // ========================================================================
    // 8. TEXT, CODE, JSON, XML & LOG VIEW
    // ========================================================================
    private static UIElement BuildTextAndLogView(
        string path,
        LoadedDocument doc,
        bool formatted,
        List<Action> cleanupActions,
        bool forceRawText = false)
    {
        var baseText = (!forceRawText && formatted)
            ? FileContentService.FormatStructuredText(doc.Content, doc.Mode)
            : doc.Content;

        var outerGrid = new Grid { RowSpacing = 8 };
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var searchBox = new TextBox { PlaceholderText = "Im Text suchen …", Width = 220 };
        var wrapCheck = new CheckBox
        {
            Content = "Zeilenumbruch",
            IsChecked = doc.Mode != PreviewMode.BinaryHex,
        };
        var lineNumCheck = new CheckBox
        {
            Content = "Zeilennummern",
            IsChecked = doc.Mode is PreviewMode.Json or PreviewMode.Xml or PreviewMode.Text,
        };

        var levelCombo = new ComboBox
        {
            ItemsSource = new[] { "ALL", "ERROR", "WARN", "INFO" },
            SelectedIndex = 0,
            Visibility = doc.Mode == PreviewMode.Log ? Visibility.Visible : Visibility.Collapsed,
        };
        var followCheck = new CheckBox
        {
            Content = "Follow (tail -f)",
            Visibility = doc.Mode == PreviewMode.Log ? Visibility.Visible : Visibility.Collapsed,
        };

        int lineCount = string.IsNullOrEmpty(baseText) ? 0 : baseText.Split('\n').Length;
        var statsLabel = new TextBlock
        {
            Text = $"{lineCount:N0} Zeilen · {doc.Encoding.WebName.ToUpperInvariant()} · {(doc.NewLine == "\r\n" ? "CRLF" : "LF")}",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Opacity = 0.75,
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        toolbar.Children.Add(searchBox);
        toolbar.Children.Add(wrapCheck);
        toolbar.Children.Add(lineNumCheck);
        toolbar.Children.Add(levelCombo);
        toolbar.Children.Add(followCheck);
        toolbar.Children.Add(statsLabel);
        Grid.SetRow(toolbar, 0);
        outerGrid.Children.Add(toolbar);

        var textView = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = wrapCheck.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(textView, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(textView, ScrollBarVisibility.Auto);

        void RefreshDisplayedText()
        {
            var level = doc.Mode == PreviewMode.Log ? levelCombo.SelectedItem as string : null;
            var filtered = FileContentService.FilterLogLines(baseText, level, searchBox.Text);
            if (lineNumCheck.IsChecked == true)
            {
                var sb = new StringBuilder();
                var split = filtered.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < split.Length; i++)
                    sb.Append($"{i + 1,5} │ ").AppendLine(split[i]);
                textView.Text = sb.ToString();
            }
            else
            {
                textView.Text = filtered;
            }
        }

        searchBox.TextChanged += (_, _) => RefreshDisplayedText();
        levelCombo.SelectionChanged += (_, _) => RefreshDisplayedText();
        lineNumCheck.Checked += (_, _) => RefreshDisplayedText();
        lineNumCheck.Unchecked += (_, _) => RefreshDisplayedText();
        wrapCheck.Checked += (_, _) => textView.TextWrapping = TextWrapping.Wrap;
        wrapCheck.Unchecked += (_, _) => textView.TextWrapping = TextWrapping.NoWrap;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += async (_, _) =>
        {
            if (followCheck.IsChecked != true) return;
            try
            {
                baseText = await FileContentService.ReadTailAsync(path);
                RefreshDisplayedText();
            }
            catch { }
        };
        followCheck.Checked += (_, _) => timer.Start();
        followCheck.Unchecked += (_, _) => timer.Stop();
        cleanupActions.Add(() => timer.Stop());

        RefreshDisplayedText();
        Grid.SetRow(textView, 1);
        outerGrid.Children.Add(textView);
        return outerGrid;
    }

    // ========================================================================
    // 9. BINARY & FILE INFO VIEW (Hex dump + SHA-256 + metadata)
    // ========================================================================
    private static UIElement BuildBinaryHexWithInfoView(LoadedDocument doc, FileEntry? entry)
    {
        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var infoCard = BuildFileInfoCard(doc, entry);
        var hexBox = BuildHexOnlyView(doc);

        Grid.SetRow(infoCard, 0);
        Grid.SetRow(hexBox, 1);
        grid.Children.Add(infoCard);
        grid.Children.Add(hexBox);
        return grid;
    }

    private static FrameworkElement BuildHexOnlyView(LoadedDocument doc)
    {
        var hexBox = new TextBox
        {
            Text = string.IsNullOrEmpty(doc.HexPreview) ? doc.Content : doc.HexPreview,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(hexBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(hexBox, ScrollBarVisibility.Auto);
        return hexBox;
    }

    private static FrameworkElement BuildFileInfoCard(LoadedDocument doc, FileEntry? entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Dateiname:         {Path.GetFileName(doc.FullPath.TrimEnd('\\', '/'))}");
        sb.AppendLine($"Vollständiger Pfad: {doc.FullPath}");
        sb.AppendLine($"Erkannter Typ:     {doc.FileTypeDescription}");
        sb.AppendLine($"Vorschaumodus:     {GetSmartModeLabel(doc.Mode)}");
        sb.AppendLine($"Dateigröße:        {Format.Size(doc.FileSize)} ({doc.FileSize:N0} Bytes)");
        if (entry is not null)
        {
            sb.AppendLine($"Zuletzt geändert:  {entry.LastWriteTime:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine($"Attribute:         {entry.Attributes}");
        }
        if (!string.IsNullOrEmpty(doc.Sha256Hex))
        {
            sb.AppendLine($"SHA-256 Prüfsumme: {doc.Sha256Hex}");
        }

        var box = new TextBox
        {
            Text = sb.ToString().TrimEnd(),
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        };
        return box;
    }

    private static string BuildDefaultMetaSummary(LoadedDocument doc, FileEntry? entry)
    {
        var mod = entry is not null && entry.LastWriteTime != default
            ? $" · Geändert: {entry.LastWriteTime:dd.MM.yyyy HH:mm}"
            : "";
        return $"{doc.FileTypeDescription} · {Format.Size(doc.FileSize)}{mod}" +
               (doc.IsTruncated && doc.Mode is PreviewMode.Text or PreviewMode.Log or PreviewMode.BinaryHex ? " (Ausschnitt)" : "");
    }

    private static string GetSmartModeLabel(PreviewMode mode) => mode switch
    {
        PreviewMode.Image => "Bild-Ansicht",
        PreviewMode.Pdf => "PDF-Dokument",
        PreviewMode.Media => "Media-Player",
        PreviewMode.Markdown => "Markdown",
        PreviewMode.Csv => "Tabelle CSV/TSV",
        PreviewMode.Json => "JSON Formatiert",
        PreviewMode.Xml => "XML/Markup Formatiert",
        PreviewMode.Archive => "Archiv-Inhalt",
        PreviewMode.Directory => "Ordner-Übersicht",
        PreviewMode.Log => "Log-Viewer",
        PreviewMode.BinaryHex => "Binär / Hex",
        _ => "Text / Code",
    };

    private static string GetModeIconGlyph(PreviewMode mode) => mode switch
    {
        PreviewMode.Image => "🖼",
        PreviewMode.Pdf => "📕",
        PreviewMode.Media => "🎬",
        PreviewMode.Markdown => "📝",
        PreviewMode.Csv => "📊",
        PreviewMode.Json or PreviewMode.Xml => "🧩",
        PreviewMode.Archive => "📦",
        PreviewMode.Directory => "📁",
        PreviewMode.Log => "📋",
        PreviewMode.BinaryHex => "🔢",
        _ => "📄",
    };

    private static Brush GetThemeBrush(string key, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var val) && val is Brush b)
            return b;
        return new SolidColorBrush(fallback);
    }
}
