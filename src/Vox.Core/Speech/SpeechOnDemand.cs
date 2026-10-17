using Vox.Core.Input;

namespace Vox.Core.Speech;

/// <summary>
/// Speech on demand: while <see cref="IsOn"/>, <see cref="SpeechQueue"/> drops every utterance
/// that doesn't answer a reporting command (<see cref="CommandCatalog.IsOnDemandReport"/>).
/// A reporting command opens a short window in which the speech it causes is marked
/// <see cref="Utterance.OnDemand"/>, wherever it is produced (answers can come back from the UIA
/// thread); Say All keeps the window open until another command arrives.
/// </summary>
public sealed class SpeechOnDemand
{
    /// <summary>How long after a reporting command its speech is still marked.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private readonly Func<long> _nowMs;
    private readonly object _lock = new();
    private long _openUntil;

    public SpeechOnDemand(Func<long>? nowMs = null)
    {
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    /// <summary>Whether the mode is on (the <c>SpeechOnDemand</c> setting). Set at startup.</summary>
    public Func<bool>? IsOnFunc { get; set; }

    public bool IsOn
    {
        get
        {
            try { return IsOnFunc?.Invoke() == true; }
            catch { return false; }
        }
    }

    /// <summary>Called for every command pressed: a report opens the window, anything else closes it.</summary>
    public void CommandReceived(NavigationCommand command)
    {
        lock (_lock)
        {
            if (!CommandCatalog.IsOnDemandReport(command))
                _openUntil = 0;
            else if (command == NavigationCommand.SayAll)
                _openUntil = long.MaxValue;
            else
                _openUntil = _nowMs() + (long)Window.TotalMilliseconds;
        }
    }

    /// <summary>True while speech is the answer to a reporting command.</summary>
    public bool IsOpen
    {
        get { lock (_lock) return _nowMs() < _openUntil; }
    }

    /// <summary>Marks <paramref name="utterance"/> when the window is open.</summary>
    public Utterance Stamp(Utterance utterance) =>
        !utterance.OnDemand && IsOpen ? utterance with { OnDemand = true } : utterance;

    /// <summary>True when the utterance must be dropped: the mode is on and it isn't marked.</summary>
    public bool Drops(Utterance utterance) => IsOn && !utterance.OnDemand;
}
