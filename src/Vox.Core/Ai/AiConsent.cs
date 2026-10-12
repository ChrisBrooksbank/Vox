using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;

namespace Vox.Core.Ai;

/// <summary>Whether something may be sent to a cloud describer (the <c>AiCloudConsent</c> setting).</summary>
public enum AiConsentMode
{
    /// <summary>Ask the first time in each session; a yes holds until Vox restarts.</summary>
    Ask,
    /// <summary>Send without asking (the rules that never send still apply).</summary>
    Allow,
    /// <summary>Never send anything: only a describer on this computer is used.</summary>
    Never,
}

/// <summary>Why nothing is sent (or nothing is described at all).</summary>
public enum AiRefusal
{
    None,
    /// <summary>The sign-in, lock or UAC screens: no AI there, ever.</summary>
    SecureScreen,
    /// <summary>The organization's policy turned AI off (DisableAI).</summary>
    TurnedOffByPolicy,
    /// <summary>The network is off (DisableNetwork policy); only this computer's describer can run.</summary>
    NetworkOff,
    /// <summary>What would be sent is, or shows, a password field.</summary>
    PasswordField,
    /// <summary>The application in front is on the private list (<c>AiPrivateApps</c>).</summary>
    PrivateApp,
    /// <summary>The <c>AiCloudConsent</c> setting is Never.</summary>
    CloudNever,
    /// <summary>The web site is on the private list (<c>AiPrivateSites</c>).</summary>
    PrivateSite,
    /// <summary>
    /// Asked in the background (automatic labels): only a describer on this computer, or the
    /// cloud when <c>AiCloudConsent</c> is Allow; nobody is asked.
    /// </summary>
    NotInBackground,
    /// <summary>The person said no when asked.</summary>
    Declined,
    /// <summary>No describer is set up.</summary>
    NotSetUp,
}

/// <summary>
/// What a request would send, for the rules that never send: is it a password field, whose window
/// is it, and which web site's page (an address, or null when it is no page).
/// </summary>
public sealed record AiTarget(bool IsPassword = false, string? ProcessName = null, string? Site = null)
{
    public static readonly AiTarget None = new();
}

/// <summary>Allowed, ask first, or refused (with the reason).</summary>
public enum AiConsentDecision { Allowed, Ask, Refused }

public readonly record struct AiConsentResult(AiConsentDecision Decision, AiRefusal Reason = AiRefusal.None)
{
    public static readonly AiConsentResult Allowed = new(AiConsentDecision.Allowed);
    public static readonly AiConsentResult Ask = new(AiConsentDecision.Ask);
    public static AiConsentResult Refused(AiRefusal reason) => new(AiConsentDecision.Refused, reason);
}

/// <summary>
/// The rules for what may be described, and where. Nothing at all on the secure screens or when
/// the organization turned AI off. A describer on this computer needs no consent. A cloud
/// describer never gets a password field, a window of an application on the private list, or
/// anything while the network is off; otherwise the <c>AiCloudConsent</c> setting decides: ask
/// (once per session), allow, or never. Thread-safe.
/// </summary>
public sealed class AiConsent(RunPolicy policy, IOptionsMonitor<VoxSettings> settings)
{
    private volatile bool _agreed;

    /// <summary>The person said yes to sending this session.</summary>
    public bool AgreedThisSession => _agreed;

    /// <summary>Remembers a yes until Vox restarts (Ask mode).</summary>
    public void AgreeForSession() => _agreed = true;

    /// <summary>Forgets a yes (the setting changed, tests).</summary>
    public void Reset() => _agreed = false;

    /// <summary>Whether anything may be described at all (on this computer included).</summary>
    public AiConsentResult ForLocal()
    {
        if (policy.IsSecure)
            return AiConsentResult.Refused(AiRefusal.SecureScreen);
        if (!policy.AllowAi)
            return AiConsentResult.Refused(AiRefusal.TurnedOffByPolicy);
        return AiConsentResult.Allowed;
    }

    /// <summary>Whether <paramref name="target"/> may be sent to a cloud describer.</summary>
    public AiConsentResult ForCloud(AiTarget target)
    {
        var local = ForLocal();
        if (local.Decision == AiConsentDecision.Refused)
            return local;
        if (!policy.AllowNetwork)
            return AiConsentResult.Refused(AiRefusal.NetworkOff);
        // These never leave the computer, whatever the setting says
        if (target.IsPassword)
            return AiConsentResult.Refused(AiRefusal.PasswordField);
        var current = settings.CurrentValue;
        if (target.ProcessName is { Length: > 0 } process
            && current.AiPrivateApps.Any(app => SameApp(app, process)))
            return AiConsentResult.Refused(AiRefusal.PrivateApp);
        if (IsPrivateSite(target.Site, current.AiPrivateSites))
            return AiConsentResult.Refused(AiRefusal.PrivateSite);

        return current.AiCloudConsent switch
        {
            AiConsentMode.Allow => AiConsentResult.Allowed,
            AiConsentMode.Never => AiConsentResult.Refused(AiRefusal.CloudNever),
            _ => _agreed ? AiConsentResult.Allowed : AiConsentResult.Ask,
        };
    }

    /// <summary>
    /// Whether <paramref name="target"/> may be sent to a cloud describer by something nobody asked
    /// for (automatic labels): only when <c>AiCloudConsent</c> is Allow, never on a yes given for
    /// the session, and with every rule of <see cref="ForCloud"/>. Never "ask".
    /// </summary>
    public AiConsentResult ForBackground(AiTarget target)
    {
        var cloud = ForCloud(target);
        if (cloud.Decision == AiConsentDecision.Refused)
            return cloud;
        return settings.CurrentValue.AiCloudConsent == AiConsentMode.Allow
            ? AiConsentResult.Allowed
            : AiConsentResult.Refused(AiRefusal.NotInBackground);
    }

    /// <summary>
    /// Whether the page at <paramref name="address"/> is on one of the <paramref name="sites"/>
    /// ("example.com" covers "mail.example.com"; "https://example.com/x" is read as its host).
    /// </summary>
    public static bool IsPrivateSite(string? address, IEnumerable<string> sites)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;
        var host = HostOf(address);
        if (host is null)
            return false;
        foreach (var listed in sites)
        {
            var name = HostOf(listed);
            if (name is { Length: > 0 }
                && (host == name || host.EndsWith("." + name, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    private static string? HostOf(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return null;
        // An address, or a bare host name ("example.com", "example.com/path")
        if ((Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            || Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out uri))
            return uri.Host.TrimEnd('.').ToLowerInvariant();
        return null;
    }

    /// <summary>What to say when nothing was described for <paramref name="reason"/>.</summary>
    public static string Explain(AiRefusal reason) => reason switch
    {
        AiRefusal.None => string.Empty,
        _ => VoxStrings.Get($"Ai.Refusal.{reason}"),
    };

    // "notepad.exe" and "Notepad" are the same application
    private static bool SameApp(string listed, string process)
    {
        var name = listed.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return string.Equals(name, process, StringComparison.OrdinalIgnoreCase);
    }
}
