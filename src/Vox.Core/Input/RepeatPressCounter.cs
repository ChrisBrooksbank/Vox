namespace Vox.Core.Input;

/// <summary>
/// Counts quick repeat presses of the same command (press twice to spell, three times to spell
/// phonetically). A press of another command, or a pause longer than the window, starts again
/// at one; after <see cref="MaxCount"/> the count starts again too.
/// </summary>
public sealed class RepeatPressCounter
{
    public const int MaxCount = 3;

    private readonly TimeSpan _window;
    private readonly Func<long> _clockMs;
    private readonly object _lock = new();
    private long _lastPressAt;
    private int _lastKey = int.MinValue;
    private int _count;

    public RepeatPressCounter(TimeSpan? window = null, Func<long>? clockMs = null)
    {
        _window = window ?? TimeSpan.FromMilliseconds(500);
        _clockMs = clockMs ?? (() => Environment.TickCount64);
    }

    /// <summary>Records a press of <paramref name="key"/> and returns how many quick presses in a row it makes (1–3).</summary>
    public int Press(int key)
    {
        lock (_lock)
        {
            var now = _clockMs();
            bool repeat = key == _lastKey && now - _lastPressAt <= (long)_window.TotalMilliseconds && _count < MaxCount;
            _count = repeat ? _count + 1 : 1;
            _lastKey = key;
            _lastPressAt = now;
            return _count;
        }
    }
}
