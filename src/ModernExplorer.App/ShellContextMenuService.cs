using System.Runtime.InteropServices;
using System.Text;

namespace ModernExplorer.App;

/// <summary>
/// Displays the original, native Windows Explorer context menu (<c>IContextMenu</c> / <c>IContextMenu2</c> / <c>IContextMenu3</c>)
/// for one or more files/folders — including "Open with", "Send to", shell extensions (7-Zip, Git, VS Code, etc.),
/// and "Properties".
/// </summary>
public static class ShellContextMenuService
{
    private const uint FirstCmdId = 0x0001;
    private const uint LastCmdId = 0x7FFF;

    private const uint CMF_NORMAL = 0x00000000;
    private const uint CMF_EXPLORE = 0x00000004;
    private const uint CMF_CANRENAME = 0x00000010;
    private const uint CMF_EXTENDEDVERBS = 0x00000100;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    private const uint GCS_VERBW = 0x00000004;
    private const uint CMIC_MASK_UNICODE = 0x00004000;
    private const uint CMIC_MASK_PTINVOKE = 0x20000000;
    private const int SW_SHOWNORMAL = 1;

    private const uint WM_DRAWITEM = 0x002B;
    private const uint WM_MEASUREITEM = 0x002C;
    private const uint WM_INITMENUPOPUP = 0x0117;
    private const uint WM_MENUCHAR = 0x0120;

    private static Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static Guid IID_IContextMenu2 = new("000214F4-0000-0000-C000-000000000046");
    private static Guid IID_IContextMenu3 = new("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");

    private static IContextMenu2? _activeMenu2;
    private static IContextMenu3? _activeMenu3;
    private static readonly SUBCLASSPROC SubclassDelegate = MenuSubclassProc;

    public sealed record ContextMenuResult(bool Handled, string? SelectedVerb);

    public static ContextMenuResult ShowForPaths(
        IntPtr hwnd,
        IReadOnlyList<string> fullPaths,
        bool isShiftPressed)
    {
        if (hwnd == IntPtr.Zero || fullPaths.Count == 0)
            return new ContextMenuResult(false, null);

        var pidls = new List<IntPtr>(fullPaths.Count);
        IntPtr parentFolderPtr = IntPtr.Zero;
        IntPtr contextMenuPtr = IntPtr.Zero;
        IntPtr contextMenu2Ptr = IntPtr.Zero;
        IntPtr contextMenu3Ptr = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;

        try
        {
            foreach (var rawPath in fullPaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath)) continue;
                var normPath = Path.GetFullPath(rawPath);
                if (SHParseDisplayName(normPath, IntPtr.Zero, out var pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
                    pidls.Add(pidl);
            }

            if (pidls.Count == 0)
                return new ContextMenuResult(false, null);

            int hr = SHBindToParent(pidls[0], ref IID_IShellFolder, out parentFolderPtr, out _);
            if (hr < 0 || parentFolderPtr == IntPtr.Zero)
                return new ContextMenuResult(false, null);

            var shellFolder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(parentFolderPtr, typeof(IShellFolder));

            var childPidls = new IntPtr[pidls.Count];
            for (int i = 0; i < pidls.Count; i++)
                childPidls[i] = ILFindLastID(pidls[i]);

            uint rgfReserved = 0;
            hr = shellFolder.GetUIObjectOf(
                hwnd,
                (uint)childPidls.Length,
                childPidls,
                ref IID_IContextMenu,
                ref rgfReserved,
                out contextMenuPtr);

            if (hr < 0 || contextMenuPtr == IntPtr.Zero)
                return new ContextMenuResult(false, null);

            var contextMenu = (IContextMenu)Marshal.GetTypedObjectForIUnknown(contextMenuPtr, typeof(IContextMenu));

            if (Marshal.QueryInterface(contextMenuPtr, in IID_IContextMenu3, out contextMenu3Ptr) >= 0 && contextMenu3Ptr != IntPtr.Zero)
                _activeMenu3 = (IContextMenu3)Marshal.GetTypedObjectForIUnknown(contextMenu3Ptr, typeof(IContextMenu3));

            if (Marshal.QueryInterface(contextMenuPtr, in IID_IContextMenu2, out contextMenu2Ptr) >= 0 && contextMenu2Ptr != IntPtr.Zero)
                _activeMenu2 = (IContextMenu2)Marshal.GetTypedObjectForIUnknown(contextMenu2Ptr, typeof(IContextMenu2));

            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
                return new ContextMenuResult(false, null);

            uint flags = CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME;
            if (isShiftPressed)
                flags |= CMF_EXTENDEDVERBS;

            hr = contextMenu.QueryContextMenu(hMenu, 0, FirstCmdId, LastCmdId, flags);
            if (hr < 0)
                return new ContextMenuResult(false, null);

            GetCursorPos(out var pt);

            bool subclassed = SetWindowSubclass(hwnd, SubclassDelegate, 0x4D45, IntPtr.Zero);
            uint selectedCmd;
            try
            {
                selectedCmd = TrackPopupMenuEx(
                    hMenu,
                    TPM_LEFTALIGN | TPM_RETURNCMD | TPM_RIGHTBUTTON,
                    pt.X,
                    pt.Y,
                    hwnd,
                    IntPtr.Zero);
            }
            finally
            {
                if (subclassed)
                    RemoveWindowSubclass(hwnd, SubclassDelegate, 0x4D45);
                _activeMenu2 = null;
                _activeMenu3 = null;
            }

            if (selectedCmd < FirstCmdId || selectedCmd > LastCmdId)
                return new ContextMenuResult(true, null);

            uint cmdOffset = selectedCmd - FirstCmdId;
            string? verb = TryGetVerb(contextMenu, cmdOffset);

            // "rename" and "open" (for single folders) can be handled directly by the calling explorer
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase))
                return new ContextMenuResult(true, "rename");

            if (fullPaths.Count == 1 &&
                Directory.Exists(fullPaths[0]) &&
                (string.Equals(verb, "open", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(verb, "explore", StringComparison.OrdinalIgnoreCase)))
            {
                return new ContextMenuResult(true, "open_folder");
            }

            string? workingDir = Path.GetDirectoryName(fullPaths[0]);
            var invoke = new CMINVOKECOMMANDINFOEX
            {
                cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                hwnd = hwnd,
                lpVerb = (IntPtr)cmdOffset,
                lpVerbW = (IntPtr)cmdOffset,
                lpDirectory = workingDir,
                lpDirectoryW = workingDir,
                nShow = SW_SHOWNORMAL,
                ptInvoke = pt,
            };

            contextMenu.InvokeCommand(ref invoke);
            return new ContextMenuResult(true, verb ?? "invoked");
        }
        catch
        {
            return new ContextMenuResult(false, null);
        }
        finally
        {
            _activeMenu2 = null;
            _activeMenu3 = null;
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (contextMenu3Ptr != IntPtr.Zero) Marshal.Release(contextMenu3Ptr);
            if (contextMenu2Ptr != IntPtr.Zero) Marshal.Release(contextMenu2Ptr);
            if (contextMenuPtr != IntPtr.Zero) Marshal.Release(contextMenuPtr);
            if (parentFolderPtr != IntPtr.Zero) Marshal.Release(parentFolderPtr);
            foreach (var p in pidls)
            {
                if (p != IntPtr.Zero) CoTaskMemFree(p);
            }
        }
    }

    private static string? TryGetVerb(IContextMenu menu, uint cmdOffset)
    {
        try
        {
            var sb = new StringBuilder(256);
            int hr = menu.GetCommandString((UIntPtr)cmdOffset, GCS_VERBW, IntPtr.Zero, sb, (uint)sb.Capacity);
            if (hr >= 0 && sb.Length > 0)
                return sb.ToString();
        }
        catch { }
        return null;
    }

    private static IntPtr MenuSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        try
        {
            if (uMsg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
            {
                if (_activeMenu3 is not null)
                {
                    if (_activeMenu3.HandleMenuMsg2(uMsg, wParam, lParam, out var res) >= 0)
                        return res;
                }
                else if (_activeMenu2 is not null)
                {
                    if (_activeMenu2.HandleMenuMsg(uMsg, wParam, lParam) >= 0)
                        return IntPtr.Zero;
                }
            }
        }
        catch { }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig]
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, ref Guid riid, ref uint rgfReserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, uint cchMax);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, uint cchMax);
        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, uint cchMax);
        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTitleW;
        public POINT ptInvoke;
    }

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr pidl,
        ref Guid riid,
        out IntPtr ppv,
        out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr hmenu,
        uint fuFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SUBCLASSPROC pfnSubclass,
        UIntPtr uIdSubclass,
        IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        SUBCLASSPROC pfnSubclass,
        UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam);
}
