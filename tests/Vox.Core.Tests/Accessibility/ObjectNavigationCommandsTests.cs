using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
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

public class ObjectNavigationCommandHandlingTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly ActionableObject _button = new("Print", canFocus: false, canActivate: true);
    private readonly ActionableObject _edit = new("Name", canFocus: true, canActivate: false);
    private readonly MockNavigatorObject _window;
    private readonly ObjectNavigationCommands _commands;
    private INavigatorObject? _focused;

    /// <summary>An object that records focus and activation requests.</summary>
    private sealed class ActionableObject(string name, bool canFocus, bool canActivate) : INavigatorObject
    {
        public INavigatorObject? Parent { get; set; }
        public INavigatorObject? Next { get; set; }
        public INavigatorObject? Previous { get; set; }
        public int FocusRequests { get; private set; }
        public int Activations { get; private set; }

        public INavigatorObject? GetParent() => Parent;
        public INavigatorObject? GetFirstChild() => null;
        public INavigatorObject? GetLastChild() => null;
        public INavigatorObject? GetNextSibling() => Next;
        public INavigatorObject? GetPreviousSibling() => Previous;
        public FocusChangedEvent Describe() => new(DateTimeOffset.UtcNow, name, "Button");
        public bool SetFocus() { FocusRequests++; return canFocus; }
        public bool Activate() { Activations++; return canActivate; }
    }

    private sealed class Source(Func<INavigatorObject?> focused) : INavigatorObjectSource
    {
        public INavigatorObject? GetFocused() => focused();
    }

    public ObjectNavigationCommandHandlingTests()
    {
        _window = new MockNavigatorObject("Window", "Window");
        _edit.Parent = _window;
        _button.Parent = _window;
        _edit.Next = _button;
        _button.Previous = _edit;
        _focused = _edit;
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings());
        _commands = new ObjectNavigationCommands(_uiaThread, new Source(() => _focused), _queue, new AnnouncementBuilder(),
            Mock.Of<IAudioCuePlayer>(), settings, NullLogger<ObjectNavigationCommands>.Instance);
    }

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    [Theory]
    [InlineData(NavigationCommand.NavigatorParent)]
    [InlineData(NavigationCommand.NavigatorFirstChild)]
    [InlineData(NavigationCommand.NavigatorPrevious)]
    [InlineData(NavigationCommand.NavigatorNext)]
    [InlineData(NavigationCommand.ReportNavigator)]
    [InlineData(NavigationCommand.NavigatorToFocus)]
    [InlineData(NavigationCommand.FocusToNavigator)]
    [InlineData(NavigationCommand.ActivateNavigator)]
    public void ObjectNavigationCommands_AreHandled(NavigationCommand command) =>
        Assert.True(_commands.TryHandle(command));

    [Fact]
    public void OtherCommands_AreNotHandled() =>
        Assert.False(_commands.TryHandle(NavigationCommand.SayAll));

    [Fact]
    public async Task Report_SaysTheNavigatorObject()
    {
        await _commands.MoveAsync(NavigatorMove.Next);
        _engine.Clear();

        await _commands.ReportAsync();

        await _engine.WaitForTextAsync("Print, button");
    }

    [Fact]
    public async Task NavigatorToFocus_ReturnsToTheFocusedObject()
    {
        await _commands.MoveAsync(NavigatorMove.Next);

        await _commands.NavigatorToFocusAsync();

        await _engine.WaitForTextAsync("Name, button");
    }

    [Fact]
    public async Task NavigatorToFocus_FollowsTheCurrentFocus()
    {
        await _commands.ReportAsync();
        _focused = _button;

        await _commands.NavigatorToFocusAsync();

        await _engine.WaitForTextAsync("Print, button");
    }

    [Fact]
    public async Task FocusToNavigator_FocusesTheObject()
    {
        await _commands.FocusToNavigatorAsync();

        Assert.Equal(1, _edit.FocusRequests);
        Assert.Empty(_engine.SpokenText);
    }

    [Fact]
    public async Task FocusToNavigator_NotFocusable_SaysSo()
    {
        await _commands.MoveAsync(NavigatorMove.Next);

        await _commands.FocusToNavigatorAsync();

        await _engine.WaitForTextAsync("Not focusable");
    }

    [Fact]
    public async Task Activate_RunsTheDefaultAction()
    {
        await _commands.MoveAsync(NavigatorMove.Next);

        await _commands.ActivateAsync();

        Assert.Equal(1, _button.Activations);
    }

    [Fact]
    public async Task Activate_NoAction_SaysSo()
    {
        await _commands.ActivateAsync();

        await _engine.WaitForTextAsync("No action");
    }

    [Fact]
    public async Task Report_NoFocus_SaysNoNavigatorObject()
    {
        _focused = null;

        await _commands.ReportAsync();

        await _engine.WaitForTextAsync("No navigator object");
    }
}
