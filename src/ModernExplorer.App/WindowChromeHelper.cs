using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ModernExplorer.Core.Settings;
using Windows.UI;

namespace ModernExplorer.App;

/// <summary>
/// Ensures that the main window and all child windows (Quick-View F3, Editor F4, Settings, etc.)
/// use a theme-integrated custom title bar, proper Win32 DWM dark/light frame attributes,
/// and the application window icon.
/// </summary>
public static partial class WindowChromeHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static void ApplyCustomTitleBarAndTheme(
        Window window,
        FrameworkElement rootElement,
        UIElement? titleBarDragElement,
        AppTheme theme)
    {
        bool isLight = theme == AppTheme.Light;
        rootElement.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
                window.AppWindow.SetIcon(iconPath);
        }
        catch { }

        try
        {
            window.ExtendsContentIntoTitleBar = true;
            if (titleBarDragElement is not null)
                window.SetTitleBar(titleBarDragElement);
        }
        catch { }

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd != IntPtr.Zero)
            {
                int dark = isLight ? 0 : 1;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));

                // COLORREF format is 0x00BBGGRR
                int captionColorRef = isLight ? 0x00F3EEF0 : 0x00241D1B;
                int textColorRef = isLight ? 0x001C1818 : 0x00F7F3F3;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColorRef, sizeof(int));
                _ = DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColorRef, sizeof(int));
            }
        }
        catch { }

        try
        {
            if (AppWindowTitleBar.IsCustomizationSupported() && window.AppWindow is { TitleBar: { } tb })
            {
                tb.ExtendsContentIntoTitleBar = true;
                tb.ButtonBackgroundColor = Colors.Transparent;
                tb.ButtonInactiveBackgroundColor = Colors.Transparent;

                if (isLight)
                {
                    tb.BackgroundColor = Color.FromArgb(255, 238, 240, 244);
                    tb.InactiveBackgroundColor = Color.FromArgb(255, 238, 240, 244);
                    tb.ForegroundColor = Color.FromArgb(255, 24, 24, 28);
                    tb.InactiveForegroundColor = Color.FromArgb(140, 40, 40, 48);
                    tb.ButtonForegroundColor = Color.FromArgb(255, 24, 24, 28);
                    tb.ButtonHoverForegroundColor = Colors.Black;
                    tb.ButtonHoverBackgroundColor = Color.FromArgb(22, 0, 0, 0);
                    tb.ButtonPressedBackgroundColor = Color.FromArgb(42, 0, 0, 0);
                    tb.ButtonInactiveForegroundColor = Color.FromArgb(140, 40, 40, 48);
                }
                else
                {
                    tb.BackgroundColor = Color.FromArgb(255, 27, 29, 36);
                    tb.InactiveBackgroundColor = Color.FromArgb(255, 27, 29, 36);
                    tb.ForegroundColor = Colors.White;
                    tb.InactiveForegroundColor = Color.FromArgb(140, 255, 255, 255);
                    tb.ButtonForegroundColor = Colors.White;
                    tb.ButtonHoverForegroundColor = Colors.White;
                    tb.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
                    tb.ButtonPressedBackgroundColor = Color.FromArgb(50, 255, 255, 255);
                    tb.ButtonInactiveForegroundColor = Color.FromArgb(140, 255, 255, 255);
                }
            }
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Reliably brings the specified WinUI 3 window to the foreground and gives it input focus,
    /// bypassing Windows foreground lock restrictions after a self-update restart.
    /// </summary>
    public static void BringWindowToForeground(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd != IntPtr.Zero)
            {
                if (window.AppWindow?.Presenter is OverlappedPresenter pCheck &&
                    pCheck.State == OverlappedPresenterState.Minimized)
                {
                    _ = ShowWindow(hwnd, SW_RESTORE);
                }
                else
                {
                    _ = ShowWindow(hwnd, SW_SHOW);
                }

                IntPtr fgHwnd = GetForegroundWindow();
                uint fgThread = fgHwnd != IntPtr.Zero ? GetWindowThreadProcessId(fgHwnd, out _) : 0;
                uint curThread = GetCurrentThreadId();
                bool attached = false;

                try
                {
                    if (fgThread != 0 && fgThread != curThread)
                        attached = AttachThreadInput(fgThread, curThread, true);

                    _ = BringWindowToTop(hwnd);
                    _ = SetForegroundWindow(hwnd);
                    SwitchToThisWindow(hwnd, true);
                }
                finally
                {
                    if (attached)
                        _ = AttachThreadInput(fgThread, curThread, false);
                }
            }

            if (window.AppWindow?.Presenter is OverlappedPresenter presenter)
            {
                bool wasTop = presenter.IsAlwaysOnTop;
                presenter.IsAlwaysOnTop = true;
                window.Activate();
                presenter.IsAlwaysOnTop = wasTop;
            }
            else
            {
                window.Activate();
            }
        }
        catch
        {
            try { window.Activate(); } catch { }
        }
    }
}
