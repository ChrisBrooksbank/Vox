using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Accessibility;

/// <summary>
/// Moves the navigator object through the UIA control view and speaks where it lands, with the
/// boundary cue when there is nothing in that direction. The <see cref="ObjectNavigator"/> and
/// the objects it holds are only touched on the UIA thread; command handlers never await it.
/// </summary>
public sealed class ObjectNavigationCommands
{
    private readonly UIAThread _uiaThread;
    private readonly INavigatorObjectSource _source;
    private readonly SpeechQueue _speechQueue;
    private readonly AnnouncementBuilder _announcementBuilder;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger<ObjectNavigationCommands> _logger;
    private readonly ObjectNavigator _navigator = new();
    // UIA thread generation the navigator object belongs to; a replaced thread's objects are dropped
    private int _generation = -1;

    public ObjectNavigationCommands(UIAThread uiaThread, INavigatorObjectSource source, SpeechQueue speechQueue,
        AnnouncementBuilder announcementBuilder, IAudioCuePlayer audioCuePlayer, IOptionsMonitor<VoxSettings> settings,
        ILogger<ObjectNavigationCommands> logger)
    {
        _uiaThread = uiaThread;
        _source = source;
        _speechQueue = speechQueue;
        _announcementBuilder = announcementBuilder;
        _audioCuePlayer = audioCuePlayer;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Runs <paramref name="command"/> if it is an object navigation command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.NavigatorParent: _ = MoveAsync(NavigatorMove.Parent); return true;
            case NavigationCommand.NavigatorFirstChild: _ = MoveAsync(NavigatorMove.FirstChild); return true;
            case NavigationCommand.NavigatorPrevious: _ = MoveAsync(NavigatorMove.Previous); return true;
            case NavigationCommand.NavigatorNext: _ = MoveAsync(NavigatorMove.Next); return true;
            case NavigationCommand.ReportNavigator: _ = ReportAsync(); return true;
            case NavigationCommand.NavigatorToFocus: _ = NavigatorToFocusAsync(); return true;
            case NavigationCommand.FocusToNavigator: _ = FocusToNavigatorAsync(); return true;
            case NavigationCommand.ActivateNavigator: _ = ActivateAsync(); return true;
            default: return false;
        }
    }

    /// <summary>Moves the navigator object and speaks it (or plays the boundary cue).</summary>
    public Task MoveAsync(NavigatorMove move)
    {
        bool simpleReview = _settings.CurrentValue.SimpleReviewMode;
        return RunAsync(() =>
        {
            EnsureNavigator();
            _navigator.SimpleReview = simpleReview;
            return _navigator.Move(move) is { } landed ? Say(landed.Describe()) : Outcome.Boundary;
        }, "move the navigator object");
    }

    /// <summary>Says the navigator object again.</summary>
    public Task ReportAsync() => RunAsync(() =>
    {
        EnsureNavigator();
        return _navigator.Current is { } current ? Say(current.Describe()) : Outcome.Speak("No navigator object");
    }, "report the navigator object");

    /// <summary>Moves the navigator object to the focused object and says it.</summary>
    public Task NavigatorToFocusAsync() => RunAsync(() =>
    {
        _generation = _uiaThread.Generation;
        _navigator.MoveTo(_source.GetFocused());
        return _navigator.Current is { } current ? Say(current.Describe()) : Outcome.Speak("No focus");
    }, "move the navigator object to focus");

    /// <summary>Gives the navigator object keyboard focus (the focus change is then announced).</summary>
    public Task FocusToNavigatorAsync() => RunAsync(() =>
    {
        EnsureNavigator();
        if (_navigator.Current is not { } current)
            return Outcome.Speak("No navigator object");
        return current.SetFocus() ? Outcome.Silent : Outcome.Speak("Not focusable");
    }, "focus the navigator object");

    /// <summary>Runs the navigator object's default action.</summary>
    public Task ActivateAsync() => RunAsync(() =>
    {
        EnsureNavigator();
        if (_navigator.Current is not { } current)
            return Outcome.Speak("No navigator object");
        return current.Activate() ? Outcome.Silent : Outcome.Speak("No action");
    }, "activate the navigator object");

    /// <summary>What a command ends with, decided on the UIA thread and carried out off it.</summary>
    private readonly record struct Outcome(string? Text, bool IsBoundary)
    {
        public static Outcome Silent => default;
        public static Outcome Boundary => new(null, true);
        public static Outcome Speak(string text) => new(text, false);
    }

    private Outcome Say(FocusChangedEvent target) => Outcome.Speak(Describe(target));

    private async Task RunAsync(Func<Outcome> onUiaThread, string what)
    {
        Outcome outcome;
        try
        {
            outcome = await _uiaThread.RunAsync(onUiaThread).ConfigureAwait(false);
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

        if (outcome.IsBoundary)
            _audioCuePlayer.Play("boundary");
        else if (outcome.Text is { } text)
            Speak(text);
    }

    /// <summary>Seeds the navigator from focus when it has no object yet (UIA thread).</summary>
    private void EnsureNavigator()
    {
        if (_generation != _uiaThread.Generation)
        {
            _navigator.MoveTo(null);
            _generation = _uiaThread.Generation;
        }
        if (_navigator.Current is null)
            _navigator.MoveTo(_source.GetFocused());
    }

    /// <summary>What to say for an object: the focus announcement, or at least its type.</summary>
    private string Describe(FocusChangedEvent target)
    {
        var settings = _settings.CurrentValue;
        var text = _announcementBuilder.Build(target, VerbosityProfile.For(settings.VerbosityLevel), settings.AnnounceVisitedLinks);
        if (!string.IsNullOrWhiteSpace(text))
            return text;
        return string.IsNullOrWhiteSpace(target.ControlType) || target.ControlType == "Unknown"
            ? "Unknown"
            : target.ControlType.ToLowerInvariant();
    }

    private void Speak(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
