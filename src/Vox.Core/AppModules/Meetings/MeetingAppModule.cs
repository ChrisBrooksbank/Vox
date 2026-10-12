using Vox.Core.Accessibility;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.AppModules.Meetings;

/// <summary>
/// What Teams and Zoom share: the call's state read from the window's buttons, said on command and
/// after the application's own mute, camera and hand keys (bound with "passThrough", so they still
/// reach it); and the application's announcements that repeat what Vox has just said are dropped.
/// </summary>
public abstract class MeetingAppModule : AppModule
{
    // Long enough for the application to have renamed its buttons after the key
    private static readonly TimeSpan StateCheckDelay = TimeSpan.FromMilliseconds(500);

    private readonly IWindowButtonSource _buttons;
    private readonly IEventSink _events;
    private readonly TimeSpan _delay;
    private readonly RecentSpeech _recentSpeech;

    // Pipeline thread only
    private MeetingState? _state;
    private long _sequence;

    protected MeetingAppModule(IWindowButtonSource buttons, SpeechQueue speech, IEventSink events, RecentSpeech recentSpeech, TimeSpan? stateCheckDelay = null)
    {
        _buttons = buttons;
        Speech = speech;
        _events = events;
        _recentSpeech = recentSpeech;
        _delay = stateCheckDelay ?? StateCheckDelay;
    }

    protected SpeechQueue Speech { get; }

    /// <summary>The command that says the call's state.</summary>
    protected abstract NavigationCommand StateCommand { get; }

    /// <summary>The command bound (passing through) to the application's own call keys.</summary>
    protected abstract NavigationCommand CallKeyCommand { get; }

    public override void OnActivated(AppActivation activation)
    {
        base.OnActivated(activation);
        _state = null;
    }

    public override bool TryHandle(NavigationCommand command)
    {
        if (command == StateCommand)
        {
            ReadState(TimeSpan.Zero, (_, state) => MeetingControls.Describe(state));
            return true;
        }
        if (command == CallKeyCommand)
        {
            // The key has gone on to the application: say what it changed
            ReadState(_delay, (before, after) => before is null ? MeetingControls.Describe(after) : MeetingControls.Changes(before, after));
            return true;
        }
        return false;
    }

    /// <summary>Drops the application's announcements of what Vox has just said.</summary>
    public override bool ShouldProcess(ScreenReaderEvent evt) => evt switch
    {
        LiveRegionChangedEvent live => !_recentSpeech.IsRepeat(live.Text),
        NotificationEvent notification => !_recentSpeech.IsRepeat(notification.NotificationText),
        _ => true,
    };

    private void ReadState(TimeSpan delay, Func<MeetingState?, MeetingState, string?> describe)
    {
        long sequence = ++_sequence;
        _ = Task.Run(async () =>
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay).ConfigureAwait(false);
            var names = await _buttons.ButtonNamesAsync().ConfigureAwait(false);
            _events.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () =>
            {
                if (sequence != _sequence || !IsActive)
                    return;
                if (names is null)
                {
                    Speech.Enqueue(new Utterance(VoxStrings.Get("Meeting.ControlsUnreadable"), SpeechPriority.Interrupt));
                    return;
                }
                var state = MeetingControls.FromButtons(names);
                var text = describe(_state, state);
                _state = state;
                if (!string.IsNullOrWhiteSpace(text))
                    Speech.Enqueue(new Utterance(text, SpeechPriority.High));
            }));
        });
    }
}
