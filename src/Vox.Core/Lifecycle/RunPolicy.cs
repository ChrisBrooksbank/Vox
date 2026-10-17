namespace Vox.Core.Lifecycle;

/// <summary>
/// What this Vox instance may do. Normally everything; in secure mode (<c>--secure</c>, started
/// by Vox.Service on the sign-in, lock and UAC screens, where anyone at the keyboard can reach
/// it) nothing that could expose or change a user's data or the machine: no settings writes,
/// no add-ons, no network or AI, no magnifier, no user-profile access, no setup wizard. Components check the
/// relevant switch before doing such things. An organization's policy can also switch the network,
/// add-ons, AI and the magnifier off (<see cref="Enterprise.PolicySettings.Restrict"/>).
/// </summary>
public sealed record RunPolicy(bool IsSecure)
{
    public const string SecureFlag = "--secure";

    /// <summary>Starts Vox without loading add-ons (safe mode), e.g. after one stopped Vox working.</summary>
    public const string SafeFlag = "--safe";

    /// <summary>Starts Vox in trial mode: a running NVDA or Narrator is stopped, and comes back with one key (<see cref="Trial.TrialMode"/>).</summary>
    public const string TrialFlag = "--trial";

    public static readonly RunPolicy Normal = new(false);
    public static readonly RunPolicy Secure = new(true);

    /// <summary>The organization's policy turned the network off (DisableNetwork).</summary>
    public bool NetworkOffByPolicy { get; init; }
    /// <summary>The organization's policy turned add-ons off (DisableAddOns).</summary>
    public bool AddOnsOffByPolicy { get; init; }
    /// <summary>The organization's policy turned AI off (DisableAI).</summary>
    public bool AiOffByPolicy { get; init; }
    /// <summary>The organization's policy turned the magnifier off (DisableMagnifier).</summary>
    public bool MagnifierOffByPolicy { get; init; }
    /// <summary>
    /// Safe mode (<see cref="SafeFlag"/>, or the Vox menu's "Restart with add-ons disabled"): no
    /// add-on is loaded, though they can still be managed (to remove the one causing trouble).
    /// </summary>
    public bool SafeMode { get; init; }
    /// <summary>Started with <see cref="TrialFlag"/>: begin trial mode at startup (never on secure screens).</summary>
    public bool StartTrial { get; init; }

    /// <summary>
    /// Vox has UI access: it loads only add-ons signed by a trusted publisher, the signature checked
    /// when each is loaded, so user-writable files can't borrow its reach into elevated windows.
    /// </summary>
    public bool RequireSignedAddOns { get; init; }

    public bool AllowSettingsWrites => !IsSecure;
    public bool AllowAddOns => !IsSecure && !AddOnsOffByPolicy;
    /// <summary>
    /// Whether anything may connect beyond this computer. Every network user goes through
    /// <see cref="Enterprise.NetworkGate"/>, which checks it.
    /// </summary>
    public bool AllowNetwork => !IsSecure && !NetworkOffByPolicy;
    public bool AllowAi => !IsSecure && !AiOffByPolicy;
    /// <summary>Whether the magnifier may be used (not on the secure screens for now).</summary>
    public bool AllowMagnifier => !IsSecure && !MagnifierOffByPolicy;
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
    public static RunPolicy FromArgs(IEnumerable<string> args)
    {
        var list = args.ToList();
        bool Has(string flag) => list.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "/" + flag[2..], StringComparison.OrdinalIgnoreCase));
        if (Has(SecureFlag))
            return Secure;
        bool safe = Has(SafeFlag), trial = Has(TrialFlag);
        return safe || trial ? Normal with { SafeMode = safe, StartTrial = trial } : Normal;
    }
}
