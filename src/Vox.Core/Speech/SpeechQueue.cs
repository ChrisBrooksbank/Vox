using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Vox.Core.Speech;

/// <summary>
/// Priority-based speech queue backed by Channel&lt;Utterance&gt;.
/// Interrupt cancels current speech immediately.
/// Coalescing: multiple Normal-priority utterances within 50ms window get concatenated.
/// </summary>
public sealed class SpeechQueue : IDisposable
{
    private readonly ISpeechEngine _engine;
    private readonly ILogger<SpeechQueue> _logger;
    private readonly Channel<Utterance> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    private const int CoalescingWindowMs = 50;

    public SpeechQueue(ISpeechEngine engine, ILogger<SpeechQueue> logger)
    {
        _engine = engine;
        _logger = logger;
        _channel = Channel.CreateUnbounded<Utterance>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _processingTask = Task.Run(ProcessQueueAsync, _cts.Token);
    }

    public async ValueTask EnqueueAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(utterance, cancellationToken).ConfigureAwait(false);
    }

    public void Enqueue(Utterance utterance)
    {
        _channel.Writer.TryWrite(utterance);
    }

    private async Task ProcessQueueAsync()
    {
        var token = _cts.Token;
        var reader = _channel.Reader;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // Wait for at least one utterance
                if (!await reader.WaitToReadAsync(token).ConfigureAwait(false))
                    break;

                // Drain all pending utterances and sort by priority
                var pending = new List<Utterance>();
                while (reader.TryRead(out var u))
                {
                    pending.Add(u);
                }

                if (pending.Count == 0)
                    continue;

                // Sort by priority (lower enum value = higher priority)
                pending.Sort((a, b) => a.Priority.CompareTo(b.Priority));

                // If any Interrupt utterances, cancel immediately
                if (pending.Any(u => u.Priority == SpeechPriority.Interrupt))
                {
                    _engine.Cancel();
                }

                // Coalesce Normal-priority utterances: wait for more within window
                // then concatenate consecutive Normal utterances
                if (pending.Count == 1 && pending[0].Priority == SpeechPriority.Normal)
                {
                    // Wait briefly for more Normal utterances to coalesce
                    await Task.Delay(CoalescingWindowMs, token).ConfigureAwait(false);
                    while (reader.TryRead(out var extra))
                    {
                        pending.Add(extra);
                    }
                    pending.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                }

                // Group consecutive Normal utterances and coalesce them
                var toSpeak = CoalesceUtterances(pending);

                foreach (var utterance in toSpeak)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await _engine.SpeakAsync(utterance, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error speaking utterance: {Text}", utterance.Text);
                    }
                }
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
    }

    private static List<Utterance> CoalesceUtterances(List<Utterance> utterances)
    {
        var result = new List<Utterance>();
        var i = 0;

        while (i < utterances.Count)
        {
            var current = utterances[i];

            if (current.Priority == SpeechPriority.Normal)
            {
                // Collect consecutive Normal utterances
                var texts = new List<string> { current.Text };
                var j = i + 1;
                while (j < utterances.Count && utterances[j].Priority == SpeechPriority.Normal)
                {
                    texts.Add(utterances[j].Text);
                    j++;
                }

                if (texts.Count > 1)
                {
                    result.Add(new Utterance(string.Join(". ", texts), SpeechPriority.Normal, current.SoundCue));
                }
                else
                {
                    result.Add(current);
                }
                i = j;
            }
            else
            {
                result.Add(current);
                i++;
            }
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
