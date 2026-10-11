using System.IO;
using System.Text;
using ModernExplorer.Core.Archive;
using ModernExplorer.Core.Favorites;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Operations;
using ModernExplorer.Core.Remote;
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
}
