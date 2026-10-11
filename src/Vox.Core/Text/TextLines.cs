namespace Vox.Core.Text;

/// <summary>A position's line of text and where in it the position is (for braille).</summary>
/// <param name="Line">The line's range.</param>
/// <param name="Text">The line's text (at most <see cref="TextLines.MaxLineLength"/> characters).</param>
/// <param name="Offset">The position's offset in <paramref name="Text"/>.</param>
public sealed record TextLineAt(ITextRange Line, string Text, int Offset);

/// <summary>Lines of an <see cref="ITextDocument"/> as braille shows them: text plus offsets in it.</summary>
public static class TextLines
{
    /// <summary>The longest line read (a whole document without line breaks is still one line).</summary>
    public const int MaxLineLength = 2000;

    /// <summary>The line holding <paramref name="position"/>'s start, and where in it that is.</summary>
    public static TextLineAt LineAt(ITextRange position)
    {
        var line = position.ExpandToEnclosingUnit(TextUnit.Line);
        var text = line.GetText(MaxLineLength);
        return new TextLineAt(line, text, OffsetIn(line, text.Length, position));
    }

    /// <summary>
    /// The part of <paramref name="selection"/> on <paramref name="line"/>, as offsets in the line's
    /// text; null when nothing on the line is selected.
    /// </summary>
    public static (int Start, int End)? SelectionIn(TextLineAt line, ITextRange? selection)
    {
        if (selection is null || selection.IsDegenerate)
            return null;
        // Entirely before or after the line
        if (selection.CompareEndpoints(TextEndpoint.End, line.Line, TextEndpoint.Start) <= 0
            || selection.CompareEndpoints(TextEndpoint.Start, line.Line, TextEndpoint.End) >= 0)
            return null;
        var start = selection.CompareEndpoints(TextEndpoint.Start, line.Line, TextEndpoint.Start) <= 0
            ? 0
            : OffsetIn(line.Line, line.Text.Length, selection);
        var end = selection.CompareEndpoints(TextEndpoint.End, line.Line, TextEndpoint.End) >= 0
            ? line.Text.Length
            : OffsetIn(line.Line, line.Text.Length, selection.WithEndpoint(TextEndpoint.Start, selection, TextEndpoint.End));
        return end > start ? (start, end) : null;
    }

    /// <summary>A degenerate range <paramref name="offset"/> characters into <paramref name="line"/> (clamped to it).</summary>
    public static ITextRange PositionIn(ITextRange line, int offset)
    {
        var start = line.WithEndpoint(TextEndpoint.End, line, TextEndpoint.Start);
        if (offset <= 0)
            return start;
        var (moved, count) = start.Move(TextUnit.Character, offset);
        // Not past the line's end; a move cut short ran into the document's end (a range moving by
        // character may stop before its last character), which is the end of this, the last line
        return count < offset || moved.CompareEndpoints(TextEndpoint.Start, line, TextEndpoint.End) > 0
            ? line.WithEndpoint(TextEndpoint.Start, line, TextEndpoint.End)
            : moved;
    }

    /// <summary>How many characters <paramref name="position"/>'s start is into <paramref name="line"/> (0 to its length).</summary>
    private static int OffsetIn(ITextRange line, int lineLength, ITextRange position)
    {
        if (position.CompareEndpoints(TextEndpoint.Start, line, TextEndpoint.Start) <= 0)
            return 0;
        var before = line.WithEndpoint(TextEndpoint.End, position, TextEndpoint.Start);
        return Math.Min(before.GetText(MaxLineLength).Length, lineLength);
    }
}
