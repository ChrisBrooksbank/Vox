using Microsoft.Extensions.Logging;
using Vox.Core.Buffer;
using Vox.Core.Speech;

namespace Vox.Core.Navigation;

/// <summary>
/// Implements "Say All" continuous reading (Insert+Down).
///
/// Behaviour:
///   - Starts reading from the current cursor position, one line at a time.
///   - Speaks each line as a Normal-priority utterance via SpeechQueue and waits for it to be
///     spoken before advancing the cursor, so the cursor tracks what the user has heard.
///   - Stops when the end of the document is reached (boundary).
///   - Any cancellation request (e.g. keypress) stops reading immediately and flushes queued speech.
/// </summary>
public sealed class SayAllController
{
    private readonly SpeechQueue _speechQueue;
    private readonly ILogger<SayAllController> _logger;

    private CancellationTokenSource? _cts;
    private Task _readingTask = Task.CompletedTask;

    public SayAllController(SpeechQueue speechQueue, ILogger<SayAllController> logger)
    {
        _speechQueue = speechQueue;
        _logger = logger;
    }

    /// <summary>True while Say All is actively reading.</summary>
    public bool IsReading => !_readingTask.IsCompleted;

    /// <summary>
    /// Starts continuous reading from the cursor's current position.
    /// If already reading, cancels the previous session first.
    /// </summary>
    public void Start(VBufferCursor cursor) => Start(new BufferSayAllSource(cursor));

    /// <summary>
    /// Starts continuous reading from <paramref name="source"/> (the buffer, or an edit control).
    /// If already reading, cancels the previous session first.
    /// </summary>
    public void Start(ISayAllSource source)
    {
        // Cancel any existing session
        Cancel();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _readingTask = Task.Run(async () => await ReadLoopAsync(source, token).ConfigureAwait(false), token);
    }

    /// <summary>
    /// Cancels continuous reading and discards any speech it queued. No-op if not reading.
    /// </summary>
    public void Cancel()
    {
        if (_cts is not null)
        {
            bool wasReading = IsReading;
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;

            if (wasReading)
                _speechQueue.CancelAll();
        }
    }

    // -------------------------------------------------------------------------
    // Reading loop
    // -------------------------------------------------------------------------

    private async Task ReadLoopAsync(ISayAllSource source, CancellationToken token)
    {
        try
        {
            // Read and speak the current line first
            string? currentLine = await source.CurrentLineAsync(token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(currentLine))
            {
                await SpeakLineAsync(currentLine.TrimEnd('\r', '\n'), token).ConfigureAwait(false);
            }

            // Advance line by line until end of document or cancellation
            while (!token.IsCancellationRequested)
            {
                string? line = await source.NextLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    // Reached end of document
                    _logger.LogDebug("SayAll reached end of document");
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    await SpeakLineAsync(line.TrimEnd('\r', '\n'), token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SayAll cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SayAll reading loop");
        }
    }

    private async Task SpeakLineAsync(string line, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // Wait until the line has actually been spoken. If it is interrupted (user navigation,
        // StopSpeech) the task is cancelled and reading stops.
        var utterance = new Utterance(line, SpeechPriority.Normal);
        await _speechQueue.EnqueueAndWaitAsync(utterance, token).ConfigureAwait(false);
    }
}
