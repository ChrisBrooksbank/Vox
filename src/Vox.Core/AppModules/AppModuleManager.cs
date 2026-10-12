using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;

namespace Vox.Core.AppModules;

/// <summary>
/// Picks the app module for the foreground application (by process name; none when no module is
/// for it or it is turned off in <see cref="VoxSettings.DisabledAppModules"/>), activates it,
/// swaps its keys in as the dispatcher's app layer, and passes it the pipeline's events, focus
/// announcements and commands. Everything but <see cref="ActiveModule"/> is called on the
/// pipeline thread.
/// </summary>
public sealed class AppModuleManager
{
    private const int MaxCachedProcesses = 256;

    private readonly IReadOnlyList<IAppModule> _modules;
    private readonly IForegroundWindow _foreground;
    private readonly Func<int, string?> _processName;
    private readonly Action<KeyMap?> _setAppKeyMap;
    private readonly IOptionsMonitor<VoxSettings>? _settings;
    private readonly ILogger _logger;
    private readonly Dictionary<int, string?> _names = new();
    private readonly Dictionary<string, KeyMap?> _keyMaps = new(StringComparer.Ordinal);
    private readonly object _namesLock = new();
    private volatile IAppModule? _active;
    private int _activeProcessId;
    private IntPtr _activeWindow;

    public AppModuleManager(
        IEnumerable<IAppModule> modules,
        IForegroundWindow foreground,
        Action<KeyMap?> setAppKeyMap,
        IOptionsMonitor<VoxSettings>? settings = null,
        ILogger<AppModuleManager>? logger = null,
        Func<int, string?>? processName = null)
    {
        _modules = modules.ToList();
        _foreground = foreground;
        _setAppKeyMap = setAppKeyMap;
        _settings = settings;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _processName = processName ?? ProcessName;
    }

    /// <summary>Every module Vox has.</summary>
    public IReadOnlyList<IAppModule> Modules => _modules;

    /// <summary>The module of the foreground application, or null (any thread).</summary>
    public IAppModule? ActiveModule => _active;

    /// <summary>The foreground application's process name as last seen, or null.</summary>
    public string? ActiveProcessName { get; private set; }

    /// <summary>Raised (on the pipeline thread) when another application comes to the foreground: its process name or null.</summary>
    public event EventHandler<string?>? ForegroundAppChanged;

    /// <summary>
    /// Looks at the foreground window and, when it belongs to another process than last time,
    /// switches modules. Cheap enough for every focus change: no UIA, and process names are cached.
    /// </summary>
    public void Refresh()
    {
        ForegroundWindowInfo? window;
        try { window = _foreground.Get(); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the foreground window");
            return;
        }
        if (window is not { } info)
            return;
        Update(info.ProcessId, info.Handle);
    }

    /// <summary>Activates the module for process <paramref name="processId"/> if it isn't already.</summary>
    public void Update(int processId, IntPtr window = default)
    {
        if (processId == _activeProcessId)
        {
            if (window != IntPtr.Zero)
                _activeWindow = window;
            return;
        }
        _activeProcessId = processId;
        _activeWindow = window;

        var name = processId > 0 ? CachedName(processId) : null;
        ActiveProcessName = name;
        var module = name is null ? null : ModuleFor(name);
        if (!ReferenceEquals(module, _active))
            Switch(module, new AppActivation(processId, name ?? string.Empty, window));
        else if (module is not null)
        {
            // Another instance of the same application (a second Word): the module starts again on it
            Deactivate(module);
            Activate(module, new AppActivation(processId, name!, window));
        }
        ForegroundAppChanged?.Invoke(this, name);
    }

    /// <summary>The module for <paramref name="processName"/>, unless it is turned off; null when none.</summary>
    public IAppModule? ModuleFor(string processName)
    {
        var disabled = _settings?.CurrentValue.DisabledAppModules;
        foreach (var module in _modules)
        {
            if (!module.ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
                continue;
            if (disabled is not null && disabled.Contains(module.Id, StringComparer.OrdinalIgnoreCase))
                return null;
            return module;
        }
        return null;
    }

    /// <summary>Deactivates the active module (Vox is stopping) and clears the key layer.</summary>
    public void Stop() => Switch(null, default);

    /// <summary>Picks the module again for the same application (the disabled modules changed).</summary>
    public void Reselect()
    {
        var name = ActiveProcessName;
        var module = name is null ? null : ModuleFor(name);
        if (!ReferenceEquals(module, _active))
            Switch(module, new AppActivation(_activeProcessId, name ?? string.Empty, _activeWindow));
    }

    /// <summary>False when the active module drops <paramref name="evt"/>.</summary>
    public bool ShouldProcess(ScreenReaderEvent evt)
    {
        if (_active is not { } module)
            return true;
        try { return module.ShouldProcess(evt); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App module {Module} failed filtering {Event}", module.Id, evt.GetType().Name);
            return true;
        }
    }

    /// <summary>The focus announcement as the active module would have it.</summary>
    public string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement)
    {
        if (_active is not { } module)
            return announcement;
        try { return module.FormatFocusAnnouncement(focus, announcement); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App module {Module} failed formatting a focus announcement", module.Id);
            return announcement;
        }
    }

    /// <summary>Passes a handled event to the active module.</summary>
    public void OnEvent(ScreenReaderEvent evt)
    {
        if (_active is not { } module)
            return;
        try { module.OnEvent(evt); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App module {Module} failed handling {Event}", module.Id, evt.GetType().Name);
        }
    }

    /// <summary>Runs <paramref name="command"/> if the active module has it; returns whether it did.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        if (_active is not { } module)
            return false;
        try { return module.TryHandle(command); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App module {Module} failed running {Command}", module.Id, command);
            return true;
        }
    }

    private void Switch(IAppModule? module, AppActivation activation)
    {
        if (_active is { } old)
            Deactivate(old);
        _active = null;
        _setAppKeyMap(module is null ? null : KeyMapOf(module));
        if (module is not null)
            Activate(module, activation);
    }

    private void Activate(IAppModule module, AppActivation activation)
    {
        _logger.LogDebug("App module {Module} active", module.Id);
        _active = module;
        try { module.OnActivated(activation); }
        catch (Exception ex) { _logger.LogError(ex, "App module {Module} failed to start", module.Id); }
    }

    private void Deactivate(IAppModule module)
    {
        try { module.OnDeactivated(); }
        catch (Exception ex) { _logger.LogError(ex, "App module {Module} failed to stop", module.Id); }
    }

    private KeyMap? KeyMapOf(IAppModule module)
    {
        if (_keyMaps.TryGetValue(module.Id, out var cached))
            return cached;
        KeyMap? map = null;
        if (module.KeyBindings.Count > 0)
        {
            map = KeyMap.FromBindings(module.KeyBindings, out var warnings);
            foreach (var warning in warnings)
                _logger.LogWarning("App module {Module} keymap: {Warning}", module.Id, warning);
        }
        _keyMaps[module.Id] = map;
        return map;
    }

    private string? CachedName(int processId)
    {
        lock (_namesLock)
        {
            if (_names.TryGetValue(processId, out var cached))
                return cached;
        }
        var name = _processName(processId);
        lock (_namesLock)
        {
            if (_names.Count >= MaxCachedProcesses)
                _names.Clear();
            _names[processId] = name;
        }
        return name;
    }

    private static string? ProcessName(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
