using System.Text;
using Vox.Core.Text;

namespace Vox.Core.Ocr;

/// <summary>
/// Recognised text as a reviewable <see cref="ITextDocument"/>: one line of text per recognised
/// line, words separated by spaces (not between Chinese or Japanese characters, which the
/// recogniser reports as separate words), and each word's place on the screen, so the word at
/// the review cursor can be clicked.
/// </summary>
public sealed class OcrDocument : ITextDocument
{
    private readonly List<(int Start, int End, OcrWord Word)> _words = new();
    private readonly StringTextDocument _text;

    public OcrDocument(OcrResult result)
    {
        var text = new StringBuilder();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0)
                continue;
            if (text.Length > 0)
                text.Append('\n');
            OcrWord? previous = null;
            foreach (var word in line.Words)
            {
                if (word.Text.Length == 0)
                    continue;
                if (previous is not null && !JoinsWithoutSpace(previous.Text, word.Text))
                    text.Append(' ');
                int start = text.Length;
                text.Append(word.Text);
                _words.Add((start, text.Length, word));
                previous = word;
            }
        }
        Text = text.ToString();
        _text = new StringTextDocument(Text);
    }

    /// <summary>All the recognised text.</summary>
    public string Text { get; }

    /// <summary>Nothing was recognised.</summary>
    public bool IsEmpty => Text.Length == 0;

    /// <summary>How many lines were recognised.</summary>
    public int LineCount => IsEmpty ? 0 : Text.Count(c => c == '\n') + 1;

    public ITextRange DocumentRange => _text.DocumentRange;

    // Review starts at the top
    public ITextRange? GetCaret() => _text.Range(0, 0);

    public IReadOnlyList<ITextRange> GetSelection() => [];

    /// <summary>
    /// The word at <paramref name="offset"/>: the one containing it, else (on a space or at the
    /// end of a line) the nearest one before it on the same line, else the next one.
    /// </summary>
    public OcrWord? WordAt(int offset)
    {
        if (_words.Count == 0)
            return null;
        (int Start, int End, OcrWord Word)? before = null;
        foreach (var entry in _words)
        {
            if (offset >= entry.Start && offset < entry.End)
                return entry.Word;
            if (entry.Start > offset)
            {
                bool sameLine = before is { } b && Text.IndexOf('\n', b.End, offset - b.End) < 0;
                return sameLine ? before!.Value.Word : entry.Word;
            }
            before = entry;
        }
        return before?.Word;
    }

    /// <summary>The word at a position (a range from this document).</summary>
    public OcrWord? WordAt(ITextRange position) =>
        position is OffsetTextRange range && ReferenceEquals(range.Text, Text) ? WordAt(range.Start) : null;

    // Chinese, Japanese and Korean (Hangul aside, which is spaced) are written without spaces
    private static bool JoinsWithoutSpace(string previous, string next) =>
        IsUnspacedScript(previous[^1]) && IsUnspacedScript(next[0]);

    private static bool IsUnspacedScript(char c) =>
        c is >= '\u3000' and <= '\u30FF' // CJK punctuation, hiragana, katakana
            or >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF' // ideographs
            or >= '\uFF00' and <= '\uFFEF'; // full-width forms
}
