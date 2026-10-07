using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Text;

namespace Vox.Core.Accessibility;

/// <summary>
/// Reads caret moves in the focused edit control: keys go to <see cref="TextCaretTracker"/>, and
/// each <see cref="CaretMovedEvent"/> is evaluated against the control's UIA text pattern on the
/// UIA thread, speaking what the tracker says. Call the Handle methods on the pipeline thread.
/// </summary>
public sealed class FocusedTextMonitor
{
    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly UIAEventSubscriber _subscriber;
    private readonly TextCaretTracker _caretTracker;
    private readonly SpeechQueue _speechQueue;
    private readonly ILogger<FocusedTextMonitor> _logger;

    public FocusedTextMonitor(UIAThread uiaThread, UIAProvider uiaProvider, UIAEventSubscriber subscriber,
        TextCaretTracker caretTracker, SpeechQueue speechQueue, ILogger<FocusedTextMonitor> logger)
    {
        _uiaThread = uiaThread;
        _uiaProvider = uiaProvider;
        _subscriber = subscriber;
        _caretTracker = caretTracker;
        _speechQueue = speechQueue;
        _logger = logger;
    }

    /// <summary>A key reached the focused application (it wasn't a Vox command).</summary>
    public void HandleRawKey(RawKeyEvent e)
    {
        if (e.Key.IsKeyDown)
            _caretTracker.NoteKey(e.Key.VkCode, e.Key.Modifiers);
    }

    public void HandleFocusChanged() => _caretTracker.Reset();

    public Task HandleCaretMovedAsync(CaretMovedEvent e) => EvaluateAsync(_caretTracker.OnCaretMoved, "reading the caret");

    private async Task EvaluateAsync(Func<ITextDocument, string?> evaluate, string what)
    {
        string? speech;
        try
        {
            speech = await _uiaThread.RunAsync(() =>
            {
                var element = _subscriber.FocusedTextElement;
                var document = element is null ? null : UIATextDocument.TryCreate(element, _uiaProvider.Automation);
                return document is null ? null : evaluate(document);
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UIA error while {What}", what);
            return;
        }

        if (!string.IsNullOrEmpty(speech))
            _speechQueue.Enqueue(new Utterance(speech, SpeechPriority.Interrupt));
    }
}
