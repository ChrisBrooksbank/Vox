using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Vox.Core.Ocr;

namespace Vox.Core.Magnifier;

/// <summary>
/// <see cref="IMagnifier"/> through the Windows Magnification API, on a thread of its own (the
/// API and its windows belong to the thread that initialised it): full-screen magnification with
/// <c>MagSetFullscreenTransform</c> and <c>MagSetFullscreenColorEffect</c>, and the docked and lens
/// views as a topmost, click-through window holding a Magnification control (<c>WC_MAGNIFIER</c>).
/// The docked view reserves its edge of the screen as an app bar, so windows aren't hidden under
/// it. Frames are coalesced: only the newest waiting one is drawn. Refuses to start while Windows
/// Magnifier runs. Windows removes a process's magnification when it exits, so a crash can't
/// leave the screen magnified. The thread is per-monitor DPI aware, so it works in physical pixels
/// like UI Automation.
/// </summary>
public sealed class WindowsMagnifier : IMagnifier
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(3);
    private const string HostClassName = "VoxMagnifierHost";
    // How often a docked or lens view redraws what it magnifies
    private const uint RefreshMs = 16;

    private readonly ILogger<WindowsMagnifier> _logger;
    private readonly Func<bool> _windowsMagnifierRunning;
    private readonly object _lock = new();
    private readonly ConcurrentQueue<Action> _work = new();
    private Thread? _thread;
    private uint _threadId;
    private MagnifierFrame? _pending;
    private bool _running;

    // Magnifier thread only
    private IntPtr _host;
    private IntPtr _control;
    private bool _appBar;
    private WndProc? _wndProc;
    private SmoothingSetter? _setSmoothing;

    public WindowsMagnifier(ILogger<WindowsMagnifier> logger, Func<bool>? windowsMagnifierRunning = null)
    {
        _logger = logger;
        _windowsMagnifierRunning = windowsMagnifierRunning ?? IsWindowsMagnifierRunning;
    }

    /// <summary>Windows Magnifier (Magnify.exe) is running in this session.</summary>
    public static bool IsWindowsMagnifierRunning()
    {
        var processes = Process.GetProcessesByName("Magnify");
        try
        {
            int session = Process.GetCurrentProcess().SessionId;
            return processes.Any(p => SafeSessionId(p) == session);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        static int SafeSessionId(Process process)
        {
            try { return process.SessionId; }
            catch (InvalidOperationException) { return -1; }
        }
    }

    public bool IsRunning
    {
        get { lock (_lock) return _running; }
    }

    public MagnifierStartResult Start()
    {
        lock (_lock)
        {
            if (_running)
                return MagnifierStartResult.Started;
        }
        if (_windowsMagnifierRunning())
            return MagnifierStartResult.WindowsMagnifierRunning;

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Run(started)) { IsBackground = true, Name = "Vox magnifier" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!started.Task.Wait(StartTimeout) || !started.Task.Result)
        {
            _logger.LogWarning("The Magnification API could not be initialised");
            return MagnifierStartResult.Unavailable;
        }
        lock (_lock)
        {
            _thread = thread;
            _running = true;
        }
        return MagnifierStartResult.Started;
    }

    public void Show(MagnifierFrame frame)
    {
        lock (_lock)
        {
            if (!_running)
                return;
            bool scheduled = _pending is not null;
            _pending = frame;
            if (scheduled)
                return;
        }
        Post(() =>
        {
            MagnifierFrame? next;
            lock (_lock)
            {
                next = _pending;
                _pending = null;
            }
            if (next is not null)
                Apply(next);
        });
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lock)
        {
            if (!_running)
                return;
            _running = false;
            _pending = null;
            thread = _thread;
            _thread = null;
        }
        Post(() =>
        {
            TearDown();
            PostQuitMessage(0);
        });
        if (thread is not null && !thread.Join(StartTimeout))
            _logger.LogWarning("The magnifier thread did not stop in time");
    }

    public void Dispose() => Stop();

    private void Post(Action action)
    {
        _work.Enqueue(action);
        if (_threadId != 0 && !PostThreadMessage(_threadId, WM_APP_WORK, IntPtr.Zero, IntPtr.Zero))
            _logger.LogDebug("Could not wake the magnifier thread ({Error})", Marshal.GetLastWin32Error());
    }

    // -------------------------------------------------------------------------
    // The magnifier thread
    // -------------------------------------------------------------------------

    private void Run(TaskCompletionSource<bool> started)
    {
        try
        {
            // Physical pixels, as UI Automation reports them
            SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            // The thread's message queue exists before anyone posts to it
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            _threadId = GetCurrentThreadId();
            if (!MagInitialize())
            {
                started.TrySetResult(false);
                return;
            }
            var smoothing = GetProcAddress(GetModuleHandle("Magnification.dll"), "MagSetFullscreenUseBitmapSmoothing");
            _setSmoothing = smoothing == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<SmoothingSetter>(smoothing);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _logger.LogWarning(ex, "The Magnification API is not available");
            started.TrySetResult(false);
            return;
        }
        started.TrySetResult(true);

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.hwnd == IntPtr.Zero && message.message == WM_APP_WORK)
            {
                RunWork();
                continue;
            }
            TranslateMessage(ref message);
            DispatchMessage(ref message);
            // Work posted while a window message was being handled
            RunWork();
        }
        RunWork();
        MagUninitialize();
    }

    private void RunWork()
    {
        while (_work.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Magnifier work failed");
            }
        }
    }

    private void Apply(MagnifierFrame frame)
    {
        var effect = new MAGCOLOREFFECT { transform = MagnifierColorMatrices.For(frame.Filter) };
        if (frame.View == MagnifierView.FullScreen || frame.Window is not { } window)
        {
            HideWindow();
            _setSmoothing?.Invoke(frame.Smoothing);
            if (!MagSetFullscreenTransform((float)frame.Zoom, (int)Math.Round(frame.Left), (int)Math.Round(frame.Top)))
                _logger.LogDebug("MagSetFullscreenTransform failed ({Error})", Marshal.GetLastWin32Error());
            MagSetFullscreenColorEffect(ref effect);
            return;
        }

        // A window shows the view: the screen itself stays as it is
        ResetFullScreen();
        if (!EnsureWindow())
            return;
        if (frame.View == MagnifierView.Docked)
            ReserveEdge(window);
        else
            ReleaseEdge();
        SetWindowPos(_host, HWND_TOPMOST, window.Left, window.Top, window.Width, window.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetWindowPos(_control, IntPtr.Zero, 0, 0, window.Width, window.Height, SWP_NOACTIVATE | SWP_NOZORDER);

        float zoom = (float)frame.Zoom;
        var transform = new MAGTRANSFORM { v = [zoom, 0, 0, 0, zoom, 0, 0, 0, 1] };
        MagSetWindowTransform(_control, ref transform);
        MagSetColorEffect(_control, ref effect);
        var source = new RECT
        {
            Left = (int)Math.Round(frame.Left),
            Top = (int)Math.Round(frame.Top),
            Right = (int)Math.Round(frame.Left + window.Width / frame.Zoom),
            Bottom = (int)Math.Round(frame.Top + window.Height / frame.Zoom),
        };
        MagSetWindowSource(_control, source);
    }

    private bool EnsureWindow()
    {
        if (_host != IntPtr.Zero)
            return true;
        var instance = GetModuleHandle(null);
        _wndProc ??= HostProc;
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = HostClassName,
        };
        // Already registered by an earlier start: fine
        RegisterClassEx(ref windowClass);
        _host = CreateWindowEx(WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            HostClassName, "Vox Magnifier", WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_host == IntPtr.Zero)
        {
            _logger.LogWarning("Could not create the magnifier window ({Error})", Marshal.GetLastWin32Error());
            return false;
        }
        SetLayeredWindowAttributes(_host, 0, 255, LWA_ALPHA);
        _control = CreateWindowEx(0, WC_MAGNIFIER, "Vox Magnifier view", WS_CHILD | WS_VISIBLE | MS_SHOWMAGNIFIEDCURSOR,
            0, 0, 1, 1, _host, IntPtr.Zero, instance, IntPtr.Zero);
        if (_control == IntPtr.Zero)
        {
            _logger.LogWarning("Could not create the Magnification control ({Error})", Marshal.GetLastWin32Error());
            DestroyWindow(_host);
            _host = IntPtr.Zero;
            return false;
        }
        // The view must not magnify itself
        MagSetWindowFilterList(_control, MW_FILTERMODE_EXCLUDE, 1, [_host]);
        SetTimer(_host, (UIntPtr)1, RefreshMs, IntPtr.Zero);
        return true;
    }

    private IntPtr HostProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_TIMER:
                // The control draws what is under it now
                if (_control != IntPtr.Zero)
                    InvalidateRect(_control, IntPtr.Zero, true);
                return IntPtr.Zero;
            case WM_NCHITTEST:
                return (IntPtr)HTTRANSPARENT;
            case WM_MOUSEACTIVATE:
                return (IntPtr)MA_NOACTIVATE;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void HideWindow()
    {
        ReleaseEdge();
        if (_host != IntPtr.Zero)
            ShowWindow(_host, SW_HIDE);
    }

    private static void ResetFullScreen()
    {
        MagSetFullscreenTransform(1f, 0, 0);
        var identity = new MAGCOLOREFFECT { transform = MagnifierColorMatrices.Identity };
        MagSetFullscreenColorEffect(ref identity);
    }

    /// <summary>Keeps other windows out from under the docked view (an app bar on its edge).</summary>
    private void ReserveEdge(ScreenRect window)
    {
        var data = AppBarData();
        if (!_appBar)
        {
            data.uCallbackMessage = WM_APP_BAR;
            _appBar = SHAppBarMessage(ABM_NEW, ref data) != IntPtr.Zero;
            if (!_appBar)
                return;
        }
        data.uEdge = EdgeOf(window);
        data.rc = new RECT { Left = window.Left, Top = window.Top, Right = window.Right, Bottom = window.Bottom };
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        SHAppBarMessage(ABM_SETPOS, ref data);
    }

    private void ReleaseEdge()
    {
        if (!_appBar)
            return;
        var data = AppBarData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _appBar = false;
    }

    private APPBARDATA AppBarData() => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = _host };

    /// <summary>The edge a docked window sits along, from where it is on its display.</summary>
    private static uint EdgeOf(ScreenRect window)
    {
        var monitor = MonitorFromPoint(new POINT { X = window.Left + window.Width / 2, Y = window.Top + window.Height / 2 }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return ABE_TOP;
        var display = info.rcMonitor;
        bool fullWidth = window.Width >= display.Right - display.Left;
        if (fullWidth)
            return window.Top <= display.Top ? ABE_TOP : ABE_BOTTOM;
        return window.Left <= display.Left ? ABE_LEFT : ABE_RIGHT;
    }

    private void TearDown()
    {
        ReleaseEdge();
        if (_host != IntPtr.Zero)
        {
            KillTimer(_host, (UIntPtr)1);
            DestroyWindow(_host);
        }
        _host = IntPtr.Zero;
        _control = IntPtr.Zero;
        _setSmoothing?.Invoke(false);
        ResetFullScreen();
    }

    // -------------------------------------------------------------------------
    // Interop
    // -------------------------------------------------------------------------

    private const uint WM_APP_WORK = 0x8000 + 0x56;
    private const uint WM_APP_BAR = 0x8000 + 0x57;
    private const uint WM_TIMER = 0x0113;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const int HTTRANSPARENT = -1;
    private const int MA_NOACTIVATE = 3;
    private const uint PM_NOREMOVE = 0;
    private const string WC_MAGNIFIER = "Magnifier";
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint MS_SHOWMAGNIFIEDCURSOR = 0x0001;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint MW_FILTERMODE_EXCLUDE = 0;
    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
    private const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SmoothingSetter([MarshalAs(UnmanagedType.Bool)] bool enabled);

    [StructLayout(LayoutKind.Sequential)]
    private struct MAGTRANSFORM
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public float[] v;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MAGCOLOREFFECT
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] transform;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetFullscreenTransform(float magLevel, int xOffset, int yOffset);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM transform);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetColorEffect(IntPtr hwnd, ref MAGCOLOREFFECT effect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetWindowFilterList(IntPtr hwnd, uint filterMode, int count, IntPtr[] windows);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint elapseMs, IntPtr timerProc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(IntPtr hwnd, UIntPtr id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}

/// <summary>
/// The monitors through <c>EnumDisplayMonitors</c>, in physical pixels (read per-monitor DPI
/// aware, as UI Automation reports positions); the primary monitor first.
/// </summary>
public sealed class WindowsDisplayLayout : IDisplayLayout
{
    public IReadOnlyList<ScreenRect> Displays
    {
        get
        {
            var previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            try
            {
                var displays = new List<(ScreenRect Rect, bool Primary)>();
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
                {
                    var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(monitor, ref info))
                        displays.Add((ScreenRect.FromEdges(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
                            (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
                    return true;
                }, IntPtr.Zero);
                return displays.OrderByDescending(d => d.Primary).Select(d => d.Rect).ToList();
            }
            finally
            {
                if (previous != IntPtr.Zero)
                    SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    private const uint MONITORINFOF_PRIMARY = 1;
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}

/// <summary>The mouse pointer in physical pixels (<c>GetPhysicalCursorPos</c>), as the magnifier and UI Automation use them.</summary>
public static class PhysicalPointer
{
    public static (int X, int Y)? GetPosition() => GetPhysicalCursorPos(out var point) ? (point.X, point.Y) : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalCursorPos(out POINT point);
}
