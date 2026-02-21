using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Vox.Core.Audio;
using Vox.Core.Speech;

namespace Vox.Core.Pipeline;

/// <summary>
/// Main event pipeline backed by Channel&lt;ScreenReaderEvent&gt;.
/// SingleReader = true for performance.
/// Coalescing: consecutive focus events within 30ms keep only the last.
/// Routes events to speech queue with appropriate priority.
/// LiveRegion assertive → High, polite → Low.
/// ModeChanged → audio cue before speech.
/// </summary>
public sealed class EventPipeline : IEventSink, IDisposable
{
    private readonly SpeechQueue _speechQueue;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly ILogger<EventPipeline> _logger;
    private readonly Channel<ScreenReaderEvent> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    private const int FocusCoalescingWindowMs = 30;

    public EventPipeline(
        SpeechQueue speechQueue,
        IAudioCuePlayer audioCuePlayer,
        ILogger<EventPipeline> logger)
    {
        _speechQueue = speechQueue;
        _audioCuePlayer = audioCuePlayer;
        _logger = logger;

        _channel = Channel.CreateUnbounded<ScreenReaderEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _processingTask = Task.Run(ProcessEventsAsync, _cts.Token);
    }

    /// <summary>
    /// Raised when a RawKeyEvent is processed by the pipeline.
    /// Subscribe to this to receive raw key events (e.g., for typing echo).
    /// </summary>
    public event EventHandler<RawKeyEvent>? RawKeyReceived;

    /// <summary>
    /// Raised when a NavigationCommandEvent is processed by the pipeline.
    /// Subscribe to this to handle navigation commands (NavigationManager, QuickNavHandler, SayAllController).
    /// </summary>
    public event EventHandler<NavigationCommandEvent>? NavigationCommandReceived;

    /// <summary>
    /// Raised when a FocusChangedEvent is processed by the pipeline (after coalescing).
    /// Subscribe to this for auto-mode-switching in NavigationManager.
    /// </summary>
    public event EventHandler<FocusChangedEvent>? FocusChangedProcessed;

    public void Post(ScreenReaderEvent evt)
    {
        _channel.Writer.TryWrite(evt);
    }

    public async ValueTask PostAsync(ScreenReaderEvent evt, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessEventsAsync()
    {
        var token = _cts.Token;
        var reader = _channel.Reader;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!await reader.WaitToReadAsync(token).ConfigureAwait(false))
                    break;

                if (!reader.TryRead(out var evt))
                    continue;

                // Coalesce focus events: if it's a FocusChangedEvent, wait briefly for more
                if (evt is FocusChangedEvent)
                {
                    await Task.Delay(FocusCoalescingWindowMs, token).ConfigureAwait(false);

                    // Drain and keep only the last FocusChangedEvent
                    ScreenReaderEvent? lastFocus = evt;
                    while (reader.TryRead(out var next))
                    {
                        if (next is FocusChangedEvent)
                            lastFocus = next;
                        else
                        {
                            // Process pending focus event before non-focus event
                            if (lastFocus != null)
                            {
                                await ProcessEventAsync(lastFocus, token).ConfigureAwait(false);
                                lastFocus = null;
                            }
                            await ProcessEventAsync(next, token).ConfigureAwait(false);
                        }
                    }

                    if (lastFocus != null)
                        await ProcessEventAsync(lastFocus, token).ConfigureAwait(false);
                }
                else
                {
                    await ProcessEventAsync(evt, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("EventPipeline processing cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in EventPipeline");
        }
    }

    private async Task ProcessEventAsync(ScreenReaderEvent evt, CancellationToken token)
    {
        try
        {
            switch (evt)
            {
                case FocusChangedEvent focus:
                    await HandleFocusChangedAsync(focus, token).ConfigureAwait(false);
                    break;

                case NavigationEvent nav:
                    await HandleNavigationAsync(nav, token).ConfigureAwait(false);
                    break;

                case LiveRegionChangedEvent liveRegion:
                    await HandleLiveRegionAsync(liveRegion, token).ConfigureAwait(false);
                    break;

                case ModeChangedEvent modeChanged:
                    await HandleModeChangedAsync(modeChanged, token).ConfigureAwait(false);
                    break;

                case TypingEchoEvent typingEcho:
                    await HandleTypingEchoAsync(typingEcho, token).ConfigureAwait(false);
                    break;

                case NavigationCommandEvent navigationCommand:
                    await HandleNavigationCommandAsync(navigationCommand, token).ConfigureAwait(false);
                    break;

                case RawKeyEvent rawKey:
                    // Raise event so TypingEchoHandler (and others) can process raw key events.
                    RawKeyReceived?.Invoke(this, rawKey);
                    break;

                case NotificationEvent notification:
                    // Log only — most UIA notifications are system noise.
                    // User-facing announcements come via LiveRegionChanged instead.
                    _logger.LogDebug("Notification: {Text}", notification.NotificationText);
                    break;

                case PropertyChangedEvent propertyChanged:
                    _logger.LogDebug("PropertyChanged: PropertyId={PropertyId}, NewValue={NewValue}",
                        propertyChanged.PropertyId, propertyChanged.NewValue);
                    break;

                case StructureChangedEvent structureChanged:
                    _logger.LogDebug("StructureChanged: RuntimeId={RuntimeId}",
                        structureChanged.RuntimeId is not null ? string.Join(",", structureChanged.RuntimeId) : "(null)");
                    break;

                default:
                    _logger.LogWarning("Unhandled event type: {EventType}", evt.GetType().Name);
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw; // Let pipeline-level cancellation propagate
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {EventType} event", evt.GetType().Name);
        }
    }

    private async Task HandleFocusChangedAsync(FocusChangedEvent focus, CancellationToken token)
    {
        // Notify subscribers (e.g. NavigationManager for auto-mode-switching)
        FocusChangedProcessed?.Invoke(this, focus);

        // Focus changes are high priority — interrupt current speech
        var text = BuildFocusAnnouncement(focus);
        var utterance = new Utterance(text, SpeechPriority.Interrupt);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private async Task HandleNavigationAsync(NavigationEvent nav, CancellationToken token)
    {
        var text = $"{nav.ElementName}. {nav.ControlType}";
        var utterance = new Utterance(text, SpeechPriority.High);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private async Task HandleLiveRegionAsync(LiveRegionChangedEvent liveRegion, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(liveRegion.Text))
            return;

        var priority = liveRegion.Politeness == LiveRegionPoliteness.Assertive
            ? SpeechPriority.High
            : SpeechPriority.Low;

        var utterance = new Utterance(liveRegion.Text, priority);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private async Task HandleModeChangedAsync(ModeChangedEvent modeChanged, CancellationToken token)
    {
        // Play audio cue first
        var cueName = modeChanged.NewMode == InteractionMode.Browse ? "browse_mode" : "focus_mode";
        _audioCuePlayer.Play(cueName);

        var modeText = modeChanged.NewMode == InteractionMode.Browse ? "Browse mode" : "Focus mode";
        var utterance = new Utterance(modeText, SpeechPriority.Interrupt);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private async Task HandleNavigationCommandAsync(NavigationCommandEvent evt, CancellationToken token)
    {
        _logger.LogDebug("NavigationCommand dispatched: {Command}", evt.Command);
        NavigationCommandReceived?.Invoke(this, evt);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task HandleNotificationAsync(NotificationEvent notification, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(notification.NotificationText))
            return;

        var utterance = new Utterance(notification.NotificationText, SpeechPriority.Low);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private async Task HandleTypingEchoAsync(TypingEchoEvent typingEcho, CancellationToken token)
    {
        var utterance = new Utterance(typingEcho.Text, SpeechPriority.High);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private static string BuildFocusAnnouncement(FocusChangedEvent focus)
    {
        var parts = new List<string>();

        if (focus.HeadingLevel > 0)
            parts.Add($"Heading level {focus.HeadingLevel}");

        if (!string.IsNullOrEmpty(focus.LandmarkType))
            parts.Add(focus.LandmarkType);

        if (!string.IsNullOrEmpty(focus.ElementName))
            parts.Add(focus.ElementName);

        if (!string.IsNullOrEmpty(focus.ControlType))
            parts.Add(focus.ControlType);

        if (focus.IsVisited)
            parts.Add("visited");

        if (focus.IsRequired)
            parts.Add("required");

        if (focus.IsExpandable)
            parts.Add(focus.IsExpanded ? "expanded" : "collapsed");

        return string.Join(", ", parts);
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try { _processingTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
