using Vox.Core.Accessibility;
using Vox.Core.AppModules.Meetings;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.AppModules;

/// <summary>
/// An application that voices things itself (VS Code's alerts in screen reader mode, Visual
/// Studio's and Acrobat's notifications): its live region and notification announcements that
/// repeat what Vox has just said, or that it has just said itself, are dropped.
/// </summary>
public sealed class QuietAppModule : AppModule
{
    private readonly RecentSpeech _recentSpeech;

    public QuietAppModule(string id, string name, IReadOnlyCollection<string> processNames, RecentSpeech recentSpeech)
    {
        Id = id;
        Name = name;
        ProcessNames = processNames;
        _recentSpeech = recentSpeech;
    }

    public override string Id { get; }
    public override string Name { get; }
    public override IReadOnlyCollection<string> ProcessNames { get; }

    public override bool ShouldProcess(ScreenReaderEvent evt) => evt switch
    {
        LiveRegionChangedEvent live => !_recentSpeech.IsRepeat(live.Text),
        NotificationEvent notification => !_recentSpeech.IsRepeat(notification.NotificationText),
        _ => true,
    };

    /// <summary>Visual Studio Code: its editor is read in focus mode; its alerts aren't said twice.</summary>
    public static QuietAppModule VisualStudioCode(RecentSpeech recentSpeech) => new("vscode", "Visual Studio Code", ["Code"], recentSpeech);

    public static QuietAppModule VisualStudio(RecentSpeech recentSpeech) => new("visualstudio", "Visual Studio", ["devenv"], recentSpeech);

    public static QuietAppModule Acrobat(RecentSpeech recentSpeech) => new("acrobat", "Adobe Acrobat", ["Acrobat", "AcroRd32"], recentSpeech);
}

/// <summary>Zoom: the call's state, said on command and after Zoom's own mute, video and hand keys.</summary>
public sealed class ZoomModule : MeetingAppModule
{
    public ZoomModule(IWindowButtonSource buttons, SpeechQueue speech, IEventSink events, RecentSpeech recentSpeech, TimeSpan? stateCheckDelay = null)
        : base(buttons, speech, events, recentSpeech, stateCheckDelay)
    {
    }

    public override string Id => "zoom";
    public override string Name => "Zoom";
    public override IReadOnlyCollection<string> ProcessNames { get; } = ["Zoom"];

    protected override NavigationCommand StateCommand => NavigationCommand.ZoomCallState;
    protected override NavigationCommand CallKeyCommand => NavigationCommand.ZoomCallKey;
}
