namespace Vox.Core.Text;

/// <summary>
/// The virtual buffer for the review cursor while browsing: a document the review owns (safe to
/// use on another thread), the buffer it shows and the browse cursor's offset in it.
/// </summary>
public sealed record ReviewTether(ITextDocument Document, object Buffer, int Offset);

/// <summary>What a review command says: the text of the unit reached, and whether it hit an edge.</summary>
public readonly record struct ReviewResult(string Text, bool AtBoundary);

/// <summary>
/// A position in an <see cref="ITextDocument"/> that moves by character, word and line without
/// moving the caret, so text can be reviewed (read, spelled) in place. Works over any text
/// document — a UIA text pattern, an edit's value, a window's text, the virtual buffer — and
/// must be used on the thread its document belongs to (the UIA thread for UIA documents).
/// </summary>
public sealed class ReviewCursor
{
    private ITextRange? _position;

    /// <summary>The document under review, or null before <see cref="Attach"/>.</summary>
    public ITextDocument? Document { get; private set; }

    /// <summary>The cursor position (a degenerate range), or null without a document.</summary>
    public ITextRange? Position => _position;

    /// <summary>
    /// Reviews <paramref name="document"/> from <paramref name="position"/> (its caret when null,
    /// or its start when it has no caret).
    /// </summary>
    public void Attach(ITextDocument? document, ITextRange? position = null)
    {
        Document = document;
        if (document is null)
        {
            _position = null;
            return;
        }
        var start = position ?? SafeCaret(document) ?? document.DocumentRange;
        _position = Collapse(start);
    }

    /// <summary>Forgets the document (the next command attaches a new one).</summary>
    public void Detach() => Attach(null);

    /// <summary>Puts the cursor at <paramref name="position"/> in the current document.</summary>
    public void MoveTo(ITextRange position)
    {
        if (Document is not null)
            _position = Collapse(position);
    }

    /// <summary>The <paramref name="unit"/> at the cursor; null without a document.</summary>
    public ReviewResult? Read(TextUnit unit) =>
        _position is { } position ? new ReviewResult(Speak(position, unit), false) : null;

    /// <summary>The raw text of the <paramref name="unit"/> at the cursor (for spelling and copying).</summary>
    public string? TextOf(TextUnit unit) =>
        _position?.ExpandToEnclosingUnit(unit).GetText();

    /// <summary>
    /// Moves to the previous (<paramref name="direction"/> &lt; 0) or next <paramref name="unit"/>
    /// and reads it. At either end the cursor stays put and the current unit is read again with
    /// <see cref="ReviewResult.AtBoundary"/> set. Null without a document.
    /// </summary>
    public ReviewResult? Move(TextUnit unit, int direction)
    {
        if (_position is not { } position)
            return null;
        int step = direction < 0 ? -1 : 1;
        var (moved, count) = position.Move(unit, step);
        if (count == 0)
            return new ReviewResult(Speak(position, unit), true);
        // A line break on its own is a "word" in some documents: step over it, as Ctrl+arrow does
        if (unit == TextUnit.Word && IsLineBreakOnly(moved.ExpandToEnclosingUnit(unit).GetText()))
        {
            var (further, furtherCount) = moved.Move(unit, step);
            if (furtherCount != 0)
                moved = further;
        }
        _position = Collapse(moved);
        return new ReviewResult(Speak(_position, unit), false);
    }

    /// <summary>Moves to the first line and reads it.</summary>
    public ReviewResult? Top()
    {
        if (Document is null)
            return null;
        _position = Collapse(Document.DocumentRange);
        return Read(TextUnit.Line);
    }

    /// <summary>Moves to the last line (not an empty one after a final line break) and reads it.</summary>
    public ReviewResult? Bottom()
    {
        if (Document is null)
            return null;
        var document = Document.DocumentRange;
        var end = document.WithEndpoint(TextEndpoint.Start, document, TextEndpoint.End);
        if (end.ExpandToEnclosingUnit(TextUnit.Line).GetText().Length == 0)
            end = end.Move(TextUnit.Line, -1).Range;
        _position = Collapse(end);
        return Read(TextUnit.Line);
    }

    private static bool IsLineBreakOnly(string text) =>
        text.Length > 0 && text.Trim(' ', '\t').Length > 0 && text.Trim(' ', '\t', '\r', '\n').Length == 0;

    private static string Speak(ITextRange position, TextUnit unit) =>
        TextSpeech.ForUnit(position.ExpandToEnclosingUnit(unit).GetText(), unit);

    /// <summary>A degenerate range at <paramref name="range"/>'s start.</summary>
    private static ITextRange Collapse(ITextRange range) =>
        range.IsDegenerate ? range : range.WithEndpoint(TextEndpoint.End, range, TextEndpoint.Start);

    private static ITextRange? SafeCaret(ITextDocument document)
    {
        try { return document.GetCaret(); }
        catch { return null; }
    }
}
