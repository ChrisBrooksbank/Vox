using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Vox.Core.Input;

/// <summary>
/// Low-level keyboard hook using SetWindowsHookEx(WH_KEYBOARD_LL).
/// Callback only posts a pre-allocated KeyEvent to a bounded channel (TryWrite) and returns immediately.
/// Zero allocation, zero processing in the hook callback.
/// A consumer thread reads from the channel and raises the KeyPressed event.
/// </summary>
public sealed class KeyboardHook : IKeyboardHook, IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;  // Alt
    private const int VK_INSERT = 0x2D;
    private const int VK_CAPITAL = 0x14; // CapsLock

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

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    private readonly ILogger<KeyboardHook> _logger;
    private readonly Channel<KeyEvent> _channel;
    private nint _hookHandle;
    private LowLevelKeyboardProc? _hookCallback; // Keep reference to prevent GC
    private Thread? _consumerThread;
    private CancellationTokenSource _cts = new();

    public event EventHandler<KeyEvent>? KeyPressed;

    public KeyboardHook(ILogger<KeyboardHook> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<KeyEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Install()
    {
        if (_hookHandle != nint.Zero)
        {
            _logger.LogWarning("KeyboardHook already installed");
            return;
        }

        _cts = new CancellationTokenSource();

        // Start consumer thread before installing hook
        _consumerThread = new Thread(ConsumeEvents)
        {
            IsBackground = true,
            Name = "KeyboardHookConsumer"
        };
        _consumerThread.Start();

        // Store the delegate to prevent garbage collection
        _hookCallback = HookCallback;

        var hMod = GetModuleHandle(null);
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, hMod, 0);

        if (_hookHandle == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("Failed to install keyboard hook. Win32 error: {Error}", error);
            _cts.Cancel();
        }
        else
        {
            _logger.LogInformation("Keyboard hook installed");
        }
    }

    public void Uninstall()
    {
        if (_hookHandle == nint.Zero)
            return;

        if (!UnhookWindowsHookEx(_hookHandle))
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning("Failed to uninstall keyboard hook. Win32 error: {Error}", error);
        }

        _hookHandle = nint.Zero;
        _hookCallback = null;

        _cts.Cancel();
        _channel.Writer.TryComplete();

        _consumerThread?.Join(TimeSpan.FromSeconds(2));
        _consumerThread = null;

        _logger.LogInformation("Keyboard hook uninstalled");
    }

    // This callback must complete in < 1ms. Only extract data and TryWrite to channel.
    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vkCode = (int)kbStruct.vkCode;

                // Read modifier state from key state table (fast, no allocation)
                var modifiers = KeyModifiers.None;
                if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) modifiers |= KeyModifiers.Shift;
                if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) modifiers |= KeyModifiers.Ctrl;
                if ((GetKeyState(VK_MENU) & 0x8000) != 0) modifiers |= KeyModifiers.Alt;
                if ((GetKeyState(VK_INSERT) & 0x8000) != 0) modifiers |= KeyModifiers.Insert;

                var evt = new KeyEvent
                {
                    VkCode = vkCode,
                    Modifiers = modifiers,
                    IsKeyDown = isKeyDown,
                    Timestamp = kbStruct.time
                };

                _channel.Writer.TryWrite(evt);
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void ConsumeEvents()
    {
        var token = _cts.Token;
        var reader = _channel.Reader;

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
