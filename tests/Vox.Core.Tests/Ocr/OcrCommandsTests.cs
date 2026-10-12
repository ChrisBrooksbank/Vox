using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Ocr;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Ocr;

public class OcrCommandsTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly FakeOcrEngine _ocr = new();
    private readonly FakeCapture _capture = new();
    private readonly FakeTargets _targets = new();
    private readonly Mouse _mouse = new();
    private readonly ReviewCommands _review;
    private readonly OcrCommands _commands;
    private VoxSettings _settings = new();

    private readonly MockNavigatorObject _toolbar = new("Toolbar", "ToolBar") { Text = "Toolbar" };

    private sealed class FakeOcrEngine : IOcrEngine
    {
        public bool IsAvailable { get; set; } = true;
        public OcrResult Result { get; set; } = OcrResult.Empty;
        public List<(CapturedImage Image, string? Language)> Calls { get; } = new();
        /// <summary>When set, recognition waits for it.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task<OcrResult> RecognizeAsync(CapturedImage image, string? language, CancellationToken cancellationToken = default)
        {
            lock (Calls) Calls.Add((image, language));
            if (Gate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);
            return Result;
        }
    }

    private sealed class FakeCapture : IScreenCapture
    {
        public bool IsAvailable { get; set; } = true;
        public List<ScreenRect> Captured { get; } = new();

        public CapturedImage? Capture(ScreenRect bounds)
        {
            if (!IsAvailable)
                throw new InvalidOperationException("secure");
            lock (Captured) Captured.Add(bounds);
            return new CapturedImage(bounds.Width, bounds.Height, new byte[bounds.Width * bounds.Height * 4], bounds);
        }
    }

    private sealed class FakeTargets : IOcrTargetSource
    {
        public ScreenRect? Navigator { get; set; } = new ScreenRect(100, 200, 300, 40);
        public ScreenRect? Window { get; set; } = new ScreenRect(0, 0, 800, 600);
        public ScreenRect? Element { get; set; } = new ScreenRect(50, 60, 70, 80);
        public VBufferNode? AskedFor { get; private set; }

        public Task<ScreenRect?> NavigatorBoundsAsync() => Task.FromResult(Navigator);
        public Task<ScreenRect?> WindowBoundsAsync() => Task.FromResult(Window);

        public Task<ScreenRect?> ElementBoundsAsync(VBufferNode node)
        {
            AskedFor = node;
            return Task.FromResult(Element);
        }
    }

    private sealed class Mouse : IMouseInput
    {
        public List<string> Actions { get; } = new();
        public void MoveTo(int x, int y) => Actions.Add($"move {x},{y}");
        public void Press(MouseButton button) => Actions.Add($"{button} down");
        public void Release(MouseButton button) => Actions.Add($"{button} up");
    }

    private sealed class Focus(INavigatorObject focused) : INavigatorObjectSource
    {
        public INavigatorObject? GetFocused() => focused;
    }

    private sealed class NoText : IReviewTextSource
    {
        public IntPtr ForegroundWindow => IntPtr.Zero;
        public ITextDocument? GetFocusedText() => null;
        public ITextDocument? GetWindowText(IntPtr window) => null;
    }

    public OcrCommandsTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = new Mock<IOptionsMonitor<VoxSettings>>();
        settings.Setup(m => m.CurrentValue).Returns(() => _settings);
        var audio = Mock.Of<IAudioCuePlayer>();
        var navigation = new ObjectNavigationCommands(_uiaThread, new Focus(_toolbar), _queue,
            new AnnouncementBuilder(), audio, settings.Object, NullLogger<ObjectNavigationCommands>.Instance);
        _review = new ReviewCommands(_uiaThread, navigation, new NoText(), _queue, audio, settings.Object,
            NullLogger<ReviewCommands>.Instance);
        _commands = new OcrCommands(_ocr, _capture, _targets, _review, _mouse, _queue, settings.Object,
            NullLogger<OcrCommands>.Instance);
        _ocr.Result = OcrDocumentTests.Result("File Edit View", "Save changes?");
    }

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    private async Task<string> Run(Func<Task> command)
    {
        _engine.Clear();
        await command();
        return (await _engine.WaitForAsync(s => s.Text != "Recognizing")).Text;
    }

    [Fact]
    public async Task Recognize_SaysTheText_AndReviewsIt()
    {
        Assert.Equal("File Edit View\nSave changes?", await Run(() => _commands.RecognizeAsync(wholeWindow: false)));

        Assert.Equal([new ScreenRect(100, 200, 300, 40)], _capture.Captured);
        Assert.Equal("File Edit View", await Run(() => _review.ReadAsync(TextUnit.Line)));
        Assert.Equal("Save changes?", await Run(() => _review.MoveAsync(TextUnit.Line, 1)));
    }

    [Fact]
    public async Task RecognizeWindow_CapturesTheForegroundWindow()
    {
        Assert.True(_commands.TryHandle(NavigationCommand.RecognizeWindow));
        await _engine.WaitForTextAsync("File Edit View\nSave changes?");

        Assert.Equal([new ScreenRect(0, 0, 800, 600)], _capture.Captured);
    }

    [Fact]
    public async Task Browsing_RecognisesWhatTheBrowseCursorIsOn()
    {
        var graphic = new VBufferNode { Name = "Chart", ControlType = "Image" };
        _commands.BrowseTarget = () => graphic;

        await Run(() => _commands.RecognizeAsync(wholeWindow: false));

        Assert.Same(graphic, _targets.AskedFor);
        Assert.Equal([new ScreenRect(50, 60, 70, 80)], _capture.Captured);
    }

    [Fact]
    public async Task Language_ComesFromTheSettings()
    {
        _settings = new VoxSettings { Language = "de", OcrLanguage = "fr-FR" };
        await Run(() => _commands.RecognizeAsync(wholeWindow: false));
        _settings = new VoxSettings { Language = "de" };
        await Run(() => _commands.RecognizeAsync(wholeWindow: false));

        Assert.Equal(["fr-FR", "de"], _ocr.Calls.Select(c => c.Language));
    }

    [Fact]
    public async Task SecureScreen_CapturesNothing()
    {
        _capture.IsAvailable = false;

        Assert.Equal("Not available on this screen", await Run(() => _commands.RecognizeAsync(wholeWindow: false)));
        Assert.Empty(_ocr.Calls);
    }

    [Fact]
    public async Task NoRecognitionLanguage_SaysHowToGetOne()
    {
        _ocr.IsAvailable = false;

        Assert.StartsWith("Text recognition is not available", await Run(() => _commands.RecognizeAsync(wholeWindow: false)));
        Assert.Empty(_capture.Captured);
    }

    [Fact]
    public async Task NoLocation_SaysSo()
    {
        _targets.Navigator = null;

        Assert.Equal("No location", await Run(() => _commands.RecognizeAsync(wholeWindow: false)));
        Assert.Empty(_ocr.Calls);
    }

    [Fact]
    public async Task NothingRecognised_SaysSo_AndReviewStaysOnTheObject()
    {
        _ocr.Result = OcrResult.Empty;

        Assert.Equal("No text recognized", await Run(() => _commands.RecognizeAsync(wholeWindow: false)));
        Assert.Equal("Toolbar", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task ClickWord_ClicksTheMiddleOfTheWordAtTheReviewCursor()
    {
        await Run(() => _commands.RecognizeAsync(wholeWindow: false));
        await Run(() => _review.MoveAsync(TextUnit.Word, 1));

        Assert.Equal("Click Edit", await Run(() => _commands.ClickWordAsync()));
        // Edit: 50 to 90 across, 0 to 16 down (OcrDocumentTests.Result)
        Assert.Equal(["move 70,8", "Left down", "Left up"], _mouse.Actions);
    }

    [Fact]
    public async Task ClickWord_OutsideRecognisedText_ClicksNothing()
    {
        Assert.Equal("No recognized text", await Run(() => _commands.ClickWordAsync()));
        Assert.Empty(_mouse.Actions);
    }

    [Fact]
    public async Task FocusMoving_LeavesTheRecognisedText()
    {
        await Run(() => _commands.RecognizeAsync(wholeWindow: false));

        _review.HandleFocusChanged();

        Assert.Equal("Toolbar", await Run(() => _review.ReadAsync(TextUnit.Line)));
        Assert.Equal("No recognized text", await Run(() => _commands.ClickWordAsync()));
    }

    [Fact]
    public async Task SwitchingReviewMode_LeavesTheRecognisedText()
    {
        await Run(() => _commands.RecognizeAsync(wholeWindow: false));

        _review.SwitchMode(+1);
        _review.SwitchMode(-1);
        await _engine.WaitForTextAsync("Object review");

        Assert.Equal("Toolbar", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task ANewerRecognition_ReplacesOneStillRunning()
    {
        _ocr.Gate = new TaskCompletionSource();
        var first = _commands.RecognizeAsync(wholeWindow: false);
        await WaitUntil(() => _ocr.Calls.Count == 1);

        _ocr.Gate = null;
        _ocr.Result = OcrDocumentTests.Result("Second");
        await Run(() => _commands.RecognizeAsync(wholeWindow: false));
        await first;

        Assert.DoesNotContain(_engine.SpokenText, t => t.StartsWith("File"));
        Assert.Equal("Second", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task AFailedRecognition_SaysSo()
    {
        var failing = new Mock<IOcrEngine>();
        failing.Setup(e => e.IsAvailable).Returns(true);
        failing.Setup(e => e.RecognizeAsync(It.IsAny<CapturedImage>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broken"));
        var commands = new OcrCommands(failing.Object, _capture, _targets, _review, _mouse, _queue,
            Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings()), NullLogger<OcrCommands>.Instance);

        Assert.Equal("Could not recognize text", await Run(() => commands.RecognizeAsync(wholeWindow: false)));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
