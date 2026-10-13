using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Braille;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Sdk;
using CoreAppModule = Vox.Core.AppModules.IAppModule;

namespace Vox.Core.AddOns;

/// <summary>What an installed add-on is doing.</summary>
public enum AddOnState
{
    /// <summary>Loaded and working.</summary>
    Running,
    /// <summary>Turned off by the user.</summary>
    Disabled,
    /// <summary>Made for another add-on API than this Vox's (<see cref="AddOnInfo.Problem"/> says why).</summary>
    Incompatible,
    /// <summary>Couldn't be loaded or failed to start (<see cref="AddOnInfo.Problem"/> says why).</summary>
    Failed,
    /// <summary>Turned off by Vox because it kept failing; on again only when the user enables it.</summary>
    TurnedOff,
    /// <summary>Not loaded: safe mode, or add-ons are off on this screen or by the organization.</summary>
    NotLoaded,
}

/// <summary>An add-on command as listed in the add-ons dialog: its name, description and keys.</summary>
public sealed record AddOnCommandInfo(string Name, string Description, IReadOnlyList<KeyGesture> Gestures);

/// <summary>An installed add-on, as the add-ons dialog lists it.</summary>
public sealed record AddOnInfo(
    AddOnManifest Manifest,
    AddOnState State,
    string? Problem,
    AddOnTrust Trust,
    string? Publisher,
    IReadOnlyList<AddOnCommandInfo> Commands);

/// <summary>A checked package and what installing it would do.</summary>
/// <param name="Installed">The version of it installed now, or null.</param>
/// <param name="Incompatible">Why it can't be used with this Vox (said to the user), or null.</param>
public sealed record AddOnInstallCheck(string PackagePath, AddOnPackageInfo Package, string? Installed, string? Incompatible)
{
    public AddOnManifest Manifest => Package.Manifest;
    public bool IsUpdate => Installed is not null;
}

/// <summary>How the add-on manager reaches the rest of Vox (all optional; tests set what they check).</summary>
public sealed class AddOnIntegration
{
    /// <summary>Says something for an add-on (Vox's speech queue).</summary>
    public Action<string, Sdk.SpeechPriority> Speak { get; init; } = (_, _) => { };
    public Action StopSpeaking { get; init; } = () => { };
    /// <summary>Runs work on the pipeline thread.</summary>
    public Action<Action> Post { get; init; } = work => work();
    public Func<string?> ForegroundProcessName { get; init; } = () => null;
    /// <summary>Gives the app module manager the add-ons' modules.</summary>
    public Action<IReadOnlyList<CoreAppModule>> SetAppModules { get; init; } = _ => { };
    /// <summary>The app module active now.</summary>
    public Func<CoreAppModule?> ActiveAppModule { get; init; } = () => null;
    /// <summary>Gives the key dispatcher the add-on commands' keys.</summary>
    public Action<AddOnKeyLayer?> SetKeyLayer { get; init; } = _ => { };
    public SpeechEngineRegistry? SpeechEngines { get; init; }
    /// <summary>The SpeechEngine setting, so an add-on's engine chosen there starts once it is added.</summary>
    public Func<string?> SelectedSpeechEngine { get; init; } = () => null;
    public BrailleDisplayManager? Braille { get; init; }
    /// <summary>Gives custom labels a label-pack add-on's labels (by its id), or with null takes them back.</summary>
    public Action<string, IReadOnlyList<Labels.CustomLabel>?> SetLabelPack { get; init; } = (_, _) => { };
}

/// <summary>
/// The add-ons installed in %APPDATA%\Vox\addons (one folder each, named by id, with state.json
/// beside them: which are disabled or were turned off). Loads each enabled, compatible add-on in
/// its own collectible load context (<see cref="IAddOnLoader"/>), gives what it adds to the rest of
/// Vox, and takes it all back out when it is disabled, removed, updated or keeps failing
/// (<see cref="AddOnGuard"/>). Nothing loads in safe mode (<c>--safe</c>), on the secure screens or
/// when the organization's policy turns add-ons off.
/// </summary>
/// <remarks>
/// Loading, unloading and the add-on calls (commands, plug-ins) happen on the pipeline thread;
/// <see cref="List"/>, <see cref="CheckPackage"/> and <see cref="InstallFiles"/> on any thread.
/// Announcement filters are read from the speech thread through <see cref="Filters"/>.
/// </remarks>
public sealed class AddOnManager
{
    public const string StateFileName = "state.json";
    private const string DataFolderName = ".data";

    private readonly string? _directory;
    private readonly RunPolicy _policy;
    private readonly IAddOnLoader _loader;
    private readonly AddOnTrustStore _trust;
    private readonly AddOnIntegration _integration;
    private readonly Func<string, ILogger> _loggerFor;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly AddOnWatchdog _watchdog;
    private readonly object _lock = new();
    private readonly object _fileLock = new();
    private readonly Dictionary<string, Loaded> _loaded = new(StringComparer.Ordinal);
    // Label packs in use: no code, just their labels (AddOnManifest.IsLabelPack)
    private readonly Dictionary<string, AddOnManifest> _labelPacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (AddOnState State, string? Problem)> _notLoaded = new(StringComparer.Ordinal);
    private StateFile _state;

    private volatile IReadOnlyList<GuardedFilter> _filters = [];
    private volatile IReadOnlyList<GuardedPlugin> _plugins = [];

    public AddOnManager(
        string? directory,
        RunPolicy policy,
        IAddOnLoader loader,
        AddOnTrustStore trust,
        AddOnIntegration? integration = null,
        Func<string, ILogger>? loggerFor = null,
        ILogger<AddOnManager>? logger = null,
        TimeProvider? time = null)
    {
        // Secure screens never even look at the user's add-ons
        _directory = policy.IsSecure ? null : directory;
        _policy = policy;
        _loader = loader;
        _trust = trust;
        _integration = integration ?? new AddOnIntegration();
        _loggerFor = loggerFor ?? (_ => NullLogger.Instance);
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
        _watchdog = new AddOnWatchdog(_time);
        _state = ReadState();
    }

    /// <summary>Where add-ons are installed (null on the secure screens).</summary>
    public string? Directory => _directory;

    /// <summary>Add-ons may be loaded in this Vox (not safe mode, secure screens or policy).</summary>
    public bool CanLoad => _directory is not null && _policy.AllowAddOns && !_policy.SafeMode;

    /// <summary>Add-ons may be installed, removed and switched on and off here (also in safe mode, to fix a bad one).</summary>
    public bool CanManage => _directory is not null && _policy.AllowAddOns && _policy.AllowSettingsWrites;

    public bool IsSafeMode => _policy.SafeMode;

    /// <summary>The announcement filters of the running add-ons, in load order (any thread).</summary>
    public IReadOnlyList<GuardedFilter> Filters => _filters;

    /// <summary>Raised (on the thread that changed it) after add-ons are installed, loaded, unloaded or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>Why add-ons can't be used here, said to the user; null when they can be managed.</summary>
    public string? Unavailable =>
        _policy.IsSecure ? VoxStrings.Get("AddOns.NotOnThisScreen")
        : !_policy.AllowAddOns ? VoxStrings.Get("AddOns.OffByPolicy")
        : _directory is null || !_policy.AllowSettingsWrites ? VoxStrings.Get("AddOns.NotOnThisScreen")
        : null;

    // ---- Listing ----------------------------------------------------------------------------

    /// <summary>The installed add-ons, by name.</summary>
    public IReadOnlyList<AddOnInfo> List()
    {
        if (_directory is null || !System.IO.Directory.Exists(_directory))
            return [];
        var list = new List<AddOnInfo>();
        foreach (var folder in AddOnFolders())
        {
            var id = Path.GetFileName(folder);
            AddOnManifest manifest;
            try { manifest = ReadManifest(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                list.Add(new AddOnInfo(new AddOnManifest { Id = id, Name = id, Version = "?", Author = "?" }, AddOnState.Failed,
                    VoxStrings.Get("AddOns.Damaged"), AddOnTrust.Unsigned, null, []));
                continue;
            }
            var (publisher, trust) = AddOnPackage.TrustOfFolder(folder, _trust);
            list.Add(Describe(manifest, trust, publisher));
        }
        return list.OrderBy(a => a.Manifest.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private AddOnInfo Describe(AddOnManifest manifest, AddOnTrust trust, string? publisher)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(manifest.Id, out var loaded))
                return new AddOnInfo(loaded.Manifest, AddOnState.Running, null, trust, publisher, loaded.CommandInfos());
            if (_labelPacks.TryGetValue(manifest.Id, out var pack))
                return new AddOnInfo(pack, AddOnState.Running, null, trust, publisher, []);
            var reason = AddOnCompatibility.Reason(manifest, VoxApi.Current);
            if (reason is not null)
                return new AddOnInfo(manifest, AddOnState.Incompatible, reason, trust, publisher, []);
            if (_state.TurnedOff.TryGetValue(manifest.Id, out var why))
                return new AddOnInfo(manifest, AddOnState.TurnedOff, why, trust, publisher, []);
            if (_state.Disabled.Contains(manifest.Id))
                return new AddOnInfo(manifest, AddOnState.Disabled, null, trust, publisher, []);
            if (_notLoaded.TryGetValue(manifest.Id, out var failure))
                return new AddOnInfo(manifest, failure.State, failure.Problem, trust, publisher, []);
            return new AddOnInfo(manifest, AddOnState.NotLoaded,
                _policy.SafeMode ? VoxStrings.Get("AddOns.SafeMode") : Unavailable, trust, publisher, []);
        }
    }

    // ---- Loading ----------------------------------------------------------------------------

    /// <summary>Loads every enabled, compatible add-on (at startup; pipeline thread).</summary>
    public void LoadAll()
    {
        if (!CanLoad || !System.IO.Directory.Exists(_directory))
            return;
        foreach (var folder in AddOnFolders())
            LoadFolder(folder, publish: false);
        Publish();
    }

    /// <summary>Loads the add-on <paramref name="id"/> again from its folder: after an update, or when enabled (pipeline thread).</summary>
    public void Reload(string id)
    {
        Unload(id, publish: false);
        if (CanLoad && _directory is not null && System.IO.Directory.Exists(FolderOf(id)))
            LoadFolder(FolderOf(id), publish: false);
        Publish();
    }

    private void LoadFolder(string folder, bool publish)
    {
        var id = Path.GetFileName(folder);
        lock (_lock) _notLoaded.Remove(id);
        AddOnManifest manifest;
        try { manifest = ReadManifest(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _logger.LogWarning(ex, "Add-on folder {Folder} has no valid manifest", id);
            NotLoaded(id, AddOnState.Failed, VoxStrings.Get("AddOns.Damaged"));
            return;
        }
        if (manifest.Id != id)
        {
            NotLoaded(id, AddOnState.Failed, VoxStrings.Get("AddOns.Damaged"));
            return;
        }
        if (AddOnCompatibility.Reason(manifest, VoxApi.Current) is { } incompatible)
        {
            _logger.LogInformation("Add-on {AddOn} {Version} not loaded: made for add-on API {Min}-{Max}", id, manifest.Version,
                manifest.MinApiVersion, manifest.MaxApiVersion ?? manifest.MinApiVersion);
            NotLoaded(id, AddOnState.Incompatible, incompatible);
            return;
        }
        lock (_lock)
        {
            if (_state.Disabled.Contains(id) || _state.TurnedOff.ContainsKey(id) || _loaded.ContainsKey(id) || _labelPacks.ContainsKey(id))
                return;
        }
        if (manifest.IsLabelPack)
        {
            LoadLabelPack(folder, manifest);
            if (publish)
                Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        ILoadedAddOn code;
        try { code = _loader.Load(folder, manifest); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add-on {AddOn} {Version} could not be loaded", id, manifest.Version);
            NotLoaded(id, AddOnState.Failed, ex is AddOnLoadException ? ex.Message : VoxStrings.Get("AddOns.LoadFailed"));
            return;
        }

        var logger = _loggerFor($"AddOn.{id}");
        var guard = new AddOnGuard(id, _logger, OnRepeatedFailure, _time);
        _watchdog.Watch(guard);
        var host = new AddOnHost(id, () => Path.Combine(_directory!, DataFolderName, id), logger,
            new GuardedSpeech(_integration, guard), _integration.ForegroundProcessName,
            work => _integration.Post(() => guard.Run(work, "running work on the pipeline")));
        if (!guard.Run(() => code.Instance.Initialize(host), "starting", AddOnGuard.StartBudget))
        {
            guard.Stop();
            _watchdog.Forget(guard);
            host.Stop();
            TryDispose(code.Instance, guard);
            code.Unload();
            NotLoaded(id, AddOnState.Failed, VoxStrings.Get("AddOns.StartFailed"));
            return;
        }
        host.Seal();
        var loaded = new Loaded(manifest, folder, code, guard, host);
        lock (_lock) _loaded[id] = loaded;
        Register(loaded);
        _logger.LogInformation("Add-on {AddOn} {Version} loaded", id, manifest.Version);
        if (publish)
            Publish();
    }

    /// <summary>Gives custom labels a label pack's labels (its file checked against the manifest again).</summary>
    private void LoadLabelPack(string folder, AddOnManifest manifest)
    {
        IReadOnlyList<Labels.CustomLabel> labels;
        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(folder, Labels.LabelPack.FileName));
            if (bytes.Length > Labels.LabelPack.MaxBytes || !manifest.Files.TryGetValue(Labels.LabelPack.FileName, out var hash)
                || !AddOnPackage.HashMatches(bytes, hash))
                throw new FormatException("The label pack's labels have changed since it was installed.");
            using var reader = new StreamReader(new MemoryStream(bytes));
            labels = Labels.LabelPack.Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _logger.LogWarning(ex, "Label pack {AddOn} could not be read", manifest.Id);
            NotLoaded(manifest.Id, AddOnState.Failed, VoxStrings.Get("AddOns.Damaged"));
            return;
        }
        lock (_lock) _labelPacks[manifest.Id] = manifest;
        _integration.SetLabelPack(manifest.Id, labels);
        _logger.LogInformation("Label pack {AddOn} {Version} in use: {Count} labels", manifest.Id, manifest.Version, labels.Count);
    }

    private void NotLoaded(string id, AddOnState state, string? problem)
    {
        lock (_lock) _notLoaded[id] = (state, problem);
    }

    /// <summary>Gives what an add-on added to the speech engines and braille displays.</summary>
    private void Register(Loaded loaded)
    {
        var selected = _integration.SelectedSpeechEngine();
        foreach (var registration in loaded.Contributions.SpeechEngines)
        {
            var id = registration.Id;
            var guard = loaded.Guard;
            var create = registration.Create;
            var descriptor = new SpeechEngineDescriptor(id, registration.Name ?? id, () =>
            {
                Sdk.ISpeechEngine engine;
                try { engine = create() ?? throw new InvalidOperationException("No engine"); }
                catch (Exception ex)
                {
                    guard.Failed(ex, "starting its speech engine");
                    throw;
                }
                return new AddOnSpeechEngine(engine, guard);
            });
            if (_integration.SpeechEngines?.Add(descriptor) == true)
            {
                loaded.EngineIds.Add(id);
                if (string.Equals(selected, id, StringComparison.OrdinalIgnoreCase))
                    _integration.SpeechEngines.Select(id);
            }
            else
                _logger.LogWarning("Add-on {AddOn}: a speech engine {Engine} exists already; not added", loaded.Manifest.Id, id);
        }
        foreach (var driver in loaded.Contributions.BrailleDrivers)
        {
            var adapter = new AddOnBrailleDriver(driver, loaded.Guard);
            loaded.Drivers.Add(adapter);
            _integration.Braille?.AddDriver(adapter);
        }
    }

    /// <summary>Hands the running add-ons' modules, plug-ins, filters and keys to Vox.</summary>
    private void Publish()
    {
        List<Loaded> running;
        lock (_lock) running = _loaded.Values.OrderBy(l => l.Manifest.Id, StringComparer.Ordinal).ToList();
        _filters = running.SelectMany(l => l.Contributions.AnnouncementFilters.Select(f => new GuardedFilter(f, l.Guard))).ToList();
        _plugins = running.SelectMany(l => l.Contributions.GlobalPlugins.Select(p => new GuardedPlugin(p, l.Guard))).ToList();
        _integration.SetAppModules(running.SelectMany(l => l.Modules).ToList());
        RefreshKeys();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rebuilds the add-on key layer: the active add-on module's commands first, then every add-on's global commands.</summary>
    public void RefreshKeys()
    {
        List<Loaded> running;
        lock (_lock) running = _loaded.Values.OrderBy(l => l.Manifest.Id, StringComparer.Ordinal).ToList();
        var gestures = new List<(KeyGesture, AddOnCommandRef)>();
        if (_integration.ActiveAppModule() is AddOnAppModule active && running.FirstOrDefault(l => l.Modules.Contains(active)) is { } owner)
        {
            foreach (var command in active.Commands)
                gestures.AddRange(Gestures(owner, command, active.Id));
        }
        foreach (var loaded in running)
        {
            foreach (var command in loaded.Contributions.Commands)
                gestures.AddRange(Gestures(loaded, command, null));
        }
        _integration.SetKeyLayer(gestures.Count == 0 ? null : AddOnKeyLayer.Build(gestures));
    }

    private static IEnumerable<(KeyGesture, AddOnCommandRef)> Gestures(Loaded loaded, AddOnCommand command, string? moduleId)
    {
        var reference = new AddOnCommandRef(loaded.Manifest.Id, moduleId is null ? command.Id : $"{moduleId}/{command.Id}",
            command.Name, command.Description ?? string.Empty);
        return loaded.Guard.Call(() => command.Gestures.ToList(), [], "listing a command's keys").Select(g => (g, reference));
    }

    // ---- Unloading ----------------------------------------------------------------------------

    private void Unload(string id, bool publish)
    {
        Loaded? loaded;
        bool labelPack;
        lock (_lock)
        {
            labelPack = _labelPacks.Remove(id);
            if (!_loaded.Remove(id, out loaded))
                loaded = null;
        }
        if (labelPack)
        {
            _integration.SetLabelPack(id, null);
            _logger.LogInformation("Label pack {AddOn} no longer used", id);
            if (publish)
                Changed?.Invoke(this, EventArgs.Empty);
        }
        if (loaded is null)
            return;
        // Out of Vox first, while the add-on still answers (an active module is deactivated, its
        // engine and display closed), then stopped and let go
        Publish();
        // Its background work is told to stop before its engine, display and the add-on are closed
        loaded.Host.Stop();
        foreach (var engine in loaded.EngineIds)
            _integration.SpeechEngines?.Remove(engine);
        foreach (var driver in loaded.Drivers)
        {
            _integration.Braille?.RemoveDriver(driver);
            driver.Dispose();
        }
        TryDispose(loaded.Code.Instance, loaded.Guard);
        loaded.Guard.Stop();
        _watchdog.Forget(loaded.Guard);
        loaded.Code.Unload();
        _logger.LogInformation("Add-on {AddOn} unloaded", id);
        if (publish)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TryDispose(IAddOn instance, AddOnGuard guard)
    {
        if (instance is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch (Exception ex) { _logger.LogError(ex, "Add-on {AddOn} failed to close", guard.AddOnId); }
        }
    }

    /// <summary>Unloads every add-on (Vox is stopping).</summary>
    public void UnloadAll()
    {
        List<string> ids;
        lock (_lock) ids = [.. _loaded.Keys, .. _labelPacks.Keys];
        foreach (var id in ids)
            Unload(id, publish: false);
    }

    // Posted, never run here: the watchdog calls this from its timer while the add-on may still
    // hold the pipeline thread, and TurnOff then runs once that call returns
    private void OnRepeatedFailure(AddOnGuard guard) => _integration.Post(() => TurnOff(guard.AddOnId, guard.HasHung));

    /// <summary>Turns off an add-on that kept failing (or stopped responding), and says so (pipeline thread).</summary>
    public void TurnOff(string id, bool stoppedResponding = false)
    {
        string name;
        lock (_lock)
        {
            if (!_loaded.TryGetValue(id, out var loaded))
                return;
            name = loaded.Manifest.Name;
        }
        var reason = VoxStrings.Format(stoppedResponding ? "AddOns.StoppedResponding" : "AddOns.TurnedOff", name);
        lock (_lock) _state = _state with { TurnedOff = new(_state.TurnedOff) { [id] = reason } };
        SaveState();
        Unload(id, publish: true);
        _integration.Speak(reason, Sdk.SpeechPriority.Normal);
    }

    // ---- The add-ons' calls ---------------------------------------------------------------------

    /// <summary>Runs an add-on command (pipeline thread).</summary>
    public void RunCommand(AddOnCommandRef command)
    {
        Loaded? loaded;
        lock (_lock) _loaded.TryGetValue(command.AddOnId, out loaded);
        if (loaded is null)
            return;
        var slash = command.CommandId.IndexOf('/');
        AddOnCommand? found = slash < 0
            ? loaded.Contributions.Commands.FirstOrDefault(c => c.Id == command.CommandId)
            : loaded.Modules.FirstOrDefault(m => m.Id == command.CommandId[..slash])?.Commands
                .FirstOrDefault(c => c.Id == command.CommandId[(slash + 1)..]);
        if (found is not null)
            loaded.Guard.Run(found.Run, $"running {command.CommandId}");
    }

    /// <summary>False when a global plug-in drops <paramref name="evt"/>.</summary>
    public bool ShouldProcess(ScreenReaderEvent evt)
    {
        var plugins = _plugins;
        if (plugins.Count == 0 || SdkEvents.From(evt) is not { } sdk)
            return true;
        foreach (var (plugin, guard) in plugins)
        {
            if (!guard.Call(() => plugin.ShouldProcess(sdk), true, "filtering an event"))
                return false;
        }
        return true;
    }

    /// <summary>The focus announcement as the global plug-ins have it (null or empty: nothing is said).</summary>
    public string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement)
    {
        var plugins = _plugins;
        if (plugins.Count == 0)
            return announcement;
        var info = SdkEvents.Focus(focus);
        string? text = announcement;
        foreach (var (plugin, guard) in plugins)
        {
            var current = text!;
            text = guard.Call(() => plugin.FormatFocusAnnouncement(info, current), current, "formatting a focus announcement");
            if (string.IsNullOrEmpty(text))
                return text;
        }
        return text;
    }

    /// <summary>Passes a handled event to the global plug-ins.</summary>
    public void OnEvent(ScreenReaderEvent evt)
    {
        var plugins = _plugins;
        if (plugins.Count == 0 || SdkEvents.From(evt) is not { } sdk)
            return;
        foreach (var (plugin, guard) in plugins)
            guard.Run(() => plugin.OnEvent(sdk), "handling an event");
    }

    // ---- Managing ------------------------------------------------------------------------------

    /// <summary>Turns an add-on on (pipeline thread); it loads at once unless in safe mode.</summary>
    public bool Enable(string id)
    {
        if (!CanManage)
            return false;
        lock (_lock)
        {
            var turnedOff = new Dictionary<string, string>(_state.TurnedOff, StringComparer.Ordinal);
            turnedOff.Remove(id);
            _state = new StateFile(_state.Disabled.Where(d => d != id).ToList(), turnedOff);
        }
        SaveState();
        Reload(id);
        return true;
    }

    /// <summary>Turns an add-on off (pipeline thread); its code is let go at once.</summary>
    public bool Disable(string id)
    {
        if (!CanManage)
            return false;
        lock (_lock)
        {
            if (!_state.Disabled.Contains(id))
                _state = _state with { Disabled = [.. _state.Disabled, id] };
        }
        SaveState();
        Unload(id, publish: true);
        return true;
    }

    /// <summary>Unloads and deletes an add-on (pipeline thread). Its data folder is kept.</summary>
    public bool Remove(string id)
    {
        if (!CanManage || !AddOnManifestIdIsSafe(id))
            return false;
        Unload(id, publish: false);
        lock (_fileLock)
        {
            var folder = FolderOf(id);
            if (System.IO.Directory.Exists(folder))
            {
                try { System.IO.Directory.Delete(folder, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not delete add-on {AddOn}", id);
                    return false;
                }
            }
        }
        lock (_lock)
        {
            var turnedOff = new Dictionary<string, string>(_state.TurnedOff, StringComparer.Ordinal);
            turnedOff.Remove(id);
            _state = new StateFile(_state.Disabled.Where(d => d != id).ToList(), turnedOff);
            _notLoaded.Remove(id);
        }
        SaveState();
        Changed?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Add-on {AddOn} removed", id);
        return true;
    }

    /// <summary>
    /// Checks a package before installing it (any thread): its files and signature, whether it works
    /// with this Vox, and what is installed now. Throws <see cref="AddOnPackageException"/>.
    /// <paramref name="catalogueSha256"/> is the checksum Vox's signed catalogue gives for it.
    /// </summary>
    public AddOnInstallCheck CheckPackage(string packagePath, string? catalogueSha256 = null)
    {
        var package = AddOnPackage.Read(packagePath, _trust);
        if (catalogueSha256 is not null)
        {
            if (!string.Equals(package.Sha256, catalogueSha256, StringComparison.OrdinalIgnoreCase))
                throw new AddOnPackageException(VoxStrings.Get("AddOns.CatalogueMismatch"));
            package = package with { Trust = AddOnTrust.Catalogue };
        }
        string? installed = null;
        if (_directory is not null && System.IO.Directory.Exists(FolderOf(package.Manifest.Id)))
        {
            try { installed = ReadManifest(FolderOf(package.Manifest.Id)).Version; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { installed = "?"; }
        }
        return new AddOnInstallCheck(packagePath, package, installed, AddOnCompatibility.Reason(package.Manifest, VoxApi.Current));
    }

    /// <summary>
    /// Puts a checked package's files in place (any thread), replacing an installed version; then
    /// <see cref="Reload"/> on the pipeline thread starts it. An incompatible package is refused
    /// with its reason (<see cref="AddOnPackageException"/>). The add-on's data folder is kept.
    /// </summary>
    public void InstallFiles(AddOnInstallCheck check)
    {
        if (!CanManage)
            throw new InvalidOperationException(Unavailable);
        if (check.Incompatible is { } reason)
            throw new AddOnPackageException(reason);
        var id = check.Manifest.Id;
        lock (_fileLock)
        {
            System.IO.Directory.CreateDirectory(_directory!);
            var staging = Path.Combine(_directory!, $".staging-{Guid.NewGuid():N}");
            AddOnPackage.Extract(check.PackagePath, check.Manifest, staging);
            var target = FolderOf(id);
            string? old = null;
            try
            {
                if (System.IO.Directory.Exists(target))
                {
                    old = Path.Combine(_directory!, $".old-{Guid.NewGuid():N}");
                    // Assemblies are loaded from memory, so the running version's files aren't locked
                    System.IO.Directory.Move(target, old);
                }
                System.IO.Directory.Move(staging, target);
            }
            catch
            {
                if (old is not null && !System.IO.Directory.Exists(target))
                    System.IO.Directory.Move(old, target);
                TryDeleteFolder(staging);
                throw;
            }
            if (old is not null)
                TryDeleteFolder(old);
        }
        // A new version starts afresh (it may fix what turned the old one off)
        lock (_lock)
        {
            var turnedOff = new Dictionary<string, string>(_state.TurnedOff, StringComparer.Ordinal);
            if (turnedOff.Remove(id))
                _state = _state with { TurnedOff = turnedOff };
        }
        SaveState();
        _logger.LogInformation("Add-on {AddOn} {Version} installed ({Trust})", id, check.Manifest.Version, check.Package.Trust);
    }

    // ---- Files ----------------------------------------------------------------------------------

    private IEnumerable<string> AddOnFolders() =>
        System.IO.Directory.EnumerateDirectories(_directory!)
            .Where(f => !Path.GetFileName(f).StartsWith('.') && AddOnManifestIdIsSafe(Path.GetFileName(f)))
            .Order(StringComparer.Ordinal);

    private static bool AddOnManifestIdIsSafe(string id) =>
        new AddOnManifest { Id = id }.Problems().All(p => !p.StartsWith("The id", StringComparison.Ordinal));

    private string FolderOf(string id) => Path.Combine(_directory!, id);

    private static AddOnManifest ReadManifest(string folder) =>
        AddOnManifest.Parse(File.ReadAllText(Path.Combine(folder, AddOnManifest.FileName)));

    private static void TryDeleteFolder(string folder)
    {
        try { System.IO.Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Which add-ons are disabled, and which Vox turned off and why.</summary>
    private sealed record StateFile(List<string> Disabled, Dictionary<string, string> TurnedOff)
    {
        public static StateFile Empty => new([], new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private StateFile ReadState()
    {
        if (_directory is null)
            return StateFile.Empty;
        try
        {
            var path = Path.Combine(_directory, StateFileName);
            if (!File.Exists(path))
                return StateFile.Empty;
            var state = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path));
            return state is null ? StateFile.Empty
                : new StateFile(state.Disabled ?? [], new Dictionary<string, string>(state.TurnedOff ?? [], StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the add-ons' state; all are enabled");
            return StateFile.Empty;
        }
    }

    private void SaveState()
    {
        if (_directory is null || !_policy.AllowSettingsWrites)
            return;
        StateFile state;
        lock (_lock) state = _state;
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, StateFileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the add-ons' state");
        }
    }

    // ---- A loaded add-on -------------------------------------------------------------------------

    private sealed class Loaded
    {
        public Loaded(AddOnManifest manifest, string folder, ILoadedAddOn code, AddOnGuard guard, AddOnHost host)
        {
            Manifest = manifest;
            Folder = folder;
            Code = code;
            Guard = guard;
            Host = host;
            Contributions = host.Contributions;
            Modules = Contributions.AppModules.Select(m => new AddOnAppModule(m, guard)).ToList();
        }

        public AddOnManifest Manifest { get; }
        public string Folder { get; }
        public ILoadedAddOn Code { get; }
        public AddOnGuard Guard { get; }
        public AddOnHost Host { get; }
        public AddOnContributions Contributions { get; }
        public List<AddOnAppModule> Modules { get; }
        public List<string> EngineIds { get; } = [];
        public List<AddOnBrailleDriver> Drivers { get; } = [];

        public IReadOnlyList<AddOnCommandInfo> CommandInfos() =>
            Contributions.Commands.Select(c => new AddOnCommandInfo(c.Name, c.Description ?? string.Empty, c.Gestures))
                .Concat(Modules.SelectMany(m => m.Commands.Select(c =>
                    new AddOnCommandInfo($"{c.Name} ({m.Name})", c.Description ?? string.Empty, c.Gestures))))
                .ToList();
    }

    /// <summary>An add-on's speech, guarded like its other calls into Vox are not: speaking can't fail it.</summary>
    private sealed class GuardedSpeech(AddOnIntegration integration, AddOnGuard guard) : ISpeechOutput
    {
        public void Speak(string text, Sdk.SpeechPriority priority = Sdk.SpeechPriority.Normal)
        {
            if (!guard.IsStopped && !string.IsNullOrWhiteSpace(text))
                integration.Speak(text, priority);
        }

        public void StopSpeaking()
        {
            if (!guard.IsStopped)
                integration.StopSpeaking();
        }
    }
}
