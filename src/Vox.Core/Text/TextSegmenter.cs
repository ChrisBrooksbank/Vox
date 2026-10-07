namespace Vox.Core.Text;

/// <summary>
/// Finds unit boundaries in a string: the half-open span of the <see cref="TextUnit"/> containing
/// an offset. Used by <see cref="OffsetTextRange"/>.
/// </summary>
public interface ITextSegmenter
{
    /// <summary>The span [Start, End) of the unit containing <paramref name="offset"/>.</summary>
    (int Start, int End) Enclosing(string text, int offset, TextUnit unit);
}

/// <summary>
/// Plain-text units, as edit controls report them:
/// <list type="bullet">
/// <item>Character: one character; "\r\n" and surrogate pairs count as one.</item>
/// <item>Word: a run of non-space characters plus the spaces after it (not crossing a line break);
/// a run of spaces at the start of a line is a word of its own.</item>
/// <item>Line and Paragraph: up to and including the line break ("\n", "\r\n" or "\r").</item>
/// <item>Document: everything.</item>
/// </list>
/// </summary>
public class PlainTextSegmenter : ITextSegmenter
{
    public static readonly PlainTextSegmenter Instance = new();

    public virtual (int Start, int End) Enclosing(string text, int offset, TextUnit unit)
    {
        int length = text.Length;
        offset = Math.Clamp(offset, 0, length);

        switch (unit)
        {
            case TextUnit.Document:
                return (0, length);

            case TextUnit.Line:
            case TextUnit.Paragraph:
                return HardLine(text, offset);

            case TextUnit.Word:
                return Word(text, offset);

            default:
                return Character(text, offset);
        }
    }

    protected static (int Start, int End) Character(string text, int offset)
    {
        int length = text.Length;
        if (offset >= length)
            return (length, length);
        // Inside a pair: report the whole pair
        if (offset > 0 && IsPair(text, offset - 1))
            offset--;
        return (offset, offset + (IsPair(text, offset) ? 2 : 1));
    }

    private static bool IsPair(string text, int i) =>
        i + 1 < text.Length &&
        ((text[i] == '\r' && text[i + 1] == '\n') || (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1])));

    protected static bool IsLineBreak(char c) => c is '\n' or '\r';

    /// <summary>The line containing <paramref name="offset"/>, including its line break.</summary>
    protected static (int Start, int End) HardLine(string text, int offset)
    {
        int length = text.Length;
        int start = offset;
        // A line break belongs to the line it ends
        if (start > 0 && start < length && text[start] == '\n' && text[start - 1] == '\r')
            start--;
        while (start > 0 && !IsLineBreak(text[start - 1]))
            start--;
        int end = offset;
        while (end < length && !IsLineBreak(text[end]))
            end++;
        if (end < length)
            end += end + 1 < length && text[end] == '\r' && text[end + 1] == '\n' ? 2 : 1;
        if (offset >= length && start == length && length > 0 && !IsLineBreak(text[length - 1]))
            start = HardLine(text, length - 1).Start;
        return (start, end);
    }

    private static (int Start, int End) Word(string text, int offset)
    {
        int length = text.Length;
        if (offset >= length)
            return (length, length);
        if (IsLineBreak(text[offset]))
            return Character(text, offset);

        var (lineStart, lineEnd) = HardLine(text, offset);
        int contentEnd = lineEnd;
        while (contentEnd > lineStart && IsLineBreak(text[contentEnd - 1]))
            contentEnd--;

        int start = offset;
        if (char.IsWhiteSpace(text[start]))
        {
            // Back over the spaces to the word they follow
            while (start > lineStart && char.IsWhiteSpace(text[start - 1]))
                start--;
            if (start == lineStart)
            {
                // Leading spaces: a word of their own
                int spacesEnd = offset;
                while (spacesEnd < contentEnd && char.IsWhiteSpace(text[spacesEnd]))
                    spacesEnd++;
                return (lineStart, spacesEnd);
            }
        }
        while (start > lineStart && !char.IsWhiteSpace(text[start - 1]))
            start--;

        int end = start;
        while (end < contentEnd && !char.IsWhiteSpace(text[end]))
            end++;
        while (end < contentEnd && char.IsWhiteSpace(text[end]))
            end++;
        return (start, end);
    }
}
