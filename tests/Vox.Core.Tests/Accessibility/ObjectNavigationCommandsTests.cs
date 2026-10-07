using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class ObjectNavigationCommandsTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly Mock<IAudioCuePlayer> _audio = new();
    private readonly FocusSource _source = new();
    private readonly ObjectNavigationCommands _commands;

    private readonly MockNavigatorObject _window = new("Notepad", "Window",
        new MockNavigatorObject("Text editor", "Edit"),
        new MockNavigatorObject("Status bar", "StatusBar",
            new MockNavigatorObject("Ln 1", "Text")));

    private sealed class FocusSource : INavigatorObjectSource
    {
        public INavigatorObject? Focused { get; set; }
        public INavigatorObject? GetFocused() => Focused;
    }

    public ObjectNavigationCommandsTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings());
        _source.Focused = _window.Find("Text editor");
        _commands = new ObjectNavigationCommands(_uiaThread, _source, _queue, new AnnouncementBuilder(),
            _audio.Object, settings, NullLogger<ObjectNavigationCommands>.Instance);
    }

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    [Fact]
    public async Task Move_StartsFromFocus_AndSpeaksTheNewObject()
    {
        await _commands.MoveAsync(NavigatorMove.Next);

        await _engine.WaitForTextAsync("Status bar, status bar");
    }

    [Fact]
    public async Task Move_ContinuesFromTheNavigatorObject_NotFocus()
    {
        await _commands.MoveAsync(NavigatorMove.Next);
        await _commands.MoveAsync(NavigatorMove.FirstChild);

        await _engine.WaitForTextAsync("Ln 1");
    }

    [Fact]
    public async Task Move_AtBoundary_PlaysBoundaryCue()
    {
        await _commands.MoveAsync(NavigatorMove.Previous);

        _audio.Verify(a => a.Play("boundary"), Times.Once);
        Assert.Empty(_engine.SpokenText);
    }

    [Fact]
    public async Task Move_UnnamedLayoutObject_SaysItsType()
    {
        var pane = new MockNavigatorObject("", "Pane", new MockNavigatorObject("OK"));
        _source.Focused = pane.Find("OK");

        await _commands.MoveAsync(NavigatorMove.Parent);

        await _engine.WaitForTextAsync("pane");
    }

    [Fact]
    public async Task Move_NoFocus_PlaysBoundaryCue()
    {
        _source.Focused = null;

        await _commands.MoveAsync(NavigatorMove.Next);

        _audio.Verify(a => a.Play("boundary"), Times.Once);
    }
}
