using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Vox.Core.Input;
using Vox.Core.Speech;

namespace Vox.Core.Accessibility;

/// <summary>A mouse button.</summary>
public enum MouseButton
{
    Left,
    Right,
}

/// <summary>Moves the mouse pointer and presses its buttons (any thread).</summary>
public interface IMouseInput
{
    void MoveTo(int x, int y);
    void Press(MouseButton button);
    void Release(MouseButton button);
}

/// <summary><see cref="IMouseInput"/> through SetCursorPos and SendInput.</summary>
public sealed class Win32MouseInput : IMouseInput
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    public void MoveTo(int x, int y) => SetCursorPos(x, y);

    public void Press(MouseButton button) => Send(button == MouseButton.Left ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_RIGHTDOWN);

    public void Release(MouseButton button) => Send(button == MouseButton.Left ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_RIGHTUP);

    private static void Send(uint flags)
    {
        var input = new Input { Type = INPUT_MOUSE, Mouse = new MouseInput { Flags = flags } };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT is a union of MOUSEINPUT, KEYBDINPUT and HARDWAREINPUT; MOUSEINPUT is the largest
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}

/// <summary>
/// Mouse commands for controls with no keyboard access: route the pointer to the navigator
/// object, left and right click where the pointer is, and lock the left button down (for
/// dragging). The navigator object is only read on the UIA thread.
/// </summary>
public sealed class MouseCommands : IDisposable
{
    private readonly UIAThread _uiaThread;
    private readonly ObjectNavigationCommands _navigation;
    private readonly IMouseInput _mouse;
    private readonly SpeechQueue _speechQueue;
    private readonly ILogger<MouseCommands> _logger;
    private readonly object _lock = new();
    private bool _leftLocked;

    public MouseCommands(UIAThread uiaThread, ObjectNavigationCommands navigation, IMouseInput mouse,
        SpeechQueue speechQueue, ILogger<MouseCommands> logger)
    {
        _uiaThread = uiaThread;
        _navigation = navigation;
        _mouse = mouse;
        _speechQueue = speechQueue;
        _logger = logger;
    }

    /// <summary>The left button is held down by <see cref="ToggleLeftLock"/>.</summary>
    public bool IsLeftLocked
    {
        get { lock (_lock) return _leftLocked; }
    }

    /// <summary>Runs <paramref name="command"/> if it is a mouse command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.RouteMouseToNavigator: _ = RouteToNavigatorAsync(); return true;
            case NavigationCommand.MouseLeftClick: Click(MouseButton.Left); return true;
            case NavigationCommand.MouseRightClick: Click(MouseButton.Right); return true;
            case NavigationCommand.ToggleLeftMouseLock: ToggleLeftLock(); return true;
            default: return false;
        }
    }

    /// <summary>Clicks where the pointer is (a locked left button is released first).</summary>
    public void Click(MouseButton button)
    {
        // Said before the click, which may move focus (whose announcement then follows)
        Speak(button == MouseButton.Left ? "Left click" : "Right click");
        try
        {
            lock (_lock)
            {
                if (_leftLocked)
                {
                    _leftLocked = false;
                    _mouse.Release(MouseButton.Left);
                }
                _mouse.Press(button);
                _mouse.Release(button);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not click the mouse");
        }
    }

    /// <summary>Holds the left button down, or lets it go.</summary>
    public void ToggleLeftLock()
    {
        bool locked;
        try
        {
            lock (_lock)
            {
                if (_leftLocked)
                    _mouse.Release(MouseButton.Left);
                else
                    _mouse.Press(MouseButton.Left);
                _leftLocked = !_leftLocked;
                locked = _leftLocked;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not change the left mouse button lock");
            return;
        }
        Speak(locked ? "Left mouse button locked" : "Left mouse button unlocked");
    }

    /// <summary>Moves the pointer to the navigator object.</summary>
    public async Task RouteToNavigatorAsync()
    {
        try
        {
            await _uiaThread.RunAsync(() =>
            {
                if (_navigation.CurrentObject() is not { } current)
                {
                    Speak("No navigator object");
                    return;
                }
                if (current.GetClickPoint() is not { } point)
                {
                    Speak("No location");
                    return;
                }
                _mouse.MoveTo(point.X, point.Y);
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not move the mouse to the navigator object");
            Speak("Not available");
        }
    }

    private void Speak(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));

    /// <summary>Lets go of a locked left button, so quitting never leaves it held down.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (!_leftLocked)
                return;
            _leftLocked = false;
            try { _mouse.Release(MouseButton.Left); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not release the left mouse button"); }
        }
    }
}
