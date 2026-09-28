namespace Vox.Core.Input;

/// <summary>
/// Confirms a command by a second press within a short window (e.g. Insert+Q twice to quit),
/// so a single accidental press does nothing irreversible.
/// </summary>
public sealed class RepeatPressConfirmation
{
    private readonly TimeSpan _window;
    private readonly Func<long> _clockMs;
    private long? _firstPressAt;

    public RepeatPressConfirmation(TimeSpan window, Func<long>? clockMs = null)
    {
        _window = window;
        _clockMs = clockMs ?? (() => Environment.TickCount64);
    }

    /// <summary>
    /// Records a press. Returns true when it confirms an earlier press within the window;
    /// false for a first press (the caller asks for confirmation).
    /// </summary>
    public bool Press()
    {
        var now = _clockMs();
        if (_firstPressAt is { } first && now - first <= (long)_window.TotalMilliseconds)
        {
            _firstPressAt = null;
            return true;
        }
        _firstPressAt = now;
        return false;
    }
}
