using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Vox.E2E.Tests;

/// <summary>Finding, focusing and closing the top-level windows a scenario opens.</summary>
public static class DesktopWindows
{
    /// <summary>A top-level window: its handle, title, class and owning process.</summary>
    public readonly record struct WindowInfo(IntPtr Handle, string Title, string ClassName, int ProcessId);

    /// <summary>The visible top-level windows, front to back.</summary>
    public static IReadOnlyList<WindowInfo> TopLevel(bool visibleOnly = true)
    {
        var windows = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!visibleOnly || IsWindowVisible(hwnd))
                windows.Add(Describe(hwnd));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static WindowInfo Describe(IntPtr hwnd)
    {
        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        var className = new StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);
        GetWindowThreadProcessId(hwnd, out var pid);
        return new WindowInfo(hwnd, title.ToString(), className.ToString(), (int)pid);
    }

    /// <summary>Waits for a visible top-level window matching <paramref name="match"/>.</summary>
    public static async Task<WindowInfo> WaitForAsync(Func<WindowInfo, bool> match, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var found = TopLevel().FirstOrDefault(match);
            if (found.Handle != IntPtr.Zero)
                return found;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"No window appeared: {description}");
            await Task.Delay(100);
        }
    }

    public static bool IsForeground(IntPtr hwnd) => GetForegroundWindow() == hwnd;

    public static WindowInfo Foreground() => Describe(GetForegroundWindow());

    /// <summary>
    /// Brings <paramref name="hwnd"/> to the front (a window started from the test process doesn't
    /// always get the foreground): attaches to the foreground thread's input so Windows allows it.
    /// </summary>
    public static void BringToFront(IntPtr hwnd)
    {
        if (IsForeground(hwnd))
            return;
        if (IsIconic(hwnd))
            ShowWindow(hwnd, 9); // SW_RESTORE
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var ownThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(ownThread, foregroundThread, false);
        }
    }

    /// <summary>Waits until <paramref name="hwnd"/> is in front, bringing it there if it doesn't get there on its own.</summary>
    public static async Task EnsureForegroundAsync(IntPtr hwnd, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var nudgeAt = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!IsForeground(hwnd))
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Window \"{Describe(hwnd).Title}\" never came to the front (in front: \"{Foreground().Title}\")");
            if (DateTime.UtcNow >= nudgeAt)
            {
                BringToFront(hwnd);
                nudgeAt = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            }
            await Task.Delay(100);
        }
    }

    /// <summary>Asks the window to close (WM_CLOSE), as its close button would.</summary>
    public static void Close(IntPtr hwnd) => PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Closes every visible top-level window matching <paramref name="match"/> and waits for them to go.</summary>
    public static async Task CloseAllAsync(Func<WindowInfo, bool> match, TimeSpan timeout)
    {
        foreach (var window in TopLevel().Where(match))
            Close(window.Handle);
        var deadline = DateTime.UtcNow + timeout;
        while (TopLevel().Any(match) && DateTime.UtcNow < deadline)
            await Task.Delay(100);
    }

    /// <summary>Waits until no key Windows knows of is held (a person's hotkey, before keys are injected).</summary>
    public static async Task WaitForModifiersReleasedAsync()
    {
        int[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0x2D];
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (modifiers.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }

    /// <summary>The processes started by <paramref name="processId"/>, their children, and so on (not it).</summary>
    public static IReadOnlyList<Process> Descendants(int processId)
    {
        var parents = new Dictionary<int, int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (TryGetParent(process, out var parent))
                    parents[process.Id] = parent;
            }
        }
        var found = new HashSet<int>();
        var frontier = new Queue<int>([processId]);
        while (frontier.Count > 0)
        {
            var id = frontier.Dequeue();
            foreach (var (child, parent) in parents)
            {
                if (parent == id && child != processId && found.Add(child))
                    frontier.Enqueue(child);
            }
        }
        var result = new List<Process>();
        foreach (var id in found)
        {
            try { result.Add(Process.GetProcessById(id)); }
            catch (ArgumentException) { } // already gone
        }
        return result;
    }

    /// <summary>Freezes every thread of <paramref name="process"/>, as a machine going to sleep would (to its UIA clients).</summary>
    public static void Suspend(Process process) => NtSuspendProcess(process.Handle);

    public static void Resume(Process process) => NtResumeProcess(process.Handle);

    private static bool TryGetParent(Process process, out int parent)
    {
        parent = 0;
        try
        {
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0)
                return false;
            parent = (int)info.InheritedFromUniqueProcessId;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false; // another user's or a protected process
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr process);
}
