using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Speech;
using Vox.Core.Text;

namespace Vox.Core.Accessibility;

/// <summary>What the review cursor reviews.</summary>
public enum ReviewMode
{
    /// <summary>The navigator object's text (an edit's text, or a control's name and value).</summary>
    Object,
    /// <summary>The focused control's text.</summary>
    Document,
    /// <summary>Everything in the foreground window, one text or control per line.</summary>
    Screen,
}

/// <summary>
/// The review cursor commands (keypad 7/8/9 line, 4/5/6 word, 1/2/3 character, Shift+7/9 top
/// and bottom, Insert+7/1 review mode): read text without moving the caret. The
/// <see cref="ReviewCursor"/> and its document live on the UIA thread; command handlers never
/// await it. In browse mode the review cursor is tethered to the virtual buffer instead.
/// </summary>
public sealed class ReviewCommands
{
    private readonly UIAThread _uiaThread;
    private readonly ObjectNavigationCommands _navigation;
    private readonly IReviewTextSource _source;
    private readonly SpeechQueue _speechQueue;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger<ReviewCommands> _logger;
    private readonly ReviewCursor _cursor = new();
    private readonly RepeatPressCounter _presses = new();

    private volatile ReviewMode _mode = ReviewMode.Object;
    private int _focusVersion;
    private int _caretVersion;

    // What the cursor's document was taken from (UIA thread only)
    private ReviewMode? _attachedMode;
    private INavigatorObject? _attachedObject;
    private int _attachedFocusVersion = -1;
    private int _attachedCaretVersion;
    private ReviewTether? _attachedTether;
    private IntPtr _attachedWindow;
    private int _generation = -1;

    public ReviewCommands(UIAThread uiaThread, ObjectNavigationCommands navigation, IReviewTextSource source,
        SpeechQueue speechQueue, IAudioCuePlayer audioCuePlayer, IOptionsMonitor<VoxSettings> settings,
        ILogger<ReviewCommands> logger)
    {
        _settings = settings;
        _uiaThread = uiaThread;
        _navigation = navigation;
        _source = source;
        _speechQueue = speechQueue;
        _audioCuePlayer = audioCuePlayer;
        _logger = logger;
    }

    public ReviewMode Mode => _mode;

    /// <summary>
    /// The virtual buffer to review while browsing (null when not browsing). Called on the thread
    /// commands arrive on (the pipeline thread). Set by the host.
    /// </summary>
    public Func<ReviewTether?>? BrowseTether { get; set; }

    /// <summary>Focus moved: document review picks up the newly focused control's text.</summary>
    public void HandleFocusChanged() => Interlocked.Increment(ref _focusVersion);

    /// <summary>The caret moved: the review cursor goes to it, when the setting says so.</summary>
    public void HandleCaretMoved()
    {
        if (!_settings.CurrentValue.ReviewFollowsCaret)
            return;
        Interlocked.Increment(ref _caretVersion);
        // In object review the navigator returns to the focused control with the caret
        _navigation.HandleFocusChanged();
    }

    /// <summary>Runs <paramref name="command"/> if it is a review command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        // Reading the current line, word or character again quickly spells it
        int presses = _presses.Press((int)command);
        switch (command)
        {
            case NavigationCommand.ReviewPrevLine: _ = MoveAsync(TextUnit.Line, -1); return true;
            case NavigationCommand.ReviewCurrentLine: _ = ReadAsync(TextUnit.Line, Spelling.ForPress(presses, TextUnit.Line)); return true;
            case NavigationCommand.ReviewNextLine: _ = MoveAsync(TextUnit.Line, 1); return true;
            case NavigationCommand.ReviewPrevWord: _ = MoveAsync(TextUnit.Word, -1); return true;
            case NavigationCommand.ReviewCurrentWord: _ = ReadAsync(TextUnit.Word, Spelling.ForPress(presses, TextUnit.Word)); return true;
            case NavigationCommand.ReviewNextWord: _ = MoveAsync(TextUnit.Word, 1); return true;
            case NavigationCommand.ReviewPrevChar: _ = MoveAsync(TextUnit.Character, -1); return true;
            case NavigationCommand.ReviewCurrentChar: _ = ReadAsync(TextUnit.Character, Spelling.ForPress(presses, TextUnit.Character)); return true;
            case NavigationCommand.ReviewNextChar: _ = MoveAsync(TextUnit.Character, 1); return true;
            case NavigationCommand.ReviewTop: _ = RunAsync(c => c.Top(), "move to the top"); return true;
            case NavigationCommand.ReviewBottom: _ = RunAsync(c => c.Bottom(), "move to the bottom"); return true;
            case NavigationCommand.NextReviewMode: SwitchMode(+1); return true;
            case NavigationCommand.PrevReviewMode: SwitchMode(-1); return true;
            default: return false;
        }
    }

    public Task MoveAsync(TextUnit unit, int direction) => RunAsync(c => c.Move(unit, direction), "move the review cursor");

    /// <summary>Reads the <paramref name="unit"/> at the review cursor, or spells it.</summary>
    public Task ReadAsync(TextUnit unit, SpellMode spell = SpellMode.None) => RunAsync(c =>
        spell == SpellMode.None ? c.Read(unit)
        : c.TextOf(unit) is { } text ? new ReviewResult(Spelling.Say(text, unit, spell), false)
        : null, "read at the review cursor");

    /// <summary>Switches to the next or previous review mode and says which.</summary>
    public void SwitchMode(int direction)
    {
        var modes = Enum.GetValues<ReviewMode>();
        int index = Array.IndexOf(modes, _mode) + (direction < 0 ? -1 : 1);
        if (index < 0 || index >= modes.Length)
        {
            _audioCuePlayer.Play("boundary");
            return;
        }
        _mode = modes[index];
        Speak(_mode switch
        {
            ReviewMode.Object => "Object review",
            ReviewMode.Document => "Document review",
            _ => "Screen review",
        });
    }

    private async Task RunAsync(Func<ReviewCursor, ReviewResult?> command, string what)
    {
        var mode = _mode;
        int focusVersion = Volatile.Read(ref _focusVersion);
        int caretVersion = Volatile.Read(ref _caretVersion);
        var tether = BrowseTether?.Invoke();
        bool followCaret = _settings.CurrentValue.ReviewFollowsCaret;
        var window = mode == ReviewMode.Screen && tether is null ? _source.ForegroundWindow : IntPtr.Zero;
        bool atBoundary;
        try
        {
            atBoundary = await _uiaThread.RunAsync(() =>
            {
                if (tether is not null)
                    EnsureTethered(tether, followCaret);
                else
                    EnsureDocument(mode, focusVersion, caretVersion, window);
                var result = command(_cursor);
                // Spoken here, in the order commands ran: quick presses finish in order on the UIA
                // thread, but their continuations could reach the queue out of order
                Speak(result?.Text ?? "No text");
                return result?.AtBoundary == true;
            }, window != IntPtr.Zero ? UIAThread.DocumentTimeout : null).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not {What}", what);
            Speak("Not available");
            return;
        }

        if (atBoundary)
            _audioCuePlayer.Play("boundary");
    }

    /// <summary>
    /// Browsing: reviews the buffer, from the browse cursor when it moved (following the caret) or
    /// the page changed (UIA thread).
    /// </summary>
    private void EnsureTethered(ReviewTether tether, bool followCaret)
    {
        var previous = _attachedTether;
        bool samePage = previous is not null && ReferenceEquals(previous.Buffer, tether.Buffer);
        if (!samePage || (followCaret && previous!.Offset != tether.Offset))
        {
            _cursor.Attach(tether.Document);
            _attachedTether = tether;
        }
        // Leaving browse mode later attaches the mode's own text again
        _attachedMode = null;
    }

    /// <summary>Attaches the cursor to the text the mode reviews, when that changed (UIA thread).</summary>
    private void EnsureDocument(ReviewMode mode, int focusVersion, int caretVersion, IntPtr window)
    {
        _attachedTether = null;
        if (_generation != _uiaThread.Generation)
        {
            _generation = _uiaThread.Generation;
            _attachedMode = null;
        }

        switch (mode)
        {
            case ReviewMode.Object:
                var current = _navigation.CurrentObject();
                if (_attachedMode != mode || !ReferenceEquals(current, _attachedObject))
                {
                    _cursor.Attach(current?.GetText());
                    _attachedObject = current;
                }
                else if (caretVersion != _attachedCaretVersion)
                {
                    // Same object, caret moved: back to the caret
                    _cursor.Attach(_cursor.Document);
                }
                break;

            case ReviewMode.Document:
                if (_attachedMode != mode || focusVersion != _attachedFocusVersion || caretVersion != _attachedCaretVersion)
                {
                    _cursor.Attach(_source.GetFocusedText());
                    _attachedFocusVersion = focusVersion;
                }
                break;

            case ReviewMode.Screen:
                if (_attachedMode != mode || window != _attachedWindow)
                {
                    _cursor.Attach(_source.GetWindowText(window));
                    _attachedWindow = window;
                }
                break;
        }
        _attachedMode = mode;
        _attachedCaretVersion = caretVersion;
    }

    private void Speak(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
