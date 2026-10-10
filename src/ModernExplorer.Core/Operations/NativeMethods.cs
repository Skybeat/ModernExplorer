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

    /// <summary>Moves a path to the Windows Recycle Bin (without showing shell dialogs).</summary>
    public static void MoveToRecycleBin(string path)
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
    }
}

