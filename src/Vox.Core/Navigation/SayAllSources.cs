using Vox.Core.Buffer;
using Vox.Core.Text;

namespace Vox.Core.Navigation;

/// <summary>Lines for Say All to read, from where reading starts to the end.</summary>
public interface ISayAllSource
{
    /// <summary>The line reading starts on (from the current position to its end).</summary>
    Task<string?> CurrentLineAsync(CancellationToken cancellationToken);

    /// <summary>Advances to the next line and returns it, or null at the end.</summary>
    Task<string?> NextLineAsync(CancellationToken cancellationToken);

    /// <summary>The language of the line just returned, when known.</summary>
    string? CurrentLanguage => null;
}

/// <summary>Say All over the browse-mode buffer: the cursor follows along.</summary>
public sealed class BufferSayAllSource(VBufferCursor cursor, int? endOffset = null) : ISayAllSource
{
    public Task<string?> CurrentLineAsync(CancellationToken cancellationToken) =>
        Task.FromResult<string?>(cursor.ReadLineAt(cursor.TextOffset));

    /// <summary>The next line, or null at the end (of the document, or of <c>endOffset</c>: an open modal dialog).</summary>
    public Task<string?> NextLineAsync(CancellationToken cancellationToken)
    {
        int before = cursor.TextOffset;
        var line = cursor.NextLine();
        if (line is not null && endOffset is { } end && cursor.TextOffset >= end)
        {
            cursor.MoveTo(before);
            line = null;
        }
        return Task.FromResult(line);
    }

    public string? CurrentLanguage => cursor.CurrentNode?.Language is { Length: > 0 } language ? language : null;
}

/// <summary>
/// Say All over an <see cref="ITextDocument"/> (an edit control), from the caret, one line at a
/// time. The caret itself doesn't move. Document access goes through <paramref name="run"/>,
/// which runs it on the document's thread (the UIA thread for UIA documents).
/// </summary>
public sealed class TextDocumentSayAllSource(
    Func<ITextDocument?> getDocument,
    Func<Func<string?>, Task<string?>> run) : ISayAllSource
{
    private ITextRange? _position;

    public Task<string?> CurrentLineAsync(CancellationToken cancellationToken) => run(() =>
    {
        var document = getDocument();
        var caret = document?.GetCaret();
        if (caret is null)
            return null;
        _position = caret;
        var line = caret.ExpandToEnclosingUnit(TextUnit.Line);
        // From the caret to the end of its line
        return line.WithEndpoint(TextEndpoint.Start, caret, TextEndpoint.Start).GetText(TextCaretTracker.MaxSpokenLength);
    });

    public Task<string?> NextLineAsync(CancellationToken cancellationToken) => run(() =>
    {
        if (_position is null)
            return null;
        var (next, moved) = _position.Move(TextUnit.Line, 1);
        if (moved == 0)
            return null;
        _position = next;
        return next.ExpandToEnclosingUnit(TextUnit.Line).GetText(TextCaretTracker.MaxSpokenLength);
    });
}
