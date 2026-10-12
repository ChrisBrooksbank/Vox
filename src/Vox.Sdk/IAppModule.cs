namespace Vox.Sdk;

/// <summary>The application an app module was activated for.</summary>
public sealed record AppInfo(int ProcessId, string ProcessName, nint WindowHandle);

/// <summary>
/// Support for one application, chosen by the foreground process's name. While its application is
/// in the foreground its <see cref="Commands"/>' keys apply, it can change or silence focus
/// announcements and drop events. An add-on's module comes before Vox's own module for the same
/// application. Every member is called on the pipeline thread.
/// </summary>
public interface IAppModule
{
    /// <summary>A short id, unique among app modules ("notepadplusplus"); the DisabledAppModules setting turns it off by this.</summary>
    string Id { get; }

    /// <summary>The application's name as people say it.</summary>
    string Name { get; }

    /// <summary>Process names without ".exe", any case.</summary>
    IReadOnlyCollection<string> ProcessNames { get; }

    /// <summary>Commands (and their keys) that apply only while the module is active.</summary>
    IReadOnlyList<AddOnCommand> Commands { get; }

    void OnActivated(AppInfo app);

    void OnDeactivated();

    /// <summary>What to say for a focus change: <paramref name="announcement"/> unchanged, other text, or null or empty to say nothing.</summary>
    string? FormatFocusAnnouncement(FocusInfo focus, string announcement);

    /// <summary>False drops <paramref name="evt"/> before Vox handles it.</summary>
    bool ShouldProcess(VoxEvent evt);

    /// <summary>Called after Vox has handled <paramref name="evt"/>.</summary>
    void OnEvent(VoxEvent evt);
}

/// <summary>An <see cref="IAppModule"/> that changes nothing: override what you need.</summary>
public abstract class AppModule : IAppModule
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract IReadOnlyCollection<string> ProcessNames { get; }
    public virtual IReadOnlyList<AddOnCommand> Commands => [];

    /// <summary>The application the module is active for, or null.</summary>
    protected AppInfo? App { get; private set; }

    public virtual void OnActivated(AppInfo app) => App = app;
    public virtual void OnDeactivated() => App = null;
    public virtual string? FormatFocusAnnouncement(FocusInfo focus, string announcement) => announcement;
    public virtual bool ShouldProcess(VoxEvent evt) => true;
    public virtual void OnEvent(VoxEvent evt) { }
}

/// <summary>
/// A plug-in active whatever application is in front: it sees every event and focus announcement
/// after the active app module. Every member is called on the pipeline thread.
/// </summary>
public interface IGlobalPlugin
{
    string? FormatFocusAnnouncement(FocusInfo focus, string announcement);
    bool ShouldProcess(VoxEvent evt);
    void OnEvent(VoxEvent evt);
}

/// <summary>An <see cref="IGlobalPlugin"/> that changes nothing: override what you need.</summary>
public abstract class GlobalPlugin : IGlobalPlugin
{
    public virtual string? FormatFocusAnnouncement(FocusInfo focus, string announcement) => announcement;
    public virtual bool ShouldProcess(VoxEvent evt) => true;
    public virtual void OnEvent(VoxEvent evt) { }
}

/// <summary>What an utterance is, for <see cref="IAnnouncementFilter"/>.</summary>
/// <param name="Language">The text's language (BCP 47) when known, else null.</param>
public sealed record AnnouncementInfo(SpeechPriority Priority, string? Language);

/// <summary>
/// Changes text before it is spoken (a pronunciation fix, an abbreviation spelled out). Called on
/// the speech thread for every utterance, so it must be quick, and must never log or keep the text.
/// </summary>
public interface IAnnouncementFilter
{
    /// <summary>The text to speak instead of <paramref name="text"/>, or null to leave it.</summary>
    string? Filter(string text, AnnouncementInfo info);
}
