using System.Runtime.InteropServices;

namespace Vox.E2E.Tests;

/// <summary>
/// Simulates session switches (lock/unlock) and sleep/resume for the Vox running in this process.
/// A test can't lock the workstation (it would end the run) or put the machine to sleep, so this
/// sends the notifications Windows itself would send — WM_WTSSESSION_CHANGE and WM_POWERBROADCAST
/// — to the hidden window through which .NET's <see cref="Microsoft.Win32.SystemEvents"/> raises
/// <c>SessionSwitch</c> and <c>PowerModeChanged</c> (Vox's KeyboardHook listens to the first).
/// Only the notifications are simulated: the secure desktop never actually appears.
/// </summary>
public static class SystemEventsSimulator
{
    private const uint WM_WTSSESSION_CHANGE = 0x02B1;
    private const uint WM_POWERBROADCAST = 0x0218;
    private const int WTS_SESSION_LOCK = 0x7, WTS_SESSION_UNLOCK = 0x8;
    private const int PBT_APMSUSPEND = 0x4, PBT_APMRESUMESUSPEND = 0x7, PBT_APMRESUMEAUTOMATIC = 0x12;

    /// <summary>The workstation is locked and unlocked again (Windows+L, then signing back in).</summary>
    public static void LockAndUnlock()
    {
        Send(WM_WTSSESSION_CHANGE, WTS_SESSION_LOCK);
        Send(WM_WTSSESSION_CHANGE, WTS_SESSION_UNLOCK);
    }

    /// <summary>The machine is about to sleep.</summary>
    public static void Suspend() => Send(WM_POWERBROADCAST, PBT_APMSUSPEND);

    /// <summary>The machine has woken up (automatically, then by the user).</summary>
    public static void Resume()
    {
        Send(WM_POWERBROADCAST, PBT_APMRESUMEAUTOMATIC);
        Send(WM_POWERBROADCAST, PBT_APMRESUMESUSPEND);
    }

    private static void Send(uint message, int wParam)
    {
        // Created by SystemEvents when something first subscribes (Vox's hook, at start)
        int self = Environment.ProcessId;
        var window = DesktopWindows.TopLevel(visibleOnly: false)
            .FirstOrDefault(w => w.ProcessId == self && w.ClassName.StartsWith(".NET-BroadcastEventWindow", StringComparison.Ordinal));
        if (window.Handle == IntPtr.Zero)
            throw new InvalidOperationException("No SystemEvents window in this process: has Vox's keyboard hook started?");
        SendMessage(window.Handle, message, wParam, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, nint wParam, IntPtr lParam);
}
