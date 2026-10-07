namespace Vox.Core.Text;

/// <summary>Units of text movement and reading, as in UIA's TextUnit.</summary>
public enum TextUnit
{
    Character,
    Word,
    Line,
    Paragraph,
    Document,
}

/// <summary>One end of a text range.</summary>
public enum TextEndpoint
{
    Start,
    End,
}

/// <summary>
/// Formatting at a point in the text. Null means unknown or mixed (UIA's "mixed attribute"/"not
/// supported" values).
/// </summary>
public sealed record TextAttributes
{
    public static readonly TextAttributes Unknown = new();

    public string? FontName { get; init; }
    public double? FontSize { get; init; }
    public bool? IsBold { get; init; }
    public bool? IsItalic { get; init; }
    public bool? IsUnderline { get; init; }
    public string? ForegroundColor { get; init; }

    /// <summary>The text is marked as a spelling error.</summary>
    public bool IsSpellingError { get; init; }

    /// <summary>The text is marked as a grammar error.</summary>
    public bool IsGrammarError { get; init; }

    /// <summary>BCP 47 language of the text, when the provider reports it.</summary>
    public string? Language { get; init; }
}

/// <summary>
/// A span of text in an <see cref="ITextDocument"/>. Ranges are immutable: operations return new
/// ranges. The model is UIA's ITextRangeProvider, so a UIA text range can implement it directly,
/// and so can the virtual buffer and plain strings.
/// </summary>
public interface ITextRange
{
    /// <summary>The text of the range, at most <paramref name="maxLength"/> characters (-1: all).</summary>
    string GetText(int maxLength = -1);

    /// <summary>The range is empty (a caret position).</summary>
    bool IsDegenerate { get; }

    /// <summary>The smallest range of whole <paramref name="unit"/>s containing this range's start.</summary>
    ITextRange ExpandToEnclosingUnit(TextUnit unit);

    /// <summary>
    /// Moves by <paramref name="count"/> units (negative: backwards). A degenerate range stays
    /// degenerate at the start of the unit it reaches; a non-degenerate range becomes that whole
    /// unit. Returns the new range and how many units were actually moved (fewer at either end).
    /// </summary>
    (ITextRange Range, int Moved) Move(TextUnit unit, int count);

    /// <summary>
    /// Compares this range's <paramref name="endpoint"/> with <paramref name="other"/>'s
    /// <paramref name="otherEndpoint"/>: negative if before, 0 if equal, positive if after.
    /// </summary>
    int CompareEndpoints(TextEndpoint endpoint, ITextRange other, TextEndpoint otherEndpoint);

    /// <summary>
    /// A copy of this range with <paramref name="endpoint"/> moved to <paramref name="other"/>'s
    /// <paramref name="otherEndpoint"/> (the other end follows if it would cross).
    /// </summary>
    ITextRange WithEndpoint(TextEndpoint endpoint, ITextRange other, TextEndpoint otherEndpoint);

    /// <summary>Formatting at the start of the range.</summary>
    TextAttributes GetAttributes();
}

/// <summary>
/// A text document with a caret and selection: an edit control (through UIA TextPattern or its
/// value), a terminal, or the virtual buffer.
/// </summary>
public interface ITextDocument
{
    /// <summary>The whole document.</summary>
    ITextRange DocumentRange { get; }

    /// <summary>A degenerate range at the caret, or null if the caret position is unknown.</summary>
    ITextRange? GetCaret();

    /// <summary>
    /// The selected ranges (usually one). A degenerate range means nothing is selected; an empty
    /// list means the control doesn't report selection.
    /// </summary>
    IReadOnlyList<ITextRange> GetSelection();
}
