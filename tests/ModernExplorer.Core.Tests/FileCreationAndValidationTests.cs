using System.Text;
using ModernExplorer.Core.FileSystem;

namespace ModernExplorer.Core.Tests;

public sealed class FileCreationAndValidationTests : IDisposable
{
    private readonly string _tempDir;

    public FileCreationAndValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ModernExplorer_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("LPT1.log")]
    [InlineData("datei?.txt")]
    [InlineData("a:b.txt")]
    [InlineData("TrailingDot.")]
    [InlineData("..")]
    [InlineData("")]
    public void NameValidator_RejectsInvalidAndReservedNames(string invalidName)
    {
        Assert.NotNull(NameValidator.Validate(invalidName));
    }

    [Theory]
    [InlineData("Bericht 2026.txt")]
    [InlineData("Übersicht_äöüß_🚀.md")]
    [InlineData(".gitignore")]
    public void NameValidator_AcceptsValidUnicodeNames(string validName)
    {
        Assert.Null(NameValidator.Validate(NameValidator.Normalize(validName)));
    }

    [Fact]
    public void NameValidator_WithSuffix_FormatsCollisionsProperly()
    {
        Assert.Equal("Bericht (2).txt", NameValidator.WithSuffix("Bericht.txt", 2));
        Assert.Equal("Bericht (3).txt", NameValidator.WithSuffix("Bericht (2).txt", 3));
        Assert.Equal(".gitignore (2)", NameValidator.WithSuffix(".gitignore", 2));
    }

    [Fact]
    public async Task FileCreationService_CreatesFileWithRenderedTemplate_AndRejectsCollisionUnlessAutoRename()
    {
        var store = new TemplateStore(_tempDir);
        store.Load(null);
        var service = new FileCreationService(store);
        var mdTemplate = store.Templates.First(t => t.Id == "markdown");

        var r1 = await service.CreateFileAsync(_tempDir, "Projekt.md", mdTemplate, autoRename: false);
        Assert.True(r1.Success);
        Assert.NotNull(r1.FullPath);
        Assert.Contains("# Projekt", await File.ReadAllTextAsync(r1.FullPath!, Encoding.UTF8));

        // Collision without autoRename fails gracefully without overwriting the existing file
        var r2 = await service.CreateFileAsync(_tempDir, "Projekt.md", mdTemplate, autoRename: false);
        Assert.False(r2.Success);

        // With autoRename enabled, "Projekt (2).md" is created
        var r3 = await service.CreateFileAsync(_tempDir, "Projekt.md", mdTemplate, autoRename: true);
        Assert.True(r3.Success);
        Assert.EndsWith("Projekt (2).md", r3.FullPath!);
    }

    [Fact]
    public void FileCreationService_CreatesNestedFolders()
    {
        var store = new TemplateStore(_tempDir);
        store.Load(null);
        var service = new FileCreationService(store);

        var res = service.CreateFolder(_tempDir, @"src\components\ui");
        Assert.True(res.Success);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "src", "components", "ui")));
        Assert.Equal(Path.Combine(_tempDir, "src"), res.FullPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

