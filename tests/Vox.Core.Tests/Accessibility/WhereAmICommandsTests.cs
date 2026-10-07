using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class WhereAmICommandsTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly WhereAmICommands _commands;

    private sealed class Foreground : IForegroundWindow
    {
        public ForegroundWindowInfo? Get() => new(1, "Untitled - Notepad", 10);
    }

    public WhereAmICommandsTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings());
        _commands = new WhereAmICommands(_uiaThread, new UIAProvider(_uiaThread, NullLogger<UIAProvider>.Instance),
            new Foreground(), _queue, new AnnouncementBuilder(), settings, NullLogger<WhereAmICommands>.Instance)
        {
            Now = () => new DateTime(2026, 10, 7, 9, 5, 0),
            Power = () => new PowerInfo(true, 72, false, false),
        };
    }

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    [Fact]
    public async Task SayTitle()
    {
        Assert.True(_commands.TryHandle(NavigationCommand.SayTitle));
        await _engine.WaitForTextAsync("Untitled - Notepad");
    }

    [Fact]
    public async Task SayFocus_RepeatsTheFocusedElement()
    {
        _commands.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Save", "Button"));

        _commands.TryHandle(NavigationCommand.SayFocus);

        await _engine.WaitForTextAsync("Save, button");
    }

    [Fact]
    public async Task SayTime_TwiceQuickly_SaysTheDate()
    {
        _commands.TryHandle(NavigationCommand.SayTime);
        await _engine.WaitForAsync(s => s.Text.Contains("9:05") || s.Text.Contains("09:05"));
        _commands.TryHandle(NavigationCommand.SayTime);
        await _engine.WaitForAsync(s => s.Text.Contains("2026"));
    }

    [Fact]
    public async Task SayBattery()
    {
        _commands.TryHandle(NavigationCommand.SayBattery);
        await _engine.WaitForTextAsync("72 percent, on battery");
    }

    [Fact]
    public void OtherCommands_AreNotHandled()
    {
        Assert.False(_commands.TryHandle(NavigationCommand.NextHeading));
    }
}
