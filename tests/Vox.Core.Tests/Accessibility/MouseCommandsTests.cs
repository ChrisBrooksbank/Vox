using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class MouseCommandsTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly Mouse _mouse = new();
    private readonly Focus _focus = new();
    private readonly ObjectNavigationCommands _navigation;
    private readonly MouseCommands _commands;

    private readonly MockNavigatorObject _window = new("Toolbar", "ToolBar",
        new MockNavigatorObject("Bold", "Button") { ClickPoint = (120, 40) },
        new MockNavigatorObject("Hidden", "Button"));

    private sealed class Mouse : IMouseInput
    {
        public List<string> Actions { get; } = new();
        public void MoveTo(int x, int y) => Actions.Add($"move {x},{y}");
        public void Press(MouseButton button) => Actions.Add($"{button} down");
        public void Release(MouseButton button) => Actions.Add($"{button} up");
    }

    private sealed class Focus : INavigatorObjectSource
    {
        public INavigatorObject? Focused { get; set; }
        public INavigatorObject? GetFocused() => Focused;
    }

    public MouseCommandsTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings());
        _navigation = new ObjectNavigationCommands(_uiaThread, _focus, _queue, new AnnouncementBuilder(),
            Mock.Of<IAudioCuePlayer>(), settings, NullLogger<ObjectNavigationCommands>.Instance);
        _commands = new MouseCommands(_uiaThread, _navigation, _mouse, _queue, NullLogger<MouseCommands>.Instance);
    }

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    [Fact]
    public async Task Route_MovesThePointerToTheNavigatorObject()
    {
        _focus.Focused = _window.Find("Bold");

        await _commands.RouteToNavigatorAsync();

        Assert.Equal(["move 120,40"], _mouse.Actions);
    }

    [Fact]
    public async Task Route_ObjectWithNoLocation_SaysSo()
    {
        _focus.Focused = _window.Find("Hidden");

        await _commands.RouteToNavigatorAsync();

        Assert.Empty(_mouse.Actions);
        await _engine.WaitForTextAsync("No location");
    }

    [Fact]
    public async Task Route_NoNavigatorObject_SaysSo()
    {
        await _commands.RouteToNavigatorAsync();

        await _engine.WaitForTextAsync("No navigator object");
    }

    [Fact]
    public async Task LeftAndRightClick_PressAndRelease()
    {
        Assert.True(_commands.TryHandle(NavigationCommand.MouseLeftClick));
        await _engine.WaitForTextAsync("Left click");
        Assert.True(_commands.TryHandle(NavigationCommand.MouseRightClick));
        await _engine.WaitForTextAsync("Right click");

        Assert.Equal(["Left down", "Left up", "Right down", "Right up"], _mouse.Actions);
    }

    [Fact]
    public async Task LeftLock_HoldsTheButtonUntilToggledAgain()
    {
        _commands.ToggleLeftLock();
        Assert.True(_commands.IsLeftLocked);
        await _engine.WaitForTextAsync("Left mouse button locked");

        _commands.ToggleLeftLock();
        Assert.False(_commands.IsLeftLocked);
        await _engine.WaitForTextAsync("Left mouse button unlocked");

        Assert.Equal(["Left down", "Left up"], _mouse.Actions);
    }

    [Fact]
    public void ClickWhileLocked_ReleasesTheLockFirst()
    {
        _commands.ToggleLeftLock();
        _commands.Click(MouseButton.Left);

        Assert.False(_commands.IsLeftLocked);
        Assert.Equal(["Left down", "Left up", "Left down", "Left up"], _mouse.Actions);
    }

    [Fact]
    public void Dispose_ReleasesALockedButton()
    {
        _commands.ToggleLeftLock();
        _commands.Dispose();
        _commands.Dispose();

        Assert.Equal(["Left down", "Left up"], _mouse.Actions);
    }

    [Fact]
    public void OtherCommands_AreNotHandled()
    {
        Assert.False(_commands.TryHandle(NavigationCommand.SayAll));
        Assert.Empty(_mouse.Actions);
    }
}
