using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.Buffer;
using Vox.Core.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace Vox.Core.Tests.Diagnostics;

/// <summary>
/// The latency budget, measured in-process: from a key reaching Vox (as the keyboard hook delivers
/// it) to the speech engine being asked to speak, through the real dispatcher, keymap, pipeline,
/// browse-mode controller and speech queue. Keypress-to-speech must stay under 50 ms at the 95th
/// percentile (robustness-secure-desktop.md); the engine's own start-up time comes on top.
/// </summary>
public class KeyToSpeechLatencyTests : IDisposable
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(50);
    private const int VK_DOWN = 0x28;

    private readonly ITestOutputHelper _output;
    private readonly FakeHook _hook = new();
    private readonly TimedEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly EventPipeline _pipeline;
    private readonly KeyInputDispatcher _dispatcher;
    private readonly BrowseModeController _controller;

    private sealed class FakeHook : IKeyboardHook
    {
        public event EventHandler<KeyEvent>? KeyPressed;
        public Func<KeyEvent, KeyDecision>? SuppressionFilter { get; set; }
        public ModifierKey ScreenReaderModifier { get; set; }
        public void Install() { }
        public void Uninstall() { }

        /// <summary>As the real hook: decide at press time, then deliver the key with the decision.</summary>
        public void Press(int vk)
        {
            var down = new KeyEvent { VkCode = vk, IsKeyDown = true, Timestamp = (uint)Environment.TickCount };
            var decision = SuppressionFilter?.Invoke(down) ?? default;
            KeyPressed?.Invoke(this, down with { Decision = decision });
            KeyPressed?.Invoke(this, new KeyEvent { VkCode = vk, IsKeyDown = false, Decision = decision });
        }
    }

    /// <summary>Records when each SpeakAsync call arrives.</summary>
    private sealed class TimedEngine : ISpeechEngine
    {
        private readonly object _lock = new();
        public List<(string Text, long Ticks)> Calls { get; } = new();
        public bool IsSpeaking => false;
        public Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
        {
            lock (_lock) Calls.Add((utterance.Text, Stopwatch.GetTimestamp()));
            return Task.CompletedTask;
        }
        public int CallCount { get { lock (_lock) return Calls.Count; } }
        public long LastCallTicks { get { lock (_lock) return Calls[^1].Ticks; } }
        public void Cancel() { }
        public void SetRate(int wpm) { }
        public void SetVoice(string voiceName) { }
        public IReadOnlyList<string> GetAvailableVoices() => [];
    }

    public KeyToSpeechLatencyTests(ITestOutputHelper output)
    {
        _output = output;
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var audio = Mock.Of<IAudioCuePlayer>();
        _pipeline = new EventPipeline(_queue, audio, NullLogger<EventPipeline>.Instance);
        var navigationManager = new NavigationManager(_pipeline, NullLogger<NavigationManager>.Instance);
        var settings = Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings());
        _controller = new BrowseModeController(_queue, audio, navigationManager, new QuickNavHandler(audio),
            new SayAllController(_queue, NullLogger<SayAllController>.Instance), new AnnouncementBuilder(),
            new TypingEchoHandler(_pipeline, () => TypingEchoMode.None, NullLogger<TypingEchoHandler>.Instance),
            settings, _pipeline, Mock.Of<IBrowseDocumentActions>(), Mock.Of<IElementsListPresenter>(),
            NullLogger<BrowseModeController>.Instance);
        _dispatcher = new KeyInputDispatcher(_hook, KeyMap.LoadBuiltIn(), _pipeline, NullLogger<KeyInputDispatcher>.Instance);

        // Wired as in ScreenReaderService
        _pipeline.NavigationCommandReceived += (_, e) => _controller.HandleCommand(e.Command);
        _pipeline.DocumentChangedProcessed += (_, e) => _controller.HandleDocumentChanged(e);
        _dispatcher.SetMode(InteractionMode.Browse);
        _dispatcher.SetDocumentActive(true);
        _dispatcher.Start();
    }

    public void Dispose()
    {
        _dispatcher.Stop();
        _pipeline.Dispose();
        _queue.Dispose();
    }

    private async Task LoadDocumentAsync(int lines)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        for (int i = 0; i < lines; i++)
            root.AddChild(new MockElement { RuntimeId = [i + 2], ControlType = "Group" }
                .AddChild(new MockElement { RuntimeId = [i + 10_000], Name = $"Line number {i} of the test page" }));
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _controller.DocumentActiveChanged += (_, isActive) => { if (isActive) active.TrySetResult(); };
        _pipeline.Post(new DocumentChangedEvent(DateTimeOffset.UtcNow, new VBufferBuilder().Build(root), null));
        await active.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private async Task<TimeSpan> PressAndTimeAsync(int vk)
    {
        int before = _engine.CallCount;
        long pressed = Stopwatch.GetTimestamp();
        _hook.Press(vk);
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        while (_engine.CallCount == before)
        {
            if (Stopwatch.GetTimestamp() > deadline)
                throw new TimeoutException("Nothing was spoken for the key");
            await Task.Yield();
        }
        return Stopwatch.GetElapsedTime(pressed, _engine.LastCallTicks);
    }

    [Fact]
    public async Task NextLine_KeyToSpeech_P95_WithinBudget()
    {
        await LoadDocumentAsync(lines: 60);
        for (int i = 0; i < 5; i++)
            await PressAndTimeAsync(VK_DOWN); // warm up (JIT)

        var samples = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            samples.Add((await PressAndTimeAsync(VK_DOWN)).TotalMilliseconds);
            await Task.Delay(5);
        }

        samples.Sort();
        double p95 = samples[(int)Math.Ceiling(0.95 * samples.Count) - 1];
        _output.WriteLine($"key to speech: median {samples[samples.Count / 2]:0.0} ms, p95 {p95:0.0} ms");
        Assert.True(p95 <= Budget.TotalMilliseconds, $"p95 {p95:0.0} ms is over the {Budget.TotalMilliseconds} ms budget");
    }
}
