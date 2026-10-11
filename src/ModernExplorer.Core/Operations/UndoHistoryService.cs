using System.IO;

namespace ModernExplorer.Core.Operations;

/// <summary>
/// Kind of reversible file system operation stored in the Undo history stack.
/// </summary>
public enum UndoActionKind
{
    Rename = 0,
    BatchRename = 1,
    Copy = 2,
    Move = 3,
    CreateFile = 4,
    CreateFolder = 5,
}

/// <summary>
/// Represents a single reversible operation in <see cref="UndoHistoryService"/>.
/// </summary>
public sealed record UndoEntry(
    UndoActionKind Kind,
    string Description,
    DateTime TimestampUtc,
    IReadOnlyList<(string SourcePath, string TargetPath)> PathPairs);

/// <summary>
/// Result of executing an Undo (Ctrl+Z) operation.
/// </summary>
public sealed record UndoResult(
    bool Success,
    string Summary,
    IReadOnlyList<string> Errors);

/// <summary>
/// Tracks reversible file operations (rename, batch-rename, copy, move, create file/folder)
/// and executes Ctrl+Z undo operations safely.
/// </summary>
public sealed class UndoHistoryService
{
    private const int MaxUndoDepth = 50;
    private readonly Stack<UndoEntry> _stack = new();
    private readonly object _sync = new();

    public event EventHandler? HistoryChanged;

    public bool CanUndo
    {
        get
        {
            lock (_sync)
                return _stack.Count > 0;
        }
    }

    public string? NextUndoDescription
    {
        get
        {
            lock (_sync)
                return _stack.TryPeek(out var top) ? top.Description : null;
        }
    }

    public IReadOnlyList<UndoEntry> GetEntriesSnapshot()
    {
        lock (_sync)
            return _stack.ToList();
    }

    public void RecordRename(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
            return;

        Push(new UndoEntry(
            UndoActionKind.Rename,
            $"Umbenennen: {Path.GetFileName(oldPath)} → {Path.GetFileName(newPath)}",
            DateTime.UtcNow,
            [(oldPath, newPath)]));
    }

    public void RecordBatchRename(IReadOnlyList<(string OldPath, string NewPath)> pairs)
    {
        if (pairs.Count == 0)
            return;

        Push(new UndoEntry(
            UndoActionKind.BatchRename,
            $"Mehrfach-Umbenennung ({pairs.Count} Elemente)",
            DateTime.UtcNow,
            pairs.ToList()));
    }

    public void RecordCopy(IReadOnlyList<(string SourcePath, string CreatedDestPath)> copiedPairs)
    {
        if (copiedPairs.Count == 0)
            return;

        Push(new UndoEntry(
            UndoActionKind.Copy,
            $"Kopieren ({copiedPairs.Count} Elemente)",
            DateTime.UtcNow,
            copiedPairs.ToList()));
    }

    public void RecordCopy(IReadOnlyList<string> createdDestPaths, string? description = null)
    {
        if (createdDestPaths.Count == 0)
            return;

        Push(new UndoEntry(
            UndoActionKind.Copy,
            description ?? $"Kopieren ({createdDestPaths.Count} Elemente)",
            DateTime.UtcNow,
            createdDestPaths.Select(p => ("", p)).ToList()));
    }

    public void RecordMove(IReadOnlyList<(string OriginalPath, string MovedDestPath)> movedPairs, string? description = null)
    {
        if (movedPairs.Count == 0)
            return;

        Push(new UndoEntry(
            UndoActionKind.Move,
            description ?? $"Verschieben ({movedPairs.Count} Elemente)",
            DateTime.UtcNow,
            movedPairs.ToList()));
    }

    public void RecordCreate(string createdPath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(createdPath))
            return;

        Push(new UndoEntry(
            isDirectory ? UndoActionKind.CreateFolder : UndoActionKind.CreateFile,
            $"{(isDirectory ? "Neuer Ordner" : "Neue Datei")}: {Path.GetFileName(createdPath)}",
            DateTime.UtcNow,
            [("", createdPath)]));
    }

    public void RecordCreateFile(string createdPath) => RecordCreate(createdPath, isDirectory: false);

    public void RecordCreateFolder(string createdPath) => RecordCreate(createdPath, isDirectory: true);

    public UndoResult UndoLast()
    {
        UndoEntry? entry;
        lock (_sync)
        {
            if (!_stack.TryPop(out entry))
                return new UndoResult(false, "Keine Aktion zum Rückgängigmachen vorhanden.", []);
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);

        var errors = new List<string>();
        int reversedCount = 0;

        switch (entry.Kind)
        {
            case UndoActionKind.Rename:
            case UndoActionKind.BatchRename:
            case UndoActionKind.Move:
            {
                // Reverse in two phases if BatchRename, or direct move from TargetPath back to SourcePath
                var stage = new List<(string OriginalSource, string TempPath, bool IsDir)>();
                foreach (var (originalSource, currentTarget) in entry.PathPairs.Reverse())
                {
                    try
                    {
                        bool isDir = Directory.Exists(currentTarget);
                        bool isFile = !isDir && File.Exists(currentTarget);
                        if (!isDir && !isFile)
                        {
                            errors.Add($"Nicht mehr vorhanden: {Path.GetFileName(currentTarget)}");
                            continue;
                        }

                        var parent = Path.GetDirectoryName(currentTarget) ?? "";
                        var temp = Path.Combine(parent, $".__me_undo_{Guid.NewGuid():N}");
                        if (isDir)
                            Directory.Move(currentTarget, temp);
                        else
                            File.Move(currentTarget, temp);

                        stage.Add((originalSource, temp, isDir));
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex.Message);
                    }
                }

                foreach (var (originalSource, temp, isDir) in stage)
                {
                    try
                    {
                        var origParent = Path.GetDirectoryName(originalSource);
                        if (!string.IsNullOrEmpty(origParent))
                            Directory.CreateDirectory(origParent);

                        if (isDir)
                            Directory.Move(temp, originalSource);
                        else
                            File.Move(temp, originalSource);

                        reversedCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex.Message);
                    }
                }
                break;
            }

            case UndoActionKind.Copy:
            case UndoActionKind.CreateFile:
            case UndoActionKind.CreateFolder:
            {
                foreach (var (_, createdPath) in entry.PathPairs.Reverse())
                {
                    try
                    {
                        if (File.Exists(createdPath))
                        {
                            File.Delete(createdPath);
                            reversedCount++;
                        }
                        else if (Directory.Exists(createdPath))
                        {
                            Directory.Delete(createdPath, recursive: entry.Kind == UndoActionKind.Copy);
                            reversedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(createdPath)}: {ex.Message}");
                    }
                }
                break;
            }
        }

        bool ok = reversedCount > 0 && errors.Count == 0;
        string summary = reversedCount > 0
            ? $"Rückgängig ausgeführt: {entry.Description}"
            : $"Rückgängig fehlgeschlagen: {entry.Description}";

        return new UndoResult(ok, summary, errors);
    }

    private void Push(UndoEntry entry)
    {
        lock (_sync)
        {
            if (_stack.Count >= MaxUndoDepth)
            {
                var items = _stack.Take(MaxUndoDepth - 1).Reverse().ToList();
                _stack.Clear();
                foreach (var item in items)
                    _stack.Push(item);
            }
            _stack.Push(entry);
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
