using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Vox.Core.AppModules;
using Vox.Core.AppModules.Explorer;
using Vox.Core.AppModules.Meetings;
using Vox.Core.AppModules.Teams;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.Buffer;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AppModules;

/// <summary>Runs posted pipeline work at once, as the pipeline thread would.</summary>
internal sealed class InlineSink : IEventSink
{
    public void Post(ScreenReaderEvent evt)
    {
        if (evt is PipelineWorkEvent work)
            lock (this) work.Work();
    }
}

public class MeetingControlsTests
{
    [Fact]
    public void Teams_ButtonsGiveTheState()
    {
        var state = MeetingControls.FromButtons(["Chat", "Unmute (Ctrl+Shift+M)", "Turn camera on (Ctrl+Shift+O)", "Lower your hand", "Leave (Ctrl+Shift+H)"]);

        Assert.Equal(new MeetingState(true, true, false, true, null), state);
        Assert.Equal("In a call, microphone muted, camera off, hand raised", MeetingControls.Describe(state));
    }

    [Fact]
    public void Zoom_ButtonsGiveTheState()
    {
        var state = MeetingControls.FromButtons(["Mute audio", "Stop video", "Raise hand", "Stop sharing", "End meeting"]);

        Assert.Equal(new MeetingState(true, false, true, false, true), state);
        Assert.Equal("In a call, microphone on, camera on, sharing your screen", MeetingControls.Describe(state));
    }

    [Fact]
    public void NoCallButtons() =>
        Assert.Equal("Not in a call", MeetingControls.Describe(MeetingControls.FromButtons(["New chat", "Mute all notifications"])));

    [Fact]
    public void Changes_SaysOnlyWhatChanged()
    {
        var before = new MeetingState(true, false, true, false, null);

        Assert.Equal("Microphone muted", MeetingControls.Changes(before, before with { Muted = true }));
        Assert.Equal("Camera off, Hand raised", MeetingControls.Changes(before, before with { CameraOn = false, HandRaised = true }));
        Assert.Equal("Call ended", MeetingControls.Changes(before, MeetingState.None));
        Assert.Null(MeetingControls.Changes(before, before));
    }
}

public class RecentSpeechTests
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Repeat_WithinTheWindow_InsideALongerUtterance()
    {
        var recent = new RecentSpeech(TimeSpan.FromSeconds(2), () => _now);
        recent.Add("Line 5, Column 1");

        Assert.True(recent.IsRepeat("line  5"));
        Assert.False(recent.IsRepeat("Line 6"));
        // An application saying the same twice: the second is a repeat
        Assert.True(recent.IsRepeat("Line 6"));
    }

    [Fact]
    public void ShortText_IsARepeatOnlyWhenSaidAlone()
    {
        var recent = new RecentSpeech(TimeSpan.FromSeconds(2), () => _now);
        recent.Add("Page 1 of 3");

        Assert.False(recent.IsRepeat("1"));
        Assert.True(recent.IsRepeat("1"));
    }

    [Fact]
    public void AfterTheWindow_NotARepeat()
    {
        var recent = new RecentSpeech(TimeSpan.FromSeconds(2), () => _now);
        recent.Add("Saved");
        _now += TimeSpan.FromSeconds(3);

        Assert.False(recent.IsRepeat("Saved"));
        Assert.False(recent.IsRepeat(null));
    }
}

public class ExplorerModuleTests : IDisposable
{
    private sealed class FakeRows : IDetailsRowSource
    {
        public IReadOnlyList<DetailsCell>? Row { get; set; }
        /// <summary>When set, reading the row waits for this (UIA answering late).</summary>
        public Task? Gate { get; set; }

        public async Task<IReadOnlyList<DetailsCell>?> FocusedRowAsync()
        {
            if (Gate is { } gate)
                await gate.ConfigureAwait(false);
            return Row;
        }
    }

    private readonly FakeRows _rows = new();
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _speech;
    private readonly TestOptionsMonitor<VoxSettings> _settings = new(new VoxSettings());
    private readonly ExplorerModule _module;

    private static readonly IReadOnlyList<DetailsCell> Row =
    [
        new("Name", "report.docx"), new("Date modified", "‎03/05/2026 10:12"), new("Type", "Word document"), new("Size", ""),
    ];

    public ExplorerModuleTests()
    {
        _speech = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _module = new ExplorerModule(_rows, _speech, new InlineSink(), _settings);
        _module.OnActivated(new AppActivation(1, "explorer", IntPtr.Zero));
    }

    public void Dispose() => _speech.Dispose();

    private static FocusChangedEvent Item(string name) => new(DateTimeOffset.UtcNow, name, "ListItem", PositionInSet: 3, SizeOfSet: 10);

    [Fact]
    public void Format_NameThenHeadersAndValues()
    {
        Assert.Equal("report.docx, Date modified: 03/05/2026 10:12, Type: Word document, 3 of 10", DetailsRowFormatter.Format(Row, true, 3, 10));
        Assert.Equal("report.docx, 03/05/2026 10:12, Word document", DetailsRowFormatter.Format(Row, false));
    }

    [Fact]
    public void Format_NotADetailsView_IsNull()
    {
        Assert.Null(DetailsRowFormatter.Format([new("", "report.docx")], true));
        Assert.Null(DetailsRowFormatter.Format([new("", "a"), new("", "b")], true));
    }

    [Fact]
    public async Task DetailsItem_IsReadWithItsColumns()
    {
        _rows.Row = Row;

        Assert.Null(_module.FormatFocusAnnouncement(Item("report.docx"), "report.docx, list item"));

        await _engine.WaitForTextAsync("report.docx, Date modified: 03/05/2026 10:12, Type: Word document, 3 of 10");
    }

    [Fact]
    public async Task OtherItems_KeepVoxsAnnouncement()
    {
        _rows.Row = null;

        _module.FormatFocusAnnouncement(Item("Recycle Bin"), "Recycle Bin, list item, 3 of 10");

        await _engine.WaitForTextAsync("Recycle Bin, list item, 3 of 10");
    }

    [Fact]
    public async Task FocusMovingToAnotherControl_DropsTheRowStillBeingRead()
    {
        var gate = new TaskCompletionSource();
        _rows.Gate = gate.Task;
        _rows.Row = Row;
        _module.FormatFocusAnnouncement(Item("report.docx"), "report.docx, list item");

        // F2 before the row was read: the rename box's announcement must not be cut off
        _module.FormatFocusAnnouncement(new FocusChangedEvent(DateTimeOffset.UtcNow, "report.docx", "Edit"), "report.docx, edit");
        gate.SetResult();
        await Task.Delay(100);

        Assert.Empty(_engine.SpokenText);
    }

    [Fact]
    public void Buttons_AreNotChanged() =>
        Assert.Equal("Start, button", _module.FormatFocusAnnouncement(new FocusChangedEvent(DateTimeOffset.UtcNow, "Start", "Button"), "Start, button"));
}

public class TeamsModuleTests : IDisposable
{
    private sealed class FakeButtons : IWindowButtonSource
    {
        public IReadOnlyList<string>? Names { get; set; } = [];
        public Task<IReadOnlyList<string>?> ButtonNamesAsync() => Task.FromResult(Names);
    }

    private readonly FakeButtons _buttons = new();
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _speech;
    private readonly TestOptionsMonitor<VoxSettings> _settings = new(new VoxSettings());
    private VBufferDocument? _document;
    private readonly TeamsModule _module;

    public TeamsModuleTests()
    {
        _speech = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _module = new TeamsModule(_buttons, _speech, new InlineSink(), new RecentSpeech(), _settings, () => _document, TimeSpan.FromMilliseconds(1));
        _module.OnActivated(new AppActivation(1, "ms-teams", IntPtr.Zero));
    }

    public void Dispose() => _speech.Dispose();

    private static VBufferDocument Chat(params string[] messages)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Name = "Chat" };
        var nav = new MockElement { RuntimeId = [2], ControlType = "List", AriaRole = "list", Name = "Chats" };
        nav.AddChild(new MockElement { RuntimeId = [3], ControlType = "ListItem", AriaRole = "listitem", Name = "Ann" });
        root.AddChild(nav);
        var list = new MockElement { RuntimeId = [4], ControlType = "List", AriaRole = "list", Name = "Chat content" };
        int id = 10;
        foreach (var message in messages)
            list.AddChild(new MockElement { RuntimeId = [id++], ControlType = "ListItem", AriaRole = "listitem" }.AddChild(new MockElement { RuntimeId = [id++], Name = message }));
        root.AddChild(list);
        return new VBufferBuilder().Build(root);
    }

    [Theory]
    [InlineData("New message from Ann: lunch?", TeamsMessageAnnouncements.All, true)]
    [InlineData("New message from Ann: lunch?", TeamsMessageAnnouncements.MentionsOnly, false)]
    [InlineData("Ann mentioned you in Project", TeamsMessageAnnouncements.MentionsOnly, true)]
    [InlineData("Ann replied to your message", TeamsMessageAnnouncements.Off, false)]
    [InlineData("Microphone muted", TeamsMessageAnnouncements.Off, true)]
    public void MessageFilter(string text, TeamsMessageAnnouncements setting, bool said) =>
        Assert.Equal(said, TeamsMessages.ShouldAnnounce(text, setting));

    [Fact]
    public void Messages_AreTheChatListsItems()
    {
        Assert.Equal(["Hello", "Lunch at 1?"], TeamsMessages.Messages(Chat("Hello", "Lunch at 1?")));
        Assert.Empty(TeamsMessages.Messages(null));
    }

    [Fact]
    public async Task ReadMessage_CountsBackFromTheLatest()
    {
        _document = Chat("First", "Second", "Third");

        _module.TryHandle(NavigationCommand.TeamsReadMessage1);
        await _engine.WaitForTextAsync("Third");
        _module.TryHandle(NavigationCommand.TeamsReadMessage3);
        await _engine.WaitForTextAsync("First");
        _module.TryHandle(NavigationCommand.TeamsReadMessage5);
        await _engine.WaitForTextAsync("Only 3 messages");
    }

    [Fact]
    public void Filter_DropsMessagesPerTheSetting_AndRepeats()
    {
        _settings.Set(new VoxSettings { TeamsMessageAnnouncements = TeamsMessageAnnouncements.MentionsOnly });

        Assert.False(_module.ShouldProcess(new LiveRegionChangedEvent(DateTimeOffset.UtcNow, "New message from Ann: hi", LiveRegionPoliteness.Polite)));
        Assert.True(_module.ShouldProcess(new NotificationEvent(DateTimeOffset.UtcNow, "a", "Bob mentioned you")));
        // Teams saying it again
        Assert.False(_module.ShouldProcess(new NotificationEvent(DateTimeOffset.UtcNow, "a", "Bob mentioned you")));
        Assert.True(_module.ShouldProcess(new FocusChangedEvent(DateTimeOffset.UtcNow, "x", "Button")));
    }

    [Fact]
    public async Task CallKey_SaysWhatChanged()
    {
        _buttons.Names = ["Mute", "Turn camera off", "Leave"];
        _module.TryHandle(NavigationCommand.TeamsCallState);
        await _engine.WaitForTextAsync("In a call, microphone on, camera on");

        _buttons.Names = ["Unmute", "Turn camera off", "Leave"];
        _module.TryHandle(NavigationCommand.TeamsCallKey);
        await _engine.WaitForTextAsync("Microphone muted");
    }

    [Fact]
    public async Task CallKey_WithoutAnEarlierState_SaysTheWholeState()
    {
        _buttons.Names = ["Unmute", "Leave"];

        _module.TryHandle(NavigationCommand.TeamsCallKey);

        await _engine.WaitForTextAsync("In a call, microphone muted");
    }

    [Fact]
    public async Task CallState_UnreadableWindow()
    {
        _buttons.Names = null;

        _module.TryHandle(NavigationCommand.TeamsCallState);

        await _engine.WaitForTextAsync("The call's controls can't be read");
    }

    [Fact]
    public void CallKeys_PassThroughToTeams()
    {
        var keys = AppModuleKeyMaps.Load("teams").Where(b => b.Command == nameof(NavigationCommand.TeamsCallKey)).ToList();

        Assert.Equal(3, keys.Count);
        Assert.All(keys, k => Assert.True(k.PassThrough));
        Assert.All(AppModuleKeyMaps.Load("zoom").Where(b => b.Command == nameof(NavigationCommand.ZoomCallKey)), k => Assert.True(k.PassThrough));
    }
}

public class QuietAppModuleTests
{
    [Fact]
    public void DropsAnnouncementsOfWhatWasJustSaid()
    {
        var recent = new RecentSpeech();
        var code = QuietAppModule.VisualStudioCode(recent);
        recent.Add("Line 12");

        Assert.Equal(["Code"], code.ProcessNames);
        Assert.False(code.ShouldProcess(new LiveRegionChangedEvent(DateTimeOffset.UtcNow, "Line 12", LiveRegionPoliteness.Assertive)));
        Assert.True(code.ShouldProcess(new LiveRegionChangedEvent(DateTimeOffset.UtcNow, "Build succeeded", LiveRegionPoliteness.Polite)));
        Assert.True(code.ShouldProcess(new FocusChangedEvent(DateTimeOffset.UtcNow, "Line 12", "Edit")));
    }

    [Fact]
    public void EveryModule_HasAUniqueIdAndIsOnTheSettingsPage()
    {
        var recent = new RecentSpeech();
        var ids = new[] { "word", "excel", "outlook", "powerpoint", "explorer", "teams", "zoom",
            QuietAppModule.VisualStudioCode(recent).Id, QuietAppModule.VisualStudio(recent).Id, QuietAppModule.Acrobat(recent).Id };

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(ids.OrderBy(i => i), SettingsPages.AppModules.Select(m => m.Id).OrderBy(i => i));
    }
}
