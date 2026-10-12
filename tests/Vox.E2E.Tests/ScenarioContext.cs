using System.Runtime.InteropServices;
using Vox.Core.Tests.TestSupport;

namespace Vox.E2E.Tests;

/// <summary>
/// What a desktop or fault scenario runs with: the Vox under test, keys pressed through it, and
/// checks on what it said. A pacer decides how fast it goes: straight through (tests), with a
/// pause before each step (VOX_E2E_PACE, or the interactive runner's --delay), or one step per
/// hotkey press (the runner's --step).
/// </summary>
public sealed class ScenarioContext
{
    private readonly IScenarioPacer _pacer;
    private readonly Action<string> _log;

    public ScenarioContext(VoxHarness vox, IScenarioPacer? pacer = null, Action<string>? log = null)
    {
        Vox = vox;
        _pacer = pacer ?? ScenarioPacers.FromEnvironment();
        _log = log ?? (_ => { });
    }

    public VoxHarness Vox { get; }

    public RecordingSpeechEngine Speech => Vox.Speech;

    /// <summary>The window the scenario is driving; a stepping pacer brings it back to the front before each key.</summary>
    public IntPtr Target { get; set; }

    /// <summary>How long Vox must be quiet after a key before the next step.</summary>
    public TimeSpan Quiet { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>The longest wait for Vox to go quiet after a key.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>How long an expectation waits by default.</summary>
    public TimeSpan ExpectTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public void Log(string message) => _log(message);

    /// <summary>Forgets what has been said so far: expectations only look at what is said after this.</summary>
    public void Mark() => Speech.Clear();

    /// <summary>A step that isn't a key press (the pacer waits before it as before a key).</summary>
    public Task StepAsync(string description)
    {
        Log("Step: " + description);
        return _pacer.BeforeStepAsync(this, description);
    }

    /// <summary>Presses <paramref name="keys"/> ("Tab", "Insert+F1") and waits until Vox has finished responding.</summary>
    public async Task PressAsync(string keys, string? why = null)
    {
        await StepAsync(why is null ? keys : $"{keys} ({why})");
        KeyNotation.Press(keys);
        await QuietAsync();
    }

    /// <summary>
    /// Types <paramref name="text"/> key by key, as a person would (letters, digits and spaces as
    /// their keys, so typing echo sees ordinary key presses; anything else as a Unicode character),
    /// and waits until Vox is quiet.
    /// </summary>
    public async Task TypeAsync(string text)
    {
        await StepAsync($"type \"{text}\"");
        foreach (var c in text)
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
                KeyInjector.Press(c);
            else if (char.IsAsciiLetterUpper(c))
                KeyInjector.Press(c, KeyInjector.Shift);
            else if (c == ' ')
                KeyInjector.Press((ushort)0x20);
            else
                KeyInjector.Type(c.ToString());
            await Task.Delay(120);
        }
        await QuietAsync();
    }

    public Task QuietAsync() => SpeechWaits.QuietAsync(Speech, Quiet, MaxWait);

    /// <summary>Waits until Vox says something (since the last <see cref="Mark"/>) that <paramref name="match"/> accepts.</summary>
    public async Task<SpokenUtterance> ExpectAsync(Func<string, bool> match, string description, TimeSpan? timeout = null)
    {
        try
        {
            var said = await Speech.WaitForAsync(s => match(s.Text), timeout ?? ExpectTimeout);
            Log($"  heard: {said.Text}");
            return said;
        }
        catch (TimeoutException ex)
        {
            throw new ScenarioFailedException($"Vox never said {description}. {ex.Message}");
        }
    }

    /// <summary>Waits until Vox says something containing every one of <paramref name="parts"/> (ignoring case).</summary>
    public Task<SpokenUtterance> ExpectSaidAsync(params string[] parts) =>
        ExpectAsync(text => parts.All(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)),
            string.Join(" + ", parts.Select(p => $"\"{p}\"")));

    /// <summary>Checks that nothing said since the last <see cref="Mark"/> matches.</summary>
    public void ExpectNotSaid(Func<string, bool> match, string description)
    {
        var said = Speech.SpokenText.FirstOrDefault(match);
        if (said is not null)
            throw new ScenarioFailedException($"Vox said {description}: \"{said}\"");
    }

    /// <summary>
    /// Presses <paramref name="keys"/> until Vox says something <paramref name="match"/> accepts
    /// (at most <paramref name="maxPresses"/> times), and returns that.
    /// </summary>
    public async Task<string> PressUntilAsync(string keys, Func<string, bool> match, int maxPresses, string description)
    {
        for (int i = 0; i < maxPresses; i++)
        {
            Mark();
            await PressAsync(keys, $"until {description}");
            var said = Speech.SpokenText.FirstOrDefault(match);
            if (said is not null)
                return said;
        }
        throw new ScenarioFailedException($"{maxPresses} presses of {keys} never reached {description}");
    }

    /// <summary>Everything said since the last <see cref="Mark"/>, for failure messages.</summary>
    public string Said => string.Join(" | ", Speech.SpokenText);
}

public sealed class ScenarioFailedException(string message) : Exception(message);

/// <summary>Decides when each step of a scenario runs.</summary>
public interface IScenarioPacer
{
    Task BeforeStepAsync(ScenarioContext context, string step);
}

public static class ScenarioPacers
{
    /// <summary>Straight through.</summary>
    public static IScenarioPacer None { get; } = new DelayPacer(TimeSpan.Zero);

    /// <summary>VOX_E2E_PACE=&lt;milliseconds&gt; slows tests down to watch them; otherwise none.</summary>
    public static IScenarioPacer FromEnvironment() =>
        int.TryParse(Environment.GetEnvironmentVariable("VOX_E2E_PACE"), out var ms) && ms > 0
            ? new DelayPacer(TimeSpan.FromMilliseconds(ms))
            : None;
}

/// <summary>Waits a fixed time before each step.</summary>
public sealed class DelayPacer(TimeSpan delay) : IScenarioPacer
{
    public Task BeforeStepAsync(ScenarioContext context, string step) =>
        delay > TimeSpan.Zero ? Task.Delay(delay) : Task.CompletedTask;
}

/// <summary>
/// Runs one step each time a hotkey is pressed: Pause, or Ctrl+Shift+Alt+N on keyboards without
/// one. They are system-wide hotkeys, so the window being tested keeps the focus (pressing Enter
/// in a console would take it away). Writes each step to <paramref name="output"/> first.
/// </summary>
public sealed class HotkeyStepPacer : IScenarioPacer, IDisposable
{
    private const int HotkeyPause = 1, HotkeyChord = 2;
    private const uint VK_PAUSE = 0x13, VK_N = 0x4E;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    private const uint WM_HOTKEY = 0x0312, WM_QUIT = 0x0012;

    private readonly TextWriter _output;
    private readonly SemaphoreSlim _pressed = new(0);
    private readonly Thread _thread;
    private readonly TaskCompletionSource _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _threadId;

    public HotkeyStepPacer(TextWriter output)
    {
        _output = output;
        _thread = new Thread(Pump) { IsBackground = true, Name = "E2E step hotkeys" };
        _thread.Start();
        _registered.Task.GetAwaiter().GetResult();
    }

    public async Task BeforeStepAsync(ScenarioContext context, string step)
    {
        while (_pressed.CurrentCount > 0)
            await _pressed.WaitAsync();
        _output.WriteLine($"Next: {step}   [Pause or Ctrl+Shift+Alt+N to run it]");
        await _pressed.WaitAsync();
        await DesktopWindows.WaitForModifiersReleasedAsync();
        if (context.Target != IntPtr.Zero && !DesktopWindows.IsForeground(context.Target))
        {
            DesktopWindows.BringToFront(context.Target);
            await Task.Delay(300);
        }
    }

    private void Pump()
    {
        _threadId = GetCurrentThreadId();
        bool pause = RegisterHotKey(IntPtr.Zero, HotkeyPause, MOD_NOREPEAT, VK_PAUSE);
        bool chord = RegisterHotKey(IntPtr.Zero, HotkeyChord, MOD_CONTROL | MOD_SHIFT | MOD_ALT | MOD_NOREPEAT, VK_N);
        if (!pause && !chord)
        {
            _registered.SetException(new InvalidOperationException("Neither step hotkey (Pause, Ctrl+Shift+Alt+N) could be registered"));
            return;
        }
        _registered.SetResult();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.Message == WM_HOTKEY)
                _pressed.Release();
        }
        UnregisterHotKey(IntPtr.Zero, HotkeyPause);
        UnregisterHotKey(IntPtr.Zero, HotkeyChord);
    }

    public void Dispose()
    {
        if (_threadId != 0)
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
