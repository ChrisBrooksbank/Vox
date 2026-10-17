using Vox.Core.Accessibility;
using Vox.Core.AppModules.Meetings;
using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.Localization;
using Xunit;

namespace Vox.Core.Tests.AppModules;

public class MicrophoneCommandsTests
{
    private sealed class FakeMicrophone : IMicrophone
    {
        public bool? Muted = false;
        public bool CanSet = true;
        public bool? IsMuted() => Muted;
        public bool SetMuted(bool muted)
        {
            if (!CanSet) return false;
            Muted = muted;
            return true;
        }
    }

    private sealed class FakeWindow : IWindowButtonPresser
    {
        public string[] Buttons = [];
        public string? Pressed;
        public Task<bool> PressButtonAsync(Func<string, bool> match)
        {
            Pressed = Buttons.FirstOrDefault(match);
            return Task.FromResult(Pressed is not null);
        }
    }

    private readonly FakeMicrophone _mic = new();
    private readonly FakeWindow _window = new();
    private readonly List<string> _said = [];
    private readonly List<string> _activated = [];
    private string? _process;
    private string? _site;
    private VBufferDocument? _document;

    private MicrophoneCommands Create() => new(_mic, _window, () => _process, () => _document, () => _site,
        node => { _activated.Add(node.Name); return Task.FromResult(true); }, _said.Add);

    [Theory]
    [InlineData("Mute", true)]
    [InlineData("Unmute (Ctrl+Shift+M)", false)]
    [InlineData("Turn off microphone (ctrl + d)", true)]
    [InlineData("Turn on microphone", false)]
    [InlineData("Mute all", null)]
    [InlineData("Share", null)]
    public void PressingMutes_ReadsTheButtonName(string name, bool? expected) =>
        Assert.Equal(expected, MeetingControls.PressingMutes(name));

    [Fact]
    public void OtherCommands_AreNotHandled() => Assert.False(Create().TryHandle(NavigationCommand.NextHeading));

    [Fact]
    public async Task InTeams_PressesTheApplicationsButton()
    {
        _process = "ms-teams";
        _window.Buttons = ["Leave", "Mute (Ctrl+Shift+M)"];
        await Create().ToggleAsync();
        Assert.Equal("Mute (Ctrl+Shift+M)", _window.Pressed);
        Assert.Equal(VoxStrings.Get("Meeting.MicrophoneMuted"), _said[^1]);
        Assert.False(_mic.Muted);
    }

    [Fact]
    public async Task InZoom_UnmuteSaysMicrophoneOn()
    {
        _process = "Zoom";
        _window.Buttons = ["Unmute"];
        await Create().ToggleAsync();
        Assert.Equal(VoxStrings.Get("Meeting.MicrophoneOn"), _said[^1]);
    }

    [Fact]
    public async Task InTeamsWithoutAMuteButton_TogglesTheDevice()
    {
        _process = "Teams";
        _window.Buttons = ["Leave"];
        await Create().ToggleAsync();
        Assert.True(_mic.Muted);
        Assert.Equal(VoxStrings.Get("Meeting.MicrophoneMuted"), _said[^1]);
    }

    [Fact]
    public async Task InAMeetPage_ActivatesThePagesButton()
    {
        _process = "msedge";
        _site = "https://meet.google.com";
        _document = Page(("Turn off microphone (ctrl + d)", "Button"));
        await Create().ToggleAsync();
        Assert.Equal(["Turn off microphone (ctrl + d)"], _activated);
        Assert.Equal(VoxStrings.Get("Meeting.MicrophoneMuted"), _said[^1]);
        Assert.False(_mic.Muted);
    }

    [Fact]
    public async Task OnAnotherSite_TogglesTheDevice()
    {
        _process = "msedge";
        _site = "https://example.com";
        _document = Page(("Mute", "Button"));
        _mic.Muted = true;
        await Create().ToggleAsync();
        Assert.Empty(_activated);
        Assert.False(_mic.Muted);
        Assert.Equal(VoxStrings.Get("Meeting.MicrophoneOn"), _said[^1]);
    }

    [Fact]
    public async Task WhenTheDeviceCannotBeChanged_SaysSo()
    {
        _mic.CanSet = false;
        await Create().ToggleAsync();
        Assert.Equal(VoxStrings.Get("Meeting.MicrophoneFailed"), _said[^1]);
    }

    [Fact]
    public async Task WithNoMicrophone_SaysSo()
    {
        _mic.Muted = null;
        await Create().ToggleAsync();
        Assert.Equal(VoxStrings.Get("Meeting.MicrophoneFailed"), _said[^1]);
    }

    private static VBufferDocument Page(params (string Name, string Type)[] buttons)
    {
        var root = new VBufferNode { ControlType = "Document", Name = "Call" };
        var children = buttons.Select(b => new VBufferNode { ControlType = b.Type, Name = b.Name }).ToList();
        root.Children.AddRange(children);
        return new VBufferDocument("Call", root, [root, .. children]);
    }
}
