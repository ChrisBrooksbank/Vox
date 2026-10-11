using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;
using Vox.Core.Braille;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Text;

namespace Vox.Core.Accessibility;

/// <summary>
/// The objects around the focus, for braille's focus context: the focused element's ancestors from
/// the top-level window down to its parent (the desktop left out), read on the UIA thread.
/// </summary>
public sealed class UIAFocusAncestry
{
    /// <summary>Ancestors further up than this are left out (the window is reached long before).</summary>
    public const int MaxDepth = 25;

    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly ILogger<UIAFocusAncestry> _logger;

    public UIAFocusAncestry(UIAThread uiaThread, UIAProvider uiaProvider, ILogger<UIAFocusAncestry> logger)
    {
        _uiaThread = uiaThread;
        _uiaProvider = uiaProvider;
        _logger = logger;
    }

    /// <summary>The focused element's ancestors, outermost first; empty when they can't be read.</summary>
    public async Task<IReadOnlyList<BrailleObject>> GetAsync()
    {
        try
        {
            return await _uiaThread.RunAsync<IReadOnlyList<BrailleObject>>(() =>
            {
                var automation = _uiaProvider.Automation;
                var cache = _uiaProvider.CacheRequest;
                var root = automation.GetRootElement();
                var walker = automation.ControlViewWalker;
                var element = automation.GetFocusedElementBuildCache(cache);
                var ancestors = new List<BrailleObject>();
                for (var depth = 0; element is not null && depth < MaxDepth; depth++)
                {
                    var parent = walker.GetParentElementBuildCache(element, cache);
                    if (parent is null || automation.CompareElements(parent, root) != 0)
                        break;
                    ancestors.Add(BrailleObject.From(UIAEventSubscriber.DescribeCached(parent)));
                    element = parent;
                }
                ancestors.Reverse();
                return ancestors;
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the focus's ancestors for braille");
            return [];
        }
    }
}

/// <summary>
/// <see cref="IBrailleRouter"/> for the running screen reader: the caret through the focused
/// control's text range (UIA thread), the review cursor through <see cref="ReviewCommands"/>, the
/// browse cursor and commands through the pipeline.
/// </summary>
public sealed class BrailleRouter : IBrailleRouter
{
    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly IFocusedTextSource _focusedText;
    private readonly ReviewCommands _review;
    private readonly IEventSink _pipeline;
    private readonly ILogger<BrailleRouter> _logger;

    public BrailleRouter(UIAThread uiaThread, UIAProvider uiaProvider, IFocusedTextSource focusedText,
        ReviewCommands review, IEventSink pipeline, ILogger<BrailleRouter> logger)
    {
        _uiaThread = uiaThread;
        _uiaProvider = uiaProvider;
        _focusedText = focusedText;
        _review = review;
        _pipeline = pipeline;
        _logger = logger;
    }

    public void MoveCaret(int offsetInLine) => _ = RunAsync(() =>
    {
        var caret = _focusedText.GetFocusedDocument()?.GetCaret();
        if (caret is null)
            return;
        var position = TextLines.PositionIn(caret.ExpandToEnclosingUnit(Text.TextUnit.Line), offsetInLine);
        if (!position.Select())
            _logger.LogDebug("The focused control's caret can't be moved");
    }, "move the caret");

    public void MoveReviewCursor(int offsetInLine) => _ = _review.RouteToAsync(offsetInLine);

    public void RouteBrowse(int offset, bool activate) =>
        _pipeline.Post(new BrailleRouteEvent(DateTimeOffset.UtcNow, offset, activate));

    public void ActivateFocus() => _ = RunAsync(() =>
    {
        // Its own action only: a click would land on whatever is under its clickable point
        UIANavigatorObject.Focused(_uiaProvider)?.Activate();
    }, "activate the focus");

    public void RunCommand(NavigationCommand command) =>
        _pipeline.Post(new NavigationCommandEvent(DateTimeOffset.UtcNow, command));

    private async Task RunAsync(Action action, string what)
    {
        try
        {
            await _uiaThread.RunAsync(action).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not {What} for a braille routing key", what);
        }
    }
}
