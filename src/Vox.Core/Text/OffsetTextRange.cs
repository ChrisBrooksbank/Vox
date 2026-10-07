namespace Vox.Core.Text;

/// <summary>
/// A text range over a string, by character offsets. Unit boundaries come from an
/// <see cref="ITextSegmenter"/>, so the same range type serves plain text and the virtual buffer.
/// </summary>
public sealed class OffsetTextRange : ITextRange
{
    private readonly Func<int, TextAttributes>? _attributesAt;

    public OffsetTextRange(string text, int start, int end, ITextSegmenter? segmenter = null,
        Func<int, TextAttributes>? attributesAt = null)
    {
        Text = text;
        Start = Math.Clamp(Math.Min(start, end), 0, text.Length);
        End = Math.Clamp(Math.Max(start, end), 0, text.Length);
        Segmenter = segmenter ?? PlainTextSegmenter.Instance;
        _attributesAt = attributesAt;
    }

    /// <summary>The whole text the range is in.</summary>
    public string Text { get; }

    public int Start { get; }
    public int End { get; }
    public ITextSegmenter Segmenter { get; }

    public bool IsDegenerate => Start == End;

    private OffsetTextRange With(int start, int end) => new(Text, start, end, Segmenter, _attributesAt);

    public string GetText(int maxLength = -1)
    {
        int length = End - Start;
        if (maxLength >= 0 && maxLength < length)
            length = maxLength;
        return Text.Substring(Start, length);
    }

    public ITextRange ExpandToEnclosingUnit(TextUnit unit)
    {
        var (start, end) = Segmenter.Enclosing(Text, Start, unit);
        return With(start, end);
    }

    public (ITextRange Range, int Moved) Move(TextUnit unit, int count)
    {
        int position = Segmenter.Enclosing(Text, Start, unit).Start;
        // A degenerate range part-way into a unit: moving back first reaches that unit's start
        int moved = 0;
        if (count < 0 && IsDegenerate && Start > position)
        {
            moved = -1;
            count++;
        }

        while (count > 0)
        {
            int next = Segmenter.Enclosing(Text, position, unit).End;
            if (next <= position || next >= Text.Length)
                break;
            position = next;
            count--;
            moved++;
        }
        while (count < 0)
        {
            if (position == 0)
                break;
            position = Segmenter.Enclosing(Text, position - 1, unit).Start;
            count++;
            moved--;
        }

        if (IsDegenerate)
            return (With(position, position), moved);
        var (start, end) = Segmenter.Enclosing(Text, position, unit);
        return (With(start, end), moved);
    }

    public int CompareEndpoints(TextEndpoint endpoint, ITextRange other, TextEndpoint otherEndpoint)
    {
        var o = AsOffsetRange(other);
        return Point(this, endpoint).CompareTo(Point(o, otherEndpoint));
    }

    public ITextRange WithEndpoint(TextEndpoint endpoint, ITextRange other, TextEndpoint otherEndpoint)
    {
        int point = Point(AsOffsetRange(other), otherEndpoint);
        return endpoint == TextEndpoint.Start
            ? With(point, Math.Max(point, End))
            : With(Math.Min(Start, point), point);
    }

    public TextAttributes GetAttributes() => _attributesAt?.Invoke(Start) ?? TextAttributes.Unknown;

    private static int Point(OffsetTextRange range, TextEndpoint endpoint) =>
        endpoint == TextEndpoint.Start ? range.Start : range.End;

    private OffsetTextRange AsOffsetRange(ITextRange other) =>
        other as OffsetTextRange ?? throw new ArgumentException("Ranges from different kinds of document can't be compared", nameof(other));

    public override string ToString() => $"[{Start},{End}) \"{GetText(40)}\"";
}
