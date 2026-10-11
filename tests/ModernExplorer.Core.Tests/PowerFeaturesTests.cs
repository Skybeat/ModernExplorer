using System.IO;
using System.Text;
using ModernExplorer.Core.Archive;
using ModernExplorer.Core.Favorites;
using ModernExplorer.Core.Features;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.Remote;
using ModernExplorer.Core.Session;
using ModernExplorer.Core.Settings;
using ModernExplorer.Core.ViewModels;
using Xunit;

namespace ModernExplorer.Core.Tests;

public sealed class PowerFeaturesTests : IDisposable
{
    private readonly string _tempDir;

    public PowerFeaturesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ModernExplorer_PowerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void BatchRename_Placeholders_SearchReplace_And_Execute_With_Undo()
    {
        string f1 = Path.Combine(_tempDir, "IMG_2025_alpha.jpg");
        string f2 = Path.Combine(_tempDir, "IMG_2025_beta.jpg");
        File.WriteAllText(f1, "hello1");
        File.WriteAllText(f2, "hello2");

        var entries = new[]
        {
            FileEntry.TryFromPath(f1)!,
            FileEntry.TryFromPath(f2)!,
        };

        var options = new BatchRenameOptions
        {
            NamePattern = "Photo_[C]_[N10-14]",
            ExtensionPattern = "[E]",
            SearchText = "alpha",
            ReplaceText = "first",
            CaseTransform = BatchCaseTransform.Lowercase,
            CounterStart = 1,
            CounterStep = 1,
            CounterDigits = 3,
        };

        var preview = BatchRenameService.GeneratePreview(entries, options);
        Assert.Equal(2, preview.Count);
        Assert.All(preview, p => Assert.False(p.HasConflict));
        Assert.Equal("photo_001_first.jpg", preview[0].NewName);
        Assert.Equal("photo_002_beta.jpg", preview[1].NewName);

        var exec = BatchRenameService.ExecuteBatchRename(preview);
        Assert.Equal(2, exec.RenamedCount);
        Assert.Empty(exec.Errors);
        Assert.True(File.Exists(Path.Combine(_tempDir, "photo_001_first.jpg")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "photo_002_beta.jpg")));

        var undo = new UndoHistoryService();
        undo.RecordBatchRename(exec.RenamedPairs);
        Assert.True(undo.CanUndo);

        var undoRes = undo.UndoLast();
        Assert.True(undoRes.Success);
        Assert.True(File.Exists(f1));
        Assert.True(File.Exists(f2));
    }

    [Fact]
    public async Task DirectorySync_Compare_And_Synchronize_Works()
    {
        string left = Path.Combine(_tempDir, "Left");
        string right = Path.Combine(_tempDir, "Right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);

        File.WriteAllText(Path.Combine(left, "only_left.txt"), "left content");
        File.WriteAllText(Path.Combine(right, "only_right.txt"), "right content");
        File.WriteAllText(Path.Combine(left, "same.txt"), "identical");
        File.WriteAllText(Path.Combine(right, "same.txt"), "identical");

        var diffs = await DirectorySyncService.CompareDirectoriesAsync(
            left,
            right,
            SyncCompareMode.Sha256Content,
            includeSubdirectories: true);

        Assert.Equal(3, diffs.Count);
        Assert.Contains(diffs, d => d.RelativePath == "only_left.txt" && d.Status == SyncItemStatus.LeftOnly);
        Assert.Contains(diffs, d => d.RelativePath == "only_right.txt" && d.Status == SyncItemStatus.RightOnly);
        Assert.Contains(diffs, d => d.RelativePath == "same.txt" && d.Status == SyncItemStatus.Equal);

        var sync = await DirectorySyncService.SynchronizeAsync(
            left,
            right,
            diffs,
            SyncDirectionMode.BidirectionalMerge,
            deleteOrphans: false);

        Assert.Equal(1, sync.CopiedLeftToRight);
        Assert.Equal(1, sync.CopiedRightToLeft);
        Assert.True(File.Exists(Path.Combine(right, "only_left.txt")));
        Assert.True(File.Exists(Path.Combine(left, "only_right.txt")));
    }

    [Fact]
    public async Task DeepSearch_Wildcard_And_Content_Finds_Matching_Files()
    {
        string sub = Path.Combine(_tempDir, "SubFolder");
        Directory.CreateDirectory(sub);

        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "ModernExplorer DeepSearch MagicToken123", Encoding.UTF8);
        File.WriteAllText(Path.Combine(sub, "notes.txt"), "Some other text without token", Encoding.UTF8);
        File.WriteAllText(Path.Combine(sub, "match.md"), "Another file containing MagicToken123!", Encoding.UTF8);

        var results = await DeepSearchService.SearchAsync(new DeepSearchQuery
        {
            RootPath = _tempDir,
            NamePattern = "*.md",
            ContentText = "MagicToken123",
            IncludeSubdirectories = true,
        });

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Name == "readme.md");
        Assert.Contains(results, r => r.Name == "match.md");
    }

    [Fact]
    public async Task DuplicateFinder_Detects_Exact_Sha256_Duplicates()
    {
        string d1 = Path.Combine(_tempDir, "A");
        string d2 = Path.Combine(_tempDir, "B");
        Directory.CreateDirectory(d1);
        Directory.CreateDirectory(d2);

        byte[] payload = Encoding.UTF8.GetBytes("Duplicate payload content repeated 1234567890");
        File.WriteAllBytes(Path.Combine(d1, "orig.bin"), payload);
        File.WriteAllBytes(Path.Combine(d2, "copy1.bin"), payload);
        File.WriteAllBytes(Path.Combine(d2, "unique.bin"), Encoding.UTF8.GetBytes("Different content of another length"));

        var scan = await DuplicateFinderService.FindDuplicatesAsync(_tempDir);
        Assert.Single(scan.Groups);
        Assert.Equal(2, scan.Groups[0].Files.Count);
        Assert.Equal(payload.Length, scan.TotalWastedBytes);
    }

    [Fact]
    public async Task DuplicateFinder_Reports_Detailed_Progress_Stages_Up_To_100_Percent()
    {
        string d1 = Path.Combine(_tempDir, "ProgA");
        string d2 = Path.Combine(_tempDir, "ProgB");
        Directory.CreateDirectory(d1);
        Directory.CreateDirectory(d2);

        byte[] payload = Encoding.UTF8.GetBytes("Progress reporting duplicate content 0123456789");
        File.WriteAllBytes(Path.Combine(d1, "f1.bin"), payload);
        File.WriteAllBytes(Path.Combine(d2, "f2.bin"), payload);

        var reports = new List<DuplicateScanProgress>();
        var syncProgress = new SynchronousProgress<DuplicateScanProgress>(p => reports.Add(p));

        var scan = await DuplicateFinderService.FindDuplicatesAsync(
            _tempDir,
            includeSubdirectories: true,
            minFileSizeBytes: 1,
            progress: null,
            detailedProgress: syncProgress);

        Assert.Single(scan.Groups);
        Assert.NotEmpty(reports);
        Assert.Contains(reports, r => r.IsIndeterminate);
        Assert.Contains(reports, r => !r.IsIndeterminate && r.Percentage > 0 && r.Percentage < 100);
        Assert.Equal(100, reports.Last().Percentage);
        Assert.False(reports.Last().IsIndeterminate);
    }

    [Fact]
    public async Task DuplicateFinder_DeleteDuplicateFiles_Deletes_Marked_And_ReadOnly_Files()
    {
        string d1 = Path.Combine(_tempDir, "DelA");
        string d2 = Path.Combine(_tempDir, "DelB");
        Directory.CreateDirectory(d1);
        Directory.CreateDirectory(d2);

        string origPath = Path.Combine(d1, "orig.txt");
        string dupPath = Path.Combine(d2, "dup.txt");
        byte[] content = Encoding.UTF8.GetBytes("Identical duplicate content for deletion test");

        File.WriteAllBytes(origPath, content);
        File.WriteAllBytes(dupPath, content);
        File.SetAttributes(dupPath, File.GetAttributes(dupPath) | FileAttributes.ReadOnly);

        var scan = await DuplicateFinderService.FindDuplicatesAsync(_tempDir);
        Assert.Single(scan.Groups);
        Assert.Equal(2, scan.Groups[0].Files.Count);

        var delRes = DuplicateFinderService.DeleteDuplicateFiles([dupPath]);
        Assert.Equal(1, delRes.DeletedCount);
        Assert.Equal(content.Length, delRes.FreedBytes);
        Assert.Empty(delRes.Errors);
        Assert.False(File.Exists(dupPath));
        Assert.True(File.Exists(origPath));
    }

    [Fact]
    public void FeatureToolTips_Are_Localized_And_ShowToolTips_Setting_Persists()
    {
        var settings = new AppSettings { ShowToolTips = false }.CloneNormalized();
        Assert.False(settings.ShowToolTips);

        var vm = new SettingsViewModel();
        vm.ApplyFromModel(settings);
        Assert.False(vm.ShowToolTips);
        vm.ShowToolTips = true;
        Assert.True(vm.ToModel().ShowToolTips);

        var loc = LocalizationService.Instance;
        loc.SetLanguage("de");
        string deBatchTip = loc["Feature.BatchRename.Tip"];
        string deDupeTip = loc["Feature.Duplicates.Tip"];
        Assert.Contains("Strg+M", deBatchTip);
        Assert.Contains("SHA-256", deDupeTip);

        loc.SetLanguage("en");
        string enBatchTip = loc["Feature.BatchRename.Tip"];
        string enDupeTip = loc["Feature.Duplicates.Tip"];
        Assert.Contains("Ctrl+M", enBatchTip);
        Assert.NotEqual(deBatchTip, enBatchTip);
        Assert.NotEqual(deDupeTip, enDupeTip);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public void FavoritesStore_Add_Toggle_Remove_Persists()
    {
        string favFile = Path.Combine(_tempDir, "favorites.json");
        var store = new FavoritesStore(favFile);

        string customFolder = Path.Combine(_tempDir, "MyProject");
        Directory.CreateDirectory(customFolder);

        bool added = store.ToggleBookmark(customFolder, "MyProject");
        Assert.True(added);
        Assert.True(store.IsBookmarked(customFolder));

        var reloaded = new FavoritesStore(favFile);
        Assert.True(reloaded.IsBookmarked(customFolder));

        bool afterSecondToggle = reloaded.ToggleBookmark(customFolder);
        Assert.False(afterSecondToggle);
        Assert.False(reloaded.IsBookmarked(customFolder));
    }

    [Fact]
    public async Task ArchiveService_Create_Browse_Virtual_And_Extract_Works()
    {
        string srcDir = Path.Combine(_tempDir, "Src");
        Directory.CreateDirectory(srcDir);
        string srcFile = Path.Combine(srcDir, "hello.txt");
        File.WriteAllText(srcFile, "Hello Archive World!");

        string zipPath = Path.Combine(_tempDir, "packed.zip");
        await ArchiveService.CreateArchiveAsync([srcFile], zipPath, ArchiveFormat.Zip);
        Assert.True(File.Exists(zipPath));
        Assert.True(ArchiveService.IsBrowsableArchiveFile(zipPath));

        string virtualPath = ArchiveService.BuildVirtualArchivePath(zipPath);
        var entries = ArchiveService.ListVirtualArchiveEntries(virtualPath);
        Assert.Contains(entries, e => e.Name == "hello.txt" && e.IsArchiveVirtualEntry);

        string extractDir = Path.Combine(_tempDir, "Extracted");
        int count = await ArchiveService.ExtractArchiveAsync(zipPath, extractDir);
        Assert.Equal(1, count);
        Assert.Equal("Hello Archive World!", File.ReadAllText(Path.Combine(extractDir, "hello.txt")));
    }

    [Fact]
    public void RemoteFileSystemService_UriParsing_And_CredentialEncryption_Works()
    {
        var profile = new RemoteConnectionProfile
        {
            Name = "Prod SFTP",
            Protocol = RemoteProtocol.Sftp,
            Host = "sftp.example.com",
            Port = 2222,
            Username = "deploy",
            InitialRemotePath = "/var/www",
        }.WithPlainPassword("S3cr3t!Pass");

        Assert.Equal("S3cr3t!Pass", profile.GetDecryptedPassword());
        string uri = profile.BuildRootUri();
        Assert.Equal("sftp://deploy@sftp.example.com:2222/var/www", uri);

        Assert.True(RemoteFileSystemService.TryParseRemoteUri(uri, out var proto, out var host, out var port, out var user, out var rPath));
        Assert.Equal(RemoteProtocol.Sftp, proto);
        Assert.Equal("sftp.example.com", host);
        Assert.Equal(2222, port);
        Assert.Equal("deploy", user);
        Assert.Equal("/var/www", rPath);

        string parent = RemoteFileSystemService.GetParentRemoteUri(uri);
        Assert.Equal("sftp://deploy@sftp.example.com:2222/var", parent);
    }

    [Fact]
    public async Task StorageTreemapService_ScansTree_ComputesStats_LayoutsRects_And_RecalculatesOnRemove()
    {
        string root = Path.Combine(_tempDir, "TreemapRoot");
        string mediaDir = Path.Combine(root, "Media");
        string codeDir = Path.Combine(root, "Code");
        Directory.CreateDirectory(mediaDir);
        Directory.CreateDirectory(codeDir);

        string videoFile = Path.Combine(mediaDir, "movie.mp4");
        string zipFile = Path.Combine(mediaDir, "backup.zip");
        string csFile = Path.Combine(codeDir, "Program.cs");

        await File.WriteAllBytesAsync(videoFile, new byte[40_000]);
        await File.WriteAllBytesAsync(zipFile, new byte[20_000]);
        await File.WriteAllBytesAsync(csFile, new byte[10_000]);

        var progressUpdates = new List<StorageScanProgress>();
        var progress = new SynchronousProgress<StorageScanProgress>(p => progressUpdates.Add(p));

        var tree = await StorageTreemapService.ScanDirectoryTreeAsync(root, progress);

        Assert.Equal(70_000, tree.Size);
        Assert.Equal(3, tree.FileCount);
        Assert.Equal(2, tree.FolderCount);
        Assert.NotEmpty(progressUpdates);

        var stats = StorageTreemapService.ComputeExtensionStats(tree);
        Assert.Equal(3, stats.Count);
        Assert.Equal(".mp4", stats[0].Extension);
        Assert.Equal(40_000, stats[0].TotalBytes);
        Assert.Equal(StorageCategory.VideoAudio, stats[0].Category);

        var rects = StorageTreemapService.BuildSquarifiedTreemapLayout(tree, 800, 500, maxDepth: 2);
        Assert.NotEmpty(rects);
        Assert.Contains(rects, r => r.Node.Name == "movie.mp4" && !r.IsGroupContainer);

        var zipNode = tree.Children
            .First(c => c.Name == "Media")
            .Children.First(c => c.Name == "backup.zip");

        StorageTreemapService.RemoveNodeAndRecalculate(zipNode);
        Assert.Equal(50_000, tree.Size);
        Assert.Equal(2, tree.FileCount);
    }

    [Fact]
    public async Task StorageTreemap_CustomCategoryColors_And_RefreshNodeColors_Work()
    {
        string root = Path.Combine(_tempDir, "treemap_colors");
        Directory.CreateDirectory(root);
        string videoFile = Path.Combine(root, "clip.mp4");
        await File.WriteAllBytesAsync(videoFile, new byte[8_192]);

        var settings = SettingsViewModel.Current;
        string originalColor = settings.TreemapVideoMediaColor;
        try
        {
            settings.SetTreemapCategoryColor(StorageCategory.VideoAudio, "#123456");
            Assert.Equal("#123456", StorageTreemapService.GetCategoryColorHex(StorageCategory.VideoAudio));

            var tree = await StorageTreemapService.ScanDirectoryTreeAsync(root);
            var mp4Node = tree.Children.Single(c => c.Name == "clip.mp4");
            Assert.Equal("#123456", mp4Node.ColorHex);

            settings.SetTreemapCategoryColor(StorageCategory.VideoAudio, "#654321");
            StorageTreemapService.RefreshNodeColors(tree);
            Assert.Equal("#654321", mp4Node.ColorHex);
        }
        finally
        {
            settings.SetTreemapCategoryColor(StorageCategory.VideoAudio, originalColor);
        }
    }

    [Fact]
    public void SettingsViewModel_ChildWindowFontSizes_And_GlobalFontSizeOption_Work()
    {
        var settings = SettingsViewModel.Current;
        bool origGlobalFlag = settings.UseGlobalFontSize;
        double origGlobalSize = settings.GlobalFontSize;
        double origAnalyzerSize = settings.StorageAnalyzerFontSize;
        double origQuickViewerSize = settings.QuickViewerFontSize;

        try
        {
            settings.UseGlobalFontSize = false;
            settings.StorageAnalyzerFontSize = 16.5;
            settings.QuickViewerFontSize = 14.0;

            Assert.Equal(16.5, settings.GetChildWindowFontSize(ChildWindowFontTarget.StorageAnalyzer));
            Assert.Equal(14.0, settings.GetChildWindowFontSize(ChildWindowFontTarget.QuickViewer));

            settings.UseGlobalFontSize = true;
            settings.GlobalFontSize = 18.0;

            Assert.Equal(18.0, settings.GetChildWindowFontSize(ChildWindowFontTarget.StorageAnalyzer));
            Assert.Equal(18.0, settings.GetChildWindowFontSize(ChildWindowFontTarget.QuickViewer));
            Assert.Equal(18.0, settings.GetChildWindowFontSize(ChildWindowFontTarget.AboutWindow));
        }
        finally
        {
            settings.UseGlobalFontSize = origGlobalFlag;
            settings.GlobalFontSize = origGlobalSize;
            settings.StorageAnalyzerFontSize = origAnalyzerSize;
            settings.QuickViewerFontSize = origQuickViewerSize;
        }
    }

    [Fact]
    public async Task StorageTreemapService_LocalizesCategoryNames_And_NoExtensionLabel_InEnglishAndGerman()
    {
        string root = Path.Combine(_tempDir, "treemap_loc");
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, "README"), new byte[1_024]);

        var loc = LocalizationService.Instance;
        string prevLang = loc.EffectiveLanguage;
        try
        {
            loc.SetLanguage("en");
            Assert.Equal("Video & Audio", StorageTreemapService.GetCategoryDisplayName(StorageCategory.VideoAudio));
            Assert.Equal("Virtual Disks, DB & System", StorageTreemapService.GetCategoryDisplayName(StorageCategory.VirtualDiskSystem));
            Assert.Contains("Disk Space Analyzer & Graphical Treemap", LocalizationService.T("Treemap.WindowTitle"));

            var enTree = await StorageTreemapService.ScanDirectoryTreeAsync(root);
            var enFile = enTree.Children.Single(c => c.Name == "README");
            Assert.Equal("<no extension>", enFile.Extension);

            var enStats = StorageTreemapService.ComputeExtensionStats(enTree);
            Assert.Single(enStats);
            Assert.Equal("<no extension>", enStats[0].Extension);

            loc.SetLanguage("de");
            Assert.Equal("Video & Audio", StorageTreemapService.GetCategoryDisplayName(StorageCategory.VideoAudio));
            Assert.Equal("Virtuelle Disks, DB & System", StorageTreemapService.GetCategoryDisplayName(StorageCategory.VirtualDiskSystem));
            Assert.Contains("Speicherplatz-Analysator", LocalizationService.T("Treemap.WindowTitle"));

            var deTree = await StorageTreemapService.ScanDirectoryTreeAsync(root);
            var deFile = deTree.Children.Single(c => c.Name == "README");
            Assert.Equal("<ohne Endung>", deFile.Extension);
        }
        finally
        {
            loc.SetLanguage(prevLang);
        }
    }

    [Fact]
    public void SettingsViewModel_ColorPickerTargets_SupportAllTenColorSettings_RgbUpdate_And_ResetToDefault()
    {
        var vm = new SettingsViewModel();
        var allTargets = Enum.GetValues<ColorSettingTarget>();
        Assert.Equal(10, allTargets.Length);

        foreach (var target in allTargets)
        {
            string defaultHex = SettingsViewModel.GetDefaultColorSettingHex(target);
            Assert.Equal(defaultHex, vm.GetColorSettingHex(target));

            vm.SetColorSettingRgb(target, 0x1A, 0xBC, 0x9C);
            Assert.Equal("#1ABC9C", vm.GetColorSettingHex(target));

            Assert.True(AppSettings.TryParseHexRgb(vm.GetColorSettingHex(target), out byte r, out byte g, out byte b));
            Assert.Equal((byte)0x1A, r);
            Assert.Equal((byte)0xBC, g);
            Assert.Equal((byte)0x9C, b);

            vm.ResetColorSettingToDefault(target);
            Assert.Equal(defaultHex, vm.GetColorSettingHex(target));
        }
    }

    [Fact]
    public void FileDiffService_ComputeDiff_DetectsModifiedAddedAndRemovedLines_And_MergesHunks()
    {
        string leftText = "Line 1\nLine 2 Old\nLine 3\nLine 4 Removed";
        string rightText = "Line 1\nLine 2 New\nLine 3\nLine 5 Added";

        var result = FileDiffService.ComputeDiff(leftText, rightText);
        Assert.Equal(2, result.UnchangedCount);
        Assert.True(result.ModifiedCount >= 1);
        Assert.True(result.Hunks.Count >= 1);

        // Merge first hunk from Left to Right
        var merged = FileDiffService.ApplyHunkLeftToRight(result, result.Hunks[0].HunkIndex);
        Assert.Contains("Line 2 Old", merged.RightRawText);
    }

    [Fact]
    public async Task ChecksumBatchService_ComputesHashes_MatchesTarget_And_VerifiesManifest()
    {
        string file1 = Path.Combine(_tempDir, "hash_test1.txt");
        string file2 = Path.Combine(_tempDir, "hash_test2.txt");
        await File.WriteAllTextAsync(file1, "Hello Modern Explorer Checksum");
        await File.WriteAllTextAsync(file2, "Second file payload");

        var entries = await ChecksumBatchService.ComputeChecksumsAsync([file1, file2]);
        Assert.Equal(2, entries.Count);
        Assert.NotEmpty(entries[0].Sha256);
        Assert.NotEmpty(entries[0].Sha512);
        Assert.NotEmpty(entries[0].Md5);
        Assert.NotEmpty(entries[0].Crc32);

        Assert.True(entries[0].MatchesExpectedHash(entries[0].Sha256));
        Assert.True(entries[0].MatchesExpectedHash(entries[0].Crc32.ToLowerInvariant()));

        string manifestPath = Path.Combine(_tempDir, "checksums.sha256");
        await File.WriteAllTextAsync(manifestPath, ChecksumBatchService.GenerateManifestContent(entries, ChecksumAlgorithm.Sha256));

        var verifyResults = await ChecksumBatchService.VerifyManifestFileAsync(manifestPath);
        Assert.Equal(2, verifyResults.Count);
        Assert.All(verifyResults, r => Assert.Equal(ChecksumVerifyStatus.VerifiedOk, r.Status));
    }

    [Fact]
    public async Task FileSplitAndShredService_SplitsRejoinsAndSecurelyShredsFiles()
    {
        string sourceFile = Path.Combine(_tempDir, "large_payload.bin");
        byte[] payload = new byte[25000];
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(sourceFile, payload);

        string splitDir = Path.Combine(_tempDir, "split_out");
        var splitResult = await FileSplitAndShredService.SplitFileAsync(sourceFile, splitDir, 10000);
        Assert.Equal(3, splitResult.CreatedPartPaths.Count);
        Assert.True(File.Exists(splitResult.ManifestPath));

        string joinPath = Path.Combine(_tempDir, "rejoined_payload.bin");
        await FileSplitAndShredService.JoinFilePartsAsync(splitResult.CreatedPartPaths[0], joinPath);
        byte[] rejoinedBytes = await File.ReadAllBytesAsync(joinPath);
        Assert.Equal(payload, rejoinedBytes);

        // Secure shred
        var shredRes = await FileSplitAndShredService.SecureShredAsync([joinPath], passes: 1);
        Assert.Equal(1, shredRes.ShreddedFileCount);
        Assert.False(File.Exists(joinPath));
    }

    [Fact]
    public void FileTagService_SetsReadsRemovesTags_And_MatchesFilterQuery()
    {
        string customTagsFile = Path.Combine(_tempDir, "test-file-tags.json");
        FileTagService.UseCustomStorePath(customTagsFile);
        try
        {
            string sampleFile = Path.Combine(_tempDir, "tagged_report.pdf");

            FileTagService.SetTag([sampleFile], FileTagColor.Red, "Urgent tax document");
            var entry = FileTagService.GetTag(sampleFile);
            Assert.NotNull(entry);
            Assert.Equal(FileTagColor.Red, entry.Color);
            Assert.Equal("Urgent tax document", entry.Note);
            Assert.Equal("#EF4444", FileTagService.GetColorHex(entry.Color));

            Assert.True(FileTagService.MatchesTagFilter(sampleFile, "tag:rot"));
            Assert.True(FileTagService.MatchesTagFilter(sampleFile, "tag:red"));
            Assert.True(FileTagService.MatchesTagFilter(sampleFile, "tag:*"));
            Assert.True(FileTagService.MatchesTagFilter(sampleFile, "note:tax"));
            Assert.False(FileTagService.MatchesTagFilter(sampleFile, "tag:blue"));

            FileTagService.ClearTag([sampleFile]);
            Assert.Null(FileTagService.GetTag(sampleFile));
        }
        finally
        {
            FileTagService.UseCustomStorePath(null);
        }
    }

    [Fact]
    public void WorkspaceProfileService_SavesLoadsAndDeletesDualPaneProfiles()
    {
        string customFile = Path.Combine(_tempDir, "test-workspaces.json");
        WorkspaceProfileService.UseCustomStorePath(customFile);
        try
        {
            var profile = WorkspaceProfileService.CaptureProfile(
                name: "Web Dev Layout",
                description: "Dual-Pane Test",
                isDualMode: true,
                splitRatio: 0.5,
                activeSide: PanelSide.Left,
                leftState: new PanelState { ActiveIndex = 0, Tabs = [new TabState { Path = _tempDir, Pinned = true }] },
                rightState: new PanelState { ActiveIndex = 0, Tabs = [new TabState { Path = _tempDir }] });

            var loaded = WorkspaceProfileService.GetProfiles();
            Assert.Single(loaded);
            Assert.Equal("Web Dev Layout", loaded[0].Name);
            Assert.Single(loaded[0].LeftPanel.Tabs);

            Assert.True(WorkspaceProfileService.DeleteProfile(profile.Id));
            Assert.Empty(WorkspaceProfileService.GetProfiles());
        }
        finally
        {
            WorkspaceProfileService.UseCustomStorePath(null);
        }
    }

    [Fact]
    public void SmartExportService_FormatsPathsMarkdownCsvAndJson()
    {
        string sampleFile = Path.Combine(_tempDir, "export_demo.txt");
        File.WriteAllText(sampleFile, "12345");
        var entry = FileEntry.TryFromPath(sampleFile)!;
        Assert.NotNull(entry);

        string mdLinks = SmartExportService.FormatEntries([entry], SmartExportFormat.MarkdownFileLinks, _tempDir);
        Assert.Contains("[export_demo.txt](file:///", mdLinks);

        string csv = SmartExportService.FormatEntries([entry], SmartExportFormat.CsvTable, _tempDir);
        Assert.Contains("export_demo.txt", csv);

        string json = SmartExportService.FormatEntries([entry], SmartExportFormat.JsonArray, _tempDir);
        Assert.Contains("export_demo.txt", json);
    }

    [Fact]
    public void FileBasketService_AddsRemovesAndSummarizesVirtualCollection()
    {
        FileBasketService.Clear();
        try
        {
            string fileA = Path.Combine(_tempDir, "basket_a.txt");
            File.WriteAllText(fileA, "Basket item A");

            int added = FileBasketService.AddPaths([fileA]);
            Assert.Equal(1, added);
            Assert.Equal(1, FileBasketService.Count);
            Assert.True(FileBasketService.TotalFileBytes > 0);

            // Adding duplicate does not increase count
            Assert.Equal(0, FileBasketService.AddPaths([fileA]));
            Assert.True(FileBasketService.RemoveByPath(fileA));
            Assert.Equal(0, FileBasketService.Count);
        }
        finally
        {
            FileBasketService.Clear();
        }
    }

    [Fact]
    public void MetadataAndAttributeService_ReadsPngDimensions_ModifiesTimestamps_And_SupportsBatchRenamePlaceholders()
    {
        string pngPath = Path.Combine(_tempDir, "sample_image.png");
        // Minimal valid 24-byte PNG header with IHDR width = 320, height = 240
        byte[] pngBytes =
        [
            0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0x00, 0x00, 0x01, 0x40, // 320
            0x00, 0x00, 0x00, 0xF0  // 240
        ];
        File.WriteAllBytes(pngPath, pngBytes);

        var info = MetadataAndAttributeService.InspectMediaMetadata(pngPath);
        Assert.Equal(320, info.Width);
        Assert.Equal(240, info.Height);
        Assert.Equal("320x240", info.ResolutionText);

        // Test BatchRenameService with [Width], [Height], [Resolution]
        var fileEntry = FileEntry.TryFromPath(pngPath)!;
        var preview = BatchRenameService.GeneratePreview([fileEntry], new BatchRenameOptions
        {
            NamePattern = "wallpaper_[Resolution]_[Width]w",
            ExtensionPattern = "[E]"
        });
        Assert.Single(preview);
        Assert.Equal("wallpaper_320x240_320w.png", preview[0].NewName);

        // Shift timestamps by +2 hours
        DateTime beforeMod = File.GetLastWriteTime(pngPath);
        MetadataAndAttributeService.ApplyBatchChanges([pngPath], new BatchAttributeTimestampOptions
        {
            ShiftTimestamps = true,
            ShiftOffset = TimeSpan.FromHours(2)
        });
        DateTime afterMod = File.GetLastWriteTime(pngPath);
        Assert.True((afterMod - beforeMod).TotalHours >= 1.9);
    }

    [Fact]
    public async Task QuickCommandRunnerService_ExecutesInlineCommand_And_HandlesCdNavigation()
    {
        var runner = new QuickCommandRunnerService();
        var subDir = Path.Combine(_tempDir, "cmd_sub");
        Directory.CreateDirectory(subDir);

        var cdResult = await runner.ExecuteAsync("cd cmd_sub", _tempDir);
        Assert.Equal(0, cdResult.ExitCode);
        Assert.Equal(Path.GetFullPath(subDir), cdResult.NavigateToDirectory);

        var echoResult = await runner.ExecuteAsync("echo HelloModernConsole", _tempDir);
        Assert.Contains("HelloModernConsole", echoResult.OutputText);
    }

    [Fact]
    public async Task StorageTreemapService_SupportsAgeHeatmapColors_TopLargestFiles_And_StorageReportExport()
    {
        string bigFile = Path.Combine(_tempDir, "biggest_file.dat");
        await File.WriteAllBytesAsync(bigFile, new byte[4096]);

        var root = await StorageTreemapService.ScanDirectoryTreeAsync(_tempDir);

        var topFiles = StorageTreemapService.GetTopLargestFiles(root, 10);
        Assert.NotEmpty(topFiles);
        Assert.Equal("biggest_file.dat", topFiles[0].Name);

        string colorRecent = StorageTreemapService.GetAgeHeatmapColorHex(DateTime.Now.AddDays(-1));
        string colorOld = StorageTreemapService.GetAgeHeatmapColorHex(DateTime.Now.AddYears(-5));
        Assert.NotEqual(colorRecent, colorOld);

        string mdReport = StorageTreemapService.ExportStorageReport(root, markdownFormat: true);
        Assert.Contains("biggest_file.dat", mdReport);
        string csvReport = StorageTreemapService.ExportStorageReport(root, markdownFormat: false);
        Assert.Contains("biggest_file.dat", csvReport);
    }
}

