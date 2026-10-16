using System.Runtime.InteropServices;

namespace Vox.Core.Accessibility;

/// <summary>
/// The system caret through Win32, for edit controls without a UIA text pattern: the window
/// that owns the caret (GetGUIThreadInfo) and, for classic edit controls, the selection
/// (EM_GETSEL, sent with a timeout so a hung application can't block Vox).
/// </summary>
public static class Win32Caret
{
    private const uint EM_GETSEL = 0x00B0;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint TimeoutMs = 100;

    /// <summary>The window that owns the caret in the foreground thread, or zero.</summary>
    public static IntPtr GetCaretWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return IntPtr.Zero;
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(thread, ref info) ? info.hwndCaret : IntPtr.Zero;
    }

    /// <summary>
    /// The selection of an edit control as character offsets (start == end: the caret), or null
    /// if the window doesn't answer EM_GETSEL in time.
    /// </summary>
    public static (int Start, int End)? GetEditSelection(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;
        int start = 0, end = 0;
        var ok = SendMessageTimeout(hwnd, EM_GETSEL, ref start, ref end, SMTO_ABORTIFHUNG, TimeoutMs, out _);
        if (ok == IntPtr.Zero || start < 0 || end < start)
            return null;
        return (start, end);
    }

    /// <summary>
    /// Where the system caret of the foreground thread is on screen, in physical pixels (as UI
    /// Automation reports positions), or null when there is no caret. Edit controls, Chromium,
    /// Office and most other applications keep a system caret, shown or not, for magnifiers.
    /// </summary>
    public static Ocr.ScreenRect? GetCaretBounds()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return null;
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(thread, ref info) || info.hwndCaret == IntPtr.Zero)
            return null;
        var previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            var topLeft = new POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
            var bottomRight = new POINT { X = info.rcCaret.Right, Y = info.rcCaret.Bottom };
            if (!ClientToScreen(info.hwndCaret, ref topLeft) || !ClientToScreen(info.hwndCaret, ref bottomRight))
                return null;
            // A caret is often 1 pixel wide or less: still somewhere to show
            var rect = Ocr.ScreenRect.FromEdges(topLeft.X, topLeft.Y, Math.Max(bottomRight.X, topLeft.X + 1), Math.Max(bottomRight.Y, topLeft.Y + 1));
            return rect;
        }
        finally
        {
            if (previous != IntPtr.Zero)
                SetThreadDpiAwarenessContext(previous);
        }
    }

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, ref int wParam, ref int lParam,
        uint flags, uint timeout, out IntPtr result);
}
