using System.ComponentModel;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using ModernExplorer.Core.FileSystem;

namespace ModernExplorer.Core.Operations;

/// <summary>
/// Executes a single file operation synchronously on the worker thread.
/// </summary>
internal sealed class FileOperationExecutor(IConflictResolver resolver)
{
    private const long NoBufferingThreshold = 256L * 1024 * 1024;

    private sealed record CopyItem(string Source, string Target, bool IsDirectory, long Size);

    public void Execute(OperationHandle h)
    {
        var op = h.Operation;
        switch (op.Kind)
        {
            case OpKind.Copy:
            case OpKind.Move:
                CopyOrMove(h, op);
                break;
            case OpKind.Delete:
                Delete(h, op);
                break;
            case OpKind.Rename:
                Rename(h, op);
                break;
        }
    }

    // ------------------------------------------------------------------ Copy / Move

    private void CopyOrMove(OperationHandle h, FileOperation op)
    {
        var dest = Path.GetFullPath(op.Destination ?? throw new ArgumentException("Ziel fehlt"));
        if (!Directory.Exists(dest)) throw new DirectoryNotFoundException($"Ziel existiert nicht: {dest}");

        foreach (var src in op.Sources)
        {
            var full = Path.GetFullPath(src).TrimEnd('\\');
            if (Directory.Exists(full) && (dest + "\\").StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"„{Path.GetFileName(full)}“ kann nicht in sich selbst kopiert/verschoben werden.");
        }

        var policy = op.Options.Conflicts;
        bool isMove = op.Kind == OpKind.Move;
        var sameVolume = op.Sources.All(s =>
            string.Equals(FileOperationQueue.VolumeKey(s), FileOperationQueue.VolumeKey(dest), StringComparison.OrdinalIgnoreCase));

        // Fast path: moving within the same volume => rename instead of copy.
        var remaining = new List<string>();
        if (isMove && sameVolume)
        {
            int done = 0;
            foreach (var src in op.Sources)
            {
                h.WaitIfPaused();
                var target = Path.Combine(dest, Path.GetFileName(src.TrimEnd('\\')));
                if (string.Equals(Path.GetFullPath(src).TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase)) { done++; continue; }

                if (!NameValidator.Exists(target))
                {
                    try
                    {
                        if (Directory.Exists(src)) Directory.Move(src, target); else File.Move(src, target);
                        h.CreatedPaths.Add(target);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        h.AddError($"{src}: {ex.Message}");
                    }
                }
                else
                {
                    remaining.Add(src); // Conflict => merge via copy path
                }
                h.Report(0, 0, ++done, op.Sources.Count, Path.GetFileName(src));
            }
            if (remaining.Count == 0) return;
        }
        else
        {
            remaining.AddRange(op.Sources);
        }

        // Build execution plan (total size for progress/ETA).
        var items = new List<CopyItem>();
        foreach (var src in remaining)
        {
            h.Token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(src.TrimEnd('\\'));
            var target = Path.Combine(dest, name);
            if (Directory.Exists(src))
            {
                items.Add(new CopyItem(src, target, true, 0));
                foreach (var it in EnumerateTree(src, target)) items.Add(it);
            }
            else if (File.Exists(src))
            {
                items.Add(new CopyItem(src, target, false, new FileInfo(src).Length));
            }
            else h.AddError($"Nicht gefunden: {src}");
        }

        long totalBytes = items.Sum(i => i.Size);
        int totalFiles = items.Count(i => !i.IsDirectory);
        long bytesDone = 0;
        int filesDone = 0;
        var skippedDirs = new List<string>();
        var copiedSources = new List<string>();

        foreach (var item in items)
        {
            h.WaitIfPaused();
            if (skippedDirs.Any(d => item.Source.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase))) continue;

            if (item.IsDirectory)
            {
                if (File.Exists(item.Target))
                {
                    h.AddError($"Ziel ist eine Datei: {item.Target}");
                    skippedDirs.Add(item.Source);
                    continue;
                }
                Directory.CreateDirectory(item.Target);
                if (IsTopLevel(item.Source, remaining)) h.CreatedPaths.Add(item.Target);
                continue;
            }

            var target = item.Target;
            bool overwrite = false;
            if (NameValidator.Exists(target))
            {
                var (choice, applyAll) = Resolve(h, policy, item.Source, target);
                if (applyAll) policy = choice switch
                {
                    ConflictChoice.Overwrite => ConflictPolicy.Overwrite,
                    ConflictChoice.Skip => ConflictPolicy.Skip,
                    ConflictChoice.Rename => ConflictPolicy.Rename,
                    ConflictChoice.KeepNewer => ConflictPolicy.KeepNewer,
                    _ => policy,
                };

                switch (choice)
                {
                    case ConflictChoice.Cancel: throw new OperationCanceledException();
                    case ConflictChoice.Skip:
                        bytesDone += item.Size; filesDone++;
                        continue;
                    case ConflictChoice.KeepNewer:
                        if (File.GetLastWriteTimeUtc(item.Source) <= File.GetLastWriteTimeUtc(target))
                        {
                            bytesDone += item.Size; filesDone++;
                            continue;
                        }
                        overwrite = true;
                        break;
                    case ConflictChoice.Rename:
                        target = Path.Combine(Path.GetDirectoryName(target)!, NameValidator.MakeUnique(Path.GetDirectoryName(target)!, Path.GetFileName(target)));
                        break;
                    case ConflictChoice.Overwrite:
                        if (Directory.Exists(target)) { h.AddError($"Ziel ist ein Ordner: {target}"); continue; }
                        overwrite = true;
                        break;
                }
            }

            var baseBytes = bytesDone;
            var name = Path.GetFileName(item.Source);
            try
            {
                CopyFile(item.Source, target, overwrite, item.Size, h,
                    transferred => h.Report(baseBytes + transferred, totalBytes, filesDone, totalFiles, name));
                copiedSources.Add(item.Source);
                if (IsTopLevel(item.Source, remaining)) h.CreatedPaths.Add(target);
                if (isMove) TryDeleteFile(item.Source, h);
            }
            catch (OperationCanceledException)
            {
                TryDeletePartial(target);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                h.AddError($"{item.Source}: {ex.Message}");
            }

            bytesDone = baseBytes + item.Size;
            filesDone++;
            h.Report(bytesDone, totalBytes, filesDone, totalFiles, name);
        }

        // Cross-volume move: remove empty source directories bottom-up.
        if (isMove)
        {
            foreach (var dir in items.Where(i => i.IsDirectory).Select(i => i.Source).OrderByDescending(s => s.Length))
            {
                try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        h.Report(totalBytes, totalBytes, totalFiles, totalFiles, "", force: true);
    }

    private static bool IsTopLevel(string source, List<string> tops) =>
        tops.Any(t => string.Equals(t.TrimEnd('\\'), source, StringComparison.OrdinalIgnoreCase));

    private (ConflictChoice, bool) Resolve(OperationHandle h, ConflictPolicy policy, string src, string dst)
    {
        switch (policy)
        {
            case ConflictPolicy.Overwrite: return (ConflictChoice.Overwrite, false);
            case ConflictPolicy.Skip: return (ConflictChoice.Skip, false);
            case ConflictPolicy.Rename: return (ConflictChoice.Rename, false);
            case ConflictPolicy.KeepNewer: return (ConflictChoice.KeepNewer, false);
        }

        var si = new FileInfo(src);
        var di = new FileInfo(dst);
        var info = new ConflictInfo(src, dst, si.Exists ? si.Length : 0, si.LastWriteTime,
                                    di.Exists ? di.Length : 0, di.Exists ? di.LastWriteTime : Directory.GetLastWriteTime(dst));
        var decision = resolver.ResolveAsync(info, h.Token).GetAwaiter().GetResult();
        return (decision.Choice, decision.ApplyToAll);
    }

    private static IEnumerable<CopyItem> EnumerateTree(string srcRoot, string dstRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };
        var e = new FileSystemEnumerable<CopyItem>(srcRoot,
            (ref FileSystemEntry entry) =>
            {
                var full = entry.ToFullPath();
                var rel = Path.GetRelativePath(srcRoot, full);
                return new CopyItem(full, Path.Combine(dstRoot, rel), entry.IsDirectory, entry.IsDirectory ? 0 : entry.Length);
            }, options)
        {
            // Do not recursively follow junctions/symlinks (avoid infinite loops).
            ShouldRecursePredicate = (ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0,
        };
        // Directories before their contents: sorting by path guarantees parents come first.
        return e.OrderBy(i => i.Source, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CopyFile(string src, string dst, bool overwrite, long size, OperationHandle h, Action<long> onProgress)
    {
        if (overwrite && File.Exists(dst))
        {
            var attrs = File.GetAttributes(dst);
            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden)) != 0)
                File.SetAttributes(dst, attrs & ~(FileAttributes.ReadOnly | FileAttributes.Hidden));
        }

        NativeMethods.CopyProgressRoutine cb = (total, transferred, _, _, _, _, _, _, _) =>
        {
            onProgress(transferred);
            try { h.WaitIfPaused(); }
            catch (OperationCanceledException) { return NativeMethods.PROGRESS_CANCEL; }
            return h.Token.IsCancellationRequested ? NativeMethods.PROGRESS_CANCEL : NativeMethods.PROGRESS_CONTINUE;
        };

        uint flags = overwrite ? 0 : NativeMethods.COPY_FILE_FAIL_IF_EXISTS;
        if (size > NoBufferingThreshold) flags |= NativeMethods.COPY_FILE_NO_BUFFERING;

        int cancel = 0;
        if (!NativeMethods.CopyFileEx(LongPath.Ensure(src), LongPath.Ensure(dst), cb, IntPtr.Zero, ref cancel, flags))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == NativeMethods.ERROR_REQUEST_ABORTED || h.Token.IsCancellationRequested)
                throw new OperationCanceledException();
            throw new IOException(new Win32Exception(err).Message, err);
        }
        GC.KeepAlive(cb);
    }

    private static void TryDeleteFile(string path, OperationHandle h)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            h.AddError($"Quelle konnte nicht entfernt werden: {path}: {ex.Message}");
        }
    }

    private static void TryDeletePartial(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    // ------------------------------------------------------------------ Delete

    private static void Delete(OperationHandle h, FileOperation op)
    {
        int done = 0;
        foreach (var src in op.Sources)
        {
            h.WaitIfPaused();
            var name = Path.GetFileName(src.TrimEnd('\\'));
            h.Report(0, 0, done, op.Sources.Count, name);
            try
            {
                if (op.Options.Recycle) NativeMethods.MoveToRecycleBin(src);
                else if (Directory.Exists(src)) DeleteTree(new DirectoryInfo(src), h);
                else if (File.Exists(src)) TryDeleteFile(src, h);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                h.AddError($"{src}: {ex.Message}");
            }
            h.Report(0, 0, ++done, op.Sources.Count, name, force: done == op.Sources.Count);
        }
    }

    private static void DeleteTree(DirectoryInfo dir, OperationHandle h)
    {
        // Only remove reparse points (junctions) as links; do not touch their target contents.
        if ((dir.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            foreach (var child in dir.EnumerateFileSystemInfos())
            {
                h.Token.ThrowIfCancellationRequested();
                if (child is DirectoryInfo d) DeleteTree(d, h);
                else TryDeleteFile(child.FullName, h);
            }
        }
        if ((dir.Attributes & FileAttributes.ReadOnly) != 0) dir.Attributes &= ~FileAttributes.ReadOnly;
        dir.Delete(false);
    }

    // ------------------------------------------------------------------ Rename

    private static void Rename(OperationHandle h, FileOperation op)
    {
        var src = op.Sources[0].TrimEnd('\\');
        var newName = NameValidator.Normalize(op.NewName ?? "");
        var err = NameValidator.Validate(newName);
        if (err is not null) throw new IOException(err);

        var dir = Path.GetDirectoryName(src)!;
        var target = Path.Combine(dir, newName);
        bool caseOnly = string.Equals(src, target, StringComparison.OrdinalIgnoreCase);
        if (src == target) return;
        if (!caseOnly && NameValidator.Exists(target)) throw new IOException($"„{newName}“ existiert bereits.");

        if (Directory.Exists(src))
        {
            if (caseOnly)
            {
                var tmp = Path.Combine(dir, $".~rename-{Guid.NewGuid():N}");
                Directory.Move(src, tmp);
                Directory.Move(tmp, target);
            }
            else Directory.Move(src, target);
        }
        else File.Move(src, target);

        h.CreatedPaths.Add(target);
    }
}

internal static class LongPath
{
    public static string Ensure(string path)
    {
        if (path.Length < 248 || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }
}

