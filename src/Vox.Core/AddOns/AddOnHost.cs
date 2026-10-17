using Microsoft.Extensions.Logging;
using Vox.Sdk;

namespace Vox.Core.AddOns;

/// <summary>What one add-on added to Vox while it started.</summary>
public sealed class AddOnContributions
{
    public List<AddOnCommand> Commands { get; } = [];
    public List<Sdk.IAppModule> AppModules { get; } = [];
    public List<IGlobalPlugin> GlobalPlugins { get; } = [];
    public List<IAnnouncementFilter> AnnouncementFilters { get; } = [];
    public List<SpeechEngineRegistration> SpeechEngines { get; } = [];
    public List<IBrailleDriver> BrailleDrivers { get; } = [];
    public List<AddOnSetting> Settings { get; } = [];
}

/// <summary>
/// The <see cref="IAddOnHost"/> one add-on is given: it collects what the add-on adds while it
/// starts (then <see cref="Seal"/> closes it) and passes speech, logging and pipeline work on.
/// <see cref="Stop"/> cancels <see cref="Stopping"/> before the add-on is disposed.
/// </summary>
public sealed class AddOnHost(
    string addOnId,
    Func<string> dataDirectory,
    ILogger logger,
    ISpeechOutput speech,
    Func<string?> foregroundProcessName,
    Action<Action> post,
    IAddOnObjectSource? objects = null,
    AddOnIntegration? output = null) : IAddOnHost
{
    private readonly CancellationTokenSource _stopping = new();
    private bool _sealed;

    public AddOnContributions Contributions { get; } = new();

    public string AddOnId => addOnId;
    public Version ApiVersion => VoxApi.Current;
    public ILogger Logger => logger;
    public ISpeechOutput Speech => speech;
    public string? ForegroundProcessName => foregroundProcessName();
    public CancellationToken Stopping => _stopping.Token;

    public string DataDirectory
    {
        get
        {
            var directory = dataDirectory();
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    /// <summary>The add-on's string table (set before it starts).</summary>
    public AddOnStrings Strings { get; init; } = AddOnStrings.Empty;

    public string GetString(string key) => Strings.Lookup(key) ?? key;

    public Task<IVoxObject?> GetFocusedObjectAsync() => ReadObjectAsync(o => o.GetFocused());

    public Task<IVoxObject?> GetNavigatorObjectAsync() => ReadObjectAsync(o => o.GetNavigator());

    private async Task<IVoxObject?> ReadObjectAsync(Func<IAddOnObjectSource, Navigation.INavigatorObject?> find)
    {
        if (objects is null || _stopping.IsCancellationRequested)
            return null;
        try
        {
            return await objects.RunAsync(() => VoxObject.Wrap(find(objects), objects)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Add-on {AddOn} could not read an object", addOnId);
            return null;
        }
    }

    public void PlayEarcon(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !_stopping.IsCancellationRequested)
            output?.PlayEarcon(name);
    }

    public void ShowBrailleMessage(string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && !_stopping.IsCancellationRequested)
            output?.ShowBrailleMessage(text);
    }

    public Task ShowDocumentAsync(string text)
    {
        if (string.IsNullOrEmpty(text) || output is null || _stopping.IsCancellationRequested)
            return Task.CompletedTask;
        return output.ShowDocument(text);
    }

    public bool RunCommand(string commandName)
    {
        if (output is null || _stopping.IsCancellationRequested
            || !Enum.TryParse<Input.NavigationCommand>(commandName, ignoreCase: false, out var command)
            || !Enum.IsDefined(command) || command == Input.NavigationCommand.Quit)
            return false;
        output.RunCommand(command);
        return true;
    }

    public void Seal() => _sealed = true;

    /// <summary>
    /// Cancels <see cref="Stopping"/> (once), running the add-on's callbacks on this thread; one that
    /// throws is logged, the rest still run.
    /// </summary>
    public void Stop()
    {
        if (_stopping.IsCancellationRequested)
            return;
        try { _stopping.Cancel(throwOnFirstException: false); }
        catch (AggregateException ex) { logger.LogError(ex, "Add-on {AddOn} failed stopping its work", addOnId); }
    }

    public void AddCommand(AddOnCommand command)
    {
        Open();
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Name) || command.Run is null)
            throw new ArgumentException("A command needs an id, a name and something to run.", nameof(command));
        if (Contributions.Commands.Any(c => c.Id == command.Id))
            throw new ArgumentException($"There is already a command '{command.Id}'.", nameof(command));
        Contributions.Commands.Add(command);
    }

    public void AddSettings(IReadOnlyList<AddOnSetting> settings)
    {
        Open();
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var setting in settings)
        {
            if (setting is null || string.IsNullOrWhiteSpace(setting.Id) || string.IsNullOrWhiteSpace(setting.Label))
                throw new ArgumentException("A setting needs an id and a label.", nameof(settings));
            if (setting is AddOnChoiceSetting { Choices.Count: 0 })
                throw new ArgumentException($"The setting '{setting.Id}' has no choices.", nameof(settings));
            if (setting is not (AddOnToggleSetting or AddOnNumberSetting or AddOnChoiceSetting))
                throw new ArgumentException($"The setting '{setting.Id}' is not a toggle, number or choice.", nameof(settings));
            if (Contributions.Settings.Any(s => s.Id == setting.Id) || settings.Count(s => s?.Id == setting.Id) > 1)
                throw new ArgumentException($"There is already a setting '{setting.Id}'.", nameof(settings));
        }
        Contributions.Settings.AddRange(settings);
    }

    public string? GetSetting(string id) =>
        output is null ? null : AddOnSettingsModel.Value(output.CurrentSettings(), addOnId, Contributions.Settings, id);

    public void AddAppModule(Sdk.IAppModule module)
    {
        Open();
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.Id) || module.ProcessNames is not { Count: > 0 })
            throw new ArgumentException("An app module needs an id and at least one process name.", nameof(module));
        Contributions.AppModules.Add(module);
    }

    public void AddGlobalPlugin(IGlobalPlugin plugin)
    {
        Open();
        Contributions.GlobalPlugins.Add(plugin ?? throw new ArgumentNullException(nameof(plugin)));
    }

    public void AddAnnouncementFilter(IAnnouncementFilter filter)
    {
        Open();
        Contributions.AnnouncementFilters.Add(filter ?? throw new ArgumentNullException(nameof(filter)));
    }

    public void AddSpeechEngine(SpeechEngineRegistration engine)
    {
        Open();
        ArgumentNullException.ThrowIfNull(engine);
        if (string.IsNullOrWhiteSpace(engine.Id) || engine.Create is null)
            throw new ArgumentException("A speech engine needs an id and a way to start it.", nameof(engine));
        Contributions.SpeechEngines.Add(engine);
    }

    public void AddBrailleDriver(IBrailleDriver driver)
    {
        Open();
        ArgumentNullException.ThrowIfNull(driver);
        Contributions.BrailleDrivers.Add(driver);
    }

    public void RunOnPipeline(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        post(work);
    }

    private void Open()
    {
        if (_sealed)
            throw new InvalidOperationException("Add-ons add to Vox only while they start (in IAddOn.Initialize).");
    }
}
