using Vox.Core.Buffer;
using Vox.Core.Input;

namespace Vox.Core.Navigation;

/// <summary>What a selection command changed: the text newly selected or unselected.</summary>
public readonly record struct SelectionChange(string Text, bool Selected);

/// <summary>
/// The browse-mode selection: from an anchor (where selecting started) to the active end, which
/// the browse cursor follows (Shift+arrows, Ctrl+Shift+arrows, Shift+Home/End, as in an edit
/// control). The active end may be the end of the text, where the cursor itself can't go.
/// Moving the cursor any other way, or a new document, ends the selection.
/// Pipeline thread only.
/// </summary>
public sealed class BrowseSelection
{
    private VBufferDocument? _document;
    private int _cursorOffset;

    public int Anchor { get; private set; }
    public int Active { get; private set; }

    /// <summary>Start (inclusive) and end (exclusive) of the selected text.</summary>
    public int Start => Math.Min(Anchor, Active);
    public int End => Math.Max(Anchor, Active);

    /// <summary>The selected text, or empty when nothing is selected for <paramref name="cursor"/>.</summary>
    public string TextFor(VBufferCursor cursor) =>
        IsValidFor(cursor) ? cursor.Document.FlatText[Start..End] : string.Empty;

    /// <summary>
    /// Whether the selection still applies: same document, and the cursor is where the last
    /// selection command left it.
    /// </summary>
    public bool IsValidFor(VBufferCursor cursor) =>
        _document is not null && ReferenceEquals(_document, cursor.Document) && cursor.TextOffset == _cursorOffset
        && End <= cursor.Document.FlatText.Length;

    public void Clear() => _document = null;

    public static bool IsSelectionCommand(NavigationCommand command) => command is
        NavigationCommand.SelectNextChar or NavigationCommand.SelectPrevChar or
        NavigationCommand.SelectNextWord or NavigationCommand.SelectPrevWord or
        NavigationCommand.SelectNextLine or NavigationCommand.SelectPrevLine or
        NavigationCommand.SelectToStartOfLine or NavigationCommand.SelectToEndOfLine or
        NavigationCommand.SelectToTop or NavigationCommand.SelectToBottom or
        NavigationCommand.SelectAll;

    /// <summary>
    /// Moves the active end (and the cursor) by the command's unit, starting a selection at the
    /// cursor if there is none. Returns what changed, or null when the end can't move (at the
    /// start or end of the text).
    /// </summary>
    public SelectionChange? Extend(VBufferCursor cursor, NavigationCommand command)
    {
        if (!IsValidFor(cursor))
        {
            _document = cursor.Document;
            Anchor = Active = cursor.TextOffset;
        }

        string text = cursor.Document.FlatText;
        int oldActive = Active;
        if (command == NavigationCommand.SelectAll)
        {
            Anchor = 0;
            oldActive = 0;
        }

        if (Target(cursor, oldActive, command) is not { } target || (target == oldActive && command != NavigationCommand.SelectAll))
            return null;

        Active = target;
        cursor.MoveTo(target);
        _cursorOffset = cursor.TextOffset;

        int oldSide = Math.Sign(oldActive - Anchor);
        int newSide = Math.Sign(target - Anchor);
        if (oldSide != 0 && newSide != 0 && oldSide != newSide)
            return new SelectionChange(text[Start..End], true); // crossed the anchor

        return new SelectionChange(
            text[Math.Min(oldActive, target)..Math.Max(oldActive, target)],
            Math.Abs(target - Anchor) > Math.Abs(oldActive - Anchor));
    }

    /// <summary>
    /// The document was updated: keeps the selection when the replaced text
    /// [<paramref name="oldStart"/>, <paramref name="oldEnd"/>) is outside it (shifted by
    /// <paramref name="delta"/> when it was before it), otherwise ends it.
    /// </summary>
    public void Rebase(VBufferDocument updated, int oldStart, int oldEnd, int delta, int cursorOffset)
    {
        if (_document is null)
            return;
        if (Start >= oldEnd)
        {
            Anchor += delta;
            Active += delta;
        }
        else if (End > oldStart)
        {
            Clear();
            return;
        }
        _document = updated;
        _cursorOffset = cursorOffset;
    }

    /// <summary>Where the active end moves to from <paramref name="active"/>, or null at the edge.</summary>
    private static int? Target(VBufferCursor cursor, int active, NavigationCommand command)
    {
        string text = cursor.Document.FlatText;
        int length = text.Length;
        switch (command)
        {
            case NavigationCommand.SelectNextChar:
                return active < length ? active + 1 : null;

            case NavigationCommand.SelectPrevChar:
                return active > 0 ? active - 1 : null;

            case NavigationCommand.SelectNextWord:
            {
                // To the start of the next word, so a word is selected with its following space
                if (active >= length)
                    return null;
                int pos = active;
                while (pos < length && !char.IsWhiteSpace(text[pos]))
                    pos++;
                while (pos < length && char.IsWhiteSpace(text[pos]))
                    pos++;
                return pos;
            }

            case NavigationCommand.SelectPrevWord:
            {
                if (active == 0)
                    return null;
                int pos = active - 1;
                while (pos > 0 && char.IsWhiteSpace(text[pos]))
                    pos--;
                while (pos > 0 && !char.IsWhiteSpace(text[pos - 1]))
                    pos--;
                return pos;
            }

            case NavigationCommand.SelectNextLine:
            {
                // The same column of the next line (or its end); past the last line, the end
                if (active >= length)
                    return null;
                int column = active - cursor.LineStartAt(active);
                int end = cursor.LineEndAt(active);
                int next = end < length && text[end] == '\n' ? end + 1 : end;
                if (next >= length)
                    return length;
                return next + Math.Min(column, cursor.LineEndAt(next) - next);
            }

            case NavigationCommand.SelectPrevLine:
            {
                if (active == 0)
                    return null;
                int start = cursor.LineStartAt(active);
                if (start == 0)
                    return 0;
                int previous = cursor.LineStartAt(start - 1);
                return previous + Math.Min(active - start, cursor.LineEndAt(previous) - previous);
            }

            case NavigationCommand.SelectToStartOfLine:
                return cursor.LineStartAt(active);

            case NavigationCommand.SelectToEndOfLine:
                return active >= length ? length : cursor.LineEndAt(active);

            case NavigationCommand.SelectToTop:
                return 0;

            case NavigationCommand.SelectToBottom:
            case NavigationCommand.SelectAll:
                return length;

            default:
                return null;
        }
    }
}
