using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Text;

namespace Vox.Core.Accessibility;

/// <summary>
/// Reads new output in the focused terminal (Windows Terminal, the classic console): on each
/// text change it snapshots the visible lines on the UIA thread, works out what was printed
/// (<see cref="TerminalOutputTracker"/>) and speaks it, paced by <see cref="TerminalSpeechThrottle"/>.
/// Call the Handle methods on the pipeline thread.
/// </summary>
public sealed class TerminalMonitor
{
    private readonly UIAThread _uiaThread;
    private readonly IFocusedTextSource _source;
    private readonly SpeechQueue _speechQueue;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<TerminalMonitor> _logger;
    private readonly object _lock = new();
    private readonly TerminalOutputTracker _tracker;
    private readonly TerminalSpeechThrottle _throttle = new();
    private bool _flushScheduled;

    public TerminalMonitor(UIAThread uiaThread, IFocusedTextSource source, SpeechQueue speechQueue,
        ILogger<TerminalMonitor> logger)
        : this(uiaThread, source, speechQueue, logger, () => DateTimeOffset.UtcNow)
    {
    }

    public TerminalMonitor(UIAThread uiaThread, IFocusedTextSource source, SpeechQueue speechQueue,
        ILogger<TerminalMonitor> logger, Func<DateTimeOffset> clock)
    {
        _uiaThread = uiaThread;
        _source = source;
        _speechQueue = speechQueue;
        _logger = logger;
        _clock = clock;
        _tracker = new TerminalOutputTracker(clock);
    }

    /// <summary>A key reached the focused application: in a terminal its echo isn't output.</summary>
    public void HandleRawKey(RawKeyEvent e)
    {
        if (e.Key.IsKeyDown && _source.IsTerminal && (e.Key.Modifiers & (Input.KeyModifiers.Ctrl | Input.KeyModifiers.Alt | Input.KeyModifiers.Insert)) == 0)
            lock (_lock) _tracker.NoteKeyPress();
    }

    /// <summary>Focus moved: start from what the (new) terminal shows now, so it isn't all read out.</summary>
    public async Task HandleFocusChangedAsync()
    {
        lock (_lock)
        {
            _tracker.Reset();
            _throttle.Clear();
        }
        if (!_source.IsTerminal)
            return;
        var lines = await GetLinesAsync().ConfigureAwait(false);
        if (lines is not null)
            lock (_lock) _tracker.Reset(lines);
    }

    /// <summary>The focused control's text changed.</summary>
    public async Task HandleTextEditedAsync(TextEditedEvent e)
    {
        if (!_source.IsTerminal)
            return;
        var lines = await GetLinesAsync().ConfigureAwait(false);
        if (lines is null)
            return;

        bool schedule;
        lock (_lock)
        {
            _throttle.Add(_tracker.Update(lines));
            schedule = _throttle.HasPending && !_flushScheduled;
            if (schedule)
                _flushScheduled = true;
        }
        if (schedule)
            await FlushWhenDueAsync().ConfigureAwait(false);
    }

    private async Task FlushWhenDueAsync()
    {
        while (true)
        {
            TimeSpan wait;
            string? text;
            lock (_lock)
            {
                var now = _clock();
                text = _throttle.Flush(now);
                if (text is null && _throttle.HasPending)
                {
                    wait = _throttle.DueAt - now;
                }
                else
                {
                    _flushScheduled = false;
                    wait = TimeSpan.Zero;
                }
            }
            if (text is not null)
                _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Normal));
            if (wait <= TimeSpan.Zero)
                return;
            await Task.Delay(wait).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<string>?> GetLinesAsync()
    {
        try
        {
            return await _uiaThread.RunAsync(_source.GetVisibleLines).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the terminal");
            return null;
        }
    }
}
