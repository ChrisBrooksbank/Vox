using Microsoft.Extensions.Logging;
using Vox.Core.Buffer;
using Vox.Core.Ocr;
using Vox.Core.Pipeline;
using Vox.Core.Text;

namespace Vox.Core.Magnifier;

/// <summary>
/// Feeds <see cref="MagnifierController"/> where things are: the focus (its bounds come with the
/// focus event), the caret (read after caret events, and shortly after typing for controls that
/// raise none), the browse cursor's element (looked up on the UIA thread, one lookup at a time,
/// the newest element winning), the review cursor and the mouse pointer (polled). Nothing is
/// looked up for a source the magnifier isn't following.
/// </summary>
public sealed class MagnifierFeed
{
    /// <summary>How long after a key is typed the caret is read (time for the application to move it).</summary>
    public static readonly TimeSpan CaretDelay = TimeSpan.FromMilliseconds(50);

    private readonly MagnifierController _controller;
    private readonly Func<VBufferNode, Task<ScreenRect?>> _elementBounds;
    private readonly Func<ScreenRect?> _caretBounds;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    // Guarded by _lock
    private VBufferNode? _browseNode;
    private VBufferNode? _wanted;
    private bool _lookingUp;
    private (int X, int Y)? _pointer;

    public MagnifierFeed(MagnifierController controller, Func<VBufferNode, Task<ScreenRect?>> elementBounds,
        Func<ScreenRect?> caretBounds, ILogger logger)
    {
        _controller = controller;
        _elementBounds = elementBounds;
        _caretBounds = caretBounds;
        _logger = logger;
    }

    /// <summary>Focus moved: the view follows the newly focused object.</summary>
    public void FocusChanged(FocusChangedEvent e) => _controller.FocusMoved(e.Bounds);

    /// <summary>The caret moved or the text changed: the view follows the caret.</summary>
    public void CaretMoved()
    {
        if (!_controller.Follows(MagnifierTrackingSource.Caret))
            return;
        ScreenRect? bounds;
        try
        {
            bounds = _caretBounds();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the caret's position");
            return;
        }
        _controller.CaretMoved(bounds);
    }

    /// <summary>A key was typed (not a command): read the caret once the application has moved it.</summary>
    public async Task KeyTypedAsync(TimeSpan? delay = null)
    {
        if (!_controller.Follows(MagnifierTrackingSource.Caret))
            return;
        await Task.Delay(delay ?? CaretDelay).ConfigureAwait(false);
        CaretMoved();
    }

    /// <summary>
    /// The browse cursor may have moved (after a command or a page update): when it is on another
    /// element, the view follows it.
    /// </summary>
    public void BrowseUpdated(VBufferNode? node)
    {
        lock (_lock)
        {
            if (node is null || ReferenceEquals(node, _browseNode))
                return;
            _browseNode = node;
            if (!_controller.Follows(MagnifierTrackingSource.BrowseCursor))
                return;
            _wanted = node;
            if (_lookingUp)
                return;
            _lookingUp = true;
        }
        _ = LookUpAsync();
    }

    private async Task LookUpAsync()
    {
        while (true)
        {
            VBufferNode? node;
            lock (_lock)
            {
                node = _wanted;
                _wanted = null;
                if (node is null)
                {
                    _lookingUp = false;
                    return;
                }
            }
            ScreenRect? bounds = null;
            try
            {
                bounds = await _elementBounds(node).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not find where the browse cursor's element is");
            }
            lock (_lock)
            {
                // The cursor has moved on: its element is looked up next instead
                if (_wanted is not null)
                    continue;
            }
            _controller.BrowseCursorMoved(bounds);
        }
    }

    /// <summary>The review cursor moved (UIA thread): the view follows the text it is on.</summary>
    public void ReviewMoved(ITextRange position)
    {
        if (!_controller.Follows(MagnifierTrackingSource.ReviewCursor))
            return;
        ScreenRect? bounds;
        try
        {
            bounds = position.GetBounds();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not find where the review cursor is");
            return;
        }
        _controller.ReviewCursorMoved(bounds);
    }

    /// <summary>The pointer was polled at <paramref name="position"/>: when it moved, the view follows it.</summary>
    public void PointerPolled((int X, int Y)? position)
    {
        if (position is not { } at)
            return;
        lock (_lock)
        {
            // The first position after a pause is where the pointer rests, not a move to follow
            bool first = _pointer is null;
            if (_pointer == at)
                return;
            _pointer = at;
            if (first)
                return;
        }
        _controller.MouseMoved(at.X, at.Y);
    }

    /// <summary>The pointer isn't being polled (the magnifier is off or doesn't follow it): its next position starts afresh.</summary>
    public void PointerPaused()
    {
        lock (_lock)
            _pointer = null;
    }
}
