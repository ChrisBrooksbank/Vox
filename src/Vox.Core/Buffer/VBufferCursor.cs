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
        _offset = Math.Clamp(offset, 0, Math.Max(0, document.FlatText.Length - 1));
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
            return HandleBoundaryString(atEnd: true);

        int pos = _offset;

        // Skip current word characters
        while (pos < len && !char.IsWhiteSpace(text[pos]))
            pos++;

        // Skip whitespace
        while (pos < len && char.IsWhiteSpace(text[pos]))
            pos++;

        if (pos >= len)
            return HandleBoundaryString(atEnd: true);

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
            return HandleBoundaryString(atEnd: false);

        int pos = _offset - 1;

        // Skip whitespace backward
        while (pos > 0 && char.IsWhiteSpace(text[pos]))
            pos--;

        if (pos == 0 && char.IsWhiteSpace(text[pos]))
            return HandleBoundaryString(atEnd: false);

        // Find start of this word
        while (pos > 0 && !char.IsWhiteSpace(text[pos - 1]))
            pos--;

        _offset = pos;
        return ReadWordAt(_offset);
    }

    // -------------------------------------------------------------------------
    // Line movement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Move to the start of the next line (after the next '\n').
    /// Returns the line text, or null if at end.
    /// </summary>
    public string? NextLine()
    {
        string text = _document.FlatText;
        int len = text.Length;

        if (_offset >= len)
            return HandleBoundaryString(atEnd: true);

        // Find end of current line
        int nlPos = text.IndexOf('\n', _offset);
        if (nlPos < 0 || nlPos == len - 1)
            return HandleBoundaryString(atEnd: true);

        _offset = nlPos + 1;
        return ReadLineAt(_offset);
    }

    /// <summary>
    /// Move to the start of the previous line.
    /// Returns the line text, or null if at start.
    /// </summary>
    public string? PrevLine()
    {
        string text = _document.FlatText;

        if (_offset == 0)
            return HandleBoundaryString(atEnd: false);

        // If we're right at the start of a line, step back one char to get into prev line
        int pos = _offset - 1;
        // Skip any newline just before current position
        if (pos >= 0 && text[pos] == '\n')
            pos--;

        if (pos < 0)
            return HandleBoundaryString(atEnd: false);

        // Find start of this line
        int nlPos = text.LastIndexOf('\n', pos);
        int lineStart = nlPos < 0 ? 0 : nlPos + 1;

        _offset = lineStart;
        return ReadLineAt(_offset);
    }

    // -------------------------------------------------------------------------
    // Read helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns the text of the line starting at <paramref name="pos"/>.</summary>
    public string ReadLineAt(int pos)
    {
        string text = _document.FlatText;
        int end = text.IndexOf('\n', pos);
        if (end < 0) end = text.Length;
        return text.Substring(pos, end - pos);
    }

    /// <summary>Returns the word text starting at <paramref name="pos"/>.</summary>
    public string ReadWordAt(int pos)
    {
        string text = _document.FlatText;
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
            _audioCuePlayer.Play("wrap");
            return _document.FlatText.Length > 0 ? _document.FlatText[_offset] : (char?)null;
        }
        _audioCuePlayer.Play("boundary");
        return null;
    }

    private string? HandleBoundaryString(bool atEnd)
    {
        if (WrapEnabled)
        {
            _offset = atEnd ? 0 : Math.Max(0, _document.FlatText.Length - 1);
            _audioCuePlayer.Play("wrap");
            // Return current line/word at new position
            return ReadLineAt(_offset);
        }
        _audioCuePlayer.Play("boundary");
        return null;
    }
}
