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
