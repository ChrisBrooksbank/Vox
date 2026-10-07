namespace Vox.Core.Lifecycle;

/// <summary>
/// What this Vox instance may do. Normally everything; in secure mode (<c>--secure</c>, started
/// by Vox.Service on the sign-in, lock and UAC screens, where anyone at the keyboard can reach
/// it) nothing that could expose or change a user's data or the machine: no settings writes,
/// no add-ons, no network or AI, no user-profile access, no setup wizard. Components check the
/// relevant switch before doing such things.
/// </summary>
public sealed record RunPolicy(bool IsSecure)
{
    public const string SecureFlag = "--secure";

    public static readonly RunPolicy Normal = new(false);
    public static readonly RunPolicy Secure = new(true);

    public bool AllowSettingsWrites => !IsSecure;
    public bool AllowAddOns => !IsSecure;
    public bool AllowNetwork => !IsSecure;
    public bool AllowAi => !IsSecure;
    public bool AllowUserProfileAccess => !IsSecure;
    public bool AllowSetupWizard => !IsSecure;

    /// <summary>Settings shared with the secure screens (written by "use current settings on sign-in screens").</summary>
    public static string SecureSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vox", "secure-settings.json");

    /// <summary>The settings file this instance reads.</summary>
    public string SettingsPath => IsSecure ? SecureSettingsPath : Configuration.SettingsManager.DefaultUserSettingsPath;

    /// <summary>Where this instance writes its logs (secure mode doesn't use a user profile).</summary>
    public string LogDirectory => Path.Combine(
        Environment.GetFolderPath(IsSecure ? Environment.SpecialFolder.CommonApplicationData : Environment.SpecialFolder.ApplicationData),
        "Vox", IsSecure ? "secure-logs" : "logs");

    /// <summary>The policy for these command-line arguments.</summary>
    public static RunPolicy FromArgs(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, SecureFlag, StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/secure", StringComparison.OrdinalIgnoreCase))
            ? Secure
            : Normal;
}
