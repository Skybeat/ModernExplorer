using System.IO;
using ModernExplorer.Core.FileSystem;

namespace ModernExplorer.Core.Features;

/// <summary>
/// Global virtual staging basket (Drop-Basket / Sammelkorb) for collecting files and folders
/// across arbitrary directories and drives before running a unified batch action
/// (Copy Here, Move Here, Pack into Archive, Batch Rename, or Export List).
/// </summary>
public static class FileBasketService
{
    private static readonly object SyncLock = new();
    private static readonly List<FileEntry> BasketItems = [];

    public static event EventHandler? BasketChanged;

    public static int Count
    {
        get
        {
            lock (SyncLock) return BasketItems.Count;
        }
    }

    public static long TotalFileBytes
    {
        get
        {
            lock (SyncLock)
            {
                return BasketItems.Where(e => !e.IsDirectory).Sum(e => e.Size);
            }
        }
    }

    public static IReadOnlyList<FileEntry> GetItems()
    {
        lock (SyncLock)
        {
            return BasketItems.ToList();
        }
    }

    public static IReadOnlyList<string> GetPaths()
    {
        lock (SyncLock)
        {
            return BasketItems.Select(e => e.FullPath).ToList();
        }
    }

    public static int AddEntries(IEnumerable<FileEntry> entries)
    {
        int added = 0;
        lock (SyncLock)
        {
            foreach (var entry in entries)
            {
                if (entry is null || entry.IsParentLink || string.IsNullOrWhiteSpace(entry.FullPath))
                    continue;

                if (BasketItems.Any(x => string.Equals(x.FullPath, entry.FullPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                BasketItems.Add(entry);
                added++;
            }
        }

        if (added > 0)
            BasketChanged?.Invoke(null, EventArgs.Empty);
        return added;
    }

    public static int AddPaths(IEnumerable<string> paths)
    {
        var entries = new List<FileEntry>();
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var entry = FileEntry.TryFromPath(p);
            if (entry is not null)
                entries.Add(entry);
        }
        return AddEntries(entries);
    }

    public static bool RemoveByPath(string fullPath)
    {
        bool removed;
        lock (SyncLock)
        {
            removed = BasketItems.RemoveAll(x => string.Equals(x.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)) > 0;
        }
        if (removed)
            BasketChanged?.Invoke(null, EventArgs.Empty);
        return removed;
    }

    public static void Clear()
    {
        bool hadItems;
        lock (SyncLock)
        {
            hadItems = BasketItems.Count > 0;
            BasketItems.Clear();
        }
        if (hadItems)
            BasketChanged?.Invoke(null, EventArgs.Empty);
    }
}
