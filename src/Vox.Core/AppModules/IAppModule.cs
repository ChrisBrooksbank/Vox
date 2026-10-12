using Vox.Core.Input;
using Vox.Core.Pipeline;

namespace Vox.Core.AppModules;

/// <summary>The application an app module was activated for: its foreground window and process.</summary>
public readonly record struct AppActivation(int ProcessId, string ProcessName, IntPtr WindowHandle);

/// <summary>
/// Support for one application (Word, Excel, Outlook, ...), chosen by the foreground process's
/// name. While its application is in the foreground a module can add keys
/// (<see cref="KeyBindings"/>, a layer over the keymap), run commands, change or silence focus
/// announcements and drop events. Every member is called on the pipeline thread, so a module never
/// needs a lock for its own state; work that waits on another application (its object model, UIA)
/// must not block the pipeline: start it and post the result back (<see cref="PipelineWorkEvent"/>)
/// or speak it from where it finishes.
/// </summary>
public interface IAppModule
{
    /// <summary>A short stable id ("word", "excel"), used by the settings and the module's keymap file.</summary>
    string Id { get; }

    /// <summary>The application's name as people say it ("Microsoft Word").</summary>
    string Name { get; }

    /// <summary>Process names (without ".exe", any case) the module is for: "WINWORD".</summary>
    IReadOnlyCollection<string> ProcessNames { get; }

    /// <summary>
    /// Keys the module adds while active, checked before the keymap's (see <see cref="AppModuleKeyMaps"/>).
    /// Mode "Any" bindings also apply outside web documents, like the keymap's.
    /// </summary>
    IReadOnlyList<KeyBinding> KeyBindings { get; }

    /// <summary>The module's application came to the foreground.</summary>
    void OnActivated(AppActivation activation);

    /// <summary>Another application came to the foreground (or Vox is stopping).</summary>
    void OnDeactivated();

    /// <summary>Runs <paramref name="command"/> if it is one of the module's; returns whether it was.</summary>
    bool TryHandle(NavigationCommand command);

    /// <summary>
    /// What to say for a focus change: <paramref name="announcement"/> (Vox's own) unchanged, other
    /// text, or null or empty to say nothing (the module speaks for itself later).
    /// </summary>
    string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement);

    /// <summary>False drops <paramref name="evt"/> before Vox handles it (e.g. notifications the application voices itself).</summary>
    bool ShouldProcess(ScreenReaderEvent evt);

    /// <summary>Called after Vox has handled <paramref name="evt"/> (focus, caret, property, notification...).</summary>
    void OnEvent(ScreenReaderEvent evt);
}

/// <summary>
/// An <see cref="IAppModule"/> that changes nothing: modules override what they need. Its keys come
/// from the module's keymap file (<see cref="AppModuleKeyMaps.Load"/>) unless overridden.
/// </summary>
public abstract class AppModule : IAppModule
{
    private IReadOnlyList<KeyBinding>? _keyBindings;

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract IReadOnlyCollection<string> ProcessNames { get; }

    public virtual IReadOnlyList<KeyBinding> KeyBindings => _keyBindings ??= AppModuleKeyMaps.Load(Id);

    /// <summary>The application the module is active for, or null while it isn't.</summary>
    protected AppActivation? Activation { get; private set; }

    public bool IsActive => Activation is not null;

    public virtual void OnActivated(AppActivation activation) => Activation = activation;

    public virtual void OnDeactivated() => Activation = null;

    public virtual bool TryHandle(NavigationCommand command) => false;

    public virtual string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement) => announcement;

    public virtual bool ShouldProcess(ScreenReaderEvent evt) => true;

    public virtual void OnEvent(ScreenReaderEvent evt)
    {
    }
}
