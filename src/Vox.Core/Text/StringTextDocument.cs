namespace Vox.Core.Text;

/// <summary>
/// An <see cref="ITextDocument"/> over a plain string with a caret and selection: for edit
/// controls known only by their value (no UIA TextPattern), and as a test double.
/// </summary>
public sealed class StringTextDocument : ITextDocument
{
    private readonly ITextSegmenter _segmenter;
    private readonly Func<int, TextAttributes>? _attributesAt;

    public StringTextDocument(string text, int caret = 0, int? selectionStart = null, int? selectionEnd = null,
        ITextSegmenter? segmenter = null, Func<int, TextAttributes>? attributesAt = null)
    {
        Text = text;
        Caret = Math.Clamp(caret, 0, text.Length);
        SelectionStart = Math.Clamp(selectionStart ?? Caret, 0, text.Length);
        SelectionEnd = Math.Clamp(selectionEnd ?? Caret, 0, text.Length);
        _segmenter = segmenter ?? PlainTextSegmenter.Instance;
        _attributesAt = attributesAt;
    }

    public string Text { get; }
    public int Caret { get; }
    public int SelectionStart { get; }
    public int SelectionEnd { get; }

    public ITextRange DocumentRange => Range(0, Text.Length);

    public ITextRange? GetCaret() => Range(Caret, Caret);

    public IReadOnlyList<ITextRange> GetSelection() => [Range(SelectionStart, SelectionEnd)];

    /// <summary>A range of this document by offsets.</summary>
    public OffsetTextRange Range(int start, int end) => new(Text, start, end, _segmenter, _attributesAt);
}
