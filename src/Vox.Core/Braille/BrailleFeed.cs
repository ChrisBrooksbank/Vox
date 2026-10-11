using Microsoft.Extensions.Logging;
using Vox.Core.Buffer;
using Vox.Core.Pipeline;
using Vox.Core.Text;

namespace Vox.Core.Braille;

/// <summary>
/// Turns what the rest of Vox tracks into braille lines for <see cref="BrailleController"/>: the
/// focus (then its ancestors, read in the background), the caret's line whenever the focused
/// text is read, the browse cursor after each pipeline event, and the review cursor after each
/// review command.
/// </summary>
public sealed class BrailleFeed
{
    private readonly BrailleController _controller;
    private readonly Func<Task<IReadOnlyList<BrailleObject>>> _ancestors;
    private readonly ILogger _logger;
    private long _focusSequence;

    // The browse line last sent (pipeline thread only), so unchanged positions send nothing
    private (VBufferDocument Document, int LineStart, int Offset, (int, int)? Selection, int MaxLineLength)? _lastBrowse;

    public BrailleFeed(BrailleController controller, Func<Task<IReadOnlyList<BrailleObject>>> ancestors, ILogger logger)
    {
        _controller = controller;
        _ancestors = ancestors;
        _logger = logger;
    }

    /// <summary>Focus moved (pipeline thread): the object now, its context when it has been read.</summary>
    public void FocusChanged(FocusChangedEvent focus)
    {
        var obj = BrailleObject.From(focus);
        var sequence = Interlocked.Increment(ref _focusSequence);
        // The new focus replaces the browse line: the next browse update sends it again
        _lastBrowse = null;
        _controller.ShowFocus(obj, []);
        if (_controller.IsActive)
            _ = AddContextAsync(obj, sequence);
    }

    private async Task AddContextAsync(BrailleObject focus, long sequence)
    {
        try
        {
            var ancestors = await _ancestors().ConfigureAwait(false);
            // Focus moved on meanwhile: these are another object's ancestors
            if (ancestors.Count > 0 && Interlocked.Read(ref _focusSequence) == sequence)
                _controller.ShowFocus(focus, ancestors);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not add the focus context to braille");
        }
    }

    /// <summary>The focused control's text was read (UIA thread): shows the caret's line.</summary>
    public void TextRead(ITextDocument document)
    {
        if (!_controller.IsActive)
            return;
        var caret = document.GetCaret();
        if (caret is null)
            return;
        var line = TextLines.LineAt(caret);
        var selection = document.GetSelection();
        var selected = selection.Count > 0 ? TextLines.SelectionIn(line, selection[0]) : null;
        _controller.ShowEditLine(line.Text, line.Offset, selected);
    }

    /// <summary>The review cursor moved (UIA thread): shows its line.</summary>
    public void ReviewMoved(ITextRange position)
    {
        if (!_controller.IsActive)
            return;
        var line = TextLines.LineAt(position);
        _controller.ShowReviewLine(line.Text, line.Offset);
    }

    /// <summary>
    /// After a pipeline event (pipeline thread): the browse cursor's line while browsing, sent when
    /// the line, the cursor or the selection changed; when browsing stops, the focus shows again.
    /// </summary>
    public void BrowseUpdated((VBufferCursor Cursor, (int Start, int End)? Selection)? position)
    {
        if (position is not ({ } cursor, var selection))
        {
            if (_lastBrowse is not null)
            {
                _lastBrowse = null;
                _controller.ClearBrowseLine();
            }
            return;
        }
        var offset = cursor.TextOffset;
        var lineStart = cursor.LineStartAt(offset);
        var state = (cursor.Document, lineStart, offset, selection, cursor.MaxLineLength);
        if (_lastBrowse is { } last && ReferenceEquals(last.Document, state.Document) && last.LineStart == lineStart
            && last.Offset == offset && last.Selection == selection && last.MaxLineLength == state.MaxLineLength)
            return;
        _lastBrowse = state;
        var line = _controller.Presenter.ForBrowseLine(cursor.Document, lineStart, cursor.LineEndAt(offset), offset, selection);
        _controller.ShowBrowseLine(line);
    }
}
