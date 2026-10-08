using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Input;

namespace Vox.Core.Speech;

/// <summary>
/// Reviewing what Vox said: previous/next step through the last <see cref="ReviewDepth"/>
/// entries of the speech history (the boundary cue at either end), and pressing the same key
/// twice quickly copies the entry to the clipboard. Something new being said starts the review
/// again from the latest entry. Reviewed entries are not added to the history again.
/// </summary>
public sealed class SpeechHistoryCommands
{
    /// <summary>How far back the history can be reviewed.</summary>
    public const int ReviewDepth = 100;

    private readonly SpeechHistory _history;
    private readonly SpeechQueue _speechQueue;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly IClipboard _clipboard;
    private readonly RepeatPressCounter _presses;
    private readonly object _lock = new();
    // Index into the reviewable entries (oldest first); null: not reviewing, start from the latest
    private int? _position;

    public SpeechHistoryCommands(SpeechHistory history, SpeechQueue speechQueue, IAudioCuePlayer audioCuePlayer,
        IClipboard clipboard, Func<long>? clockMs = null)
    {
        _history = history;
        _speechQueue = speechQueue;
        _audioCuePlayer = audioCuePlayer;
        _clipboard = clipboard;
        _presses = new RepeatPressCounter(clockMs: clockMs);
        _history.Added += (_, _) => { lock (_lock) _position = null; };
    }

    /// <summary>Runs <paramref name="command"/> if it is a speech history command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.SpeechHistoryPrevious: Step(-1, (int)command); return true;
            case NavigationCommand.SpeechHistoryNext: Step(+1, (int)command); return true;
            default: return false;
        }
    }

    private void Step(int direction, int key)
    {
        var entries = _history.Entries;
        if (entries.Count > ReviewDepth)
            entries = entries.Skip(entries.Count - ReviewDepth).ToList();
        if (entries.Count == 0)
        {
            Say("No speech history");
            return;
        }

        string entry;
        bool boundary = false;
        lock (_lock)
        {
            if (_presses.Press(key) == 2 && _position is { } shown && shown < entries.Count)
            {
                // Pressed twice: copy what was just said, without moving
                entry = entries[shown];
                Say(_clipboard.SetText(entry) ? "Copied" : "Could not copy");
                return;
            }

            // The first press says the latest entry (the previous one with "previous")
            int start = _position ?? entries.Count;
            int next = start + direction;
            if (next < 0 || next >= entries.Count)
            {
                boundary = _position is not null;
                next = Math.Clamp(next, 0, entries.Count - 1);
            }
            _position = next;
            entry = entries[next];
        }
        if (boundary)
            _audioCuePlayer.Play("boundary");
        Say(entry);
    }

    private void Say(string text) =>
        _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt) { RecordInHistory = false });
}
