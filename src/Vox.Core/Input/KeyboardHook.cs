using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Vox.Core.Configuration;

namespace Vox.Core.Input;

/// <summary>
/// Low-level keyboard hook using SetWindowsHookEx(WH_KEYBOARD_LL).
/// Callback only updates modifier state, asks <see cref="SuppressionFilter"/> whether to swallow the key,
/// posts a KeyEvent to a bounded channel (TryWrite) and returns immediately.
/// A consumer thread reads from the channel and raises the KeyPressed event.
/// </summary>
public sealed class KeyboardHook : IKeyboardHook, IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_CAPITAL = 0x14; // CapsLock

    // dwExtraInfo on keys Vox injects itself, so the hook lets them through untouched
    private const nuint VoxInjectedMarker = 0x566F78; // "Vox"
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    // INPUT: the union includes MOUSEINPUT (its largest member) so the struct size and the
    // union's alignment match Windows on both 32- and 64-bit
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static readonly Func<int, bool> IsPhysicallyDown = vk => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    // Win32 message pump functions
    // Returns: >0 = message received, 0 = WM_QUIT, -1 = error
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpmsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    private readonly ILogger<KeyboardHook> _logger;
    private readonly KeyStateTracker _keyState = new();
    // Filter decision for each key currently down, so the matching key-up is treated the same
    // way (swallowed if the key-down was) and carries the same decision (indexed by vkCode)
    private readonly KeyDecision[] _keyDecisions = new KeyDecision[256];
    // Set from the session-switch event thread; applied on the hook thread
    private volatile bool _resetRequested;
    private Channel<KeyEvent> _channel = CreateChannel();
    private nint _hookHandle;
    private LowLevelKeyboardProc? _hookCallback; // Keep reference to prevent GC
    private Thread? _consumerThread;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private CancellationTokenSource _cts = new();

    public event EventHandler<KeyEvent>? KeyPressed;

    public Func<KeyEvent, KeyDecision>? SuppressionFilter { get; set; }

    public ModifierKey ScreenReaderModifier
    {
        get => _keyState.ScreenReaderModifier;
        set
        {
            bool becomingCapsLock = value == ModifierKey.CapsLock && _keyState.ScreenReaderModifier != ModifierKey.CapsLock;
            _keyState.ScreenReaderModifier = value;

            // Once CapsLock is the screen reader key every press is swallowed, so a Caps Lock that
            // is on at this moment could never be turned off again: turn it off now
            if (becomingCapsLock && (GetKeyState(VK_CAPITAL) & 0x0001) != 0)
                PressCapsLockOnce();
        }
    }

    /// <summary>Toggles Caps Lock with an injected press that the hook passes through.</summary>
    private void PressCapsLockOnce()
    {
        try
        {
            var inputs = new[]
            {
                new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CAPITAL, dwExtraInfo = VoxInjectedMarker } } },
                new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CAPITAL, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = VoxInjectedMarker } } },
            };
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
                _logger.LogWarning("Could not turn Caps Lock off (SendInput error {Error})", Marshal.GetLastWin32Error());
            _keyState.SetCapsLockState(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not turn Caps Lock off");
        }
    }

    public KeyboardHook(ILogger<KeyboardHook> logger)
    {
        _logger = logger;
    }

    private static Channel<KeyEvent> CreateChannel() =>
        Channel.CreateBounded<KeyEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public void Install()
    {
        if (_hookHandle != nint.Zero)
        {
            _logger.LogWarning("KeyboardHook already installed");
            return;
        }

        _cts.Dispose();
        _cts = new CancellationTokenSource();

        // A fresh channel per install: Uninstall completes the previous one
        _channel = CreateChannel();
        var channel = _channel;
        var token = _cts.Token;

        // Start consumer thread before installing hook
        _consumerThread = new Thread(() => ConsumeEvents(channel, token))
        {
            IsBackground = true,
            Name = "KeyboardHookConsumer"
        };
        _consumerThread.Start();

        // Install the hook on a dedicated thread with a message pump.
        // WH_KEYBOARD_LL requires a message pump — without one, Windows
        // silently unhooks the callback after a few seconds.
        var hookReady = new ManualResetEventSlim(false);
        _hookThread = new Thread(() => HookThreadProc(hookReady))
        {
            IsBackground = true,
            Name = "KeyboardHookMsgPump"
        };
        _hookThread.Start();
        hookReady.Wait(); // Wait for hook to be installed before returning
        hookReady.Dispose();

        if (_hookHandle != nint.Zero)
        {
            try { Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch; }
            catch (Exception ex) { _logger.LogDebug(ex, "Session switch notifications unavailable"); }
        }

        if (_hookHandle == nint.Zero)
        {
            // Installation failed: stop the consumer and report it to the caller
            var error = _installError;
            _channel.Writer.TryComplete();
            _consumerThread.Join(TimeSpan.FromSeconds(2));
            _consumerThread = null;
            _hookThread.Join(TimeSpan.FromSeconds(2));
            _hookThread = null;
            throw new Win32Exception(error, "Failed to install low-level keyboard hook (SetWindowsHookEx).");
        }
    }

    private int _installError;

    private void HookThreadProc(ManualResetEventSlim hookReady)
    {
        _hookThreadId = GetCurrentThreadId();

        _hookCallback = HookCallback;
        Array.Clear(_keyDecisions);
        _keyState.SetCapsLockState((GetKeyState(VK_CAPITAL) & 0x0001) != 0);
        var hMod = GetModuleHandle(null);
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, hMod, 0);

        if (_hookHandle == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("Failed to install keyboard hook. Win32 error: {Error}", error);
            _installError = error;
            _cts.Cancel();
            hookReady.Set();
            return;
        }

        _logger.LogInformation("Keyboard hook installed");
        hookReady.Set();

        // Run a Windows message pump so the hook stays alive
        // GetMessage returns 0 for WM_QUIT, -1 on error, positive otherwise
        int getResult;
        while ((getResult = GetMessage(out var msg, nint.Zero, 0, 0)) != 0)
        {
            if (getResult == -1)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("GetMessage failed in keyboard hook pump. Win32 error: {Error}", error);
                break;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // Clean up hook when message pump exits
        if (_hookHandle != nint.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }
    }

    public void Uninstall()
    {
        if (_hookHandle == nint.Zero)
            return;

        try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; }
        catch { /* not subscribed */ }

        // Post WM_QUIT to the hook thread's message pump to make it exit cleanly
        if (_hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, WM_QUIT, nint.Zero, nint.Zero);
        }

        _cts.Cancel();
        _channel.Writer.TryComplete();

        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _hookCallback = null; // Null after join — hook thread must be done before delegate can be GC'd
        _hookThread = null;
        _hookThreadId = 0;

        _consumerThread?.Join(TimeSpan.FromSeconds(2));
        _consumerThread = null;

        _logger.LogInformation("Keyboard hook uninstalled");
    }

    // This callback must complete in < 1ms. Only update key state, consult the suppression
    // filter (a dictionary lookup) and TryWrite to the channel.
    private unsafe nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                var kbStruct = *(KBDLLHOOKSTRUCT*)lParam;
                var vkCode = (int)kbStruct.vkCode;

                // Keys Vox injected itself (e.g. turning Caps Lock off) go straight through
                if (kbStruct.dwExtraInfo == VoxInjectedMarker)
                    return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

                if (_resetRequested)
                {
                    // Session switch (lock/unlock): key-ups may have been missed
                    _resetRequested = false;
                    _keyState.Reset();
                    Array.Clear(_keyDecisions);
                }

                // Clear modifiers whose key-up was missed on the secure desktop (one syscall per key)
                if (isKeyDown && !KeyStateTracker.IsModifierKey(vkCode))
                    _keyState.Reconcile(IsPhysicallyDown);

                var modifiers = _keyState.Process(vkCode, isKeyDown, out bool isScreenReaderModifier);

                var evt = new KeyEvent
                {
                    VkCode = vkCode,
                    Modifiers = modifiers,
                    IsKeyDown = isKeyDown,
                    Timestamp = kbStruct.time,
                    ScanCode = (int)kbStruct.scanCode,
                    CapsLockOn = _keyState.ScreenReaderModifier != ModifierKey.CapsLock && _keyState.CapsLockOn
                };

                KeyDecision decision;
                var slot = vkCode & 0xFF;
                if (isScreenReaderModifier)
                {
                    // The screen reader modifier never reaches applications
                    decision = KeyDecision.Swallow;
                }
                else if (isKeyDown)
                {
                    decision = Decide(evt);
                    _keyDecisions[slot] = decision;
                }
                else
                {
                    // A key-up gets its key-down's decision (swallowed only if the key-down was)
                    decision = _keyDecisions[slot];
                    _keyDecisions[slot] = default;
                }

                bool suppress = decision.Suppress;
                evt = evt with { Decision = decision };

                _channel.Writer.TryWrite(evt);

                if (suppress)
                    return 1;
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private KeyDecision Decide(KeyEvent evt)
    {
        var filter = SuppressionFilter;
        if (filter is null) return KeyDecision.Pass;
        try
        {
            return filter(evt);
        }
        catch
        {
            // Never let an exception escape into the native hook chain
            return KeyDecision.Pass;
        }
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e) =>
        _resetRequested = true;

    private void ConsumeEvents(Channel<KeyEvent> channel, CancellationToken token)
    {
        var reader = channel.Reader;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // Synchronously wait for items using the async method's GetAwaiter pattern
                var waitTask = reader.WaitToReadAsync(token).AsTask();
                waitTask.Wait(token);

                if (!waitTask.Result)
                    break;

                while (reader.TryRead(out var evt))
                {
                    try
                    {
                        KeyPressed?.Invoke(this, evt);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in KeyPressed event handler");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Keyboard hook consumer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in keyboard hook consumer");
        }
    }

    public void Dispose()
    {
        Uninstall();
        _cts.Dispose();
    }
}
