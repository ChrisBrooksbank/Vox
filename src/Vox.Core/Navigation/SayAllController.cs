using Microsoft.Extensions.Logging;
using Vox.Core.Buffer;
using Vox.Core.Speech;

namespace Vox.Core.Navigation;

/// <summary>
/// Implements "Say All" continuous reading (Insert+Down).
///
/// Behaviour:
///   - Starts reading from the current cursor position, one line at a time.
///   - Speaks each line as a Normal-priority utterance via SpeechQueue.
///   - Advances the cursor after each utterance.
///   - Stops when the end of the document is reached (boundary).
///   - Any cancellation request (e.g. keypress) stops reading immediately
///     via CancellationTokenSource.
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
    public void Start(VBufferCursor cursor)
    {
        // Cancel any existing session
        Cancel();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _readingTask = Task.Run(async () => await ReadLoopAsync(cursor, token).ConfigureAwait(false), token);
    }

    /// <summary>
    /// Cancels continuous reading. No-op if not reading.
    /// </summary>
    public void Cancel()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    // -------------------------------------------------------------------------
    // Reading loop
    // -------------------------------------------------------------------------

    private async Task ReadLoopAsync(VBufferCursor cursor, CancellationToken token)
    {
        try
        {
            // Read and speak the current line first
            string currentLine = cursor.ReadLineAt(cursor.TextOffset);
            if (!string.IsNullOrWhiteSpace(currentLine))
            {
                await SpeakLineAsync(currentLine, token).ConfigureAwait(false);
            }

            // Advance line by line until end of document or cancellation
            while (!token.IsCancellationRequested)
            {
                string? line = cursor.NextLine();
                if (line is null)
                {
                    // Reached end of document
                    _logger.LogDebug("SayAll reached end of document");
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    await SpeakLineAsync(line, token).ConfigureAwait(false);
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

        var utterance = new Utterance(line, SpeechPriority.Normal);
        await _speechQueue.EnqueueAsync(utterance, token).ConfigureAwait(false);

        // Give the speech engine time to speak before advancing.
        // We yield so cancellation can be observed between lines.
        await Task.Yield();
    }
}
