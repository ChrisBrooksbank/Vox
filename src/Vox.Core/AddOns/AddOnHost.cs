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
}

/// <summary>
/// The <see cref="IAddOnHost"/> one add-on is given: it collects what the add-on adds while it
/// starts (then <see cref="Seal"/> closes it) and passes speech, logging and pipeline work on.
/// <see cref="Stop"/> cancels <see cref="Stopping"/> before the add-on is disposed.
/// </summary>
internal sealed class AddOnHost(
    string addOnId,
    Func<string> dataDirectory,
    ILogger logger,
    ISpeechOutput speech,
    Func<string?> foregroundProcessName,
    Action<Action> post) : IAddOnHost
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
