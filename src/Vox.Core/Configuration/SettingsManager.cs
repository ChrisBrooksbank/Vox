using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vox.Core.Configuration;

/// <summary>
/// Manages loading and saving of VoxSettings from %APPDATA%/Vox/settings.json,
/// with fallback to the bundled default-settings.json asset.
/// </summary>
public sealed class SettingsManager
{
    public static readonly string DefaultUserSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vox", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        }
    };

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

    /// <summary>
    /// Loads settings from the user settings file, falling back to defaults if not found or invalid.
    /// </summary>
    public VoxSettings Load()
    {
        if (File.Exists(_userSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(_userSettingsPath);
                var settings = JsonSerializer.Deserialize<VoxSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    _logger.LogInformation("Loaded settings from {Path}", _userSettingsPath);
                    return settings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load user settings from {Path}, falling back to defaults", _userSettingsPath);
            }
        }

        return LoadDefaults();
    }

    /// <summary>
    /// Saves settings to %APPDATA%/Vox/settings.json, creating the directory if needed.
    /// </summary>
    public void Save(VoxSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_userSettingsPath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_userSettingsPath, json);
            _logger.LogInformation("Saved settings to {Path}", _userSettingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _userSettingsPath);
        }
    }

    private VoxSettings LoadDefaults()
    {
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
    private VoxSettings _current;
    private readonly object _listenerLock = new();
    private readonly List<Action<VoxSettings, string?>> _listeners = new();
    private FileSystemWatcher? _watcher;
    // Track last programmatic save time to suppress the resulting file watcher event
    private long _lastProgrammaticSaveTick;

    public SettingsMonitor(SettingsManager manager, ILogger<SettingsMonitor> logger)
    {
        _manager = manager;
        _logger = logger;
        _current = _manager.Load();
        StartWatching();
    }

    public VoxSettings CurrentValue => _current;

    public VoxSettings Get(string? name) => _current;

    /// <summary>
    /// Updates the current settings in memory and persists to disk.
    /// Notifies all registered OnChange listeners.
    /// </summary>
    public void UpdateSettings(VoxSettings settings)
    {
        _current = settings;
        Interlocked.Exchange(ref _lastProgrammaticSaveTick, Environment.TickCount64);
        _manager.Save(settings);
        Action<VoxSettings, string?>[] snapshot;
        lock (_listenerLock) { snapshot = _listeners.ToArray(); }
        foreach (var listener in snapshot)
            listener(settings, null);
    }

    public IDisposable? OnChange(Action<VoxSettings, string?> listener)
    {
        lock (_listenerLock) { _listeners.Add(listener); }
        return new CallbackRegistration(() => { lock (_listenerLock) { _listeners.Remove(listener); } });
    }

    private void StartWatching()
    {
        var dir = Path.GetDirectoryName(SettingsManager.DefaultUserSettingsPath)!;
        if (!Directory.Exists(dir))
            return;

        try
        {
            _watcher = new FileSystemWatcher(dir, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
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
            var reloaded = _manager.Load();
            _current = reloaded;
            _logger.LogInformation("Settings reloaded from file change");
            Action<VoxSettings, string?>[] snapshot;
            lock (_listenerLock) { snapshot = _listeners.ToArray(); }
            foreach (var listener in snapshot)
                listener(reloaded, null);
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
