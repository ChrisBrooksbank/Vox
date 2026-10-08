using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class SpeechEngineRegistryTests
{
    private sealed class FakeEngine(string name) : ISpeechEngine, IDisposable
    {
        public string Name { get; } = name;
        public List<string> Spoken { get; } = new();
        public bool FailToSpeak { get; set; }
        public int? Rate { get; private set; }
        public int Pitch { get; private set; } = ISpeechEngine.DefaultPitch;
        public int Volume { get; private set; } = 100;
        public string? Voice { get; private set; }
        public int Cancels { get; private set; }
        public bool Disposed { get; private set; }

        public bool IsSpeaking => false;

        public Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
        {
            if (FailToSpeak)
                throw new InvalidOperationException("audio device lost");
            Spoken.Add(utterance.Text);
            return Task.CompletedTask;
        }

        public void Cancel() => Cancels++;
        public void SetRate(int wpm) => Rate = wpm;
        public void SetPitch(int pitch) => Pitch = pitch;
        public void SetVolume(int volume) => Volume = volume;
        public void SetVoice(string voiceName) => Voice = voiceName;
        public IReadOnlyList<string> GetAvailableVoices() => [Name + " voice"];
        public void Dispose() => Disposed = true;
    }

    private readonly Dictionary<string, List<FakeEngine>> _started = new();
    private readonly HashSet<string> _failToStart = new();

    private SpeechEngineDescriptor Engine(string id) => new(id, id, () =>
    {
        if (_failToStart.Contains(id))
            throw new InvalidOperationException($"{id} is not installed");
        var engine = new FakeEngine(id);
        if (!_started.TryGetValue(id, out var list))
            _started[id] = list = new();
        list.Add(engine);
        return engine;
    });

    private SpeechEngineRegistry Create() =>
        new([Engine("OneCore"), Engine("Other"), Engine("SAPI")], NullLogger<SpeechEngineRegistry>.Instance);

    private FakeEngine Started(string id) => _started[id].Last();

    [Fact]
    public async Task PrefersTheFirstEngine()
    {
        using var registry = Create();

        await registry.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal));

        Assert.Equal("OneCore", registry.CurrentId);
        Assert.Equal(["Hello"], Started("OneCore").Spoken);
        Assert.False(_started.ContainsKey("SAPI"));
    }

    [Fact]
    public void EngineThatFailsToStart_FallsBackToTheNextOne()
    {
        _failToStart.Add("OneCore");
        using var registry = Create();

        Assert.Equal("Other", registry.Select(null));

        _failToStart.Add("Other");
        using var another = Create();
        Assert.Equal("SAPI", another.Select("Other"));
    }

    [Fact]
    public void NoEngineStarts_Throws()
    {
        _failToStart.UnionWith(["OneCore", "Other", "SAPI"]);
        using var registry = Create();

        Assert.Throws<InvalidOperationException>(() => registry.Select(null));
    }

    [Fact]
    public async Task Select_SwitchesAtRuntime_KeepingRatePitchVolumeAndVoice()
    {
        using var registry = Create();
        registry.SetRate(300);
        registry.SetPitch(70);
        registry.SetVolume(60);
        registry.SetVoice("Hazel");
        var oneCore = Started("OneCore");
        string? changedTo = null;
        registry.EngineChanged += (_, id) => changedTo = id;

        Assert.Equal("SAPI", registry.Select("sapi"));
        await registry.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal));

        var sapi = Started("SAPI");
        Assert.Equal("SAPI", changedTo);
        Assert.Equal((300, 70, 60, "Hazel"), (sapi.Rate, sapi.Pitch, sapi.Volume, sapi.Voice));
        Assert.Equal(["Hello"], sapi.Spoken);
        Assert.True(oneCore.Disposed);
        Assert.Equal(1, oneCore.Cancels);
        Assert.Equal(["SAPI voice"], registry.GetAvailableVoices());
    }

    [Fact]
    public void SelectingTheEngineInUse_DoesNotRestartIt()
    {
        using var registry = Create();
        registry.Select("OneCore");

        registry.Select("OneCore");
        registry.Select(null);

        Assert.Single(_started["OneCore"]);
    }

    [Fact]
    public void UnknownEngine_SelectsThePreferredOne()
    {
        using var registry = Create();

        Assert.Equal("OneCore", registry.Select("eSpeak"));
    }

    [Fact]
    public async Task EngineFailingWhileSpeaking_IsReplaced_AndTheUtteranceIsSaid()
    {
        using var registry = Create();
        registry.Select(null);
        Started("OneCore").FailToSpeak = true;

        await registry.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal));

        Assert.Equal("Other", registry.CurrentId);
        Assert.Equal(["Hello"], Started("Other").Spoken);
    }

    [Fact]
    public async Task LastEngineFailingWhileSpeaking_Throws()
    {
        using var registry = Create();
        registry.Select("SAPI");
        Started("SAPI").FailToSpeak = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal)));
        Assert.Equal("SAPI", registry.CurrentId);
    }

    [Fact]
    public void Cancel_GoesToTheCurrentEngine()
    {
        using var registry = Create();
        registry.Select(null);

        registry.Cancel();

        Assert.Equal(1, Started("OneCore").Cancels);
    }
}
