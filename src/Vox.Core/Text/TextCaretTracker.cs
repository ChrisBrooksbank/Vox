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

    private sealed record PendingKey(TextUnit Unit, bool ExtendsSelection, DateTimeOffset At, bool SelectsAll = false);

    // The selection after the last caret evaluation, to tell what a Shift+key added or removed
    private ITextRange? _lastSelection;

    private const int VK_A = 0x41;

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
            if (vkCode == VK_A && modifiers == KeyModifiers.Ctrl)
            {
                _pending = new PendingKey(TextUnit.Document, true, _clock(), SelectsAll: true);
                return true;
            }
            _pending = null;
            return false;
        }
    }

    /// <summary>Forgets the pending key (focus moved to another control).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _pending = null;
            _lastSelection = null;
        }
    }

    /// <summary>
    /// The caret moved in <paramref name="document"/>: returns what to say, or null if this move
    /// wasn't made by a caret key (or it was already read). Run on the document's thread.
    /// </summary>
    public string? OnCaretMoved(ITextDocument document)
    {
        PendingKey? key;
        ITextRange? previousSelection;
        var selections = document.GetSelection();
        var selection = selections.Count > 0 ? selections[0] : null;
        lock (_lock)
        {
            key = _pending;
            _pending = null;
            previousSelection = _lastSelection;
            _lastSelection = selection;
        }
        if (key is null || _clock() - key.At > KeyLifetime)
            return null;
        if (key.ExtendsSelection)
            return selection is null ? null : DescribeSelectionChange(document, previousSelection, selection, key.SelectsAll);

        var caret = document.GetCaret();
        if (caret is null)
            return null;
        var text = caret.ExpandToEnclosingUnit(key.Unit).GetText(MaxSpokenLength);
        return TextSpeech.ForUnit(text, key.Unit);
    }

    /// <summary>
    /// What a selection change says: "selected X" for text added to the selection, "unselected X"
    /// for text removed from it, "all selected" when Ctrl+A selected the whole document.
    /// </summary>
    private static string? DescribeSelectionChange(ITextDocument document, ITextRange? previous, ITextRange current, bool selectsAll)
    {
        if (selectsAll)
        {
            var all = document.DocumentRange;
            bool coversAll = !current.IsDegenerate
                && current.CompareEndpoints(TextEndpoint.Start, all, TextEndpoint.Start) <= 0
                && current.CompareEndpoints(TextEndpoint.End, all, TextEndpoint.End) >= 0;
            return coversAll ? "all selected" : null;
        }

        // Nothing known before: everything selected now is new
        if (previous is null || previous.IsDegenerate)
            return current.IsDegenerate ? null : Phrase("selected", current);

        if (current.IsDegenerate)
            return Phrase("unselected", previous);

        // The selection jumped to the other side of its anchor (or elsewhere): say what is selected now
        bool disjoint = current.CompareEndpoints(TextEndpoint.Start, previous, TextEndpoint.End) >= 0
            || current.CompareEndpoints(TextEndpoint.End, previous, TextEndpoint.Start) <= 0;
        if (disjoint)
            return Phrase("selected", current);

        var parts = new List<string>(2);
        int startDelta = current.CompareEndpoints(TextEndpoint.Start, previous, TextEndpoint.Start);
        if (startDelta < 0)
            parts.Add(Phrase("selected", Between(current, current, TextEndpoint.Start, previous, TextEndpoint.Start)));
        else if (startDelta > 0)
            parts.Add(Phrase("unselected", Between(previous, previous, TextEndpoint.Start, current, TextEndpoint.Start)));

        int endDelta = current.CompareEndpoints(TextEndpoint.End, previous, TextEndpoint.End);
        if (endDelta > 0)
            parts.Add(Phrase("selected", Between(current, previous, TextEndpoint.End, current, TextEndpoint.End)));
        else if (endDelta < 0)
            parts.Add(Phrase("unselected", Between(previous, current, TextEndpoint.End, previous, TextEndpoint.End)));

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The text from <paramref name="from"/>'s endpoint to <paramref name="to"/>'s, in <paramref name="source"/>'s document.</summary>
    private static ITextRange Between(ITextRange source, ITextRange from, TextEndpoint fromEndpoint, ITextRange to, TextEndpoint toEndpoint) =>
        source.WithEndpoint(TextEndpoint.Start, from, fromEndpoint).WithEndpoint(TextEndpoint.End, to, toEndpoint);

    private static string Phrase(string verb, ITextRange range)
    {
        var text = range.GetText(MaxSpokenLength);
        var spoken = text.Length <= 2 ? TextSpeech.ForCharacter(text) : TextSpeech.ForUnit(text, TextUnit.Word);
        return $"{verb} {spoken}";
    }

    /// <summary>Longest text read for one caret move (a huge paragraph is cut short).</summary>
    public const int MaxSpokenLength = 2000;
}
