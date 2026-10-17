using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Magnifier;

namespace Vox.Core.ScreenCurtain;

/// <summary>
/// The screen curtain's toggle and its start-up option (<see cref="VoxSettings.ScreenCurtainAtStart"/>,
/// said with a warning that the display is black). The curtain wins over the magnifier, which is
/// told through <see cref="MagnifierController.ScreenCurtainActive"/>. Never on the secure screens.
/// The curtain is taken off when Vox exits.
/// </summary>
public sealed class ScreenCurtainCommands : IDisposable
{
    private readonly IScreenCurtain _curtain;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action<string> _say;
    private readonly MagnifierController? _magnifier;
    private readonly RunPolicy _policy;
    private readonly IAudioCuePlayer? _cues;
    private readonly ILogger? _logger;

    public ScreenCurtainCommands(IScreenCurtain curtain, IOptionsMonitor<VoxSettings> settings, Action<string> say,
        MagnifierController? magnifier = null, RunPolicy? policy = null, IAudioCuePlayer? cues = null, ILogger? logger = null)
    {
        _curtain = curtain;
        _settings = settings;
        _say = say;
        _magnifier = magnifier;
        _policy = policy ?? RunPolicy.Normal;
        _cues = cues;
        _logger = logger;
    }

    public bool IsOn => _curtain.IsActive;

    /// <summary>Runs <paramref name="command"/> if it is the curtain's; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.ToggleScreenCurtain)
            return false;
        Toggle();
        return true;
    }

    /// <summary>Turns the curtain on at start-up when the setting says, with a spoken warning.</summary>
    public void StartIfWanted()
    {
        if (_policy.AllowScreenCurtain && _settings.CurrentValue.ScreenCurtainAtStart && !IsOn)
            TurnOn(warn: true);
    }

    public void Toggle()
    {
        if (!_policy.AllowScreenCurtain)
        {
            _say(VoxStrings.Get("ScreenCurtain.NotAvailable"));
            return;
        }
        if (IsOn)
            TurnOff();
        else
            TurnOn(warn: false);
    }

    private void TurnOn(bool warn)
    {
        bool applied;
        try { applied = _curtain.Apply(); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "The screen curtain could not be turned on");
            applied = false;
        }
        if (!applied)
        {
            _say(VoxStrings.Get("ScreenCurtain.Failed"));
            return;
        }
        if (_magnifier is not null)
            _magnifier.ScreenCurtainActive = true;
        _cues?.Play("checked");
        _say(VoxStrings.Get(warn ? "ScreenCurtain.OnAtStart" : "ScreenCurtain.On"));
    }

    private void TurnOff()
    {
        _curtain.Remove();
        if (_magnifier is not null)
            _magnifier.ScreenCurtainActive = false;
        _cues?.Play("not_checked");
        _say(VoxStrings.Get("ScreenCurtain.Off"));
    }

    public void Dispose()
    {
        if (!_curtain.IsActive)
            return;
        _curtain.Remove();
        if (_magnifier is not null)
            _magnifier.ScreenCurtainActive = false;
    }
}
