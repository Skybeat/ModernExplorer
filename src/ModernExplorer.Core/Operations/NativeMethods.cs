using System.Runtime.InteropServices;

namespace ModernExplorer.Core.Operations;

/// <summary>Native Windows P/Invoke declarations for file operations.</summary>
internal static partial class NativeMethods
{
    public const uint PROGRESS_CONTINUE = 0;
    public const uint PROGRESS_CANCEL = 1;
    public const uint COPY_FILE_FAIL_IF_EXISTS = 0x1;
    public const uint COPY_FILE_NO_BUFFERING = 0x1000;
    public const int ERROR_REQUEST_ABORTED = 1235;
    public const int ERROR_FILE_EXISTS = 80;
    public const int ERROR_ALREADY_EXISTS = 183;

    public delegate uint CopyProgressRoutine(
        long totalFileSize, long totalBytesTransferred, long streamSize, long streamBytesTransferred,
        uint streamNumber, uint callbackReason, IntPtr sourceFile, IntPtr destinationFile, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CopyFileExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CopyFileEx(string existingFileName, string newFileName,
        CopyProgressRoutine? progressRoutine, IntPtr data, ref int cancel, uint copyFlags);

    // ---- Recycle Bin via SHFileOperation ----
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    /// <summary>Moves a path to the system Trash / Recycle Bin on Windows, macOS, and Linux.</summary>
    public static void MoveToRecycleBin(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            };
            var rc = SHFileOperation(ref op);
            if (rc != 0) throw new IOException($"Papierkorb-Löschen fehlgeschlagen (Code 0x{rc:X}): {path}");
            if (op.fAnyOperationsAborted) throw new OperationCanceledException();
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var trashDir = Path.Combine(home, ".Trash");
            Directory.CreateDirectory(trashDir);
            var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var targetName = ModernExplorer.Core.FileSystem.NameValidator.MakeUnique(trashDir, fileName);
            var targetPath = Path.Combine(trashDir, targetName);
            MoveEntryCrossPlatform(path, targetPath);
            return;
        }

        // Linux / FreeDesktop.org Trash specification
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(xdgDataHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            xdgDataHome = Path.Combine(home, ".local", "share");
        }

        var trashFilesDir = Path.Combine(xdgDataHome, "Trash", "files");
        var trashInfoDir = Path.Combine(xdgDataHome, "Trash", "info");
        Directory.CreateDirectory(trashFilesDir);
        Directory.CreateDirectory(trashInfoDir);

        var baseName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var uniqueTrashName = ModernExplorer.Core.FileSystem.NameValidator.MakeUnique(trashFilesDir, baseName);
        var trashTarget = Path.Combine(trashFilesDir, uniqueTrashName);
        var trashInfoFile = Path.Combine(trashInfoDir, uniqueTrashName + ".trashinfo");

        var normalizedTrashPath = Path.GetFullPath(path).Replace('\\', '/');
        var infoContent = $"[Trash Info]\nPath={normalizedTrashPath}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n";
        File.WriteAllText(trashInfoFile, infoContent);
        MoveEntryCrossPlatform(path, trashTarget);
    }

    private static void MoveEntryCrossPlatform(string sourcePath, string targetPath)
    {
        if (Directory.Exists(sourcePath))
            Directory.Move(sourcePath, targetPath);
        else if (File.Exists(sourcePath))
            File.Move(sourcePath, targetPath);
    }
}

