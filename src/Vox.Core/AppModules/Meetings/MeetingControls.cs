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
        var parts = new List<string> { state.InCall ? "In a call" : "Not in a call" };
        if (state.Muted is { } muted)
            parts.Add(muted ? "microphone muted" : "microphone on");
        if (state.CameraOn is { } camera)
            parts.Add(camera ? "camera on" : "camera off");
        if (state.HandRaised == true)
            parts.Add("hand raised");
        if (state.Sharing == true)
            parts.Add("sharing your screen");
        return string.Join(", ", parts);
    }

    /// <summary>What changed from <paramref name="before"/> to <paramref name="after"/>: "Microphone muted"; null when nothing did.</summary>
    public static string? Changes(MeetingState before, MeetingState after)
    {
        var parts = new List<string>();
        if (before.InCall != after.InCall)
            parts.Add(after.InCall ? "Call started" : "Call ended");
        if (after.Muted is { } muted && before.Muted != muted)
            parts.Add(muted ? "Microphone muted" : "Microphone on");
        if (after.CameraOn is { } camera && before.CameraOn != camera)
            parts.Add(camera ? "Camera on" : "Camera off");
        if (after.HandRaised is { } hand && (before.HandRaised ?? false) != hand)
            parts.Add(hand ? "Hand raised" : "Hand lowered");
        if ((before.Sharing ?? false) != (after.Sharing ?? false))
            parts.Add(after.Sharing == true ? "Sharing your screen" : "Stopped sharing");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static bool StartsWith(string name, string prefix) => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
