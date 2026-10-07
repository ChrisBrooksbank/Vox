namespace Vox.Core.Speech;

/// <summary>
/// The most recent utterances, oldest first: for the speech viewer, and for reviewing what was
/// said. Thread-safe.
/// </summary>
public sealed class SpeechHistory
{
    private readonly object _lock = new();
    private readonly LinkedList<string> _entries = new();
    private readonly int _capacity;

    public SpeechHistory(int capacity = 500)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Raised (on the adding thread) after an entry is added.</summary>
    public event EventHandler<string>? Added;

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        lock (_lock)
        {
            _entries.AddLast(text);
            while (_entries.Count > _capacity)
                _entries.RemoveFirst();
        }
        Added?.Invoke(this, text);
    }

    public IReadOnlyList<string> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }
}
