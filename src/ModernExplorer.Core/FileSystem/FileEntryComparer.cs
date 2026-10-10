using System.Runtime.InteropServices;
using ModernExplorer.Core.Session;

namespace ModernExplorer.Core.FileSystem;

/// <summary>Natural string sorting matching Windows Explorer ("File2" before "File10").</summary>
public sealed partial class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return StrCmpLogicalW(x, y);
    }
}

/// <summary>Comparer for file entries according to <see cref="SortSpec"/>.</summary>
public sealed class FileEntryComparer(SortSpec spec) : IComparer<FileEntry>
{
    public int Compare(FileEntry? a, FileEntry? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        // ".." always comes first, regardless of sort direction
        if (a.IsParentLink != b.IsParentLink) return a.IsParentLink ? -1 : 1;

        if (spec.FoldersFirst && a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;

        int r = spec.Column switch
        {
            SortColumn.Size => a.IsDirectory && b.IsDirectory ? 0 : a.Size.CompareTo(b.Size),
            SortColumn.Modified => a.LastWriteTime.CompareTo(b.LastWriteTime),
            SortColumn.Type => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
            _ => 0,
        };
        if (r == 0) r = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        return spec.Descending ? -r : r;
    }
}

