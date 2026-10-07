using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public class SettingsRingTests : IDisposable
{
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private VoxSettings _settings = new() { SpeechRateWpm = 200 };
    private readonly List<VoxSettings> _saved = new();
    private readonly SettingsRing _ring;

    private static readonly SpeechEngineDescriptor[] Engines =
    [
        new("OneCore", "OneCore", () => throw new NotSupportedException()),
        new("SAPI", "SAPI 5", () => throw new NotSupportedException()),
    ];

    public SettingsRingTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var monitor = new Mock<IOptionsMonitor<VoxSettings>>();
        monitor.Setup(m => m.CurrentValue).Returns(() => _settings);
        _ring = new SettingsRing(_engine, Engines, monitor.Object, s => { _settings = s; _saved.Add(s); }, _queue);
    }

    public void Dispose() => _queue.Dispose();

    [Fact]
    public void StartsAtRate_AndMovesThroughTheSettings_WrappingAround()
    {
        Assert.Equal(SettingsRing.Setting.Rate, _ring.Current);
        Assert.Equal("Pitch 50", _ring.Move(+1));
        Assert.Equal("Volume 100", _ring.Move(+1));
        Assert.Equal("Punctuation some", _ring.Move(+1));
        Assert.Equal("Synthesizer OneCore", _ring.Move(+1));
        Assert.Equal("Voice default", _ring.Move(+1));
        Assert.Equal("Synthesizer OneCore", _ring.Move(-1));
    }

    [Fact]
    public void Rate_ChangesInSteps_UpToTheEnginesMaximum()
    {
        Assert.Equal("210", _ring.Change(+1));
        Assert.Equal(210, _settings.SpeechRateWpm);

        _settings = _settings with { SpeechRateWpm = 440 };
        Assert.Equal("450", _ring.Change(+1)); // the engine's maximum
        Assert.Equal("450", _ring.Change(+1));
        Assert.Equal("425", _ring.Change(-1));
    }

    [Fact]
    public void ChangeAtTheEnd_SavesNothing()
    {
        _settings = _settings with { SpeechRateWpm = 150 };

        Assert.Equal("150", _ring.Change(-1));

        Assert.Empty(_saved);
    }

    [Fact]
    public void Volume_NeverGoesSilent()
    {
        _ring.Move(+1);
        _ring.Move(+1);
        _settings = _settings with { SpeechVolume = 10 };

        Assert.Equal("10", _ring.Change(-1));
    }

    [Fact]
    public void Punctuation_StepsThroughTheLevels()
    {
        _ring.Move(+1);
        _ring.Move(+1);
        _ring.Move(+1);

        Assert.Equal("most", _ring.Change(+1));
        Assert.Equal("all", _ring.Change(+1));
        Assert.Equal("all", _ring.Change(+1));
        Assert.Equal(PunctuationLevel.All, _settings.PunctuationLevel);
    }

    [Fact]
    public void Engine_SwitchesAndResetsTheVoice()
    {
        _settings = _settings with { VoiceName = "Microsoft Aria" };
        _ring.Move(-1);
        _ring.Move(-1); // Voice → Synthesizer (backwards from Rate: Voice, then Synthesizer)

        Assert.Equal("SAPI 5", _ring.Change(+1));

        Assert.Equal("SAPI", _settings.SpeechEngine);
        Assert.Null(_settings.VoiceName);
    }

    [Fact]
    public void Voice_StepsThroughTheEnginesVoices()
    {
        _ring.Move(-1);

        Assert.Equal("Test Voice", _ring.Change(+1));
        Assert.Equal("Test Voice", _settings.VoiceName);
    }

    [Fact]
    public async Task Commands_SpeakTheResult()
    {
        Assert.True(_ring.TryHandle(NavigationCommand.SettingsRingIncrease));
        await _engine.WaitForTextAsync("210");
        Assert.True(_ring.TryHandle(NavigationCommand.SettingsRingNext));
        await _engine.WaitForTextAsync("Pitch 50");

        Assert.False(_ring.TryHandle(NavigationCommand.SayAll));
    }

    [Fact]
    public void EngineWithoutPitchOrVolume_LeavesThemOut()
    {
        var engine = new Mock<ISpeechEngine>();
        engine.Setup(e => e.Capabilities).Returns(SpeechCapabilities.None);
        var ring = new SettingsRing(engine.Object, Engines.Take(1).ToList(), Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == _settings),
            _ => { }, _queue);

        Assert.Equal([SettingsRing.Setting.Voice, SettingsRing.Setting.Rate, SettingsRing.Setting.Punctuation], ring.Settings);
    }
}
