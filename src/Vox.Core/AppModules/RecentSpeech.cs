namespace Vox.Core.AppModules;

/// <summary>
/// What Vox has said in the last moments, so an application's own announcement of the same text
/// (VS Code's alerts, Teams' and Zoom's repeated notifications) is not said a second time.
/// Thread-safe: speech is recorded from the speech queue's callers, looked up on the pipeline thread.
/// </summary>
public sealed class RecentSpeech
{
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Queue<(string Text, DateTimeOffset At)> _said = new();
    private readonly object _lock = new();
    private const int MaxEntries = 32;

    public RecentSpeech(TimeSpan? window = null, Func<DateTimeOffset>? clock = null)
    {
        _window = window ?? TimeSpan.FromSeconds(2);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Records text Vox is saying.</summary>
    public void Add(string text)
    {
        var key = Normalize(text);
        if (key.Length == 0)
            return;
        lock (_lock)
        {
            _said.Enqueue((key, _clock()));
            while (_said.Count > MaxEntries)
                _said.Dequeue();
        }
    }

    /// <summary>
    /// Whether <paramref name="text"/> was said within the window, alone or as part of a longer
    /// utterance ("Line 5" inside "Line 5, Column 1"); records it when it wasn't.
    /// </summary>
    public bool IsRepeat(string? text)
    {
        var key = Normalize(text);
        if (key.Length == 0)
            return false;
        var now = _clock();
        lock (_lock)
        {
            while (_said.Count > 0 && now - _said.Peek().At > _window)
                _said.Dequeue();
            // A short text ("1") counts only when said alone
            if (_said.Any(s => key.Length < 4 ? s.Text == key : s.Text.Contains(key, StringComparison.Ordinal)))
                return true;
        }
        Add(text!);
        return false;
    }

    private static string Normalize(string? text) =>
        text is null ? string.Empty : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
