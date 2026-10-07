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
    private readonly IClipboard? _clipboard;
    // More than enough for any control; a huge document is cut off rather than stalling the UIA thread
    private const int MaxCopyLength = 1_000_000;
    private const int MaxSpokenCopyLength = 100;
    private readonly ObjectNavigator _navigator = new();
    // UIA thread generation the navigator object belongs to; a replaced thread's objects are dropped
    private int _generation = -1;
    // Focus moved and the navigator follows it: re-seed from focus on next use
    private volatile bool _followFocus;

    public ObjectNavigationCommands(UIAThread uiaThread, INavigatorObjectSource source, SpeechQueue speechQueue,
        AnnouncementBuilder announcementBuilder, IAudioCuePlayer audioCuePlayer, IOptionsMonitor<VoxSettings> settings,
        ILogger<ObjectNavigationCommands> logger, IClipboard? clipboard = null)
    {
        _clipboard = clipboard;
        _uiaThread = uiaThread;
        _source = source;
        _speechQueue = speechQueue;
        _announcementBuilder = announcementBuilder;
        _audioCuePlayer = audioCuePlayer;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Focus moved (or the caret did): the navigator follows it, when the setting says so.</summary>
    public void HandleFocusChanged()
    {
        if (_settings.CurrentValue.ReviewFollowsFocus)
            _followFocus = true;
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
            case NavigationCommand.CopyNavigatorText: _ = CopyTextAsync(); return true;
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
        _followFocus = false;
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

    /// <summary>Copies the navigator object's text to the clipboard.</summary>
    public async Task CopyTextAsync()
    {
        string? text;
        try
        {
            text = await _uiaThread.RunAsync(() =>
            {
                EnsureNavigator();
                return _navigator.Current?.GetText().DocumentRange.GetText(MaxCopyLength);
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the navigator object's text");
            Speak("Not available");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Speak("No text");
            return;
        }
        if (_clipboard?.SetText(text) != true)
        {
            Speak("Could not copy");
            return;
        }
        // Short text is said, so it's clear what was copied
        Speak(text.Length <= MaxSpokenCopyLength && !text.Contains('\n')
            ? $"{text.Trim()}, copied to clipboard"
            : "Copied to clipboard");
    }

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
            outcome = await _uiaThread.RunAsync(() =>
            {
                var result = onUiaThread();
                // Spoken here, in the order commands ran (their continuations could race)
                if (result.Text is { } text)
                    Speak(text);
                return result;
            }).ConfigureAwait(false);
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
    }

    /// <summary>
    /// The navigator object (seeded from focus), for reviewing its text. UIA thread only. Each
    /// move gives a new object, so callers can tell a moved navigator by reference.
    /// </summary>
    public INavigatorObject? CurrentObject()
    {
        EnsureNavigator();
        return _navigator.Current;
    }

    /// <summary>Seeds the navigator from focus when it has no object yet (UIA thread).</summary>
    private void EnsureNavigator()
    {
        if (_generation != _uiaThread.Generation)
        {
            _navigator.MoveTo(null);
            _generation = _uiaThread.Generation;
        }
        if (_followFocus)
        {
            _followFocus = false;
            _navigator.MoveTo(null);
        }
        if (_navigator.Current is null)
            _navigator.MoveTo(_source.GetFocused());
    }

    /// <summary>What to say for an object: the focus announcement, or at least its type.</summary>
    private string Describe(FocusChangedEvent target) => Describe(_announcementBuilder, _settings.CurrentValue, target);

    /// <summary>What to say for an object: the focus announcement, or at least its type.</summary>
    internal static string Describe(AnnouncementBuilder announcementBuilder, VoxSettings settings, FocusChangedEvent target)
    {
        var text = announcementBuilder.Build(target, VerbosityProfile.For(settings.VerbosityLevel), settings.AnnounceVisitedLinks);
        if (!string.IsNullOrWhiteSpace(text))
            return text;
        return string.IsNullOrWhiteSpace(target.ControlType) || target.ControlType == "Unknown"
            ? "Unknown"
            : target.ControlType.ToLowerInvariant();
    }

    private void Speak(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
