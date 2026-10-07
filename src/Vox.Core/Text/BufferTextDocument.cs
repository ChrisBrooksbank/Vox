using Vox.Core.Buffer;

namespace Vox.Core.Text;

/// <summary>
/// The virtual buffer as an <see cref="ITextDocument"/>: the caret is the browse-mode cursor, and
/// lines are the cursor's lines (long lines split at <see cref="VBufferCursor.MaxLineLength"/>),
/// so text read through either says the same thing. Browse mode has no selection yet, so the
/// selection is the (degenerate) caret.
/// </summary>
public sealed class BufferTextDocument : ITextDocument
{
    private readonly VBufferCursor _cursor;
    private readonly BufferSegmenter _segmenter;

    public BufferTextDocument(VBufferCursor cursor)
    {
        _cursor = cursor;
        _segmenter = new BufferSegmenter(cursor);
    }

    private string Text => _cursor.Document.FlatText;

    public ITextRange DocumentRange => Range(0, Text.Length);

    public ITextRange? GetCaret() => Range(_cursor.TextOffset, _cursor.TextOffset);

    public IReadOnlyList<ITextRange> GetSelection() => [GetCaret()!];

    public OffsetTextRange Range(int start, int end) => new(Text, start, end, _segmenter);

    /// <summary>Plain-text units, except that lines are the cursor's (wrapped) lines.</summary>
    private sealed class BufferSegmenter(VBufferCursor cursor) : PlainTextSegmenter
    {
        public override (int Start, int End) Enclosing(string text, int offset, TextUnit unit)
        {
            if (unit != TextUnit.Line || !ReferenceEquals(text, cursor.Document.FlatText))
                return base.Enclosing(text, offset, unit);

            offset = Math.Clamp(offset, 0, text.Length);
            if (offset == text.Length && offset > 0)
                offset--;
            int start = cursor.LineStartAt(offset);
            int end = cursor.LineEndAt(offset);
            // The line break belongs to the line it ends
            if (end < text.Length && text[end] == '\n')
                end++;
            return (start, Math.Max(start, end));
        }
    }
}
