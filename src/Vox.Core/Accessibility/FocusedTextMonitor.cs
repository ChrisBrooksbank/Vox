using Microsoft.Extensions.Logging;
using Vox.Core.Navigation;
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
public sealed class FocusedTextMonitor : IFocusedTextReader
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

    /// <summary>
    /// Focus moved: forgets the old control and, once the new one's text is known, records its caret
    /// line and selection so the first deletion or selection in it can be read.
    /// </summary>
    public async Task HandleFocusChanged()
    {
        _caretTracker.Reset();
        await Task.Delay(PrimeDelay).ConfigureAwait(false);
        if (_source.HasText)
            await EvaluateAsync(document => { _caretTracker.Prime(document); return null; }, "reading the focused text").ConfigureAwait(false);
    }

    /// <summary>How long after a focus change the new control's text is first read (time for the subscriber to find it).</summary>
    public TimeSpan PrimeDelay { get; init; } = TimeSpan.FromMilliseconds(150);

    public Task HandleCaretMovedAsync(CaretMovedEvent e) => EvaluateAsync(_caretTracker.OnCaretMoved, "reading the caret");

    public Task HandleTextEditedAsync(TextEditedEvent e) => EvaluateAsync(_caretTracker.OnTextChanged, "reading a text change");

    public bool HasFocusedText => _source.HasText;

    public void Read(TextReadKind kind) => _ = ReadAsync(kind);

    /// <summary>Reads <paramref name="kind"/> at the caret of the focused control.</summary>
    public Task ReadAsync(TextReadKind kind) => EvaluateAsync(document => Describe(document, kind), "reading the focused text");

    /// <summary>What a read command says for the focused document.</summary>
    public static string? Describe(ITextDocument document, TextReadKind kind)
    {
        if (kind == TextReadKind.Selection)
        {
            var selection = document.GetSelection();
            var selected = selection.Count > 0 ? selection[0] : null;
            if (selected is null || selected.IsDegenerate)
                return "No selection";
            var text = selected.GetText(TextCaretTracker.MaxSpokenLength);
            return text.Length <= 2 ? TextSpeech.ForCharacter(text) : TextSpeech.ForUnit(text, TextUnit.Word);
        }

        var caret = document.GetCaret();
        if (caret is null)
            return null;
        var unit = kind switch
        {
            TextReadKind.Character => TextUnit.Character,
            TextReadKind.Word => TextUnit.Word,
            _ => TextUnit.Line,
        };
        return TextSpeech.ForUnit(caret.ExpandToEnclosingUnit(unit).GetText(TextCaretTracker.MaxSpokenLength), unit);
    }

    private async Task ReadAfterDelayAsync()
    {
        await Task.Delay(FallbackDelay).ConfigureAwait(false);
        await EvaluateAsync(
            document => _caretTracker.OnCaretMoved(document) ?? _caretTracker.OnTextChanged(document),
            "reading the caret of a value-only control").ConfigureAwait(false);
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
