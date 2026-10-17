using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class SpeechOnDemandCommandsTests : IDisposable
{
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly Mock<IAudioCuePlayer> _audio = new();
    private readonly Mock<IOptionsMonitor<VoxSettings>> _monitor = new();
    private VoxSettings _settings = new();
    private readonly SpeechOnDemandCommands _commands;

    public SpeechOnDemandCommandsTests()
    {
        _monitor.Setup(m => m.CurrentValue).Returns(() => _settings);
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _queue.OnDemand.IsOnFunc = () => _settings.SpeechOnDemand;
        _commands = new SpeechOnDemandCommands(_monitor.Object, s => _settings = s, _queue, _audio.Object);
    }

    public void Dispose() => _queue.Dispose();

    [Fact]
    public void OtherCommands_AreNotHandled()
    {
        Assert.False(_commands.TryHandle(NavigationCommand.NextHeading));
        Assert.False(_settings.SpeechOnDemand);
    }

    [Fact]
    public async Task Toggle_TurnsOnWithCueAndSaysIt_EvenThoughTheModeDropsOtherSpeech()
    {
        Assert.True(_commands.TryHandle(NavigationCommand.ToggleSpeechOnDemand));

        Assert.True(_settings.SpeechOnDemand);
        _audio.Verify(a => a.Play("checked"), Times.Once);
        var spoken = await _engine.WaitForAsync(_ => true);
        Assert.Equal(VoxStrings.Get("App.SpeechOnDemandOn"), spoken.Text);
    }

    [Fact]
    public async Task Toggle_TurnsOffAgainAndSaysIt()
    {
        _settings = _settings with { SpeechOnDemand = true };

        _commands.Toggle();

        Assert.False(_settings.SpeechOnDemand);
        _audio.Verify(a => a.Play("not_checked"), Times.Once);
        var spoken = await _engine.WaitForAsync(_ => true);
        Assert.Equal(VoxStrings.Get("App.SpeechOnDemandOff"), spoken.Text);
    }

    [Fact]
    public void Command_IsInCatalogueAndMenuAndDesktopKeymap()
    {
        Assert.True(CommandCatalog.Has(NavigationCommand.ToggleSpeechOnDemand));
        Assert.Contains(NavigationCommand.ToggleSpeechOnDemand, VoxMenu.Items.Select(i => i.Command));
        Assert.Contains(KeyMap.LoadBuiltIn().Bindings, b => b.Command == NavigationCommand.ToggleSpeechOnDemand);
        Assert.False(CommandCatalog.IsOnDemandReport(NavigationCommand.ToggleSpeechOnDemand));
    }
}
