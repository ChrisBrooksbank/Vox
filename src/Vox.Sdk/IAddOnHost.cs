using Microsoft.Extensions.Logging;

namespace Vox.Sdk;

/// <summary>
/// What Vox gives an add-on. The <c>Add...</c> methods may only be called from
/// <see cref="IAddOn.Initialize"/> (later they throw <see cref="InvalidOperationException"/>);
/// <see cref="Speech"/>, <see cref="Logger"/> and <see cref="RunOnPipeline"/> work at any time.
/// </summary>
/// <remarks>
/// Every call Vox makes into an add-on is guarded: an exception is logged and that call skipped,
/// and an add-on that keeps throwing is turned off (and the user told). An add-on must never block
/// the thread it is called on; slow work goes to a task, and its result comes back through
/// <see cref="RunOnPipeline"/>.
/// </remarks>
public interface IAddOnHost
{
    /// <summary>The add-on's id, from its manifest.</summary>
    string AddOnId { get; }

    /// <summary>The API version of the running Vox (<see cref="VoxApi.Current"/>).</summary>
    Version ApiVersion { get; }

    /// <summary>A folder of the add-on's own for its settings and data (created when first asked for), kept across updates.</summary>
    string DataDirectory { get; }

    /// <summary>Writes to Vox's log, tagged with the add-on's id. Never log text the user typed or field values.</summary>
    ILogger Logger { get; }

    /// <summary>Speaks through Vox's speech queue.</summary>
    ISpeechOutput Speech { get; }

    /// <summary>The foreground application's process name (without ".exe"), or null.</summary>
    string? ForegroundProcessName { get; }

    /// <summary>A command available everywhere, with the keys it is on.</summary>
    void AddCommand(AddOnCommand command);

    /// <summary>Support for one application, active while it is in the foreground (before Vox's own module for it).</summary>
    void AddAppModule(IAppModule module);

    /// <summary>A plug-in that sees every event and focus announcement, whatever application is in front.</summary>
    void AddGlobalPlugin(IGlobalPlugin plugin);

    /// <summary>A step every utterance goes through before it is spoken (after Vox's dictionaries, before symbols).</summary>
    void AddAnnouncementFilter(IAnnouncementFilter filter);

    /// <summary>A speech engine people can choose in the speech settings.</summary>
    void AddSpeechEngine(SpeechEngineRegistration engine);

    /// <summary>A braille display driver, tried after Vox's own when looking for a display.</summary>
    void AddBrailleDriver(IBrailleDriver driver);

    /// <summary>Runs <paramref name="work"/> on Vox's pipeline thread, where app modules and plug-ins are called.</summary>
    void RunOnPipeline(Action work);

    /// <summary>
    /// Cancelled when the add-on is being stopped (disabled, removed, updated, turned off for failing,
    /// or Vox is closing), before it is disposed: pass it to background work so that work ends with
    /// the add-on. Since API 1.1.
    /// </summary>
    CancellationToken Stopping { get; }

    /// <summary>The object with keyboard focus, or null when there is none. Since API 1.1.</summary>
    Task<IVoxObject?> GetFocusedObjectAsync() => Task.FromResult<IVoxObject?>(null);

    /// <summary>The navigator object (the one object navigation is on, which follows focus until moved), or null. Since API 1.1.</summary>
    Task<IVoxObject?> GetNavigatorObjectAsync() => Task.FromResult<IVoxObject?>(null);

    /// <summary>Plays one of the earcons of the current scheme by name (for example "boundary"); an unknown name is ignored. Since API 1.1.</summary>
    void PlayEarcon(string name) { }

    /// <summary>Flashes <paramref name="text"/> on the braille display for the message timeout (nothing without a display). Since API 1.1.</summary>
    void ShowBrailleMessage(string text) { }

    /// <summary>
    /// Shows <paramref name="text"/> as a document the user reads with the review cursor, from its
    /// start, until focus or the caret moves. Since API 1.1.
    /// </summary>
    Task ShowDocumentAsync(string text) => Task.CompletedTask;

    /// <summary>
    /// Runs a Vox command by its name (a <c>NavigationCommand</c> such as "NextHeading"), as if its
    /// key had been pressed. Returns false when there is no such command or it can't be run by an
    /// add-on (Quit). Since API 1.1.
    /// </summary>
    bool RunCommand(string commandName) => false;

    /// <summary>
    /// The add-on's settings, shown as its page in the settings dialog (named for the add-on). Only
    /// from <see cref="IAddOn.Initialize"/>. Since API 1.1.
    /// </summary>
    void AddSettings(IReadOnlyList<AddOnSetting> settings) { }

    /// <summary>
    /// The current value of one of the add-on's settings as text (<c>"true"</c>/<c>"false"</c>, digits,
    /// or the chosen value), with the user's profiles and the organization's policy applied; its
    /// default before anyone changes it, null for an unknown id. Read it when needed, not once. Since API 1.1.
    /// </summary>
    string? GetSetting(string id) => null;
}

/// <summary>How urgently something is said.</summary>
public enum SpeechPriority
{
    /// <summary>Stops what is being said and drops what is waiting (use for the answer to a key press).</summary>
    Interrupt,
    High,
    Normal,
    /// <summary>Said when nothing else is waiting.</summary>
    Low,
}

/// <summary>Vox's speech, for add-ons.</summary>
public interface ISpeechOutput
{
    void Speak(string text, SpeechPriority priority = SpeechPriority.Normal);

    /// <summary>Stops speaking and drops everything waiting to be said.</summary>
    void StopSpeaking();
}
