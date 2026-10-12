using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Speech;
using Vox.Core.Text;

namespace Vox.Core.Ocr;

/// <summary>
/// Text recognition commands: recognise the navigator object (Insert+R; while browsing, the
/// graphic at the browse cursor or else the page as shown) or the foreground window
/// (Insert+Ctrl+R), say what was recognised and put the review cursor in it, and click the
/// recognised word at the review cursor (Insert+Ctrl+Enter). Everything is recognised on this
/// computer; nothing is captured in secure mode. Commands return at once; a newer recognition
/// replaces one still running.
/// </summary>
public sealed class OcrCommands
{
    private readonly IOcrEngine _engine;
    private readonly IScreenCapture _capture;
    private readonly IOcrTargetSource _targets;
    private readonly ReviewCommands _review;
    private readonly IMouseInput _mouse;
    private readonly SpeechQueue _speechQueue;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger<OcrCommands> _logger;
    private CancellationTokenSource? _current;

    public OcrCommands(IOcrEngine engine, IScreenCapture capture, IOcrTargetSource targets, ReviewCommands review,
        IMouseInput mouse, SpeechQueue speechQueue, IOptionsMonitor<VoxSettings> settings, ILogger<OcrCommands> logger)
    {
        _engine = engine;
        _capture = capture;
        _targets = targets;
        _review = review;
        _mouse = mouse;
        _speechQueue = speechQueue;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// While browsing, what Insert+R recognises (the graphic at the browse cursor, else the
    /// page); null otherwise. Called on the thread commands arrive on. Set by the host.
    /// </summary>
    public Func<VBufferNode?>? BrowseTarget { get; set; }

    /// <summary>Raised with each recognition's result (tests; the document is empty when nothing was found).</summary>
    public event EventHandler<OcrDocument>? Recognized;

    /// <summary>Runs <paramref name="command"/> if it is a recognition command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.RecognizeNavigator: _ = RecognizeAsync(wholeWindow: false); return true;
            case NavigationCommand.RecognizeWindow: _ = RecognizeAsync(wholeWindow: true); return true;
            case NavigationCommand.ClickRecognizedWord: _ = ClickWordAsync(); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Recognises the navigator object (or what is being browsed), or the foreground window.
    /// Call on the thread commands arrive on: what is being browsed is read before it returns.
    /// </summary>
    public Task RecognizeAsync(bool wholeWindow)
    {
        var node = wholeWindow ? null : BrowseTarget?.Invoke();
        var tether = _review.BrowseTether?.Invoke();
        return RecognizeAsync(wholeWindow, node, tether);
    }

    private async Task RecognizeAsync(bool wholeWindow, VBufferNode? node, ReviewTether? tether)
    {
        if (!_capture.IsAvailable)
        {
            Speak(VoxStrings.Get("Ocr.NotOnThisScreen"));
            return;
        }
        if (!_engine.IsAvailable)
        {
            Speak(VoxStrings.Get("Ocr.NotInstalled"));
            return;
        }

        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _current, cancellation)?.Cancel();
        var token = cancellation.Token;
        Speak(VoxStrings.Get("Ocr.Recognizing"));
        try
        {
            var bounds = node is not null ? await _targets.ElementBoundsAsync(node).ConfigureAwait(false)
                : wholeWindow ? await _targets.WindowBoundsAsync().ConfigureAwait(false)
                : await _targets.NavigatorBoundsAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var image = bounds is { IsEmpty: false } area
                ? await Task.Run(() => _capture.Capture(area), token).ConfigureAwait(false)
                : null;
            if (image is null)
            {
                Speak(VoxStrings.Get("Ocr.NoLocation"));
                return;
            }

            var settings = _settings.CurrentValue;
            var result = await _engine.RecognizeAsync(image, settings.OcrLanguage ?? settings.Language, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var document = new OcrDocument(result);
            if (document.IsEmpty)
            {
                Speak(VoxStrings.Get("Ocr.NoText"));
                Recognized?.Invoke(this, document);
                return;
            }

            await _review.ShowRecognizedAsync(document, tether).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // Never logged: it is whatever was on the screen
            _logger.LogDebug("Recognised {Lines} lines of text", document.LineCount);
            Speak(document.Text);
            Recognized?.Invoke(this, document);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer recognition took over
        }
        catch (ObjectDisposedException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Text recognition failed");
            Speak(VoxStrings.Get("Ocr.Failed"));
        }
        finally
        {
            Interlocked.CompareExchange(ref _current, null, cancellation);
        }
    }

    /// <summary>
    /// Clicks the middle of the recognised word at the review cursor (after saying it). Call on
    /// the thread commands arrive on.
    /// </summary>
    public async Task ClickWordAsync()
    {
        OcrWord? word;
        try
        {
            word = await _review.RecognizedWordAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Could not find the recognised word");
            word = null;
        }
        if (word is null)
        {
            Speak(VoxStrings.Get("Ocr.NoRecognizedText"));
            return;
        }

        // Said before the click, which may move focus (whose announcement then follows)
        Speak(VoxStrings.Format("Ocr.Clicking", word.Text));
        try
        {
            var (x, y) = word.Bounds.Center;
            _mouse.MoveTo(x, y);
            _mouse.Press(MouseButton.Left);
            _mouse.Release(MouseButton.Left);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not click the recognised word");
        }
    }

    private void Speak(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
