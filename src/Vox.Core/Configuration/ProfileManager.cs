using System.Text.Json.Nodes;

namespace Vox.Core.Configuration;

/// <summary>
/// Which configuration profiles are active, and the settings they make. Layered in this order,
/// a later one winning: the normal configuration, the foreground application's profile, the
/// triggered profile (Say All), the profile chosen by hand. Changes made while a profile is active
/// go to the top one (<see cref="EditTarget"/>), as in NVDA, except settings that only make sense
/// once (<see cref="GlobalSettings"/>). Thread-safe: the foreground application is set on the
/// pipeline thread, Say All from its reading task, the rest from the profiles dialog.
/// </summary>
public sealed class ProfileManager
{
    /// <summary>Settings never kept in a profile: they apply to Vox as a whole.</summary>
    public static readonly IReadOnlySet<string> GlobalSettings = new HashSet<string>
    {
        nameof(VoxSettings.SchemaVersion), nameof(VoxSettings.FirstRunCompleted), nameof(VoxSettings.SleepApps),
        nameof(VoxSettings.StartAtLogon), nameof(VoxSettings.UpdateChannel), nameof(VoxSettings.AnnounceProfileSwitches),
    };

    private readonly ProfileStore _store;
    private readonly object _lock = new();
    private List<ConfigurationProfile> _profiles;
    private string? _app;
    private bool _sayAll;
    private string? _manual;

    public ProfileManager(ProfileStore store)
    {
        _store = store;
        _profiles = [.. store.LoadAll()];
    }

    /// <summary>Raised when the active profiles or their settings change (the effective settings may have).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when the application or the hand-chosen profile changes which profile is on top: its
    /// name, or null for the normal configuration. Not for Say All, which must not be talked over.
    /// </summary>
    public event EventHandler<string?>? Switched;

    /// <summary>Whether profiles can be made here (not in secure mode).</summary>
    public bool IsAvailable => _store.IsAvailable;

    public IReadOnlyList<ConfigurationProfile> Profiles
    {
        get { lock (_lock) return _profiles.ToList(); }
    }

    /// <summary>The name of the profile chosen by hand, or null.</summary>
    public string? ManualProfile
    {
        get { lock (_lock) return _manual; }
    }

    /// <summary>The active profiles, lowest first: the application's, Say All's, the hand-chosen one.</summary>
    public IReadOnlyList<ConfigurationProfile> Active
    {
        get { lock (_lock) return ActiveLocked(); }
    }

    /// <summary>The profile changes go to: the top active one; null for the normal configuration.</summary>
    public ConfigurationProfile? EditTarget => Active.LastOrDefault();

    /// <summary>The settings <paramref name="normal"/> becomes with the active profiles laid over it.</summary>
    public VoxSettings Apply(VoxSettings normal)
    {
        var active = Active;
        return active.Count == 0 ? normal : ProfileSettings.Merge(normal, active.Select(p => p.Settings));
    }

    /// <summary>The foreground application changed (its process name, or null).</summary>
    public void SetForegroundApp(string? processName) =>
        Change(() =>
        {
            var before = TopName();
            _app = processName;
            return TopName() != before;
        });

    /// <summary>Say All started or stopped.</summary>
    public void SetSayAll(bool reading) => Change(() => { _sayAll = reading; return false; });

    /// <summary>Activates a profile by hand (null: none); returns false when there is no such profile.</summary>
    public bool ActivateManually(string? name)
    {
        bool found = true;
        Change(() =>
        {
            if (name is not null && Find(name) is null)
            {
                found = false;
                return false;
            }
            var before = TopName();
            _manual = name;
            return TopName() != before;
        });
        return found;
    }

    /// <summary>Makes a profile with no settings of its own; false when the name is taken or empty, or profiles can't be kept here.</summary>
    public bool Create(string name, IReadOnlyList<string>? apps = null, bool sayAll = false)
    {
        name = name.Trim();
        if (name.Length == 0 || !_store.IsAvailable)
            return false;
        ConfigurationProfile profile;
        lock (_lock)
        {
            if (Find(name) is not null || _profiles.Any(p => ProfileStore.FileName(p.Name).Equals(ProfileStore.FileName(name), StringComparison.OrdinalIgnoreCase)))
                return false;
            profile = new ConfigurationProfile(name, new JsonObject(), apps ?? [], sayAll);
            _profiles.Add(profile);
        }
        _store.Save(profile);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Deletes a profile (and stops using it).</summary>
    public void Delete(string name)
    {
        Change(() =>
        {
            var before = TopName();
            _profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_manual, name, StringComparison.OrdinalIgnoreCase))
                _manual = null;
            return TopName() != before;
        });
        _store.Delete(name);
    }

    /// <summary>Renames a profile; false when the new name is taken or empty.</summary>
    public bool Rename(string name, string newName)
    {
        newName = newName.Trim();
        ConfigurationProfile? renamed = null;
        lock (_lock)
        {
            if (newName.Length == 0 || Find(name) is not { } profile
                || (Find(newName) is not null && !newName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return false;
            renamed = profile with { Name = newName };
            Replace(profile, renamed);
            if (string.Equals(_manual, name, StringComparison.OrdinalIgnoreCase))
                _manual = newName;
        }
        _store.Delete(name);
        _store.Save(renamed);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Sets whether a profile is used while <paramref name="processName"/> is in the foreground.</summary>
    public void SetAppActivation(string name, string processName, bool on) =>
        Update(name, profile => profile with
        {
            Apps = on
                ? profile.IsForApp(processName) ? profile.Apps : [.. profile.Apps, processName]
                : profile.Apps.Where(a => !a.Equals(processName, StringComparison.OrdinalIgnoreCase)).ToList(),
        });

    /// <summary>Sets whether a profile is used during Say All (only one is: the others stop being).</summary>
    public void SetSayAllTrigger(string name, bool on)
    {
        if (on)
        {
            foreach (var other in Profiles.Where(p => p.SayAll && !p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                Update(other.Name, p => p with { SayAll = false });
        }
        Update(name, profile => profile with { SayAll = on });
    }

    /// <summary>
    /// Keeps <paramref name="changes"/> (settings and new values) in the top active profile.
    /// Returns the changes that belong to the normal configuration instead: all of them when no
    /// profile is active, else the global ones. Doesn't raise <see cref="Changed"/>: the caller
    /// works out the new settings.
    /// </summary>
    public JsonObject RecordChanges(JsonObject changes)
    {
        var normal = new JsonObject();
        ConfigurationProfile? saved = null;
        lock (_lock)
        {
            var target = ActiveLocked().LastOrDefault();
            if (target is null)
                return (JsonObject)changes.DeepClone();
            var settings = (JsonObject)target.Settings.DeepClone();
            foreach (var (name, value) in changes)
            {
                if (GlobalSettings.Contains(name))
                    normal[name] = value?.DeepClone();
                else
                    settings[name] = value?.DeepClone();
            }
            saved = target with { Settings = settings };
            Replace(target, saved);
        }
        _store.Save(saved);
        return normal;
    }

    private void Update(string name, Func<ConfigurationProfile, ConfigurationProfile> change)
    {
        ConfigurationProfile? saved = null;
        lock (_lock)
        {
            if (Find(name) is not { } profile)
                return;
            saved = change(profile);
            Replace(profile, saved);
        }
        _store.Save(saved);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs <paramref name="change"/> under the lock; it returns whether the profile on top switched.</summary>
    private void Change(Func<bool> change)
    {
        bool switched;
        string? top;
        lock (_lock)
        {
            switched = change();
            top = TopName();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (switched)
            Switched?.Invoke(this, top);
    }

    private List<ConfigurationProfile> ActiveLocked()
    {
        var active = new List<ConfigurationProfile>();
        if (_profiles.FirstOrDefault(p => p.IsForApp(_app)) is { } app)
            active.Add(app);
        if (_sayAll && _profiles.FirstOrDefault(p => p.SayAll) is { } trigger && !active.Contains(trigger))
            active.Add(trigger);
        if (_manual is not null && Find(_manual) is { } manual)
        {
            active.Remove(manual);
            active.Add(manual);
        }
        return active;
    }

    // The top profile apart from Say All's
    private string? TopName()
    {
        string? top = null;
        if (_profiles.FirstOrDefault(p => p.IsForApp(_app)) is { } app)
            top = app.Name;
        if (_manual is not null && Find(_manual) is { } manual)
            top = manual.Name;
        return top;
    }

    private ConfigurationProfile? Find(string name) =>
        _profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void Replace(ConfigurationProfile old, ConfigurationProfile replacement)
    {
        int index = _profiles.IndexOf(old);
        if (index >= 0)
            _profiles[index] = replacement;
    }
}
