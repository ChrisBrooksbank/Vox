using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Text;

namespace Vox.Core.Accessibility;

/// <summary>
/// Reads caret moves in the focused edit control: keys go to <see cref="TextCaretTracker"/>, and
/// each caret move is evaluated against the focused control's text (from
/// <see cref="IFocusedTextSource"/>) on the UIA thread, speaking what the tracker says. Controls
/// that raise caret events are read on <see cref="CaretMovedEvent"/>; value-only edit controls,
/// which raise none, are read <see cref="FallbackDelay"/> after each caret key.
/// Call the Handle methods on the pipeline thread.
/// </summary>
public sealed class FocusedTextMonitor
{
    /// <summary>How long after a caret key a control without caret events is read (time for it to move the caret).</summary>
    public static readonly TimeSpan DefaultFallbackDelay = TimeSpan.FromMilliseconds(50);

    private readonly UIAThread _uiaThread;
    private readonly IFocusedTextSource _source;
    private readonly TextCaretTracker _caretTracker;
    private readonly SpeechQueue _speechQueue;
    private readonly ILogger<FocusedTextMonitor> _logger;

    public FocusedTextMonitor(UIAThread uiaThread, IFocusedTextSource source, TextCaretTracker caretTracker,
        SpeechQueue speechQueue, ILogger<FocusedTextMonitor> logger)
    {
        _uiaThread = uiaThread;
        _source = source;
        _caretTracker = caretTracker;
        _speechQueue = speechQueue;
        _logger = logger;
    }

    public TimeSpan FallbackDelay { get; init; } = DefaultFallbackDelay;

    /// <summary>
    /// A key reached the focused application (it wasn't a Vox command). Returns the delayed read
    /// for a control without caret events, or a completed task.
    /// </summary>
    public Task HandleRawKey(RawKeyEvent e)
    {
        if (!e.Key.IsKeyDown)
            return Task.CompletedTask;
        bool caretKey = _caretTracker.NoteKey(e.Key.VkCode, e.Key.Modifiers);
        if (caretKey && _source.HasText && !_source.RaisesCaretEvents)
            return ReadAfterDelayAsync();
        return Task.CompletedTask;
    }

    public void HandleFocusChanged() => _caretTracker.Reset();

    public Task HandleCaretMovedAsync(CaretMovedEvent e) => EvaluateAsync(_caretTracker.OnCaretMoved, "reading the caret");

    private async Task ReadAfterDelayAsync()
    {
        await Task.Delay(FallbackDelay).ConfigureAwait(false);
        await EvaluateAsync(_caretTracker.OnCaretMoved, "reading the caret of a value-only control").ConfigureAwait(false);
    }

    private async Task EvaluateAsync(Func<ITextDocument, string?> evaluate, string what)
    {
        string? speech;
        try
        {
            speech = await _uiaThread.RunAsync(() =>
            {
                var document = _source.GetFocusedDocument();
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
