using System.IO;

namespace ModernExplorer.Core.FileSystem;

/// <summary>
/// Semantic file category used for color-coding tiles in the WinDirStat-style storage treemap.
/// </summary>
public enum StorageCategory
{
    VideoMedia = 0,
    ArchiveImage = 1,
    VirtualDiskSystem = 2,
    CodeDocument = 3,
    PictureGraphic = 4,
    BinaryExecutable = 5,
    OtherTemp = 6,

    VideoAudio = VideoMedia,
    ArchiveDiskImage = ArchiveImage,
    VirtualDiskDatabaseSystem = VirtualDiskSystem,
    CodeTextDocument = CodeDocument,
    ImageGraphics = PictureGraphic,
    ExecutableBinary = BinaryExecutable,
    Other = OtherTemp,
}

/// <summary>
/// Coloring mode for the storage analyzer treemap (by file category or by file modification age).
/// </summary>
public enum TreemapColorMode
{
    Category = 0,
    AgeHeatmap = 1,
}

/// <summary>
/// Real-time progress report emitted while scanning a drive or directory tree.
/// </summary>
public sealed record StorageScanProgress(
    string CurrentDirectory,
    int ScannedFiles,
    int ScannedDirectories,
    long TotalBytes,
    bool IsIndeterminate = true);

/// <summary>
/// Aggregated statistics for a single file extension (like WinDirStat's upper-right extension list).
/// </summary>
public sealed record StorageExtensionStat(
    string Extension,
    StorageCategory Category,
    string CategoryLabel,
    string ColorHex,
    long TotalBytes,
    double Percentage,
    int FileCount);

/// <summary>
/// Calculated 2D rectangle for rendering a node inside the squarified treemap canvas.
/// </summary>
public sealed record TreemapRect(
    StorageTreemapNode Node,
    double X,
    double Y,
    double Width,
    double Height,
    int Depth,
    bool IsGroupContainer);

/// <summary>
/// Hierarchical node representing a directory or file in the storage analyzer tree.
/// </summary>
public sealed class StorageTreemapNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required bool IsDirectory { get; init; }
    public long SizeBytes { get; set; }
    public long Size => SizeBytes;
    public DateTime LastWriteTime { get; init; }
    public DateTime ModifiedLocal => LastWriteTime;
    public string Extension { get; init; } = "";
    public StorageCategory Category { get; init; } = StorageCategory.OtherTemp;
    public string ColorHex { get; set; } = "#64748B";
    public int FileCount { get; set; }
    public int DirectoryCount { get; set; }
    public int FolderCount => DirectoryCount;
    public StorageTreemapNode? Parent { get; set; }
    public List<StorageTreemapNode> Children { get; } = [];

    public double GetPercentageOf(long totalBytes) =>
        totalBytes > 0 ? Math.Clamp(SizeBytes * 100.0 / totalBytes, 0.0, 100.0) : 0.0;
}

/// <summary>
/// Provides WinDirStat / WizTree-style recursive disk space analysis, extension breakdown,
/// squarified treemap layout calculation, and in-place tree updates on deletion.
/// </summary>
public static class StorageTreemapService
{
    private static readonly HashSet<string> VideoMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg",
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".tbz2", ".zst",
        ".iso", ".img", ".cab", ".nupkg", ".jar", ".war", ".apk", ".dmg", ".deb", ".rpm"
    };

    private static readonly HashSet<string> VirtualDiskSystemExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vhdx", ".vhd", ".vmdk", ".vdi", ".qcow2", ".ova", ".ovf",
        ".sys", ".dmp", ".bak", ".old", ".hiberfil", ".swap", ".bin", ".dat", ".db", ".sqlite", ".mdf", ".ldf"
    };

    private static readonly HashSet<string> CodeDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".fs", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".py", ".js", ".ts", ".tsx", ".jsx",
        ".html", ".htm", ".css", ".scss", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
        ".md", ".txt", ".rtf", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".tsv", ".sql", ".sh", ".ps1", ".bat", ".cmd"
    };

    private static readonly HashSet<string> PictureGraphicExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".svg", ".ico", ".tif", ".tiff",
        ".psd", ".ai", ".eps", ".raw", ".cr2", ".nef", ".dng", ".heic", ".avif", ".dds", ".tga"
    };

    private static readonly HashSet<string> BinaryExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".msi", ".msix", ".appx", ".pdb", ".so", ".dylib", ".lib", ".a", ".obj", ".o", ".wasm", ".winmd"
    };

    public static string GetDefaultCategoryColorHex(StorageCategory category) => category switch
    {
        StorageCategory.VideoMedia => ModernExplorer.Core.Settings.AppSettings.DefaultTreemapVideoMediaColor,
        StorageCategory.ArchiveImage => ModernExplorer.Core.Settings.AppSettings.DefaultTreemapArchiveImageColor,
        StorageCategory.VirtualDiskSystem => ModernExplorer.Core.Settings.AppSettings.DefaultTreemapVirtualDiskSystemColor,
        StorageCategory.CodeDocument => ModernExplorer.Core.Settings.AppSettings.DefaultTreemapCodeDocumentColor,
        StorageCategory.PictureGraphic => ModernExplorer.Core.Settings.AppSettings.DefaultTreemapPictureGraphicColor,
        StorageCategory.BinaryExecutable => ModernExplorer.Core.Settings.AppSettings.DefaultTreemapBinaryExecutableColor,
        _ => ModernExplorer.Core.Settings.AppSettings.DefaultTreemapOtherTempColor,
    };

    public static string GetCategoryColorHex(StorageCategory category)
    {
        var vm = ModernExplorer.Core.ViewModels.SettingsViewModel.Current;
        if (vm is not null)
            return vm.GetTreemapCategoryColor(category);

        return GetDefaultCategoryColorHex(category);
    }

    public static string GetCategoryDisplayName(StorageCategory category) => category switch
    {
        StorageCategory.VideoMedia => ModernExplorer.Core.Localization.LocalizationService.T("Treemap.Cat.VideoMedia"),
        StorageCategory.ArchiveImage => ModernExplorer.Core.Localization.LocalizationService.T("Treemap.Cat.ArchiveImage"),
        StorageCategory.VirtualDiskSystem => ModernExplorer.Core.Localization.LocalizationService.T("Treemap.Cat.VirtualDiskSystem"),
        StorageCategory.CodeDocument => ModernExplorer.Core.Localization.LocalizationService.T("Treemap.Cat.CodeDocument"),
        StorageCategory.PictureGraphic => ModernExplorer.Core.Localization.LocalizationService.T("Treemap.Cat.PictureGraphic"),
        StorageCategory.BinaryExecutable => ModernExplorer.Core.Localization.LocalizationService.T("Treemap.Cat.BinaryExecutable"),
        _ => ModernExplorer.Core.Localization.LocalizationService.T("Treemap.Cat.OtherTemp"),
    };

    public static (StorageCategory Category, string ColorHex, string CategoryLabel) ClassifyExtension(string? fileNameOrExtension)
    {
        string ext = "";
        if (!string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            ext = fileNameOrExtension.StartsWith('.')
                ? fileNameOrExtension.ToLowerInvariant()
                : Path.GetExtension(fileNameOrExtension).ToLowerInvariant();
        }

        StorageCategory cat = StorageCategory.OtherTemp;
        if (VideoMediaExtensions.Contains(ext))
            cat = StorageCategory.VideoMedia;
        else if (ArchiveExtensions.Contains(ext))
            cat = StorageCategory.ArchiveImage;
        else if (VirtualDiskSystemExtensions.Contains(ext))
            cat = StorageCategory.VirtualDiskSystem;
        else if (CodeDocumentExtensions.Contains(ext))
            cat = StorageCategory.CodeDocument;
        else if (PictureGraphicExtensions.Contains(ext))
            cat = StorageCategory.PictureGraphic;
        else if (BinaryExecutableExtensions.Contains(ext))
            cat = StorageCategory.BinaryExecutable;

        return (cat, GetCategoryColorHex(cat), GetCategoryDisplayName(cat));
    }

    /// <summary>
    /// Updates <see cref="StorageTreemapNode.ColorHex"/> across an existing scanned tree when user color settings or heatmap mode change.
    /// </summary>
    public static void RefreshNodeColors(StorageTreemapNode? node, TreemapColorMode mode = TreemapColorMode.Category)
    {
        if (node is null) return;
        if (!node.IsDirectory)
        {
            node.ColorHex = mode == TreemapColorMode.AgeHeatmap
                ? GetAgeHeatmapColorHex(node.LastWriteTime)
                : GetCategoryColorHex(node.Category);
            return;
        }

        foreach (var child in node.Children)
            RefreshNodeColors(child, mode);
    }

    public static string GetAgeHeatmapColorHex(DateTime lastWriteTime)
    {
        var age = DateTime.Now - lastWriteTime;
        if (age.TotalDays <= 7)
            return "#EF4444"; // Brand new (< 7 days) - Hot Red
        if (age.TotalDays <= 30)
            return "#F97316"; // Recent (< 30 days) - Orange
        if (age.TotalDays <= 180)
            return "#EAB308"; // < 6 months - Amber/Yellow
        if (age.TotalDays <= 365)
            return "#10B981"; // < 1 year - Emerald
        if (age.TotalDays <= 365 * 3)
            return "#3B82F6"; // 1-3 years - Blue
        return "#8B5CF6";     // > 3 years (Data relic) - Purple
    }

    /// <summary>
    /// Returns the top <paramref name="maxCount"/> largest individual files across the entire scanned tree.
    /// </summary>
    public static IReadOnlyList<StorageTreemapNode> GetTopLargestFiles(StorageTreemapNode rootNode, int maxCount = 100)
    {
        var files = new List<StorageTreemapNode>();

        void Collect(StorageTreemapNode n)
        {
            if (!n.IsDirectory)
            {
                files.Add(n);
                return;
            }
            foreach (var child in n.Children)
                Collect(child);
        }

        Collect(rootNode);
        return files
            .OrderByDescending(f => f.SizeBytes)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxCount))
            .ToList();
    }

    /// <summary>
    /// Generates a comprehensive storage analysis report in Markdown or CSV format.
    /// </summary>
    public static string ExportStorageReport(StorageTreemapNode rootNode, bool markdownFormat = true)
    {
        var sb = new System.Text.StringBuilder();
        var topFiles = GetTopLargestFiles(rootNode, 50);
        var extStats = ComputeExtensionStats(rootNode, 30);

        if (markdownFormat)
        {
            sb.AppendLine($"# ModernExplorer Storage Report — `{rootNode.FullPath}`");
            sb.AppendLine($"- **Gesamtgröße:** {ModernExplorer.Core.ViewModels.Format.Size(rootNode.SizeBytes)} ({rootNode.SizeBytes:N0} Bytes)");
            sb.AppendLine($"- **Dateien:** {rootNode.FileCount:N0} | **Ordner:** {rootNode.DirectoryCount:N0}");
            sb.AppendLine($"- **Erstellt am:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("## Top-Unterordner");
            sb.AppendLine("| Name | Größe | Anteil | Dateien |");
            sb.AppendLine("| :--- | ---: | ---: | ---: |");
            foreach (var child in rootNode.Children.Take(25))
            {
                sb.AppendLine($"| `{child.Name}` | {ModernExplorer.Core.ViewModels.Format.Size(child.SizeBytes)} | {child.GetPercentageOf(rootNode.SizeBytes):0.0}% | {child.FileCount:N0} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Dateityp-Statistik");
            sb.AppendLine("| Endung | Kategorie | Größe | Anteil | Anzahl |");
            sb.AppendLine("| :--- | :--- | ---: | ---: | ---: |");
            foreach (var ext in extStats)
            {
                sb.AppendLine($"| `{ext.Extension}` | {ext.CategoryLabel} | {ModernExplorer.Core.ViewModels.Format.Size(ext.TotalBytes)} | {ext.Percentage:0.0}% | {ext.FileCount:N0} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Top 50 Größte Dateien");
            sb.AppendLine("| Datei | Größe | Geändert | Relativer Pfad |");
            sb.AppendLine("| :--- | ---: | :--- | :--- |");
            foreach (var f in topFiles)
            {
                sb.AppendLine($"| `{f.Name}` | {ModernExplorer.Core.ViewModels.Format.Size(f.SizeBytes)} | {f.LastWriteTime:yyyy-MM-dd} | `{f.RelativePath}` |");
            }
        }
        else
        {
            sb.AppendLine("Category,Name,RelativePath,SizeBytes,SizeFormatted,Percentage,FileCount,Modified");
            foreach (var child in rootNode.Children)
            {
                sb.AppendLine($"Subfolder,\"{child.Name.Replace("\"", "\"\"")}\",\"{child.RelativePath.Replace("\"", "\"\"")}\",{child.SizeBytes},{ModernExplorer.Core.ViewModels.Format.Size(child.SizeBytes)},{child.GetPercentageOf(rootNode.SizeBytes):0.00},{child.FileCount},{child.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }
            foreach (var f in topFiles)
            {
                sb.AppendLine($"TopFile,\"{f.Name.Replace("\"", "\"\"")}\",\"{f.RelativePath.Replace("\"", "\"\"")}\",{f.SizeBytes},{ModernExplorer.Core.ViewModels.Format.Size(f.SizeBytes)},{f.GetPercentageOf(rootNode.SizeBytes):0.00},1,{f.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }
        }

        return sb.ToString();
    }

    public static async Task<StorageTreemapNode> ScanDirectoryTreeAsync(
        string rootPath,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            string normalizedRoot = Path.GetFullPath(rootPath);
            string rootName = Path.GetFileName(normalizedRoot.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(rootName))
                rootName = normalizedRoot;

            var rootNode = new StorageTreemapNode
            {
                Name = rootName,
                FullPath = normalizedRoot,
                RelativePath = ".",
                IsDirectory = true,
                LastWriteTime = Directory.Exists(normalizedRoot) ? Directory.GetLastWriteTime(normalizedRoot) : DateTime.Now,
                ColorHex = "#475569",
            };

            if (!Directory.Exists(normalizedRoot))
                return rootNode;

            int scannedFiles = 0;
            int scannedDirs = 0;
            long scannedBytes = 0;

            void ScanRecursive(DirectoryInfo dirInfo, StorageTreemapNode dirNode)
            {
                ct.ThrowIfCancellationRequested();
                scannedDirs++;

                if (scannedDirs % 15 == 0)
                {
                    progress?.Report(new StorageScanProgress(dirInfo.FullName, scannedFiles, scannedDirs, scannedBytes));
                }

                FileInfo[] files;
                try
                {
                    files = dirInfo.GetFiles();
                }
                catch
                {
                    return;
                }

                foreach (var fi in files)
                {
                    ct.ThrowIfCancellationRequested();
                    long len = 0;
                    try { len = fi.Length; } catch { }

                    string ext = string.IsNullOrEmpty(fi.Extension)
                        ? ModernExplorer.Core.Localization.LocalizationService.T("Treemap.NoExtension")
                        : fi.Extension.ToLowerInvariant();
                    var (cat, colorHex, _) = ClassifyExtension(ext);
                    string relPath;
                    try { relPath = Path.GetRelativePath(normalizedRoot, fi.FullName); }
                    catch { relPath = fi.Name; }

                    var fileNode = new StorageTreemapNode
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        RelativePath = relPath,
                        IsDirectory = false,
                        SizeBytes = len,
                        LastWriteTime = fi.LastWriteTime,
                        Extension = ext,
                        Category = cat,
                        ColorHex = colorHex,
                        FileCount = 1,
                        DirectoryCount = 0,
                        Parent = dirNode,
                    };

                    dirNode.Children.Add(fileNode);
                    dirNode.SizeBytes += len;
                    dirNode.FileCount += 1;

                    scannedFiles++;
                    scannedBytes += len;

                    if (scannedFiles % 80 == 0)
                    {
                        progress?.Report(new StorageScanProgress(dirInfo.FullName, scannedFiles, scannedDirs, scannedBytes));
                    }
                }

                DirectoryInfo[] subDirs;
                try
                {
                    subDirs = dirInfo.GetDirectories();
                }
                catch
                {
                    subDirs = [];
                }

                foreach (var sub in subDirs)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    string relPath;
                    try { relPath = Path.GetRelativePath(normalizedRoot, sub.FullName); }
                    catch { relPath = sub.Name; }

                    var childDirNode = new StorageTreemapNode
                    {
                        Name = sub.Name,
                        FullPath = sub.FullName,
                        RelativePath = relPath,
                        IsDirectory = true,
                        LastWriteTime = sub.LastWriteTime,
                        ColorHex = "#475569",
                        Parent = dirNode,
                    };

                    ScanRecursive(sub, childDirNode);

                    if (childDirNode.FileCount > 0 || childDirNode.SizeBytes > 0)
                    {
                        dirNode.Children.Add(childDirNode);
                        dirNode.SizeBytes += childDirNode.SizeBytes;
                        dirNode.FileCount += childDirNode.FileCount;
                        dirNode.DirectoryCount += 1 + childDirNode.DirectoryCount;
                    }
                }

                dirNode.Children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            }

            ScanRecursive(new DirectoryInfo(normalizedRoot), rootNode);
            progress?.Report(new StorageScanProgress(normalizedRoot, scannedFiles, scannedDirs, scannedBytes, IsIndeterminate: false));
            return rootNode;
        }, ct);
    }

    /// <summary>
    /// Computes aggregated extension statistics for all files inside <paramref name="rootNode"/>.
    /// </summary>
    public static IReadOnlyList<StorageExtensionStat> ComputeExtensionStats(StorageTreemapNode rootNode, int topCount = 100)
    {
        var map = new Dictionary<string, (StorageCategory Cat, string Label, string Color, long Bytes, int Count)>(StringComparer.OrdinalIgnoreCase);

        void Walk(StorageTreemapNode node)
        {
            if (!node.IsDirectory)
            {
                string ext = string.IsNullOrWhiteSpace(node.Extension) || node.Extension.StartsWith('<')
                    ? ModernExplorer.Core.Localization.LocalizationService.T("Treemap.NoExtension")
                    : node.Extension.ToLowerInvariant();
                if (!map.TryGetValue(ext, out var cur))
                {
                    var (cat, color, label) = ClassifyExtension(ext.StartsWith('<') ? "" : ext);
                    cur = (cat, label, color, 0L, 0);
                }
                map[ext] = (cur.Cat, cur.Label, cur.Color, cur.Bytes + node.SizeBytes, cur.Count + 1);
                return;
            }

            foreach (var child in node.Children)
                Walk(child);
        }

        Walk(rootNode);

        long total = Math.Max(1, rootNode.SizeBytes);
        return map
            .Select(kvp => new StorageExtensionStat(
                kvp.Key,
                kvp.Value.Cat,
                kvp.Value.Label,
                kvp.Value.Color,
                kvp.Value.Bytes,
                Math.Clamp(kvp.Value.Bytes * 100.0 / total, 0.0, 100.0),
                kvp.Value.Count))
            .OrderByDescending(s => s.TotalBytes)
            .ThenByDescending(s => s.FileCount)
            .Take(topCount)
            .ToList();
    }

    /// <summary>
    /// Removes a deleted node from its parent hierarchy and recalculates sizes and counts up to the root.
    /// </summary>
    public static bool RemoveNodeAndRecalculate(StorageTreemapNode target)
    {
        var parent = target.Parent;
        if (parent is null)
            return false;

        if (!parent.Children.Remove(target))
            return false;

        long deltaBytes = target.SizeBytes;
        int deltaFiles = target.IsDirectory ? target.FileCount : 1;
        int deltaDirs = target.IsDirectory ? 1 + target.DirectoryCount : 0;

        var curr = parent;
        while (curr is not null)
        {
            curr.SizeBytes = Math.Max(0, curr.SizeBytes - deltaBytes);
            curr.FileCount = Math.Max(0, curr.FileCount - deltaFiles);
            curr.DirectoryCount = Math.Max(0, curr.DirectoryCount - deltaDirs);
            curr.Children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            curr = curr.Parent;
        }

        target.Parent = null;
        return true;
    }

    /// <summary>
    /// Computes a 2D Squarified Treemap layout (Bruls-Huizing-van Wijk algorithm) for <paramref name="rootNode"/>.
    /// </summary>
    public static IReadOnlyList<TreemapRect> BuildSquarifiedTreemapLayout(
        StorageTreemapNode rootNode,
        double width,
        double height,
        int maxDepth = 3,
        int maxTiles = 420,
        double minTileSize = 2.5)
    {
        var results = new List<TreemapRect>();
        if (width <= 4 || height <= 4 || rootNode.SizeBytes <= 0 || rootNode.Children.Count == 0)
            return results;

        int leafCount = 0;

        void LayoutDirectory(StorageTreemapNode dirNode, double x, double y, double w, double h, int depth)
        {
            if (w < 3 || h < 3 || dirNode.SizeBytes <= 0 || dirNode.Children.Count == 0)
                return;

            var nonEmpty = dirNode.Children.Where(c => c.SizeBytes > 0).ToList();
            if (nonEmpty.Count == 0)
                return;

            var childRects = SquarifyChildren(nonEmpty, x, y, w, h, dirNode.SizeBytes);

            foreach (var (child, cx, cy, cw, ch) in childRects)
            {
                if (cw < minTileSize || ch < minTileSize)
                    continue;

                bool canRecurse = child.IsDirectory &&
                                  depth < maxDepth &&
                                  cw >= 28 &&
                                  ch >= 28 &&
                                  child.Children.Count > 0 &&
                                  leafCount < maxTiles;

                if (canRecurse)
                {
                    results.Add(new TreemapRect(child, cx, cy, cw, ch, depth, IsGroupContainer: true));
                    double headerH = ch >= 42 ? 16 : 3;
                    double pad = 2.0;
                    double innerX = cx + pad;
                    double innerY = cy + headerH;
                    double innerW = Math.Max(0, cw - pad * 2);
                    double innerH = Math.Max(0, ch - headerH - pad);
                    if (innerW >= 6 && innerH >= 6)
                    {
                        LayoutDirectory(child, innerX, innerY, innerW, innerH, depth + 1);
                    }
                }
                else
                {
                    if (leafCount >= maxTiles)
                        break;
                    leafCount++;
                    results.Add(new TreemapRect(child, cx, cy, cw, ch, depth, IsGroupContainer: false));
                }
            }
        }

        LayoutDirectory(rootNode, 0, 0, width, height, depth: 0);
        return results;
    }

    private static List<(StorageTreemapNode Node, double X, double Y, double W, double H)> SquarifyChildren(
        IReadOnlyList<StorageTreemapNode> children,
        double x,
        double y,
        double w,
        double h,
        long totalBytes)
    {
        var output = new List<(StorageTreemapNode Node, double X, double Y, double W, double H)>(children.Count);
        double totalArea = w * h;
        if (totalArea <= 0 || totalBytes <= 0)
            return output;

        var scaled = children
            .Select(c => (Node: c, Area: Math.Max(0.0001, (double)c.SizeBytes / totalBytes * totalArea)))
            .ToList();

        double curX = x;
        double curY = y;
        double curW = w;
        double curH = h;

        var currentRow = new List<(StorageTreemapNode Node, double Area)>();

        void LayoutRow(List<(StorageTreemapNode Node, double Area)> row)
        {
            if (row.Count == 0 || curW <= 0 || curH <= 0)
                return;

            double rowArea = row.Sum(r => r.Area);
            bool horizontal = curW >= curH; // Lay out along the shorter side

            if (horizontal)
            {
                double stripWidth = curH > 0 ? rowArea / curH : 0;
                stripWidth = Math.Min(curW, stripWidth);
                double offsetY = curY;
                foreach (var item in row)
                {
                    double itemH = stripWidth > 0 ? item.Area / stripWidth : 0;
                    itemH = Math.Min(curY + curH - offsetY, itemH);
                    output.Add((item.Node, curX, offsetY, stripWidth, Math.Max(0, itemH)));
                    offsetY += itemH;
                }
                curX += stripWidth;
                curW = Math.Max(0, curW - stripWidth);
            }
            else
            {
                double stripHeight = curW > 0 ? rowArea / curW : 0;
                stripHeight = Math.Min(curH, stripHeight);
                double offsetX = curX;
                foreach (var item in row)
                {
                    double itemW = stripHeight > 0 ? item.Area / stripHeight : 0;
                    itemW = Math.Min(curX + curW - offsetX, itemW);
                    output.Add((item.Node, offsetX, curY, Math.Max(0, itemW), stripHeight));
                    offsetX += itemW;
                }
                curY += stripHeight;
                curH = Math.Max(0, curH - stripHeight);
            }
        }

        static double WorstAspectRatio(List<(StorageTreemapNode Node, double Area)> row, double sideLength)
        {
            if (row.Count == 0 || sideLength <= 0)
                return double.MaxValue;

            double sum = 0;
            double maxA = 0;
            double minA = double.MaxValue;
            foreach (var r in row)
            {
                sum += r.Area;
                if (r.Area > maxA) maxA = r.Area;
                if (r.Area < minA) minA = r.Area;
            }
            if (sum <= 0 || minA <= 0)
                return double.MaxValue;

            double s2 = sum * sum;
            double w2 = sideLength * sideLength;
            return Math.Max((w2 * maxA) / s2, s2 / (w2 * minA));
        }

        foreach (var item in scaled)
        {
            if (curW <= 0.5 || curH <= 0.5)
                break;

            double shortSide = Math.Min(curW, curH);
            if (currentRow.Count == 0)
            {
                currentRow.Add(item);
                continue;
            }

            double currentWorst = WorstAspectRatio(currentRow, shortSide);
            currentRow.Add(item);
            double nextWorst = WorstAspectRatio(currentRow, shortSide);

            if (nextWorst <= currentWorst)
            {
                // Aspect ratio improved, keep in current row
                continue;
            }
            else
            {
                // Commit previous row and start a new row with item
                currentRow.RemoveAt(currentRow.Count - 1);
                LayoutRow(currentRow);
                currentRow.Clear();
                currentRow.Add(item);
            }
        }

        if (currentRow.Count > 0)
            LayoutRow(currentRow);

        return output;
    }
}
