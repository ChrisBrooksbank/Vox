using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vox.Core.Configuration;

/// <summary>
/// Manages loading and saving of VoxSettings from %APPDATA%/Vox/settings.json,
/// with fallback to the bundled default-settings.json asset.
/// </summary>
public sealed class SettingsManager
{
    public static string DefaultUserSettingsPath => Path.Combine(
        Lifecycle.VoxPaths.UserData, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        }
    };

    /// <summary>The settings file's JSON for <paramref name="settings"/>.</summary>
    public static string Serialize(VoxSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    /// <summary>Reads settings JSON; false (and null) when it isn't valid settings.</summary>
    public static bool TryParse(string json, out VoxSettings? settings)
    {
        try
        {
            settings = JsonSerializer.Deserialize<VoxSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            settings = null;
        }
        return settings is not null;
    }

    private readonly ILogger<SettingsManager> _logger;
    private readonly string _defaultSettingsPath;
    private readonly string _userSettingsPath;

    public SettingsManager(ILogger<SettingsManager> logger, string defaultSettingsPath)
        : this(logger, defaultSettingsPath, DefaultUserSettingsPath)
    {
    }

    public SettingsManager(ILogger<SettingsManager> logger, string defaultSettingsPath, string userSettingsPath)
    {
        _logger = logger;
        _defaultSettingsPath = defaultSettingsPath;
        _userSettingsPath = userSettingsPath;
    }

    /// <summary>Path of the user settings file this manager loads and saves.</summary>
    public string UserSettingsPath => _userSettingsPath;

    /// <summary>
    /// Never write the settings file (secure mode): changes apply for this session only.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// The computer's default settings (<see cref="Enterprise.MachineSettings"/>), laid between the
    /// built-in defaults and the user's file: what a new user starts with, and what a user's file
    /// doesn't say. Null: none.
    /// </summary>
    public JsonObject? MachineDefaults { get; init; }

    /// <summary>
    /// Loads settings from the user settings file, falling back to defaults if not found or invalid.
    /// </summary>
    public VoxSettings Load()
    {
        if (File.Exists(_userSettingsPath))
        {
            if (TryLoadUserSettings(out var settings))
                return settings;
            _logger.LogWarning("Failed to load user settings from {Path}, falling back to defaults", _userSettingsPath);
        }

        return LoadDefaults();
    }

    /// <summary>
    /// Reads the user settings file. Returns false (without falling back to defaults) if the file
    /// is missing, unreadable or not valid JSON — e.g. while an editor is part-way through saving it.
    /// </summary>
    public bool TryLoadUserSettings(out VoxSettings settings)
    {
        settings = new VoxSettings();
        try
        {
            if (!File.Exists(_userSettingsPath))
                return false;

            var json = File.ReadAllText(_userSettingsPath);
            if (JsonNode.Parse(json) is not JsonObject document)
                return false;

            // An older file is brought up to date (and saved so, keeping a copy of the old one);
            // a newer one is read as far as this version understands it, and never overwritten
            int version = SettingsMigrations.Migrate(document);
            _fileIsNewer = version > VoxSettings.CurrentSchemaVersion;
            var loaded = (MachineDefaults is null ? document : Enterprise.SettingValues.Overlay(MachineDefaults, document))
                .Deserialize<VoxSettings>(JsonOptions);
            if (loaded is null)
                return false;

            _logger.LogInformation("Loaded settings from {Path}", _userSettingsPath);
            if (_fileIsNewer)
                _logger.LogWarning("Settings file is from a newer Vox (schema {Version}); it won't be overwritten", version);
            else if (version < VoxSettings.CurrentSchemaVersion && !ReadOnly)
                SaveMigrated(json, version, loaded);
            settings = loaded;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read user settings from {Path}", _userSettingsPath);
            return false;
        }
    }

    // The settings file was written by a newer Vox: saving would lose what this one doesn't know
    private bool _fileIsNewer;

    /// <summary>
    /// Saves settings to %APPDATA%/Vox/settings.json, creating the directory if needed.
    /// </summary>
    public void Save(VoxSettings settings)
    {
        if (ReadOnly)
        {
            _logger.LogInformation("Settings are read-only here; not saved");
            return;
        }
        if (_fileIsNewer)
        {
            _logger.LogWarning("Settings file is from a newer Vox; not saved");
            return;
        }
        SaveTo(_userSettingsPath, settings with { SchemaVersion = VoxSettings.CurrentSchemaVersion });
    }

    /// <summary>Keeps the old file as settings.v&lt;version&gt;.json and saves the migrated settings.</summary>
    private void SaveMigrated(string oldJson, int oldVersion, VoxSettings migrated)
    {
        try
        {
            var backup = Path.Combine(Path.GetDirectoryName(_userSettingsPath)!,
                $"{Path.GetFileNameWithoutExtension(_userSettingsPath)}.v{oldVersion}{Path.GetExtension(_userSettingsPath)}");
            if (!File.Exists(backup))
                File.WriteAllText(backup, oldJson);
            if (SaveTo(_userSettingsPath, migrated with { SchemaVersion = VoxSettings.CurrentSchemaVersion }))
                _logger.LogInformation("Settings migrated from schema {Old} to {New}", oldVersion, VoxSettings.CurrentSchemaVersion);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save the migrated settings");
        }
    }

    /// <summary>Writes settings to <paramref name="path"/> atomically. Returns whether it worked.</summary>
    public bool SaveTo(string path, VoxSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            // Write to a temporary file and swap it in, so a crash mid-write can't leave a
            // truncated settings file behind
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
            _logger.LogInformation("Saved settings to {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", path);
            return false;
        }
    }

    private VoxSettings LoadDefaults()
    {
        if (MachineDefaults is not null)
        {
            try
            {
                var builtIn = File.Exists(_defaultSettingsPath) ? JsonNode.Parse(File.ReadAllText(_defaultSettingsPath)) as JsonObject : null;
                var settings = Enterprise.SettingValues.Overlay(builtIn, MachineDefaults).Deserialize<VoxSettings>(JsonOptions);
                if (settings is not null)
                {
                    _logger.LogInformation("Loaded default settings with the computer's defaults");
                    return settings;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(ex, "Failed to apply the computer's default settings");
            }
        }

        if (File.Exists(_defaultSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(_defaultSettingsPath);
                var settings = JsonSerializer.Deserialize<VoxSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    _logger.LogInformation("Loaded default settings from {Path}", _defaultSettingsPath);
                    return settings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load default settings from {Path}, using built-in defaults", _defaultSettingsPath);
            }
        }

        _logger.LogInformation("Using built-in default settings");
        return new VoxSettings();
    }
}

/// <summary>
/// IOptionsMonitor-compatible wrapper that watches the user settings file for changes
/// and reloads automatically.
/// </summary>
public sealed class SettingsMonitor : IOptionsMonitor<VoxSettings>, IDisposable
{
    private readonly SettingsManager _manager;
    private readonly ILogger<SettingsMonitor> _logger;
    // The normal configuration (the settings file), and what it makes with the active profiles
    private VoxSettings _normal;
    private VoxSettings _current;
    private readonly object _updateLock = new();
    private ProfileManager? _profiles;
    private readonly object _listenerLock = new();
    private readonly List<Action<VoxSettings, string?>> _listeners = new();
    private FileSystemWatcher? _watcher;
    // Track last programmatic save time to suppress the resulting file watcher event
    private long _lastProgrammaticSaveTick;

    // The organization's policies: laid over every settings value this monitor gives out
    private readonly Enterprise.PolicySettings _policy;

    public SettingsMonitor(SettingsManager manager, ILogger<SettingsMonitor> logger, Enterprise.PolicySettings? policy = null)
    {
        _manager = manager;
        _logger = logger;
        _policy = policy ?? Enterprise.PolicySettings.None;
        _normal = _manager.Load();
        _current = _policy.Apply(_normal);
        StartWatching();
    }

    /// <summary>The organization's policies, which these settings always follow.</summary>
    public Enterprise.PolicySettings Policy => _policy;

    /// <summary>The effective settings: the normal configuration with the active profiles, then the policies, laid over it.</summary>
    public VoxSettings CurrentValue => _current;

    /// <summary>The normal configuration, without profiles.</summary>
    public VoxSettings NormalValue => _normal;

    /// <summary>
    /// Lays the active configuration profiles over the settings from now on; when they change the
    /// settings are worked out again and listeners told.
    /// </summary>
    public void AttachProfiles(ProfileManager profiles)
    {
        _profiles = profiles;
        profiles.Changed += (_, _) => Recompute();
        Recompute();
    }

    private void Recompute()
    {
        VoxSettings settings;
        lock (_updateLock)
        {
            settings = Compose(_normal);
            if (settings == _current)
                return;
            _current = settings;
        }
        Notify(settings);
    }

    /// <summary>The effective settings for <paramref name="normal"/>: the active profiles, then the policies, over it.</summary>
    private VoxSettings Compose(VoxSettings normal) => _policy.Apply(_profiles?.Apply(normal) ?? normal);

    private void Notify(VoxSettings settings)
    {
        Action<VoxSettings, string?>[] snapshot;
        lock (_listenerLock) { snapshot = _listeners.ToArray(); }
        foreach (var listener in snapshot)
            listener(settings, null);
    }

    public VoxSettings Get(string? name) => _current;

    /// <summary>
    /// Changes the settings to <paramref name="settings"/> and saves them, notifying all OnChange
    /// listeners. While a configuration profile is active, what changed is kept in that profile
    /// (all but the global settings), as NVDA does; otherwise in the normal configuration.
    /// </summary>
    public void UpdateSettings(VoxSettings settings)
    {
        // A setting the policy sets can't be changed, whoever changes it
        settings = _policy.Restrain(_current, settings);
        VoxSettings effective;
        lock (_updateLock)
        {
            if (_profiles is { EditTarget: not null } profiles)
            {
                var forNormal = profiles.RecordChanges(ProfileSettings.Diff(_current, settings));
                if (forNormal.Count > 0)
                    SaveNormal(ProfileSettings.Merge(_normal, [forNormal]));
                effective = Compose(_normal);
            }
            else if (_profiles is { HasSiteSettings: true })
            {
                // The site's own settings stay the site's: only what changed is the normal configuration's
                SaveNormal(ProfileSettings.Merge(_normal, [ProfileSettings.Diff(_current, settings)]));
                effective = Compose(_normal);
            }
            else
            {
                SaveNormal(settings);
                effective = Compose(_normal);
            }
            _current = effective;
        }
        Notify(effective);
    }

    private void SaveNormal(VoxSettings settings)
    {
        _normal = settings;
        Interlocked.Exchange(ref _lastProgrammaticSaveTick, Environment.TickCount64);
        _manager.Save(settings);
    }

    public IDisposable? OnChange(Action<VoxSettings, string?> listener)
    {
        lock (_listenerLock) { _listeners.Add(listener); }
        return new CallbackRegistration(() => { lock (_listenerLock) { _listeners.Remove(listener); } });
    }

    private void StartWatching()
    {
        var path = _manager.UserSettingsPath;
        var dir = Path.GetDirectoryName(path)!;

        try
        {
            // Create the directory so external edits are picked up even on first run
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            // Many editors (and Save above) write a temporary file and rename it over the original
            _watcher.Renamed += OnFileChanged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start file watcher for settings");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Small delay to let the file write complete
        Thread.Sleep(100);

        // Suppress reload if the file change was triggered by a programmatic save
        // (within the past 500ms — the watcher delay is 100ms, so this is plenty)
        var lastSave = Interlocked.Read(ref _lastProgrammaticSaveTick);
        if (lastSave != 0 && Environment.TickCount64 - lastSave < 500)
            return;

        try
        {
            // Keep the current settings if the file can't be read right now (half-written by an
            // editor, a typo, deleted): falling back to defaults would reset everything live
            if (!_manager.TryLoadUserSettings(out var reloaded))
            {
                _logger.LogWarning("Settings file changed but could not be read; keeping current settings");
                return;
            }
            VoxSettings effective;
            lock (_updateLock)
            {
                _normal = _policy.Restrain(_normal, reloaded);
                effective = Compose(_normal);
                _current = effective;
            }
            _logger.LogInformation("Settings reloaded from file change");
            Notify(effective);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload settings after file change");
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }

    private sealed class CallbackRegistration(Action unregister) : IDisposable
    {
        public void Dispose() => unregister();
    }
}
