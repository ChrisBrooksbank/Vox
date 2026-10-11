namespace Vox.Core.Lifecycle;

/// <summary>
/// What this Vox instance may do. Normally everything; in secure mode (<c>--secure</c>, started
/// by Vox.Service on the sign-in, lock and UAC screens, where anyone at the keyboard can reach
/// it) nothing that could expose or change a user's data or the machine: no settings writes,
/// no add-ons, no network or AI, no user-profile access, no setup wizard. Components check the
/// relevant switch before doing such things. An organization's policy can also switch the network,
/// add-ons and AI off (<see cref="Enterprise.PolicySettings.Restrict"/>).
/// </summary>
public sealed record RunPolicy(bool IsSecure)
{
    public const string SecureFlag = "--secure";

    public static readonly RunPolicy Normal = new(false);
    public static readonly RunPolicy Secure = new(true);

    /// <summary>The organization's policy turned the network off (DisableNetwork).</summary>
    public bool NetworkOffByPolicy { get; init; }
    /// <summary>The organization's policy turned add-ons off (DisableAddOns).</summary>
    public bool AddOnsOffByPolicy { get; init; }
    /// <summary>The organization's policy turned AI off (DisableAI).</summary>
    public bool AiOffByPolicy { get; init; }

    public bool AllowSettingsWrites => !IsSecure;
    public bool AllowAddOns => !IsSecure && !AddOnsOffByPolicy;
    /// <summary>
    /// Whether anything may connect beyond this computer. Every network user goes through
    /// <see cref="Enterprise.NetworkAccess"/>, which checks it.
    /// </summary>
    public bool AllowNetwork => !IsSecure && !NetworkOffByPolicy;
    public bool AllowAi => !IsSecure && !AiOffByPolicy;
    public bool AllowUserProfileAccess => !IsSecure;
    public bool AllowSetupWizard => !IsSecure;

    /// <summary>Settings shared with the secure screens (written by "use current settings on sign-in screens").</summary>
    public static string SecureSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vox", "secure-settings.json");

    /// <summary>The settings file this instance reads.</summary>
    public string SettingsPath => IsSecure ? SecureSettingsPath : Configuration.SettingsManager.DefaultUserSettingsPath;

    /// <summary>Where this instance writes its logs (secure mode doesn't use a user profile).</summary>
    public string LogDirectory => IsSecure
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vox", "secure-logs")
        // On this computer: logs would only slow a roaming profile down
        : Path.Combine(VoxPaths.LocalData, "logs");

    /// <summary>The policy for these command-line arguments.</summary>
    public static RunPolicy FromArgs(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, SecureFlag, StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/secure", StringComparison.OrdinalIgnoreCase))
            ? Secure
            : Normal;
}
