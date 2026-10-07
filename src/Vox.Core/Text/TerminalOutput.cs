namespace Vox.Core.Text;

/// <summary>
/// Works out what a terminal printed, from snapshots of its visible lines: the lines that are new
/// or changed since the previous snapshot, allowing for scrolling. Characters the user just typed
/// (echoed on the prompt line) are not reported as output.
/// </summary>
public sealed class TerminalOutputTracker
{
    /// <summary>Keys pressed within this long before a change may be what changed it (their echo).</summary>
    public static readonly TimeSpan EchoWindow = TimeSpan.FromMilliseconds(500);

    private readonly Func<DateTimeOffset> _clock;
    private IReadOnlyList<string> _lines = [];
    private int _keysSinceUpdate;
    private DateTimeOffset _lastKeyAt = DateTimeOffset.MinValue;

    public TerminalOutputTracker(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>A key that types was pressed in the terminal.</summary>
    public void NoteKeyPress()
    {
        _keysSinceUpdate++;
        _lastKeyAt = _clock();
    }

    /// <summary>Forgets the previous snapshot (focus moved to another terminal).</summary>
    public void Reset(IReadOnlyList<string>? lines = null)
    {
        _lines = lines ?? [];
        _keysSinceUpdate = 0;
    }

    /// <summary>
    /// Takes a new snapshot of the visible lines and returns the output to speak (non-blank new or
    /// changed lines, in order).
    /// </summary>
    public IReadOnlyList<string> Update(IReadOnlyList<string> lines)
    {
        var old = _lines;
        _lines = lines;
        int keys = _keysSinceUpdate;
        _keysSinceUpdate = 0;

        // The scroll that keeps the most lines unchanged
        int bestScroll = 0, bestFirstChange = FirstChange(old, lines, 0);
        for (int scroll = 1; scroll <= old.Count && bestFirstChange < lines.Count; scroll++)
        {
            int firstChange = FirstChange(old, lines, scroll);
            if (firstChange > bestFirstChange)
            {
                bestFirstChange = firstChange;
                bestScroll = scroll;
            }
        }

        var changed = new List<string>();
        for (int i = bestFirstChange; i < lines.Count; i++)
        {
            int oldIndex = i + bestScroll;
            // A line further down that didn't change (blank space below the prompt) isn't output
            if (oldIndex < old.Count && old[oldIndex] == lines[i])
                continue;
            changed.Add(lines[i]);
        }

        // Only the typed characters appeared at the end of one line: that's the user's own echo
        if (changed.Count == 1 && IsTypingEcho(old, lines, bestFirstChange, bestScroll, keys))
            return [];

        return changed.Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
    }

    private bool IsTypingEcho(IReadOnlyList<string> old, IReadOnlyList<string> lines, int index, int scroll, int keys)
    {
        if (keys == 0 || _clock() - _lastKeyAt > EchoWindow)
            return false;
        int oldIndex = index + scroll;
        if (oldIndex >= old.Count || index >= lines.Count)
            return false;
        // Terminals pad lines with spaces, so compare without trailing spaces; a typed space can
        // then look like one more character than was typed
        string before = old[oldIndex].TrimEnd(), after = lines[index].TrimEnd();
        int allowed = keys + 1;
        // Characters added (or removed, for Backspace) at the end of the line, no more than were typed
        if (after.StartsWith(before, StringComparison.Ordinal))
            return after.Length - before.Length <= allowed;
        if (before.StartsWith(after, StringComparison.Ordinal))
            return before.Length - after.Length <= allowed;
        return false;
    }

    /// <summary>Index of the first line of <paramref name="lines"/> that differs from <paramref name="old"/> scrolled up by <paramref name="scroll"/>.</summary>
    private static int FirstChange(IReadOnlyList<string> old, IReadOnlyList<string> lines, int scroll)
    {
        int i = 0;
        while (i < lines.Count && i + scroll < old.Count && old[i + scroll] == lines[i])
            i++;
        return i;
    }
}

/// <summary>
/// Paces terminal output for speech: at most one utterance per <see cref="Interval"/>, and a
/// burst of more than <see cref="MaxLines"/> lines is summarised as "N lines of output" followed
/// by its last line.
/// </summary>
public sealed class TerminalSpeechThrottle
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    public const int MaxLines = 20;

    private readonly List<string> _pending = new();
    private DateTimeOffset _lastSpoken = DateTimeOffset.MinValue;

    public bool HasPending => _pending.Count > 0;

    public void Add(IEnumerable<string> lines) => _pending.AddRange(lines);

    /// <summary>When the pending output may be spoken.</summary>
    public DateTimeOffset DueAt => _lastSpoken + Interval;

    /// <summary>
    /// The text to speak now, or null if nothing is pending or the interval hasn't passed.
    /// </summary>
    public string? Flush(DateTimeOffset now)
    {
        if (_pending.Count == 0 || now < DueAt)
            return null;
        string text = _pending.Count > MaxLines
            ? $"{_pending.Count} lines of output. {_pending[^1]}"
            : string.Join("\n", _pending);
        _pending.Clear();
        _lastSpoken = now;
        return text;
    }

    public void Clear() => _pending.Clear();
}
