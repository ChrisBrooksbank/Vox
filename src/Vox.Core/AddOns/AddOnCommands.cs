using System.Text;
using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Sdk;
using SpeechPriority = Vox.Core.Speech.SpeechPriority;

namespace Vox.Core.AddOns;

/// <summary>What the add-ons and store dialogs do; every result is a sentence to say and show.</summary>
public interface IAddOnActions
{
    bool IsSafeMode { get; }

    /// <summary>Null when add-ons can be managed here, else why not.</summary>
    string? Unavailable { get; }

    IReadOnlyList<AddOnInfo> List();

    /// <summary>The add-on as the dialog describes it: version, author, publisher, state, commands and keys.</summary>
    string Details(AddOnInfo addOn);

    Task<string> SetEnabledAsync(string id, bool enabled);
    Task<string> RemoveAsync(string id);

    /// <summary>Checks the package, asks before installing it, installs and starts it.</summary>
    Task<string> InstallFromFileAsync(string path);

    /// <summary>The catalogue's add-ons; throws <see cref="AddOnStoreException"/> with a sentence to say.</summary>
    Task<IReadOnlyList<StoreItem>> BrowseStoreAsync();

    Task<string> InstallFromStoreAsync(StoreItem item);

    /// <summary>Installs every update the catalogue has for installed add-ons.</summary>
    Task<string> UpdateAllAsync(IReadOnlyList<StoreItem> items);
}

/// <summary>Shows the add-ons dialog (installed add-ons) over <see cref="IAddOnActions"/>.</summary>
public interface IAddOnsPresenter
{
    Task ShowAsync(IAddOnActions actions);
}

/// <summary>Shows the add-on store dialog over <see cref="IAddOnActions"/>.</summary>
public interface IAddOnStorePresenter
{
    Task ShowAsync(IAddOnActions actions);
}

/// <summary>
/// The Vox menu's "Manage add-ons", "Get add-ons" and "Restart with add-ons disabled", and the
/// add-on commands' keys. Pipeline thread for <see cref="TryHandle"/> and <see cref="Run"/>; the
/// dialogs' actions hop to the pipeline thread for anything that loads or unloads an add-on.
/// </summary>
public sealed class AddOnCommands : IAddOnActions
{
    private readonly AddOnManager _manager;
    private readonly AddOnStore _store;
    private readonly IAddOnsPresenter _addOns;
    private readonly IAddOnStorePresenter _storeDialog;
    private readonly IConfirmPresenter _confirm;
    private readonly OwnDialogRunner _dialogs;
    private readonly IEventSink _pipeline;
    private readonly SpeechQueue _speech;
    private readonly RestartRequest _restart;
    private readonly Action _stop;
    private readonly ILogger<AddOnCommands> _logger;

    public AddOnCommands(AddOnManager manager, AddOnStore store, IAddOnsPresenter addOns, IAddOnStorePresenter storeDialog,
        IConfirmPresenter confirm, OwnDialogRunner dialogs, IEventSink pipeline, SpeechQueue speech, RestartRequest restart,
        Action stop, ILogger<AddOnCommands> logger)
    {
        _manager = manager;
        _store = store;
        _addOns = addOns;
        _storeDialog = storeDialog;
        _confirm = confirm;
        _dialogs = dialogs;
        _pipeline = pipeline;
        _speech = speech;
        _restart = restart;
        _stop = stop;
        _logger = logger;
    }

    public bool IsSafeMode => _manager.IsSafeMode;
    public string? Unavailable => _manager.Unavailable;

    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.ManageAddOns:
                _ = ShowAsync(() => _addOns.ShowAsync(this));
                return true;
            case NavigationCommand.GetAddOns:
                _ = ShowAsync(() => _storeDialog.ShowAsync(this));
                return true;
            case NavigationCommand.RestartWithoutAddOns:
                Restart();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Runs an add-on command whose key was pressed.</summary>
    public void Run(AddOnCommandEvent e) => _manager.RunCommand(e.Command);

    private async Task ShowAsync(Func<Task> show)
    {
        if (_manager.Unavailable is { } why)
        {
            Say(why);
            return;
        }
        try
        {
            await _dialogs.RunAsync(async () =>
            {
                await show().ConfigureAwait(false);
                return true;
            }, false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add-ons dialog failed");
        }
    }

    /// <summary>Restarts with add-ons disabled, or from safe mode with them enabled.</summary>
    private void Restart()
    {
        if (_manager.Unavailable is { } why)
        {
            Say(why);
            return;
        }
        var safe = !_manager.IsSafeMode;
        _restart.Request(safe);
        _ = SayThenStopAsync(VoxStrings.Get(safe ? "AddOns.RestartingSafe" : "AddOns.RestartingNormal"));
    }

    private async Task SayThenStopAsync(string text)
    {
        try
        {
            await _speech.EnqueueAndWaitAsync(new Utterance(text, SpeechPriority.Interrupt)).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
        }
        _stop();
    }

    // ---- IAddOnActions ---------------------------------------------------------------------------

    public IReadOnlyList<AddOnInfo> List() => _manager.List();

    public string Details(AddOnInfo addOn) => Describe(addOn);

    /// <summary>What the add-ons dialog shows about <paramref name="addOn"/>.</summary>
    public static string Describe(AddOnInfo addOn)
    {
        var m = addOn.Manifest;
        var text = new StringBuilder();
        text.AppendLine(VoxStrings.Format("AddOns.Details.Title", m.Name, m.Version));
        text.AppendLine(VoxStrings.Format("AddOns.Details.Author", m.Author));
        text.AppendLine(VoxStrings.Format("AddOns.Details.State", StateName(addOn.State)));
        if (addOn.Problem is { } problem)
            text.AppendLine(problem);
        text.AppendLine(TrustText(addOn.Trust, addOn.Publisher));
        if (m.IsLabelPack)
            text.AppendLine(VoxStrings.Get("Store.Details.LabelPack"));
        if (!string.IsNullOrWhiteSpace(m.Description))
            text.AppendLine(m.Description);
        if (m.Url is { } url)
            text.AppendLine(url);
        if (addOn.Commands.Count > 0)
        {
            text.AppendLine(VoxStrings.Get("AddOns.Details.Commands"));
            foreach (var command in addOn.Commands)
            {
                var keys = command.Gestures.Count == 0 ? VoxStrings.Get("AddOns.Details.NoKey")
                    : string.Join(", ", command.Gestures.Select(g => InputHelp.KeyName((KeyModifiers)(int)g.Modifiers, g.VirtualKey)));
                text.AppendLine(VoxStrings.Format("AddOns.Details.Command", command.Name, keys, command.Description));
            }
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>A line of the add-ons list: name, version and state.</summary>
    public static string ListLine(AddOnInfo addOn) =>
        VoxStrings.Format("AddOns.ListLine", addOn.Manifest.Name, addOn.Manifest.Version, StateName(addOn.State));

    public static string StateName(AddOnState state) => VoxStrings.Get($"AddOns.State.{state}");

    public static string TrustText(AddOnTrust trust, string? publisher) => trust switch
    {
        AddOnTrust.Catalogue => VoxStrings.Get("AddOns.Trust.Catalogue"),
        AddOnTrust.TrustedPublisher => VoxStrings.Format("AddOns.Trust.TrustedPublisher", AddOnTrustStore.Short(publisher ?? string.Empty)),
        AddOnTrust.UnknownPublisher => VoxStrings.Format("AddOns.Trust.UnknownPublisher", AddOnTrustStore.Short(publisher ?? string.Empty)),
        _ => VoxStrings.Get("AddOns.Trust.Unsigned"),
    };

    public async Task<string> SetEnabledAsync(string id, bool enabled)
    {
        var name = NameOf(id);
        var done = await OnPipelineAsync(() => enabled ? _manager.Enable(id) : _manager.Disable(id)).ConfigureAwait(false);
        var message = !done ? _manager.Unavailable ?? VoxStrings.Format("AddOns.NotChanged", name)
            : enabled
                ? VoxStrings.Format(_manager.IsSafeMode ? "AddOns.EnabledInSafeMode" : "AddOns.Enabled", name)
                : VoxStrings.Format("AddOns.Disabled", name);
        Say(message);
        return message;
    }

    public async Task<string> RemoveAsync(string id)
    {
        var name = NameOf(id);
        bool sure = await _confirm.AskAsync(VoxStrings.Get("AddOns.RemoveTitle"), VoxStrings.Format("AddOns.RemoveQuestion", name)).ConfigureAwait(false);
        if (!sure)
            return string.Empty;
        var removed = await OnPipelineAsync(() => _manager.Remove(id)).ConfigureAwait(false);
        var message = VoxStrings.Format(removed ? "AddOns.Removed" : "AddOns.NotRemoved", name);
        Say(message);
        return message;
    }

    public async Task<string> InstallFromFileAsync(string path)
    {
        AddOnInstallCheck check;
        try
        {
            check = await Task.Run(() => _manager.CheckPackage(path)).ConfigureAwait(false);
        }
        catch (AddOnPackageException ex)
        {
            _logger.LogWarning(ex, "Add-on package refused");
            return Said(VoxStrings.Format("AddOns.Refused", ex.Message));
        }
        if (check.Incompatible is { } reason)
            return Said(reason);
        var question = VoxStrings.Format(check.IsUpdate ? "AddOns.UpdateQuestion" : "AddOns.InstallQuestion",
            check.Manifest.Name, check.Manifest.Version, check.Manifest.Author, check.Installed ?? string.Empty)
            + " " + TrustText(check.Package.Trust, check.Package.Publisher);
        if (check.Package.Trust is AddOnTrust.Unsigned or AddOnTrust.UnknownPublisher)
            question += " " + VoxStrings.Get("AddOns.TrustWarning");
        if (!await _confirm.AskAsync(VoxStrings.Get("AddOns.InstallTitle"), question).ConfigureAwait(false))
            return string.Empty;
        return await InstallAsync(check).ConfigureAwait(false);
    }

    private async Task<string> InstallAsync(AddOnInstallCheck check)
    {
        try
        {
            await Task.Run(() => _manager.InstallFiles(check)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AddOnPackageException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Add-on {AddOn} couldn't be installed", check.Manifest.Id);
            return Said(VoxStrings.Format("AddOns.InstallFailed", check.Manifest.Name, ex.Message));
        }
        await OnPipelineAsync(() =>
        {
            _manager.Reload(check.Manifest.Id);
            return true;
        }).ConfigureAwait(false);
        var state = _manager.List().FirstOrDefault(a => a.Manifest.Id == check.Manifest.Id);
        var message = VoxStrings.Format(check.IsUpdate ? "AddOns.Updated" : "AddOns.Installed", check.Manifest.Name, check.Manifest.Version);
        if (state is { State: not AddOnState.Running })
            message += " " + (state.Problem ?? StateName(state.State));
        return Said(message);
    }

    public async Task<IReadOnlyList<StoreItem>> BrowseStoreAsync()
    {
        switch (_store.Availability)
        {
            case StoreAvailability.NoKey: throw new AddOnStoreException(VoxStrings.Get("Store.NoKey"));
            case StoreAvailability.NetworkOff: throw new AddOnStoreException(VoxStrings.Get("Store.NetworkOff"));
            case StoreAvailability.AddOnsOff: throw new AddOnStoreException(_manager.Unavailable ?? VoxStrings.Get("AddOns.OffByPolicy"));
        }
        Say(VoxStrings.Get("Store.Fetching"));
        IReadOnlyList<CatalogueEntry> entries;
        try
        {
            entries = await _store.FetchAsync().ConfigureAwait(false);
        }
        catch (AddOnStoreException ex)
        {
            _logger.LogInformation(ex, "Add-on store unavailable");
            throw new AddOnStoreException(VoxStrings.Get("Store.Failed"), ex);
        }
        return AddOnCatalogue.Browse(entries, _manager.List(), VoxApi.Current);
    }

    public async Task<string> InstallFromStoreAsync(StoreItem item)
    {
        if (item.Incompatible is { } reason)
            return Said(reason);
        Say(VoxStrings.Format("Store.Downloading", item.Entry.Name));
        string path;
        try
        {
            path = await _store.DownloadAsync(item.Entry).ConfigureAwait(false);
        }
        catch (AddOnStoreException ex)
        {
            return Said(VoxStrings.Format("AddOns.InstallFailed", item.Entry.Name, ex.Message));
        }
        try
        {
            AddOnInstallCheck check;
            try
            {
                // Trusted because its checksum is the signed catalogue's
                check = await Task.Run(() => _manager.CheckPackage(path, item.Entry.Sha256)).ConfigureAwait(false);
            }
            catch (AddOnPackageException ex)
            {
                _logger.LogWarning(ex, "Add-on {AddOn} from the store refused", item.Entry.Id);
                return Said(VoxStrings.Format("AddOns.Refused", ex.Message));
            }
            if (check.Manifest.Id != item.Entry.Id || check.Manifest.Version != item.Entry.Version)
                return Said(VoxStrings.Format("AddOns.Refused", VoxStrings.Get("AddOns.CatalogueMismatch")));
            return await InstallAsync(check).ConfigureAwait(false);
        }
        finally
        {
            AddOnStore.DeleteDownload(path);
        }
    }

    public async Task<string> UpdateAllAsync(IReadOnlyList<StoreItem> items)
    {
        var updates = items.Where(i => i.UpdateAvailable).ToList();
        if (updates.Count == 0)
            return Said(VoxStrings.Get("Store.NoUpdates"));
        int updated = 0;
        foreach (var item in updates)
        {
            await InstallFromStoreAsync(item).ConfigureAwait(false);
            if (_manager.List().Any(a => a.Manifest.Id == item.Entry.Id && a.Manifest.Version == item.Entry.Version))
                updated++;
            else
                _logger.LogInformation("Add-on {AddOn} not updated", item.Entry.Id);
        }
        return Said(VoxStrings.Format("Store.Updated", updated, updates.Count));
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private string NameOf(string id) => _manager.List().FirstOrDefault(a => a.Manifest.Id == id)?.Manifest.Name ?? id;

    /// <summary>Runs <paramref name="work"/> on the pipeline thread (where add-ons load and unload) and returns its result.</summary>
    private Task<bool> OnPipelineAsync(Func<bool> work)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pipeline.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () =>
        {
            try { done.TrySetResult(work()); }
            catch (Exception ex) { done.TrySetException(ex); }
        }));
        return done.Task;
    }

    private string Said(string message)
    {
        Say(message);
        return message;
    }

    private void Say(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            _speech.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
    }
}
