using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Audio;
using Vox.Core.Configuration;
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

    /// <summary>Moves the navigator object and speaks it (or plays the boundary cue).</summary>
    public async Task MoveAsync(NavigatorMove move)
    {
        FocusChangedEvent? landed;
        try
        {
            landed = await _uiaThread.RunAsync(() =>
            {
                EnsureNavigator();
                return _navigator.Move(move)?.Describe();
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not move the navigator object");
            Speak("Not available");
            return;
        }

        if (landed is null)
            _audioCuePlayer.Play("boundary");
        else
            Speak(Describe(landed));
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
