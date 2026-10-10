using System.Text;
using ModernExplorer.Core.Viewer;

namespace ModernExplorer.Core.Tests;

public sealed class FileContentServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FileContentServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ModernExplorer_Viewer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task LoadPreview_AndSaveAtomic_PreserveEncodingAndNewLines()
    {
        var path = Path.Combine(_tempDir, "notes.md");
        var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllBytesAsync(path, [.. utf8Bom.GetPreamble(), .. utf8Bom.GetBytes("# Titel\nZeile 2\n")]);

        var doc = await FileContentService.LoadPreviewAsync(path);
        Assert.Equal(PreviewMode.Markdown, doc.Mode);
        Assert.Equal("\n", doc.NewLine);
        Assert.NotEmpty(doc.Encoding.GetPreamble());

        await FileContentService.SaveAtomicAsync(path, "# Geändert\r\nZeile 2\r\n", doc.Encoding, doc.NewLine);
        var raw = await File.ReadAllBytesAsync(path);
        Assert.Equal(0xEF, raw[0]); // Preserve BOM
        var text = utf8Bom.GetString(raw, 3, raw.Length - 3);
        Assert.DoesNotContain("\r\n", text); // Preserve LF
        Assert.Contains("# Geändert\nZeile 2\n", text);
    }

    [Fact]
    public async Task LoadPreview_DetectsBinaryAndProducesHexDump()
    {
        var path = Path.Combine(_tempDir, "sample.bin");
        await File.WriteAllBytesAsync(path, [0x4D, 0x5A, 0x00, 0x00, 0x50, 0x45, 0x00, 0x00]);

        var doc = await FileContentService.LoadPreviewAsync(path);
        Assert.Equal(PreviewMode.BinaryHex, doc.Mode);
        Assert.Contains("00000000  4D 5A 00 00", doc.Content);
    }

    [Fact]
    public void FilterLogLines_FiltersByLevelAndSearch()
    {
        const string log = "2026-10-10 INFO Start\n2026-10-10 ERROR Disk full\n2026-10-10 WARN Low memory\n";
        var errors = FileContentService.FilterLogLines(log, "ERROR", null);
        Assert.Contains("ERROR Disk full", errors);
        Assert.DoesNotContain("INFO Start", errors);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

