using Vox.Core.Configuration;
using Vox.Core.Localization;

namespace Vox.Core.Lifecycle;

/// <summary>
/// "Use current settings on sign-in screens": copies the user's settings to the shared file the
/// secure-mode instance reads (<see cref="RunPolicy.SecureSettingsPath"/>), so the sign-in, lock
/// and UAC screens speak with the same voice, rate and keys.
/// </summary>
public static class SecureScreenSettings
{
    /// <summary>
    /// Copies <paramref name="settings"/>; returns what to tell the user. Without a
    /// <paramref name="targetPath"/> they go to Vox.Service (<see cref="SecureSettingsPipe"/>), the
    /// only writer of the shared folder, or through <paramref name="send"/> when given.
    /// </summary>
    public static string Copy(SettingsManager manager, VoxSettings settings, RunPolicy policy, string? targetPath = null,
        Func<VoxSettings, bool>? send = null)
    {
        if (policy.IsSecure)
            return VoxStrings.Get("Ocr.NotOnThisScreen");
        // The secure screens never run the setup wizard, whatever the user's state
        var shared = settings with { FirstRunCompleted = true };
        var copied = targetPath is not null
            ? manager.SaveTo(targetPath, shared)
            : (send ?? (s => SecureSettingsPipe.Send(s)))(shared);
        return copied
            ? VoxStrings.Get("SecureScreen.SettingsCopied")
            : VoxStrings.Get("SecureScreen.SettingsNotCopied");
    }
}
