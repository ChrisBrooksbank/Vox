using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Vox.Core.Speech;

namespace Vox.Core.Pipeline;

/// <summary>
/// Main event pipeline backed by Channel&lt;ScreenReaderEvent&gt;.
/// SingleReader = true for performance.
/// Coalescing: consecutive focus events within 30ms keep only the last.
/// Routes events to speech queue with appropriate priority.
/// LiveRegion assertive → High, polite → Low, off → dropped; repeats and polite floods are
/// filtered by LiveRegionMonitor.
/// ModeChanged → audio cue before speech (the only place the mode cue is played).
/// </summary>
public sealed class EventPipeline : IEventSink, IDisposable
{
    private readonly SpeechQueue _speechQueue;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly ILogger<EventPipeline> _logger;
    private readonly LiveRegionMonitor _liveRegionMonitor;
    private readonly AnnouncementBuilder _announcementBuilder;
    private readonly IOptionsMonitor<VoxSettings>? _settings;
    private readonly Channel<ScreenReaderEvent> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    private const int FocusCoalescingWindowMs = 30;

    public EventPipeline(
        SpeechQueue speechQueue,
        IAudioCuePlayer audioCuePlayer,
        ILogger<EventPipeline> logger,
        LiveRegionMonitor? liveRegionMonitor = null,
        AnnouncementBuilder? announcementBuilder = null,
        IOptionsMonitor<VoxSettings>? settings = null)
    {
        _speechQueue = speechQueue;
        _audioCuePlayer = audioCuePlayer;
        _logger = logger;
        _liveRegionMonitor = liveRegionMonitor ?? new LiveRegionMonitor();
        _announcementBuilder = announcementBuilder ?? new AnnouncementBuilder();
        _settings = settings;

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

    /// <summary>
    /// Asked, after <see cref="FocusChangedProcessed"/> handlers have run, whether a focus change
    /// should be announced. Null means always announce.
    /// </summary>
    public Func<FocusChangedEvent, bool>? FocusAnnouncementFilter { get; set; }

    /// <summary>Raised when a PropertyChangedEvent is processed.</summary>
    public event EventHandler<PropertyChangedEvent>? PropertyChangedProcessed;

    /// <summary>Raised when an ElementSelectedEvent is processed.</summary>
    public event EventHandler<ElementSelectedEvent>? ElementSelectedProcessed;

    /// <summary>Raised when a StructureChangedEvent is processed (for virtual buffer updates).</summary>
    public event EventHandler<StructureChangedEvent>? StructureChangedProcessed;

    /// <summary>Raised when a DocumentChangedEvent is processed.</summary>
    public event EventHandler<DocumentChangedEvent>? DocumentChangedProcessed;

    /// <summary>Raised when a FocusInDocumentEvent is processed.</summary>
    public event EventHandler<FocusInDocumentEvent>? FocusInDocumentProcessed;

    /// <summary>Raised when a SubtreeChangedEvent is processed.</summary>
    public event EventHandler<SubtreeChangedEvent>? SubtreeChangedProcessed;

    /// <summary>Raised when an ElementsListClosedEvent is processed.</summary>
    public event EventHandler<ElementsListClosedEvent>? ElementsListClosedProcessed;

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

                case LiveRegionFlushEvent liveRegionFlush:
                    await HandleLiveRegionFlushAsync(liveRegionFlush, token).ConfigureAwait(false);
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
                    _logger.LogDebug("Notification: {Text}", notification.NotificationText);
                    await HandleNotificationAsync(notification, token).ConfigureAwait(false);
                    break;

                case NotificationFlushEvent notificationFlush:
                    await HandleNotificationFlushAsync(notificationFlush, token).ConfigureAwait(false);
                    break;

                case PropertyChangedEvent propertyChanged:
                    // Not the new value: it can be the text of an edit field
                    _logger.LogDebug("PropertyChanged: PropertyId={PropertyId}", propertyChanged.PropertyId);
                    PropertyChangedProcessed?.Invoke(this, propertyChanged);
                    break;

                case ElementSelectedEvent elementSelected:
                    ElementSelectedProcessed?.Invoke(this, elementSelected);
                    break;

                case StructureChangedEvent structureChanged:
                    _logger.LogDebug("StructureChanged: RuntimeId={RuntimeId}",
                        structureChanged.RuntimeId is not null ? string.Join(",", structureChanged.RuntimeId) : "(null)");
                    StructureChangedProcessed?.Invoke(this, structureChanged);
                    break;

                case DocumentChangedEvent documentChanged:
                    DocumentChangedProcessed?.Invoke(this, documentChanged);
                    break;

                case FocusInDocumentEvent focusInDocument:
                    FocusInDocumentProcessed?.Invoke(this, focusInDocument);
                    break;

                case SubtreeChangedEvent subtreeChanged:
                    SubtreeChangedProcessed?.Invoke(this, subtreeChanged);
                    break;

                case ElementsListClosedEvent elementsListClosed:
                    ElementsListClosedProcessed?.Invoke(this, elementsListClosed);
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

        // Subscribers may have decided this focus change shouldn't be spoken (e.g. focus returning
        // to the page after an Elements List jump, which must not talk over the jump)
        if (FocusAnnouncementFilter is { } filter && !filter(focus))
            return;

        // Nothing to say (e.g. an unnamed group): don't send an empty Interrupt that would
        // silently cut off whatever is being spoken
        var text = BuildFocusAnnouncement(focus);
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Focus changes are high priority — interrupt current speech
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
        if (liveRegion.Politeness == LiveRegionPoliteness.Off)
            return;

        // An emptied region is recorded (so the same message set again is spoken), never spoken
        if (string.IsNullOrWhiteSpace(liveRegion.Text))
        {
            if (liveRegion.SourceId is not null)
                _liveRegionMonitor.Evaluate(liveRegion.SourceId, string.Empty, liveRegion.Politeness, out _);
            return;
        }

        // Diff against last text (announcing only additions) and throttle polite updates per region
        var text = _liveRegionMonitor.Evaluate(liveRegion.SourceId, liveRegion.Text, liveRegion.Politeness, out var retryAfter);
        if (retryAfter > TimeSpan.Zero && liveRegion.SourceId is not null)
            ScheduleLiveRegionFlush(liveRegion.SourceId, retryAfter);
        if (text is null)
            return;

        var priority = liveRegion.Politeness == LiveRegionPoliteness.Assertive
            ? SpeechPriority.High
            : SpeechPriority.Low;

        var utterance = new Utterance(text, priority);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private async Task HandleLiveRegionFlushAsync(LiveRegionFlushEvent flush, CancellationToken token)
    {
        var text = _liveRegionMonitor.FlushPending(flush.SourceId, out var retryAfter);
        if (retryAfter > TimeSpan.Zero)
            ScheduleLiveRegionFlush(flush.SourceId, retryAfter);
        if (text is null)
            return;

        await _speechQueue.EnqueueAsync(new Utterance(text, SpeechPriority.Low), token).ConfigureAwait(false);
    }

    /// <summary>Posts a <see cref="LiveRegionFlushEvent"/> once a polite region's cooldown ends.</summary>
    private void ScheduleLiveRegionFlush(string sourceId, TimeSpan delay)
    {
        _ = Task.Delay(delay, _cts.Token).ContinueWith(
            t =>
            {
                if (!t.IsCanceled)
                    Post(new LiveRegionFlushEvent(DateTimeOffset.UtcNow, sourceId));
            },
            TaskScheduler.Default);
    }

    private async Task HandleModeChangedAsync(ModeChangedEvent modeChanged, CancellationToken token)
    {
        // Play audio cue first
        var cueName = modeChanged.NewMode == InteractionMode.Browse ? "browse_mode" : "focus_mode";
        _audioCuePlayer.Play(cueName);

        // Automatic switches are signalled by the cue alone
        if (!modeChanged.Announce)
            return;

        // Queued rather than Interrupt, so it never cuts off a focus announcement in progress
        var modeText = modeChanged.NewMode == InteractionMode.Browse ? "Browse mode" : "Focus mode";
        var utterance = new Utterance(modeText, SpeechPriority.High);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private async Task HandleNavigationCommandAsync(NavigationCommandEvent evt, CancellationToken token)
    {
        _logger.LogDebug("NavigationCommand dispatched: {Command}", evt.Command);
        NavigationCommandReceived?.Invoke(this, evt);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    // Latest text per "most recent" notification activity, waiting for its coalescing delay
    // (only touched on the pipeline thread)
    private readonly Dictionary<string, (string Text, SpeechPriority Priority)> _pendingNotifications = new();
    private const int NotificationCoalesceMs = 150;

    /// <summary>
    /// Speaks application notifications (toasts, "download complete", ...). Important ones are
    /// High priority and spoken from any process; others are Low and only from the foreground app. For the "most recent" kinds,
    /// a burst of notifications with the same activity id speaks only the last.
    /// </summary>
    private async Task HandleNotificationAsync(NotificationEvent notification, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(notification.NotificationText))
            return;

        // Important notifications are spoken whoever raises them: system notifications (toasts,
        // flyouts) never come from the foreground app. Others only from the app in use.
        bool important = notification.Processing is 0 or 1;
        if (!notification.IsFromForeground && !important)
            return;

        var priority = notification.Processing is 0 or 1 ? SpeechPriority.High : SpeechPriority.Low;
        bool mostRecentOnly = notification.Processing is 1 or 3 or 4;

        if (mostRecentOnly && !string.IsNullOrEmpty(notification.ActivityId))
        {
            bool alreadyScheduled = _pendingNotifications.ContainsKey(notification.ActivityId);
            _pendingNotifications[notification.ActivityId] = (notification.NotificationText, priority);
            if (!alreadyScheduled)
            {
                var activityId = notification.ActivityId;
                _ = Task.Delay(NotificationCoalesceMs, _cts.Token).ContinueWith(
                    t =>
                    {
                        if (!t.IsCanceled)
                            Post(new NotificationFlushEvent(DateTimeOffset.UtcNow, activityId));
                    },
                    TaskScheduler.Default);
            }
            return;
        }

        await _speechQueue.EnqueueAsync(new Utterance(notification.NotificationText, priority), token).ConfigureAwait(false);
    }

    private async Task HandleNotificationFlushAsync(NotificationFlushEvent flush, CancellationToken token)
    {
        if (!_pendingNotifications.Remove(flush.ActivityId, out var pending))
            return;
        await _speechQueue.EnqueueAsync(new Utterance(pending.Text, pending.Priority), token).ConfigureAwait(false);
    }

    private async Task HandleTypingEchoAsync(TypingEchoEvent typingEcho, CancellationToken token)
    {
        // Each typed character cuts off whatever is being said (as NVDA does); a completed word
        // is queued after the boundary character echoed just before it
        var priority = typingEcho.IsWord ? SpeechPriority.High : SpeechPriority.Interrupt;
        var utterance = new Utterance(typingEcho.Text, priority);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);
    }

    private string BuildFocusAnnouncement(FocusChangedEvent focus)
    {
        var settings = _settings?.CurrentValue ?? new VoxSettings();
        return _announcementBuilder.Build(
            focus, VerbosityProfile.For(settings.VerbosityLevel), settings.AnnounceVisitedLinks);
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try { _processingTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
