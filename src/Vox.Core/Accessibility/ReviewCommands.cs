using Microsoft.Extensions.Logging;
using Vox.Core.Audio;
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
/// await it.
/// </summary>
public sealed class ReviewCommands
{
    private readonly UIAThread _uiaThread;
    private readonly ObjectNavigationCommands _navigation;
    private readonly IReviewTextSource _source;
    private readonly SpeechQueue _speechQueue;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly ILogger<ReviewCommands> _logger;
    private readonly ReviewCursor _cursor = new();

    private volatile ReviewMode _mode = ReviewMode.Object;
    private int _focusVersion;

    // What the cursor's document was taken from (UIA thread only)
    private ReviewMode? _attachedMode;
    private INavigatorObject? _attachedObject;
    private int _attachedFocusVersion = -1;
    private IntPtr _attachedWindow;
    private int _generation = -1;

    public ReviewCommands(UIAThread uiaThread, ObjectNavigationCommands navigation, IReviewTextSource source,
        SpeechQueue speechQueue, IAudioCuePlayer audioCuePlayer, ILogger<ReviewCommands> logger)
    {
        _uiaThread = uiaThread;
        _navigation = navigation;
        _source = source;
        _speechQueue = speechQueue;
        _audioCuePlayer = audioCuePlayer;
        _logger = logger;
    }

    public ReviewMode Mode => _mode;

    /// <summary>Focus moved: document review picks up the newly focused control's text.</summary>
    public void HandleFocusChanged() => Interlocked.Increment(ref _focusVersion);

    /// <summary>Runs <paramref name="command"/> if it is a review command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.ReviewPrevLine: _ = MoveAsync(TextUnit.Line, -1); return true;
            case NavigationCommand.ReviewCurrentLine: _ = ReadAsync(TextUnit.Line); return true;
            case NavigationCommand.ReviewNextLine: _ = MoveAsync(TextUnit.Line, 1); return true;
            case NavigationCommand.ReviewPrevWord: _ = MoveAsync(TextUnit.Word, -1); return true;
            case NavigationCommand.ReviewCurrentWord: _ = ReadAsync(TextUnit.Word); return true;
            case NavigationCommand.ReviewNextWord: _ = MoveAsync(TextUnit.Word, 1); return true;
            case NavigationCommand.ReviewPrevChar: _ = MoveAsync(TextUnit.Character, -1); return true;
            case NavigationCommand.ReviewCurrentChar: _ = ReadAsync(TextUnit.Character); return true;
            case NavigationCommand.ReviewNextChar: _ = MoveAsync(TextUnit.Character, 1); return true;
            case NavigationCommand.ReviewTop: _ = RunAsync(c => c.Top(), "move to the top"); return true;
            case NavigationCommand.ReviewBottom: _ = RunAsync(c => c.Bottom(), "move to the bottom"); return true;
            case NavigationCommand.NextReviewMode: SwitchMode(+1); return true;
            case NavigationCommand.PrevReviewMode: SwitchMode(-1); return true;
            default: return false;
        }
    }

    public Task MoveAsync(TextUnit unit, int direction) => RunAsync(c => c.Move(unit, direction), "move the review cursor");

    public Task ReadAsync(TextUnit unit) => RunAsync(c => c.Read(unit), "read at the review cursor");

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
        var window = mode == ReviewMode.Screen ? _source.ForegroundWindow : IntPtr.Zero;
        ReviewResult? result;
        try
        {
            result = await _uiaThread.RunAsync(() =>
            {
                EnsureDocument(mode, focusVersion, window);
                return command(_cursor);
            }, mode == ReviewMode.Screen ? UIAThread.DocumentTimeout : null).ConfigureAwait(false);
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

        if (result is not { } spoken)
        {
            Speak("No text");
            return;
        }
        if (spoken.AtBoundary)
            _audioCuePlayer.Play("boundary");
        Speak(spoken.Text);
    }

    /// <summary>Attaches the cursor to the text the mode reviews, when that changed (UIA thread).</summary>
    private void EnsureDocument(ReviewMode mode, int focusVersion, IntPtr window)
    {
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
                break;

            case ReviewMode.Document:
                if (_attachedMode != mode || focusVersion != _attachedFocusVersion)
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
    }

    private void Speak(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
