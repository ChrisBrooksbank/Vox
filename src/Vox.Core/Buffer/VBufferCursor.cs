using Vox.Core.Audio;

namespace Vox.Core.Buffer;

/// <summary>
/// Maintains a position within a <see cref="VBufferDocument"/> and provides
/// movement operations at character, word, and line granularity.
///
/// Position is represented as (currentNode, textOffset) where textOffset is
/// an absolute index into VBufferDocument.FlatText.
///
/// Boundary behaviour:
///   - Attempting to move past the start/end of the document plays boundary.wav
///     and does NOT wrap (position stays at boundary).
///   - If wrap is enabled (WrapEnabled = true) the cursor wraps to the opposite
///     end and plays wrap.wav instead.
/// </summary>
public sealed class VBufferCursor
{
    private readonly IAudioCuePlayer _audioCuePlayer;
    private VBufferDocument _document;
    private int _offset;       // absolute offset into FlatText

    public bool WrapEnabled { get; set; } = false;

    /// <summary>When false, boundary and wrap cues are not played (e.g. for Say All's cursor).</summary>
    public bool PlayCues { get; set; } = true;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public VBufferCursor(VBufferDocument document, IAudioCuePlayer audioCuePlayer)
    {
        _document = document;
        _audioCuePlayer = audioCuePlayer;
        _offset = 0;
    }

    // -------------------------------------------------------------------------
    // Public position
    // -------------------------------------------------------------------------

    /// <summary>Current absolute offset into FlatText.</summary>
    public int TextOffset => _offset;

    /// <summary>The document the cursor moves over.</summary>
    public VBufferDocument Document => _document;

    /// <summary>Node that covers the current offset (may be null for empty document).</summary>
    public VBufferNode? CurrentNode => _document.FindNodeAtOffset(_offset);

    /// <summary>Current character at the cursor (or '\0' if at boundary).</summary>
    public char CurrentChar =>
        _offset < _document.FlatText.Length ? _document.FlatText[_offset] : '\0';

    // -------------------------------------------------------------------------
    // Replaces the document (e.g. after incremental update)
    // -------------------------------------------------------------------------

    public void SetDocument(VBufferDocument document, int offset = 0)
    {
        _document = document;
        MoveTo(offset);
    }

    /// <summary>Moves the cursor to an absolute offset, clamped to the document.</summary>
    public void MoveTo(int offset)
    {
        _offset = Math.Clamp(offset, 0, Math.Max(0, _document.FlatText.Length - 1));
    }

    // -------------------------------------------------------------------------
    // Character movement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Move one character forward.
    /// Returns the character now under the cursor, or null if at/past end.
    /// </summary>
    public char? NextChar()
    {
        int newOffset = _offset + 1;
        if (newOffset >= _document.FlatText.Length)
        {
            return HandleBoundary(atEnd: true);
        }
        _offset = newOffset;
        return _document.FlatText[_offset];
    }

    /// <summary>Move one character backward.</summary>
    public char? PrevChar()
    {
        if (_offset == 0)
        {
            return HandleBoundary(atEnd: false);
        }
        _offset -= 1;
        return _document.FlatText[_offset];
    }

    // -------------------------------------------------------------------------
    // Word movement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Move to the start of the next word.
    /// Words are whitespace-delimited token boundaries in FlatText.
    /// Returns the word text, or null if at end.
    /// </summary>
    public string? NextWord()
    {
        string text = _document.FlatText;
        int len = text.Length;

        if (_offset >= len - 1)
            return HandleBoundaryString(atEnd: true, BoundaryUnit.Word);

        int pos = _offset;

        // Skip current word characters
        while (pos < len && !char.IsWhiteSpace(text[pos]))
            pos++;

        // Skip whitespace
        while (pos < len && char.IsWhiteSpace(text[pos]))
            pos++;

        if (pos >= len)
            return HandleBoundaryString(atEnd: true, BoundaryUnit.Word);

        _offset = pos;
        return ReadWordAt(_offset);
    }

    /// <summary>
    /// Move to the start of the previous word.
    /// Returns the word text, or null if at start.
    /// </summary>
    public string? PrevWord()
    {
        string text = _document.FlatText;

        if (_offset == 0)
            return HandleBoundaryString(atEnd: false, BoundaryUnit.Word);

        int pos = _offset - 1;

        // Skip whitespace backward
        while (pos > 0 && char.IsWhiteSpace(text[pos]))
            pos--;

        if (pos == 0 && char.IsWhiteSpace(text[pos]))
            return HandleBoundaryString(atEnd: false, BoundaryUnit.Word);

        // Find start of this word
        while (pos > 0 && !char.IsWhiteSpace(text[pos - 1]))
            pos--;

        _offset = pos;
        return ReadWordAt(_offset);
    }

    // -------------------------------------------------------------------------
    // Line movement
    //
    // A "line" is a run of text up to '\n' (one block after inline runs are joined), split
    // further at a word boundary when longer than MaxLineLength, as NVDA does, so a long
    // paragraph is read a line at a time. A "paragraph" is a whole '\n'-terminated run.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lines longer than this are split at the last word boundary before it (0 = never split).
    /// </summary>
    public int MaxLineLength { get; set; } = 100;

    /// <summary>
    /// Move to the start of the next line.
    /// Returns the line text, or null if at end.
    /// </summary>
    public string? NextLine()
    {
        string text = _document.FlatText;
        int len = text.Length;

        if (_offset >= len)
            return HandleBoundaryString(atEnd: true, BoundaryUnit.Line);

        int lineEnd = LineEndAt(_offset);
        int hardEnd = HardLineEnd(_offset);
        if (lineEnd < hardEnd)
        {
            // The next piece of a long line
            _offset = lineEnd;
            return ReadLineAt(_offset);
        }

        // hardEnd is the '\n' ending this line
        if (hardEnd >= len - 1)
            return HandleBoundaryString(atEnd: true, BoundaryUnit.Line);

        _offset = hardEnd + 1;
        return ReadLineAt(_offset);
    }

    /// <summary>
    /// Move to the start of the previous line (from anywhere in the current line).
    /// Returns the line text, or null if already on the first line.
    /// </summary>
    public string? PrevLine()
    {
        if (_document.FlatText.Length == 0)
            return HandleBoundaryString(atEnd: false, BoundaryUnit.Line);

        int currentLineStart = LineStartAt(_offset);
        if (currentLineStart == 0)
            return HandleBoundaryString(atEnd: false, BoundaryUnit.Line);

        // currentLineStart - 1 is the '\n' ending the previous line, or the end of the previous
        // piece of the same long line
        _offset = LineStartAt(currentLineStart - 1);
        return ReadLineAt(_offset);
    }

    /// <summary>Moves to the start of the current line and returns the character there, or null if empty.</summary>
    public char? StartOfLine()
    {
        _offset = LineStartAt(_offset);
        return CharAtCursor();
    }

    /// <summary>Moves to the last character of the current line and returns it, or null if the line is empty.</summary>
    public char? EndOfLine()
    {
        int start = LineStartAt(_offset);
        int end = LineEndAt(_offset);
        string text = _document.FlatText;
        // Stay off the '\n' and any trailing space a long line was split after
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;
        _offset = end > start ? end - 1 : start;
        return CharAtCursor();
    }

    /// <summary>Moves to the start of the document and returns its first line.</summary>
    public string TopOfDocument()
    {
        _offset = 0;
        return ReadLineAt(0);
    }

    /// <summary>Moves to the start of the last line and returns it.</summary>
    public string BottomOfDocument()
    {
        string text = _document.FlatText;
        if (text.Length == 0)
            return string.Empty;
        _offset = LineStartAt(text.Length - 1);
        return ReadLineAt(_offset);
    }

    /// <summary>
    /// Move to the start of the next paragraph (the next '\n'-terminated run containing text).
    /// Returns its text, or null at the end.
    /// </summary>
    public string? NextParagraph()
    {
        string text = _document.FlatText;
        int pos = HardLineEnd(_offset) + 1;
        while (pos < text.Length)
        {
            var paragraph = ReadParagraphAt(pos);
            if (!string.IsNullOrWhiteSpace(paragraph))
            {
                _offset = pos;
                return paragraph;
            }
            pos = HardLineEnd(pos) + 1;
        }
        return HandleBoundaryString(atEnd: true, BoundaryUnit.Paragraph);
    }

    /// <summary>
    /// Move to the start of the previous paragraph containing text.
    /// Returns its text, or null at the start.
    /// </summary>
    public string? PrevParagraph()
    {
        int start = HardLineStart(_offset);
        while (start > 0)
        {
            start = HardLineStart(start - 1);
            var paragraph = ReadParagraphAt(start);
            if (!string.IsNullOrWhiteSpace(paragraph))
            {
                _offset = start;
                return paragraph;
            }
        }
        return HandleBoundaryString(atEnd: false, BoundaryUnit.Paragraph);
    }

    // -------------------------------------------------------------------------
    // Read helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns the offset where the line containing <paramref name="pos"/> starts.</summary>
    public int LineStartAt(int pos)
    {
        string text = _document.FlatText;
        pos = Math.Clamp(pos, 0, text.Length);
        int hardStart = HardLineStart(pos);
        int hardEnd = HardLineEnd(hardStart);

        // Walk the pieces of a long line to the one containing pos
        int start = hardStart;
        while (true)
        {
            int end = PieceEnd(start, hardEnd);
            if (pos < end || end >= hardEnd)
                return start;
            start = end;
        }
    }

    /// <summary>Exclusive end of the line containing <paramref name="pos"/> (its '\n' or the end of a piece).</summary>
    public int LineEndAt(int pos)
    {
        int start = LineStartAt(pos);
        return PieceEnd(start, HardLineEnd(start));
    }

    /// <summary>Start of the '\n'-terminated run containing <paramref name="pos"/>.</summary>
    private int HardLineStart(int pos)
    {
        string text = _document.FlatText;
        pos = Math.Clamp(pos, 0, text.Length);
        if (pos == 0) return 0;
        // The '\n' at the end of a line belongs to that line, so search strictly before pos
        return text.LastIndexOf('\n', pos - 1) + 1;
    }

    /// <summary>Index of the '\n' ending the run containing <paramref name="pos"/>, or the text length.</summary>
    private int HardLineEnd(int pos)
    {
        string text = _document.FlatText;
        if (pos >= text.Length) return text.Length;
        int end = text.IndexOf('\n', pos);
        return end < 0 ? text.Length : end;
    }

    /// <summary>
    /// End of the piece of a line starting at <paramref name="start"/>: the line end, or for a
    /// line longer than <see cref="MaxLineLength"/>, just after the last space within the limit.
    /// </summary>
    private int PieceEnd(int start, int hardEnd)
    {
        int max = MaxLineLength;
        if (max <= 0 || hardEnd - start <= max)
            return hardEnd;

        string text = _document.FlatText;
        for (int i = start + max; i > start; i--)
        {
            if (char.IsWhiteSpace(text[i - 1]))
                return i;
        }
        return start + max; // one very long word: split it
    }

    private char? CharAtCursor()
    {
        string text = _document.FlatText;
        return _offset < text.Length && text[_offset] != '\n' ? text[_offset] : null;
    }

    /// <summary>Returns the text of the whole line containing the cursor.</summary>
    public string ReadCurrentLine() => ReadLineAt(LineStartAt(_offset));

    /// <summary>Returns the word containing the cursor, or an empty string on whitespace.</summary>
    public string ReadCurrentWord()
    {
        string text = _document.FlatText;
        if (_offset >= text.Length || char.IsWhiteSpace(text[_offset]))
            return string.Empty;

        int start = _offset;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;
        return ReadWordAt(start);
    }

    /// <summary>
    /// Returns the text from <paramref name="pos"/> to the end of its line (without the '\n').
    /// </summary>
    public string ReadLineAt(int pos)
    {
        string text = _document.FlatText;
        if (pos >= text.Length) return string.Empty;
        int end = LineEndAt(pos);
        return text.Substring(pos, Math.Max(0, end - pos)).TrimEnd(' ');
    }

    /// <summary>Returns the whole paragraph ('\n'-terminated run) starting at <paramref name="pos"/>.</summary>
    private string ReadParagraphAt(int pos)
    {
        string text = _document.FlatText;
        if (pos >= text.Length) return string.Empty;
        return text.Substring(pos, HardLineEnd(pos) - pos);
    }

    /// <summary>Returns the word text starting at <paramref name="pos"/>.</summary>
    public string ReadWordAt(int pos)
    {
        string text = _document.FlatText;
        if (pos >= text.Length) return string.Empty;
        int end = pos;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        return text.Substring(pos, end - pos);
    }

    // -------------------------------------------------------------------------
    // Boundary handling
    // -------------------------------------------------------------------------

    private char? HandleBoundary(bool atEnd)
    {
        if (WrapEnabled)
        {
            _offset = atEnd ? 0 : Math.Max(0, _document.FlatText.Length - 1);
            PlayCue("wrap");
            return _document.FlatText.Length > 0 ? _document.FlatText[_offset] : (char?)null;
        }
        PlayCue("boundary");
        return null;
    }

    private enum BoundaryUnit { Word, Line, Paragraph }

    private string? HandleBoundaryString(bool atEnd, BoundaryUnit unit)
    {
        if (!WrapEnabled || _document.FlatText.Length == 0)
        {
            PlayCue("boundary");
            return null;
        }

        string text = _document.FlatText;
        PlayCue("wrap");

        if (atEnd)
        {
            _offset = 0;
        }
        else if (unit == BoundaryUnit.Line)
        {
            // Start of the last line
            _offset = LineStartAt(text.Length - 1);
        }
        else if (unit == BoundaryUnit.Paragraph)
        {
            _offset = HardLineStart(text.Length - 1);
        }
        else
        {
            // Start of the last word
            int pos = text.Length - 1;
            while (pos > 0 && char.IsWhiteSpace(text[pos])) pos--;
            while (pos > 0 && !char.IsWhiteSpace(text[pos - 1])) pos--;
            _offset = pos;
        }

        return unit switch
        {
            BoundaryUnit.Line => ReadLineAt(_offset),
            BoundaryUnit.Paragraph => ReadParagraphAt(_offset),
            _ => ReadWordAt(_offset),
        };
    }

    private void PlayCue(string cue)
    {
        if (PlayCues)
            _audioCuePlayer.Play(cue);
    }
}
