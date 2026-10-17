using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;
using Vox.Core.Accessibility;

namespace Vox.Core.Touch;

/// <summary><see cref="ITouchActivator"/> through ElementFromPoint and <see cref="ElementActivation"/>.</summary>
public sealed class UIATouchActivator(UIAProvider provider, IMouseInput? mouse) : ITouchActivator
{
    public bool ActivateAt(int x, int y)
    {
        var element = provider.Automation.ElementFromPointBuildCache(new tagPOINT { x = x, y = y }, provider.CacheRequest);
        if (element is null)
            return false;
        return ElementActivation.Activate(UIANavigatorObject.For(element, provider), mouse) != ActivationOutcome.None;
    }
}

/// <summary>
/// The touch layer (design: docs/technical/touch.md): a nearly transparent topmost window over
/// the virtual screen, on a thread of its own, that takes touch before the apps under it and turns
/// WM_POINTER messages into <see cref="TouchPoint"/>s. Pen and mouse pass through it
/// (HTTRANSPARENT). It exists only between <see cref="Start"/> and <see cref="Stop"/>. Without UI
/// access it covers ordinary windows only. Touch Vox does not claim is not passed on.
/// </summary>
public sealed class WindowsTouchSource(ILogger<WindowsTouchSource> logger) : ITouchSource
{
    private const string ClassName = "VoxTouchLayer";
    private const uint WM_NCHITTEST = 0x0084, WM_POINTERUPDATE = 0x0245, WM_POINTERDOWN = 0x0246, WM_POINTERUP = 0x0247,
        WM_POINTERCAPTURECHANGED = 0x024C, WM_CLOSE = 0x0010, WM_DESTROY = 0x0002, WM_QUIT = 0x0012;
    private const int HTTRANSPARENT = -1, HTCLIENT = 1, PT_TOUCH = 2;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x80000, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    private readonly object _lock = new();
    private Thread? _thread;
    private uint _threadId;
    private WndProc? _proc;
    private bool _disposed;

    public event Action<TouchPoint>? PointReceived;

    public bool Start()
    {
        lock (_lock)
        {
            if (_disposed)
                return false;
            if (_thread is not null)
                return true;
            using var ready = new ManualResetEventSlim();
            bool ok = false;
            _thread = new Thread(() => Run(ready, v => ok = v)) { IsBackground = true, Name = "Vox touch layer" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(3)) || !ok)
            {
                _thread = null;
                return false;
            }
            return true;
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lock)
        {
            thread = _thread;
            _thread = null;
        }
        if (thread is null)
            return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run(ManualResetEventSlim ready, Action<bool> result)
    {
        IntPtr hwnd = IntPtr.Zero;
        try
        {
            _threadId = GetCurrentThreadId();
            _proc = WindowProc;
            var instance = GetModuleHandle(null);
            var cls = new WndClass { lpfnWndProc = _proc, hInstance = instance, lpszClassName = ClassName };
            RegisterClass(ref cls);
            hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "Vox touch layer",
                WS_POPUP, GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN), IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                logger.LogWarning("Could not create the touch layer ({Error})", Marshal.GetLastWin32Error());
                result(false);
                return;
            }
            // Nearly transparent but still hit-testable
            SetLayeredWindowAttributes(hwnd, 0, 1, 2);
            ShowWindow(hwnd, 4);
            result(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start the touch layer");
            result(false);
            return;
        }
        finally
        {
            ready.Set();
        }

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (hwnd != IntPtr.Zero)
            DestroyWindow(hwnd);
        UnregisterClass(ClassName, GetModuleHandle(null));
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case WM_NCHITTEST:
                    // Touch and pen carry 0xFF515700 in the extra info, touch with bit 0x80 set
                    long extra = GetMessageExtraInfo().ToInt64();
                    bool touch = (extra & 0xFFFFFF00) == 0xFF515700 && (extra & 0x80) != 0;
                    return touch ? HTCLIENT : HTTRANSPARENT;
                case WM_POINTERDOWN:
                case WM_POINTERUPDATE:
                case WM_POINTERUP:
                    Post((uint)(wParam.ToInt64() & 0xFFFF), message == WM_POINTERDOWN ? TouchPhase.Down
                        : message == WM_POINTERUP ? TouchPhase.Up : TouchPhase.Move);
                    return IntPtr.Zero;
                case WM_POINTERCAPTURECHANGED:
                    PointReceived?.Invoke(new TouchPoint(0, 0, 0, TouchPhase.Cancel, DateTimeOffset.UtcNow));
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Touch message failed");
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void Post(uint id, TouchPhase phase)
    {
        if (!GetPointerInfo(id, out var info) || info.pointerType != PT_TOUCH)
            return;
        // Only the gesture names are ever logged, never positions
        PointReceived?.Invoke(new TouchPoint((int)id, info.ptPixelLocation.X, info.ptPixelLocation.Y, phase, DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        lock (_lock)
            _disposed = true;
        Stop();
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Pt { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public Pt pt; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public uint pointerType, pointerId, frameId, pointerFlags;
        public IntPtr sourceDevice, hwndTarget;
        public Pt ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
        public uint dwTime, historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong performanceCount;
        public int buttonChangeType;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WndClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();
    [DllImport("user32.dll")] private static extern bool GetPointerInfo(uint id, out PointerInfo info);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
