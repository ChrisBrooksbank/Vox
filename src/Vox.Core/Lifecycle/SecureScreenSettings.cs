using Vox.Core.Configuration;

namespace Vox.Core.Lifecycle;

/// <summary>
/// "Use current settings on sign-in screens": copies the user's settings to the shared file the
/// secure-mode instance reads (<see cref="RunPolicy.SecureSettingsPath"/>), so the sign-in, lock
/// and UAC screens speak with the same voice, rate and keys.
/// </summary>
public static class SecureScreenSettings
{
    /// <summary>Copies <paramref name="settings"/>; returns what to tell the user.</summary>
    public static string Copy(SettingsManager manager, VoxSettings settings, RunPolicy policy, string? targetPath = null)
    {
        if (policy.IsSecure)
            return "Not available on this screen";
        // The secure screens never run the setup wizard, whatever the user's state
        var shared = settings with { FirstRunCompleted = true };
        return manager.SaveTo(targetPath ?? RunPolicy.SecureSettingsPath, shared)
            ? "Settings copied to the sign-in screens"
            : "Could not copy settings to the sign-in screens";
    }
}
