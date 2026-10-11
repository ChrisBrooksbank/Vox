namespace Vox.Core.Braille;

/// <summary>What a braille line shows, which decides what a routing key does on it.</summary>
public enum BrailleLineKind
{
    /// <summary>The focused object and its context; routing activates it.</summary>
    Focus,
    /// <summary>The caret's line in an edit control; routing moves the caret.</summary>
    EditLine,
    /// <summary>The browse-mode cursor's line of a page; routing moves the cursor, a second press activates.</summary>
    BrowseLine,
    /// <summary>The review cursor's line; routing moves the review cursor.</summary>
    Review,
    /// <summary>A message shown for a few seconds; any key dismisses it.</summary>
    Message,
}

/// <summary>Where the window starts on a line with no cursor (a newly focused object).</summary>
public enum BrailleAnchorMode
{
    /// <summary>At the line's start.</summary>
    Start,
    /// <summary>At <see cref="BrailleLine.AnchorOffset"/>: what comes before it is seen by panning back.</summary>
    AtAnchor,
    /// <summary>Ending with the line, starting no later than the anchor: the context before it fills the display.</summary>
    FillBeforeAnchor,
}

/// <summary>
/// A line of braille content, as text (translated by the viewport): what it shows, where the
/// cursor and selection are on it, and where each character came from.
/// </summary>
public sealed record BrailleLine
{
    public required BrailleLineKind Kind { get; init; }

    /// <summary>The text shown (one line, no line breaks).</summary>
    public required string Text { get; init; }

    /// <summary>The cursor's offset in <see cref="Text"/> (0 to its length), or null when there is none.</summary>
    public int? CursorOffset { get; init; }

    /// <summary>The selected part of <see cref="Text"/> (start inclusive, end exclusive), or null.</summary>
    public (int Start, int End)? Selection { get; init; }

    /// <summary>The offset the window starts from on a line without a cursor (the focused object's start).</summary>
    public int AnchorOffset { get; init; }

    public BrailleAnchorMode AnchorMode { get; init; } = BrailleAnchorMode.Start;

    /// <summary>
    /// For each character of <see cref="Text"/>, the offset it came from in the source (the caret
    /// line's text, the page); null when the text is the source (offset i is source offset
    /// <see cref="SourceStart"/> + i). Labels Vox added ("lnk") map to the element they follow.
    /// </summary>
    public int[]? SourceOffsets { get; init; }

    /// <summary>The source offset of the first character when <see cref="SourceOffsets"/> is null.</summary>
    public int SourceStart { get; init; }

    /// <summary>The source offset just after the line, where routing past its last character goes.</summary>
    public int SourceEnd { get; init; }

    /// <summary>The source offset shown at text offset <paramref name="offset"/> (past the end: <see cref="SourceEnd"/>).</summary>
    public int SourceOffsetAt(int offset)
    {
        if (offset >= Text.Length)
            return SourceEnd;
        offset = Math.Max(offset, 0);
        return SourceOffsets is { } map ? map[offset] : SourceStart + offset;
    }

    /// <summary>The text offset showing source offset <paramref name="source"/>, or null when it isn't on this line.</summary>
    public int? TextOffsetOf(int source)
    {
        if (SourceOffsets is not { } map)
        {
            var offset = source - SourceStart;
            return offset >= 0 && offset <= Text.Length ? offset : null;
        }
        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] == source)
                return i;
        }
        return source == SourceEnd ? Text.Length : null;
    }

    public static BrailleLine Message(string text) => new()
    {
        Kind = BrailleLineKind.Message,
        Text = text,
        SourceEnd = text.Length,
    };
}
