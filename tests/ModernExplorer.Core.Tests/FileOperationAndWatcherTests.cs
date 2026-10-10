using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Operations;

namespace ModernExplorer.Core.Tests;

public sealed class FileOperationAndWatcherTests : IDisposable
{
    private readonly string _tempDir;

    public FileOperationAndWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ModernExplorer_Ops_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    private sealed class FixedResolver(ConflictChoice choice, bool applyAll = true) : IConflictResolver
    {
        public Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct) =>
            Task.FromResult(new ConflictDecision(choice, applyAll));
    }

    [Fact]
    public async Task FileOperationQueue_CopiesAndResolvesConflictWithAutoRename()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        var dstDir = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(dstDir);

        var srcFile = Path.Combine(srcDir, "daten.txt");
        await File.WriteAllTextAsync(srcFile, "Inhalt-Neu");
        await File.WriteAllTextAsync(Path.Combine(dstDir, "daten.txt"), "Inhalt-Alt");

        await using var queue = new FileOperationQueue(new FixedResolver(ConflictChoice.Rename));
        var handle = await queue.RunAsync(new FileOperation(OpKind.Copy, [srcFile], dstDir, new OperationOptions()));

        Assert.Equal(OperationStatus.Completed, handle.Status);
        Assert.Equal("Inhalt-Alt", await File.ReadAllTextAsync(Path.Combine(dstDir, "daten.txt")));
        Assert.Equal("Inhalt-Neu", await File.ReadAllTextAsync(Path.Combine(dstDir, "daten (2).txt")));
    }

    [Fact]
    public async Task FileOperationQueue_RenamesCaseOnlyAndDeletesPermanently()
    {
        var file = Path.Combine(_tempDir, "readme.txt");
        await File.WriteAllTextAsync(file, "abc");

        await using var queue = new FileOperationQueue(new FixedResolver(ConflictChoice.Overwrite));
        var renameHandle = await queue.RunAsync(new FileOperation(OpKind.Rename, [file], null, new OperationOptions())
        {
            NewName = "README.txt",
        });
        Assert.Equal(OperationStatus.Completed, renameHandle.Status);

        var renamedPath = Path.Combine(_tempDir, "README.txt");
        Assert.True(File.Exists(renamedPath));

        var delHandle = await queue.RunAsync(new FileOperation(OpKind.Delete, [renamedPath], null, new OperationOptions { Recycle = false }));
        Assert.Equal(OperationStatus.Completed, delHandle.Status);
        Assert.False(File.Exists(renamedPath));
    }

    [Fact]
    public void WatcherHub_SharesSingleWatcherAcrossSubscribers_AndReleasesOnDispose()
    {
        using var hub = new WatcherHub(TimeSpan.FromMilliseconds(50));
        Assert.Equal(0, hub.ActiveWatcherCount);

        var sub1 = hub.Subscribe(_tempDir, _ => { });
        var sub2 = hub.Subscribe(_tempDir, _ => { });
        Assert.Equal(1, hub.ActiveWatcherCount);

        sub1.Dispose();
        Assert.Equal(1, hub.ActiveWatcherCount);

        sub2.Dispose();
        Assert.Equal(0, hub.ActiveWatcherCount);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

