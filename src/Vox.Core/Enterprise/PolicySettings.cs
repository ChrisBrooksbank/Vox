using System.Globalization;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;

namespace Vox.Core.Enterprise;

/// <summary>Where a user's settings are kept, as a policy sets it.</summary>
public enum SettingsLocation
{
    /// <summary>%APPDATA%\Vox: follows the user with a roaming profile (the default).</summary>
    Roaming,
    /// <summary>%LOCALAPPDATA%\Vox: stays on this computer.</summary>
    Local,
}

/// <summary>
/// What the organization's Group Policy says (<c>Software\Policies\Vox</c> in HKLM, then HKCU;
/// the computer's policy wins over the user's). Set with the ADMX templates in installer/policy
/// (docs/enterprise.md). Policies override settings and lock them, so the settings dialog shows
/// them as set by the organization, and switch whole capabilities off (network, add-ons, AI, the magnifier)
/// through <see cref="RunPolicy"/>. Read once at startup.
/// </summary>
public sealed class PolicySettings
{
    public const string PolicyKey = @"Software\Policies\Vox";

    /// <summary>Any setting by name, enforced (REG_SZ or REG_DWORD values named after the setting).</summary>
    public const string SettingsKey = PolicyKey + @"\Settings";

    public const string DisableNetworkValue = "DisableNetwork";
    public const string DisableUpdatesValue = "DisableUpdates";
    public const string UpdateChannelValue = "UpdateChannel";
    public const string DisableAddOnsValue = "DisableAddOns";
    public const string DisableAiValue = "DisableAI";
    public const string DisableMagnifierValue = "DisableMagnifier";
    public const string StartAtLogonValue = "StartAtLogon";
    public const string LockSettingsValue = "LockSettings";
    public const string SettingsLocationValue = "SettingsLocation";

    /// <summary>Every value name read from <see cref="PolicyKey"/> (the ADMX templates use only these).</summary>
    public static readonly IReadOnlyList<string> ValueNames =
    [
        DisableNetworkValue, DisableUpdatesValue, UpdateChannelValue, DisableAddOnsValue, DisableAiValue,
        DisableMagnifierValue, StartAtLogonValue, LockSettingsValue, SettingsLocationValue,
    ];

    /// <summary>No policy set.</summary>
    public static readonly PolicySettings None = new(new Dictionary<string, object?>(), false, false, false, false, null, []);

    private readonly IReadOnlyDictionary<string, object?> _enforced;

    private PolicySettings(IReadOnlyDictionary<string, object?> enforced, bool disableNetwork, bool disableAddOns,
        bool disableAi, bool lockAll, SettingsLocation? location, IReadOnlyList<string> problems, bool disableMagnifier = false)
    {
        DisableMagnifier = disableMagnifier;
        _enforced = enforced;
        DisableNetwork = disableNetwork;
        DisableAddOns = disableAddOns;
        DisableAi = disableAi;
        LockAllSettings = lockAll;
        SettingsLocation = location;
        Problems = problems;
    }

    /// <summary>No connection leaves this computer (<see cref="RunPolicy.AllowNetwork"/>); no update checks.</summary>
    public bool DisableNetwork { get; }

    /// <summary>No optional components or add-ons are loaded (eSpeak NG, MathCAT, liblouis).</summary>
    public bool DisableAddOns { get; }

    /// <summary>No AI features (image descriptions, explanations), local or not.</summary>
    public bool DisableAi { get; }

    /// <summary>No magnifier: its keys, settings and wizard step say it is off.</summary>
    public bool DisableMagnifier { get; }

    /// <summary>People can't change any setting.</summary>
    public bool LockAllSettings { get; }

    /// <summary>Where users' settings are kept, when a policy says (otherwise roaming).</summary>
    public SettingsLocation? SettingsLocation { get; }

    /// <summary>Policy values that couldn't be used (unknown names, bad values), for the log.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The settings the policy sets, by name, with their values.</summary>
    public IReadOnlyDictionary<string, object?> EnforcedSettings => _enforced;

    /// <summary>True when no policy is set.</summary>
    public bool IsEmpty => _enforced.Count == 0 && !DisableNetwork && !DisableAddOns && !DisableAi && !DisableMagnifier && !LockAllSettings && SettingsLocation is null;

    /// <summary>Whether people may not change <paramref name="settingName"/>.</summary>
    public bool IsLocked(string settingName) =>
        _enforced.ContainsKey(settingName)
        || (LockAllSettings && SettingValues.Find(settingName) is not null
            // Bookkeeping, not a choice anyone makes in the dialog
            && settingName is not (nameof(VoxSettings.SchemaVersion) or nameof(VoxSettings.FirstRunCompleted)));

    /// <summary>Every setting people may not change.</summary>
    public IReadOnlySet<string> LockedSettings =>
        SettingValues.Names.Where(IsLocked).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary><paramref name="settings"/> with the policy's values put in (the same instance when nothing changes).</summary>
    public VoxSettings Apply(VoxSettings settings) => _enforced.Count == 0 ? settings : SettingValues.With(settings, _enforced);

    /// <summary>
    /// <paramref name="proposed"/> as far as the policy lets it change <paramref name="current"/>:
    /// locked settings keep their current values and enforced ones get the policy's.
    /// </summary>
    public VoxSettings Restrain(VoxSettings current, VoxSettings proposed)
    {
        if (!LockAllSettings)
            return Apply(proposed);
        var kept = LockedSettings.Where(name => !_enforced.ContainsKey(name))
            .ToDictionary(name => name, name => SettingValues.Find(name)!.GetValue(current));
        return Apply(SettingValues.With(proposed, kept));
    }

    /// <summary><paramref name="policy"/> with what this policy switches off switched off too.</summary>
    public RunPolicy Restrict(RunPolicy policy) => policy with
    {
        NetworkOffByPolicy = policy.NetworkOffByPolicy || DisableNetwork,
        AddOnsOffByPolicy = policy.AddOnsOffByPolicy || DisableAddOns,
        AiOffByPolicy = policy.AiOffByPolicy || DisableAi,
        MagnifierOffByPolicy = policy.MagnifierOffByPolicy || DisableMagnifier,
    };

    /// <summary>One line for the log.</summary>
    public string Describe()
    {
        if (IsEmpty)
            return "No Vox policies set";
        var parts = new List<string>();
        if (DisableNetwork) parts.Add("network off");
        if (DisableAddOns) parts.Add("add-ons off");
        if (DisableAi) parts.Add("AI off");
        if (DisableMagnifier) parts.Add("magnifier off");
        if (LockAllSettings) parts.Add("all settings locked");
        if (SettingsLocation is { } location) parts.Add($"settings kept {location.ToString().ToLowerInvariant()}");
        if (_enforced.Count > 0)
            parts.Add("enforced: " + string.Join(", ", _enforced.Select(e => $"{e.Key}={e.Value}")));
        return "Vox policies: " + string.Join("; ", parts);
    }

    /// <summary>
    /// Reads the policies. <paramref name="includeUser"/>: also the user's (HKCU) policies; the
    /// computer's (HKLM) win where both set a value. The secure screens read only the computer's.
    /// </summary>
    public static PolicySettings Read(IRegistryReader registry, bool includeUser = true)
    {
        var problems = new List<string>();
        var values = Merge(registry, PolicyKey, includeUser);
        var settingValues = Merge(registry, SettingsKey, includeUser);

        foreach (var name in values.Keys.Where(n => !ValueNames.Contains(n, StringComparer.OrdinalIgnoreCase)))
            problems.Add($"{name} is not a Vox policy");

        var enforced = SettingValues.Convert(settingValues, problems.Add);
        bool disableNetwork = Flag(values, DisableNetworkValue, problems);
        bool disableUpdates = Flag(values, DisableUpdatesValue, problems);

        // The named policies set settings too
        if (values.TryGetValue(UpdateChannelValue, out var channel))
            Enforce(enforced, nameof(VoxSettings.UpdateChannel), channel, problems);
        if (values.TryGetValue(StartAtLogonValue, out var startAtLogon))
            Enforce(enforced, nameof(VoxSettings.StartAtLogon), startAtLogon, problems);
        // No network: no update checks either (and the dialog says so)
        if (disableUpdates || disableNetwork)
            enforced[nameof(VoxSettings.UpdateChannel)] = Updates.UpdateChannel.Off;

        SettingsLocation? location = null;
        if (values.TryGetValue(SettingsLocationValue, out var where))
        {
            if (where is string text && Enum.TryParse<SettingsLocation>(text.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                location = parsed;
            else if (where is int number && Enum.IsDefined(typeof(SettingsLocation), number))
                location = (SettingsLocation)number;
            else
                problems.Add($"{SettingsLocationValue}: {where} is not Roaming or Local");
        }

        return new PolicySettings(enforced, disableNetwork, Flag(values, DisableAddOnsValue, problems),
            Flag(values, DisableAiValue, problems), Flag(values, LockSettingsValue, problems), location, problems,
            Flag(values, DisableMagnifierValue, problems));
    }

    private static Dictionary<string, object> Merge(IRegistryReader registry, string key, bool includeUser)
    {
        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (includeUser)
        {
            foreach (var (name, value) in registry.ReadValues(RegistryRoot.CurrentUser, key))
                merged[name] = value;
        }
        foreach (var (name, value) in registry.ReadValues(RegistryRoot.LocalMachine, key))
            merged[name] = value;
        return merged;
    }

    private static void Enforce(Dictionary<string, object?> enforced, string setting, object raw, List<string> problems)
    {
        if (SettingValues.TryConvert(SettingValues.Find(setting)!, raw, out var value))
            enforced[setting] = value;
        else
            problems.Add($"{setting}: {raw} is not a valid value");
    }

    /// <summary>A DWORD (or "1"/"0" text) policy that is on when not 0.</summary>
    private static bool Flag(Dictionary<string, object> values, string name, List<string> problems)
    {
        if (!values.TryGetValue(name, out var raw))
            return false;
        switch (raw)
        {
            case int or long:
                return Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0;
            case string text when long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number):
                return number != 0;
            case string text when bool.TryParse(text.Trim(), out var flag):
                return flag;
            default:
                problems.Add($"{name}: {raw} is not 0 or 1");
                return false;
        }
    }
}
