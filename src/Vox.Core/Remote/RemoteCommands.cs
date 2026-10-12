using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Speech;

namespace Vox.Core.Remote;

/// <summary>What the remote access dialog asks for.</summary>
public sealed record RemoteRequest(RemoteRole Role, RemoteEndpoint Endpoint, string Code, string? Address);

/// <summary>What the dialog starts with: the last choices and a new code for this computer.</summary>
public sealed record RemoteDialogDefaults(bool UseRelay, string? Relay, string? Address, int Port, string NewCode);

/// <summary>Shows the remote access dialog; the session asked for, or null when cancelled.</summary>
public interface IRemotePresenter
{
    Task<RemoteRequest?> ShowAsync(RemoteDialogDefaults defaults);
}

/// <summary>
/// The Vox menu's "Remote access" (start waiting to be controlled, connect to another computer,
/// or end the session), and the keys that switch between computers and disconnect. Pipeline
/// thread for <see cref="TryHandle"/>.
/// </summary>
public sealed class RemoteCommands(
    RemoteAccessSession session,
    IRemotePresenter presenter,
    IConfirmPresenter confirm,
    OwnDialogRunner dialogs,
    IClipboard clipboard,
    SpeechQueue speech,
    IOptionsMonitor<VoxSettings> settings,
    Action<VoxSettings> updateSettings,
    RunPolicy policy,
    ILogger<RemoteCommands> logger)
{
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.RemoteAccess:
                _ = OpenAsync();
                return true;
            case NavigationCommand.ToggleRemoteControl:
                Say(session.ToggleControl() ?? VoxStrings.Get("Remote.NoSession"));
                return true;
            case NavigationCommand.DisconnectRemote:
                if (session.State == RemoteState.Idle)
                    Say(VoxStrings.Get("Remote.NoSession"));
                else
                    session.Disconnect();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Why remote access can't be used here, or null.</summary>
    public string? Unavailable =>
        policy.IsSecure ? VoxStrings.Get("Remote.NotOnThisScreen")
        : !policy.AllowNetwork ? VoxStrings.Get("Remote.NetworkOff")
        : null;

    private async Task OpenAsync()
    {
        if (Unavailable is { } why)
        {
            Say(why);
            return;
        }
        try
        {
            if (session.State != RemoteState.Idle)
            {
                var question = session.State == RemoteState.Connected
                    ? VoxStrings.Format("Remote.DisconnectQuestion", session.PeerName ?? VoxStrings.Get("Remote.OtherComputer"))
                    : VoxStrings.Get("Remote.StopWaitingQuestion");
                var end = await dialogs.RunAsync(() => confirm.AskAsync(VoxStrings.Get("Remote.Title"), question), false).ConfigureAwait(false);
                if (end)
                    session.Disconnect();
                return;
            }
            var current = settings.CurrentValue;
            var request = await dialogs.RunAsync(() => presenter.ShowAsync(new RemoteDialogDefaults(
                current.RemoteUseRelay, current.RemoteRelay, current.RemoteAddress, current.RemotePort, RemoteCode.New())), null).ConfigureAwait(false);
            if (request is null)
                return;
            Start(request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Remote access dialog failed");
        }
    }

    /// <summary>Starts the session asked for and remembers the choices.</summary>
    public void Start(RemoteRequest request)
    {
        if (session.State != RemoteState.Idle)
            return;
        Remember(request);
        if (request.Role == RemoteRole.Controlled)
        {
            session.StartControlled(request.Endpoint, request.Code);
            var copied = clipboard.SetText(RemoteCode.Format(request.Code));
            Say(VoxStrings.Format(copied ? "Remote.WaitingCopied" : "Remote.Waiting", RemoteCode.Spoken(request.Code)));
        }
        else
        {
            session.Connect(request.Endpoint, request.Code);
            Say(VoxStrings.Get("Remote.Connecting"));
        }
    }

    private void Remember(RemoteRequest request)
    {
        var current = settings.CurrentValue;
        var updated = current with
        {
            RemoteUseRelay = request.Endpoint.Relay,
            RemoteRelay = request.Endpoint.Relay ? request.Address : current.RemoteRelay,
            RemoteAddress = !request.Endpoint.Relay && request.Role == RemoteRole.Controlling ? request.Address : current.RemoteAddress,
            RemotePort = !request.Endpoint.Relay && request.Role == RemoteRole.Controlled ? request.Endpoint.Port : current.RemotePort,
        };
        if (updated != current && policy.AllowSettingsWrites)
        {
            try { updateSettings(updated); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not remember the remote access choices"); }
        }
    }

    private void Say(string text) => speech.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
