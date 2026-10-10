using System.IO;

namespace ModernExplorer.Core.FileSystem;

/// <summary>Immutable snapshot of a directory entry.</summary>
public sealed record FileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime LastWriteTime,
    FileAttributes Attributes)
{
    public bool IsHidden => (Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public string Extension => IsDirectory ? "" : Path.GetExtension(Name);

    /// <summary>Special entry ".." pointing to the parent directory.</summary>
    public bool IsParentLink { get; init; }

    public static FileEntry ParentLink(string parentPath) =>
        new("..", parentPath, true, 0, default, FileAttributes.Directory) { IsParentLink = true };

    /// <summary>Reads a single entry; returns <c>null</c> if it does not (or no longer) exist.</summary>
    public static FileEntry? TryFromPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var d = new DirectoryInfo(path);
                return new FileEntry(d.Name, d.FullName, true, 0, d.LastWriteTime, d.Attributes);
            }
            if (File.Exists(path))
            {
                var f = new FileInfo(path);
                return new FileEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime, f.Attributes);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
