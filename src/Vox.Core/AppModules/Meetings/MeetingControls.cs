using Vox.Core.Localization;

namespace Vox.Core.AppModules.Meetings;

/// <summary>
/// A call's state as its window's buttons show it: a button offering to unmute means the
/// microphone is muted, one offering to turn the camera off means it is on, and so on. Teams and
/// Zoom name their buttons by what pressing them does, so the state is read from the names.
/// </summary>
public sealed record MeetingState(bool InCall, bool? Muted, bool? CameraOn, bool? HandRaised, bool? Sharing)
{
    public static readonly MeetingState None = new(false, null, null, null, null);
}

/// <summary>Reads a <see cref="MeetingState"/> from button names and says it, or says what changed.</summary>
public static class MeetingControls
{
    /// <summary>The state the buttons named <paramref name="buttons"/> show.</summary>
    public static MeetingState FromButtons(IEnumerable<string> buttons)
    {
        bool inCall = false;
        bool? muted = null, cameraOn = null, handRaised = null, sharing = null;
        foreach (var raw in buttons)
        {
            var name = raw.Trim();
            if (StartsWith(name, "Leave") || StartsWith(name, "Hang up") || StartsWith(name, "End call") || StartsWith(name, "End meeting"))
                inCall = true;
            // "Unmute (Ctrl+Shift+M)", Zoom's "Unmute audio" / "Mute audio"
            if (StartsWith(name, "Unmute"))
                muted = true;
            else if (StartsWith(name, "Mute") && !StartsWith(name, "Mute all"))
                muted ??= false;
            // Teams: "Turn camera on" / "Turn camera off"; Zoom: "Start video" / "Stop video"
            if (StartsWith(name, "Turn camera on") || StartsWith(name, "Start video"))
                cameraOn = false;
            else if (StartsWith(name, "Turn camera off") || StartsWith(name, "Stop video"))
                cameraOn = true;
            if (StartsWith(name, "Lower") && name.Contains("hand", StringComparison.OrdinalIgnoreCase))
                handRaised = true;
            else if (StartsWith(name, "Raise") && name.Contains("hand", StringComparison.OrdinalIgnoreCase))
                handRaised ??= false;
            if (StartsWith(name, "Stop sharing") || StartsWith(name, "Stop presenting"))
                sharing = true;
        }
        // Without a call, mute and camera buttons belong to a pre-join screen; still worth saying
        return new MeetingState(inCall, muted, cameraOn, handRaised, sharing);
    }

    /// <summary>"In a call, microphone muted, camera off, hand raised".</summary>
    public static string Describe(MeetingState state)
    {
        var parts = new List<string> { state.InCall ? VoxStrings.Get("Meeting.InCall") : VoxStrings.Get("Meeting.NotInCall") };
        if (state.Muted is { } muted)
            parts.Add(muted ? VoxStrings.Get("Meeting.MicrophoneMutedState") : VoxStrings.Get("Meeting.MicrophoneOnState"));
        if (state.CameraOn is { } camera)
            parts.Add(camera ? VoxStrings.Get("Meeting.CameraOnState") : VoxStrings.Get("Meeting.CameraOffState"));
        if (state.HandRaised == true)
            parts.Add(VoxStrings.Get("Meeting.HandRaisedState"));
        if (state.Sharing == true)
            parts.Add(VoxStrings.Get("Meeting.SharingState"));
        return string.Join(", ", parts);
    }

    /// <summary>What changed from <paramref name="before"/> to <paramref name="after"/>: "Microphone muted"; null when nothing did.</summary>
    public static string? Changes(MeetingState before, MeetingState after)
    {
        var parts = new List<string>();
        if (before.InCall != after.InCall)
            parts.Add(after.InCall ? VoxStrings.Get("Meeting.CallStarted") : VoxStrings.Get("Meeting.CallEnded"));
        if (after.Muted is { } muted && before.Muted != muted)
            parts.Add(muted ? VoxStrings.Get("Meeting.MicrophoneMuted") : VoxStrings.Get("Meeting.MicrophoneOn"));
        if (after.CameraOn is { } camera && before.CameraOn != camera)
            parts.Add(camera ? VoxStrings.Get("Meeting.CameraOn") : VoxStrings.Get("Meeting.CameraOff"));
        if (after.HandRaised is { } hand && (before.HandRaised ?? false) != hand)
            parts.Add(hand ? VoxStrings.Get("Meeting.HandRaised") : VoxStrings.Get("Meeting.HandLowered"));
        if ((before.Sharing ?? false) != (after.Sharing ?? false))
            parts.Add(after.Sharing == true ? VoxStrings.Get("Meeting.Sharing") : VoxStrings.Get("Meeting.StoppedSharing"));
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// What pressing the button named <paramref name="name"/> does to the microphone: true mutes it,
    /// false unmutes it, null when it isn't a microphone button. Covers Teams' and Zoom's "Mute" /
    /// "Unmute" and Meet's "Turn off microphone" / "Turn on microphone".
    /// </summary>
    public static bool? PressingMutes(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        name = name.Trim();
        if (StartsWith(name, "Unmute") || StartsWith(name, "Turn on microphone"))
            return false;
        if ((StartsWith(name, "Mute") && !StartsWith(name, "Mute all")) || StartsWith(name, "Turn off microphone"))
            return true;
        return null;
    }

    private static bool StartsWith(string name, string prefix) => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
