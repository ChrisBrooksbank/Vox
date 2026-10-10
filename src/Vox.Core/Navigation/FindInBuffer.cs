namespace Vox.Core.Navigation;

/// <summary>What the user asked the find prompt to look for.</summary>
public sealed record FindRequest(string Text, bool MatchCase = false);

/// <summary>Where a search matched, and whether it went past the end (or start) to get there.</summary>
public readonly record struct FindMatch(int Offset, bool Wrapped);

/// <summary>
/// Finds text in the browse-mode buffer (Insert+Ctrl+F, Insert+F3 / Insert+Shift+F3).
/// Case-insensitive unless asked otherwise; a search that reaches the end of the page carries
/// on from the top (and from the bottom going back). Keeps the session's search history, most
/// recent first, which the prompt offers; it is never written to disk.
/// Used on the pipeline thread only.
/// </summary>
public sealed class FindInBuffer
{
    public const int MaxHistory = 20;

    private readonly List<string> _history = new();

    /// <summary>Earlier searches, most recent first.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>The last search, which next/previous repeat; null before the first one.</summary>
    public FindRequest? Last { get; private set; }

    /// <summary>Makes <paramref name="request"/> the last search and puts it first in the history.</summary>
    public void Remember(FindRequest request)
    {
        Last = request;
        _history.Remove(request.Text);
        _history.Insert(0, request.Text);
        if (_history.Count > MaxHistory)
            _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
    }

    /// <summary>
    /// The next match of <paramref name="query"/> starting at or after <paramref name="start"/>
    /// (forward), or starting before it (backward), wrapping round the text; null if there is none.
    /// </summary>
    public static FindMatch? Find(string text, string query, int start, bool forward, bool matchCase = false)
    {
        if (string.IsNullOrEmpty(query) || query.Length > text.Length)
            return null;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        start = Math.Clamp(start, 0, text.Length);

        if (forward)
        {
            int index = text.AsSpan(start).IndexOf(query.AsSpan(), comparison);
            if (index >= 0)
                return new FindMatch(start + index, false);
            // Anything before the start wasn't searched yet
            index = text.AsSpan(0, Math.Min(text.Length, start + query.Length - 1)).IndexOf(query.AsSpan(), comparison);
            return index >= 0 ? new FindMatch(index, true) : null;
        }
        else
        {
            // Matches that begin before the start
            int index = text.AsSpan(0, Math.Min(text.Length, start + query.Length - 1)).LastIndexOf(query.AsSpan(), comparison);
            if (index >= 0)
                return new FindMatch(index, false);
            index = text.AsSpan().LastIndexOf(query.AsSpan(), comparison);
            return index >= 0 ? new FindMatch(index, true) : null;
        }
    }
}
