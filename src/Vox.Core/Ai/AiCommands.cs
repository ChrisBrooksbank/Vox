using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Ocr;
using Vox.Core.Speech;
using Vox.Core.Text;

namespace Vox.Core.Ai;

/// <summary>
/// The AI commands: describe the navigator object (Insert+G; while browsing, the graphic at the
/// browse cursor, else the page), ask a question about the window in front (Insert+Alt+G),
/// summarise the page being browsed (Insert+Ctrl+I), ask a question about the page (Insert+Alt+Q),
/// and set the cloud provider's key (Vox menu).
/// Answers are said and put under the review cursor. Everything goes through
/// <see cref="AiAssistant"/>, so the consent rules always apply. One request runs at a time: the
/// progress cue plays until it is answered, and pressing an AI command again (or Ctrl, stop
/// speech) cancels it.
/// </summary>
public sealed class AiCommands
{
    private readonly AiAssistant _ai;
    private readonly IScreenCapture _capture;
    private readonly IOcrTargetSource _targets;
    private readonly IAiContextSource _context;
    private readonly ReviewCommands _review;
    private readonly ITextPrompt _textPrompt;
    private readonly OwnDialogRunner? _dialogs;
    private readonly IAiKeyStore _keys;
    private readonly RunPolicy _policy;
    private readonly IAudioCuePlayer _cues;
    private readonly SpeechQueue _speechQueue;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action<VoxSettings>? _updateSettings;
    private readonly ILogger<AiCommands> _logger;
    private readonly IConfirmPresenter? _confirm;
    private readonly TimeSpan _progressInterval;
    private readonly TimeSpan _dialogSettleTime;
    private readonly List<string> _questions = new();
    private CancellationTokenSource? _current;
    private volatile bool _asking;

    // Questions asked this session, offered again in the prompt
    private const int MaxQuestions = 20;

    public AiCommands(AiAssistant ai, IScreenCapture capture, IOcrTargetSource targets, IAiContextSource context,
        ReviewCommands review, ITextPrompt textPrompt, OwnDialogRunner? dialogs, IAiKeyStore keys, RunPolicy policy,
        IAudioCuePlayer cues, SpeechQueue speechQueue, IOptionsMonitor<VoxSettings> settings, ILogger<AiCommands> logger,
        Action<VoxSettings>? updateSettings = null, TimeSpan? progressInterval = null, TimeSpan? dialogSettleTime = null,
        IConfirmPresenter? confirm = null)
    {
        _confirm = confirm;
        _ai = ai;
        _capture = capture;
        _targets = targets;
        _context = context;
        _review = review;
        _textPrompt = textPrompt;
        _dialogs = dialogs;
        _keys = keys;
        _policy = policy;
        _cues = cues;
        _speechQueue = speechQueue;
        _settings = settings;
        _updateSettings = updateSettings;
        _logger = logger;
        _progressInterval = progressInterval ?? TimeSpan.FromSeconds(2);
        // The prompt must be gone from the screen before the window is captured
        _dialogSettleTime = dialogSettleTime ?? TimeSpan.FromMilliseconds(400);
    }

    /// <summary>
    /// While browsing, what Insert+G describes (the graphic at the browse cursor, else the page);
    /// null otherwise. Called on the thread commands arrive on. Set by the host.
    /// </summary>
    public Func<VBufferNode?>? BrowseTarget { get; set; }

    /// <summary>The page being browsed (for the summary), or null when not browsing. Pipeline thread. Set by the host.</summary>
    public Func<VBufferDocument?>? BrowseDocument { get; set; }

    /// <summary>The page that has focus, browse or focus mode (to ask about), or null. Pipeline thread. Set by the host.</summary>
    public Func<VBufferDocument?>? PageDocument { get; set; }

    /// <summary>Moves the browse cursor to an element an answer named (the host runs it on the pipeline thread). Set by the host.</summary>
    public Action<VBufferNode>? GoToElement { get; set; }

    /// <summary>The key that moves to the main content, as people read it, or null. Set by the host.</summary>
    public Func<string?>? MainContentKey { get; set; }

    /// <summary>A request is running.</summary>
    public bool IsBusy => Volatile.Read(ref _current) is not null;

    /// <summary>Raised with each answer put under the review cursor (tests).</summary>
    public event EventHandler<string>? Answered;

    /// <summary>Runs <paramref name="command"/> if it is an AI command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.DescribeObject: _ = DescribeAsync(); return true;
            case NavigationCommand.AskAboutScreen: _ = AskAboutScreenAsync(); return true;
            case NavigationCommand.SummarizePage: _ = SummarizePageAsync(); return true;
            case NavigationCommand.AskPage: _ = AskPageAsync(); return true;
            case NavigationCommand.SetAiKey: _ = SetKeyAsync(); return true;
            default: return false;
        }
    }

    /// <summary>Cancels the running request (stop speech); says so when <paramref name="announce"/>.</summary>
    public bool Cancel(bool announce = false)
    {
        var running = Interlocked.Exchange(ref _current, null);
        if (running is null)
            return false;
        running.Cancel();
        if (announce)
            Speak(VoxStrings.Get("Ai.Cancelled"));
        return true;
    }

    /// <summary>
    /// Describes the navigator object, or what is being browsed. Call on the thread commands
    /// arrive on: what is being browsed is read before it returns.
    /// </summary>
    public Task DescribeAsync()
    {
        if (Cancel(announce: true))
            return Task.CompletedTask;
        var node = BrowseTarget?.Invoke();
        var page = node is null ? null : BrowseDocument?.Invoke()?.Root.Value;
        var tether = _review.BrowseTether?.Invoke();
        if (!Ready())
            return Task.CompletedTask;
        bool isPage = node is not null && node.Parent is null;
        return RunAsync(VoxStrings.Get(isPage ? "Ai.DescribingPage" : "Ai.Describing"), async token =>
        {
            var bounds = node is not null ? await _targets.ElementBoundsAsync(node).ConfigureAwait(false)
                : await _targets.NavigatorBoundsAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var image = await CaptureAsync(bounds, token).ConfigureAwait(false);
            if (image is null)
                return;

            AiTarget target;
            if (node is not null)
            {
                var window = await _context.WindowTargetAsync().ConfigureAwait(false);
                target = window with { IsPassword = window.IsPassword || node.IsPassword, Site = page };
            }
            else
            {
                target = await _context.NavigatorTargetAsync().ConfigureAwait(false);
            }

            var answer = await AskAsync(new AiRequest(AiTask.DescribeImage, image, Language: Language()), target, token)
                .ConfigureAwait(false);
            await ShowAsync(answer, tether, token).ConfigureAwait(false);
        });
    }

    /// <summary>Asks for a question, then answers it from the window in front (a picture of it and its controls' text).</summary>
    public async Task AskAboutScreenAsync()
    {
        if (Cancel(announce: true) || !Ready())
            return;
        var tether = _review.BrowseTether?.Invoke();

        // Say no before asking a question that couldn't be answered
        var before = await _context.WindowTargetAsync().ConfigureAwait(false);
        if (_ai.Describer is { IsLocal: false } && _ai.Consent.ForCloud(before) is { Decision: AiConsentDecision.Refused } refused)
        {
            Speak(AiConsent.Explain(refused.Reason));
            return;
        }

        string? question;
        try
        {
            question = await RunDialog(() => _textPrompt.AskAsync(VoxStrings.Get("Ai.Ask.Title"), VoxStrings.Get("Ai.Ask.Label"),
                history: Questions()), whenBusy: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The question prompt failed");
            return;
        }
        if (string.IsNullOrWhiteSpace(question))
            return;
        RememberQuestion(question);

        await RunAsync(VoxStrings.Get("Ai.Thinking"), async token =>
        {
            await Task.Delay(_dialogSettleTime, token).ConfigureAwait(false);
            var bounds = await _targets.WindowBoundsAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var image = await CaptureAsync(bounds, token).ConfigureAwait(false);
            if (image is null)
                return;
            var target = await _context.WindowTargetAsync().ConfigureAwait(false);
            var text = await _context.WindowTextAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var answer = await AskAsync(new AiRequest(AiTask.ExplainScreen, image, question, text, Language()), target, token)
                .ConfigureAwait(false);
            await ShowAsync(answer, tether, token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Summarises the page being browsed: with the describer on this computer, else the cloud one
    /// (with consent), else from the page's structure. Call on the pipeline thread.
    /// </summary>
    public Task SummarizePageAsync()
    {
        if (Cancel(announce: true))
            return Task.CompletedTask;
        var document = BrowseDocument?.Invoke();
        if (document is null)
        {
            Speak(VoxStrings.Get("Ai.NotOnAPage"));
            return Task.CompletedTask;
        }
        var tether = _review.BrowseTether?.Invoke();
        var outline = PageDigest.Summarize(document, MainContentKey?.Invoke());

        // Nothing to ask: the summary comes from the page itself
        if (!_ai.IsSetUp || _ai.Consent.ForLocal().Decision == AiConsentDecision.Refused)
            return ShowTextAsync(outline, tether, CancellationToken.None);

        var text = PageDigest.TextOf(document);
        return RunAsync(VoxStrings.Get("Ai.Summarizing"), async token =>
        {
            // The buffer never holds a password's value; whose window it is still counts
            var target = (await _context.WindowTargetAsync().ConfigureAwait(false)) with { IsPassword = false, Site = document.Root.Value };
            var answer = await AskAsync(new AiRequest(AiTask.SummarizePage, null, null, text, Language()), target, token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (answer.HasText)
            {
                await ShowTextAsync(answer.Text!, tether, token).ConfigureAwait(false);
                return;
            }
            // Why not, then what the page itself says
            var reason = answer.Refusal == AiRefusal.Declined ? null : AiAssistant.Explain(answer);
            await ShowTextAsync(reason is { Length: > 0 } ? $"{reason}\n{outline}" : outline, tether, token).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Ask the page: asks for a question, answers it from the page's text (the describer on this
    /// computer first, else the cloud one with consent), and when the answer is at a line of the
    /// page, offers to move the browse cursor there. Call on the pipeline thread: the page is
    /// read before it returns.
    /// </summary>
    public async Task AskPageAsync()
    {
        if (Cancel(announce: true))
            return;
        var document = PageDocument?.Invoke();
        if (document is null)
        {
            Speak(VoxStrings.Get("Ai.NotOnAPage"));
            return;
        }
        var local = _ai.Consent.ForLocal();
        if (local.Decision == AiConsentDecision.Refused)
        {
            Speak(AiConsent.Explain(local.Reason));
            return;
        }
        if (!_ai.IsSetUp)
        {
            Speak(VoxStrings.Get("Ai.Refusal.NotSetUp"));
            return;
        }
        var page = PageQuestion.For(document);
        var tether = _review.BrowseTether?.Invoke();

        // Say no before asking a question that couldn't be answered
        var target = (await _context.WindowTargetAsync().ConfigureAwait(false)) with { IsPassword = false, Site = document.Root.Value };
        if (_ai.Describer is { IsLocal: false } && _ai.Consent.ForCloud(target) is { Decision: AiConsentDecision.Refused } refused)
        {
            Speak(AiConsent.Explain(refused.Reason));
            return;
        }

        string? question;
        try
        {
            question = await RunDialog(() => _textPrompt.AskAsync(VoxStrings.Get("Ai.AskPage.Title"), VoxStrings.Get("Ai.AskPage.Label"),
                history: Questions()), whenBusy: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The question prompt failed");
            return;
        }
        if (string.IsNullOrWhiteSpace(question))
            return;
        RememberQuestion(question);

        VBufferNode? goTo = null;
        string? goToText = null;
        await RunAsync(VoxStrings.Get("Ai.Thinking"), async token =>
        {
            var answer = await AskAsync(new AiRequest(AiTask.AskPage, null, question, page.Context, Language()), target, token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!answer.HasText)
            {
                Speak(AiAssistant.Explain(answer));
                return;
            }
            var (text, node, line) = page.Read(answer.Text!);
            await ShowTextAsync(text.Length > 0 ? text : answer.Text!, tether, token).ConfigureAwait(false);
            (goTo, goToText) = (node, line);
        }).ConfigureAwait(false);

        if (goTo is null || GoToElement is null || _confirm is null)
            return;
        bool go;
        try
        {
            go = await RunDialog(() => _confirm.AskAsync(VoxStrings.Get("Ai.AskPage.GoTitle"),
                VoxStrings.Format("Ai.AskPage.Go", Shorten(goToText ?? string.Empty))), whenBusy: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The move prompt failed");
            return;
        }
        if (go)
            GoToElement(goTo);
    }

    private Task<T> RunDialog<T>(Func<Task<T>> show, T whenBusy) =>
        _dialogs is null ? show() : _dialogs.RunAsync(show, whenBusy);

    private static string Shorten(string line) => line.Length > 80 ? line[..80].TrimEnd() + "…" : line;

    /// <summary>Asks for the cloud provider's API key and keeps it (encrypted for this Windows user).</summary>
    public async Task SetKeyAsync()
    {
        if (!_policy.AllowUserProfileAccess || _policy.IsSecure)
        {
            Speak(VoxStrings.Get("Ai.Refusal.SecureScreen"));
            return;
        }
        if (!_policy.AllowAi)
        {
            Speak(VoxStrings.Get("Ai.Refusal.TurnedOffByPolicy"));
            return;
        }

        string? key;
        try
        {
            key = await RunDialog(() => _textPrompt.AskAsync(VoxStrings.Get("Ai.Key.Title"), VoxStrings.Get("Ai.Key.Label"), secret: true), whenBusy: null)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The key prompt failed");
            return;
        }
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (!_keys.Save(key))
        {
            Speak(VoxStrings.Get("Ai.Key.NotSaved"));
            return;
        }
        // A key means Claude: nothing is sent without consent all the same
        var settings = _settings.CurrentValue;
        if (settings.AiCloudProvider == AiCloudProvider.None && _updateSettings is not null)
            _updateSettings(settings with { AiCloudProvider = AiCloudProvider.Claude });
        Speak(VoxStrings.Get("Ai.Key.Saved"));
    }

    // -------------------------------------------------------------------------

    /// <summary>Says why nothing can be described, if so (secure screen, policy, nothing set up).</summary>
    private bool Ready()
    {
        var local = _ai.Consent.ForLocal();
        if (local.Decision == AiConsentDecision.Refused)
        {
            Speak(AiConsent.Explain(local.Reason));
            return false;
        }
        if (!_capture.IsAvailable)
        {
            Speak(VoxStrings.Get("Ocr.NotOnThisScreen"));
            return false;
        }
        if (!_ai.IsSetUp)
        {
            Speak(VoxStrings.Get("Ai.Refusal.NotSetUp"));
            return false;
        }
        return true;
    }

    private async Task RunAsync(string progress, Func<CancellationToken, Task> work)
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _current, cancellation)?.Cancel();
        var token = cancellation.Token;
        Speak(progress);
        using var progressCue = CancellationTokenSource.CreateLinkedTokenSource(token);
        _ = PlayProgressAsync(progressCue.Token);
        try
        {
            await work(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled, or a newer request took over
        }
        catch (ObjectDisposedException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI request failed");
            Speak(VoxStrings.Format("Ai.Failure.Failed", _ai.Describer?.Name ?? string.Empty));
        }
        finally
        {
            progressCue.Cancel();
            Interlocked.CompareExchange(ref _current, null, cancellation);
        }
    }

    /// <summary>The progress cue, every couple of seconds, while waiting for an answer (not while the person is asked).</summary>
    private async Task PlayProgressAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_progressInterval, token).ConfigureAwait(false);
                if (!_asking)
                    _cues.Play("progress");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task<AiAnswer> AskAsync(AiRequest request, AiTarget target, CancellationToken token) =>
        _ai.AskAsync(request, target, token, asking => _asking = asking);

    private async Task<CapturedImage?> CaptureAsync(ScreenRect? bounds, CancellationToken token)
    {
        var image = bounds is { IsEmpty: false } area
            ? await Task.Run(() => _capture.Capture(area), token).ConfigureAwait(false)
            : null;
        if (image is null)
            Speak(VoxStrings.Get("Ocr.NoLocation"));
        return image;
    }

    private async Task ShowAsync(AiAnswer answer, ReviewTether? tether, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!answer.HasText)
        {
            Speak(AiAssistant.Explain(answer));
            return;
        }
        await ShowTextAsync(answer.Text!, tether, token).ConfigureAwait(false);
    }

    private async Task ShowTextAsync(string text, ReviewTether? tether, CancellationToken token)
    {
        await _review.ShowRecognizedAsync(new StringTextDocument(text), tether).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        Speak(text);
        Answered?.Invoke(this, text);
    }

    private string Language() => VoxStrings.EffectiveCulture.Name;

    private IReadOnlyList<string> Questions()
    {
        lock (_questions) return _questions.ToList();
    }

    private void RememberQuestion(string question)
    {
        lock (_questions)
        {
            _questions.RemoveAll(q => string.Equals(q, question, StringComparison.OrdinalIgnoreCase));
            _questions.Insert(0, question);
            if (_questions.Count > MaxQuestions)
                _questions.RemoveAt(_questions.Count - 1);
        }
    }

    private void Speak(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
