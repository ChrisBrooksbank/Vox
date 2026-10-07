using Vox.Core.Input;

namespace Vox.Core.Text;

/// <summary>
/// Decides what to say when the caret moves in an edit control. The key that moved it says which
/// unit to read (see <see cref="CaretKeys"/>): after Down, the line the caret is now on; after
/// Ctrl+Right, the word. Caret moves with no caret key before them (typing, the app moving it)
/// say nothing here; typing echo covers typing.
/// </summary>
/// <remarks>
/// Thread-safe: keys arrive on the pipeline thread, caret changes are evaluated on the UIA thread
/// (the document's ranges belong to it).
/// </remarks>
public sealed class TextCaretTracker
{
    /// <summary>A caret key older than this when the caret moves is assumed not to have moved it.</summary>
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromSeconds(1);

    private readonly Func<DateTimeOffset> _clock;
    private readonly object _lock = new();
    private PendingKey? _pending;

    public TextCaretTracker(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private sealed record PendingKey(TextUnit Unit, bool ExtendsSelection, DateTimeOffset At);

    /// <summary>
    /// Notes a key press in the focused edit control. Returns true if it is a caret key whose
    /// caret move should be read.
    /// </summary>
    public bool NoteKey(int vkCode, KeyModifiers modifiers)
    {
        lock (_lock)
        {
            if (CaretKeys.TryGetUnit(vkCode, modifiers, out var unit, out var extends))
            {
                _pending = new PendingKey(unit, extends, _clock());
                return true;
            }
            _pending = null;
            return false;
        }
    }

    /// <summary>Forgets the pending key (focus moved to another control).</summary>
    public void Reset()
    {
        lock (_lock) _pending = null;
    }

    /// <summary>
    /// The caret moved in <paramref name="document"/>: returns what to say, or null if this move
    /// wasn't made by a caret key (or it was already read). Run on the document's thread.
    /// </summary>
    public string? OnCaretMoved(ITextDocument document)
    {
        PendingKey? key;
        lock (_lock)
        {
            key = _pending;
            _pending = null;
        }
        if (key is null || _clock() - key.At > KeyLifetime)
            return null;
        if (key.ExtendsSelection)
            return null; // selection speech is handled separately

        var caret = document.GetCaret();
        if (caret is null)
            return null;
        var text = caret.ExpandToEnclosingUnit(key.Unit).GetText(MaxSpokenLength);
        return TextSpeech.ForUnit(text, key.Unit);
    }

    /// <summary>Longest text read for one caret move (a huge paragraph is cut short).</summary>
    public const int MaxSpokenLength = 2000;
}
