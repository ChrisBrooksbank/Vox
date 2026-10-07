using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Vox.Core.Speech;

/// <summary>
/// Priority-based speech queue backed by Channel&lt;Utterance&gt;.
///
/// Interrupt handling: every Interrupt enqueue (and every <see cref="CancelAll"/>) starts a new
/// "epoch". Utterances queued in an earlier epoch are dropped, and the utterance currently being
/// spoken is cancelled immediately — the reader does not have to finish speaking first.
///
/// Coalescing: multiple Normal-priority utterances within 50ms window get concatenated.
/// </summary>
public sealed class SpeechQueue : IDisposable
{
    private readonly ISpeechEngine _engine;
    private readonly ILogger<SpeechQueue> _logger;
    private readonly Channel<QueuedUtterance> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    private const int CoalescingWindowMs = 50;

    // Utterances with an epoch older than this are stale and must not be spoken.
    private long _epoch;

    // CTS for the utterance currently being spoken (guarded by _speakLock).
    private readonly object _speakLock = new();
    private CancellationTokenSource? _currentSpeechCts;

    // Guards _suspended together with the enqueue methods' suspended-check + Prepare + write, so an
    // Enqueue racing a Suspend() can't slip an utterance in after Suspend() has taken effect.
    private readonly object _suspendLock = new();

    private sealed record QueuedUtterance(
        Utterance Utterance,
        long Epoch,
        TaskCompletionSource? Completion)
    {
        public long EnqueuedTick { get; } = Environment.TickCount64;
    }

    public SpeechQueue(ISpeechEngine engine, ILogger<SpeechQueue> logger)
    {
        _engine = engine;
        _logger = logger;
        _channel = Channel.CreateUnbounded<QueuedUtterance>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _processingTask = Task.Run(ProcessQueueAsync, _cts.Token);
    }

    /// <summary>
    /// Processing applied to each utterance's text just before it goes to the engine (history and
    /// the utterance events keep the original text). Set at startup.
    /// </summary>
    public TextProcessor TextProcessor { get; set; } = TextProcessor.None;

    /// <summary>Plays an utterance's <see cref="Utterance.SoundCue"/> just before it is spoken. Set at startup.</summary>
    public Action<string>? CuePlayer { get; set; }

    public ValueTask EnqueueAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_suspendLock)
        {
            if (_suspended) return ValueTask.CompletedTask;
            _channel.Writer.TryWrite(Prepare(utterance, null));
        }
        return ValueTask.CompletedTask;
    }

    public void Enqueue(Utterance utterance)
    {
        lock (_suspendLock)
        {
            if (_suspended) return;
            _channel.Writer.TryWrite(Prepare(utterance, null));
        }
    }

    private volatile bool _suspended;

    /// <summary>True while <see cref="Suspend"/> is in effect.</summary>
    public bool IsSuspended => _suspended;

    /// <summary>
    /// Stops speech and drops everything enqueued until <see cref="Resume"/> (e.g. while the
    /// setup wizard speaks through the engine directly): an Interrupt from the queue would
    /// otherwise cancel the engine and cut off the wizard's prompt.
    /// </summary>
    public void Suspend()
    {
        lock (_suspendLock)
        {
            _suspended = true;
            CancelAll();
        }
    }

    /// <summary>Accepts utterances again after <see cref="Suspend"/>.</summary>
    public void Resume()
    {
        lock (_suspendLock) { _suspended = false; }
    }

    /// <summary>
    /// Enqueues an utterance and returns a task that completes once it has been spoken.
    /// The task is cancelled if the utterance is interrupted, flushed by <see cref="CancelAll"/>,
    /// or <paramref name="cancellationToken"/> fires.
    /// </summary>
    public Task EnqueueAndWaitAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        }

        lock (_suspendLock)
        {
            if (_suspended || !_channel.Writer.TryWrite(Prepare(utterance, completion)))
                completion.TrySetCanceled();
        }

        return completion.Task;
    }

    /// <summary>
    /// Stops current speech and discards everything queued so far.
    /// </summary>
    public void CancelAll()
    {
        Interlocked.Increment(ref _epoch);
        CancelCurrentSpeech();
        _engine.Cancel();
    }

    private QueuedUtterance Prepare(Utterance utterance, TaskCompletionSource? completion)
    {
        long epoch;
        if (utterance.Priority == SpeechPriority.Interrupt)
        {
            // Everything queued before this interrupt is now stale; stop what is playing.
            epoch = Interlocked.Increment(ref _epoch);
            CancelCurrentSpeech();
            _engine.Cancel();
        }
        else
        {
            epoch = Interlocked.Read(ref _epoch);
        }
        return new QueuedUtterance(utterance, epoch, completion);
    }

    private void CancelCurrentSpeech()
    {
        lock (_speakLock)
        {
            _currentSpeechCts?.Cancel();
        }
    }

    private bool IsStale(QueuedUtterance item) => item.Epoch < Interlocked.Read(ref _epoch);

    private async Task ProcessQueueAsync()
    {
        var token = _cts.Token;
        var reader = _channel.Reader;

        // Utterances waiting to be spoken, in arrival order. Kept across iterations: after each
        // spoken group newly queued items are merged in and the order is recomputed, so a High
        // utterance overtakes Low/Normal ones that were queued before it.
        var pending = new List<QueuedUtterance>();

        try
        {
            while (!token.IsCancellationRequested)
            {
                // Wait for at least one utterance
                if (pending.Count == 0 && !await reader.WaitToReadAsync(token).ConfigureAwait(false))
                    break;

                // Drain all pending utterances
                int before = pending.Count;
                while (reader.TryRead(out var u))
                    pending.Add(u);

                // Coalesce Normal-priority utterances: wait for more within window
                // (not for awaited utterances such as Say All lines, which arrive one at a time)
                if (before == 0 && pending.Count == 1 && pending[0].Utterance.Priority == SpeechPriority.Normal
                    && pending[0].Completion is null)
                {
                    await Task.Delay(CoalescingWindowMs, token).ConfigureAwait(false);
                    while (reader.TryRead(out var extra))
                        pending.Add(extra);
                }

                // Drop anything superseded by an Interrupt or CancelAll
                DropStale(pending);
                if (pending.Count == 0)
                    continue;

                // Highest priority first (lower enum value = higher priority); stable for equal priority
                var ordered = pending
                    .Select((item, index) => (item, index))
                    .OrderBy(x => x.item.Utterance.Priority)
                    .ThenBy(x => x.index)
                    .Select(x => x.item)
                    .ToList();

                // Speak one group, then look at the queue again
                var group = CoalesceUtterances(ordered)[0];
                foreach (var item in group)
                    pending.Remove(item);

                token.ThrowIfCancellationRequested();

                if (group.All(IsStale))
                {
                    Complete(group, spoken: false);
                    continue;
                }

                await SpeakGroupAsync(group, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SpeechQueue processing cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SpeechQueue processing");
        }
        finally
        {
            Complete(pending, spoken: false);
        }
    }

    private void DropStale(List<QueuedUtterance> pending)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (IsStale(pending[i]))
            {
                pending[i].Completion?.TrySetCanceled();
                pending.RemoveAt(i);
            }
        }
    }

    /// <summary>Receives how long each utterance waited in the queue (latency measurement).</summary>
    public Action<TimeSpan>? QueueLatency { get; set; }

    /// <summary>Raised on the queue's thread just before an utterance is handed to the engine. Handlers must be quick.</summary>
    public event EventHandler<Utterance>? UtteranceStarted;

    /// <summary>Raised on the queue's thread when the engine has finished (or abandoned) an utterance.</summary>
    public event EventHandler<Utterance>? UtteranceFinished;

    private void RaiseSafely(EventHandler<Utterance>? handler, Utterance utterance)
    {
        try { handler?.Invoke(this, utterance); }
        catch (Exception ex) { _logger.LogError(ex, "Error in a speech queue event handler"); }
    }

    private async Task SpeakGroupAsync(List<QueuedUtterance> group, CancellationToken token)
    {
        var first = group[0].Utterance;
        var utterance = group.Count == 1
            ? first
            : new Utterance(string.Join(". ", group.Select(g => g.Utterance.Text)), first.Priority, first.SoundCue);
        var epoch = group.Max(g => g.Epoch);

        using var speechCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_speakLock)
        {
            _currentSpeechCts = speechCts;
            // An interrupt may have arrived between the stale check and now
            if (epoch < Interlocked.Read(ref _epoch))
                speechCts.Cancel();
        }

        bool spoken = false;
        try
        {
            speechCts.Token.ThrowIfCancellationRequested();
            QueueLatency?.Invoke(TimeSpan.FromMilliseconds(Environment.TickCount64 - group.Min(g => g.EnqueuedTick)));
            RaiseSafely(UtteranceStarted, utterance);
            try
            {
                var processed = TextProcessor.Process(utterance);
                if (processed.SoundCue is { } cue && CuePlayer is { } playCue)
                {
                    try { playCue(cue); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Could not play the cue {Cue}", cue); }
                }
                await _engine.SpeakAsync(processed, speechCts.Token).ConfigureAwait(false);
            }
            finally
            {
                RaiseSafely(UtteranceFinished, utterance);
            }
            spoken = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Complete(group, spoken: false);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Interrupted by a higher-priority utterance or CancelAll — normal operation
            _logger.LogDebug("Speech interrupted ({Priority}, {Length} chars)", utterance.Priority, utterance.Text.Length);
        }
        catch (Exception ex)
        {
            // Never log the text: utterances carry typed characters and form field values
            _logger.LogError(ex, "Error speaking utterance ({Priority}, {Length} chars)", utterance.Priority, utterance.Text.Length);
        }
        finally
        {
            lock (_speakLock)
            {
                if (ReferenceEquals(_currentSpeechCts, speechCts))
                    _currentSpeechCts = null;
            }
        }

        Complete(group, spoken);
    }

    private static void Complete(List<QueuedUtterance> group, bool spoken)
    {
        foreach (var item in group)
        {
            if (spoken)
                item.Completion?.TrySetResult();
            else
                item.Completion?.TrySetCanceled();
        }
    }

    /// <summary>
    /// Groups consecutive Normal utterances (to be spoken as one); every other utterance is its own group.
    /// </summary>
    private static List<List<QueuedUtterance>> CoalesceUtterances(List<QueuedUtterance> utterances)
    {
        var result = new List<List<QueuedUtterance>>();
        var i = 0;

        while (i < utterances.Count)
        {
            var group = new List<QueuedUtterance> { utterances[i] };
            if (utterances[i].Utterance.Priority == SpeechPriority.Normal)
            {
                var j = i + 1;
                while (j < utterances.Count && utterances[j].Utterance.Priority == SpeechPriority.Normal)
                {
                    group.Add(utterances[j]);
                    j++;
                }
                i = j;
            }
            else
            {
                i++;
            }
            result.Add(group);
        }

        return result;
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try { _processingTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
