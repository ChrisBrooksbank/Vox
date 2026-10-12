using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Ai;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Navigation;
using Vox.Core.Ocr;
using Vox.Core.Speech;
using Vox.Core.Tests.Buffer;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Ai;

public class AiCommandsTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly FakeDescriber _local = new("llava", isLocal: true) { IsAvailable = false };
    private readonly FakeDescriber _cloud = new("Claude", isLocal: false);
    private readonly FakeConsentPrompt _consent = new();
    private readonly FakeCapture _capture = new();
    private readonly FakeTargets _targets = new();
    private readonly FakeContext _context = new();
    private readonly FakeTextPrompt _textPrompt = new();
    private readonly FakeKeyStore _keys = new();
    private readonly List<string> _cues = new();
    private readonly ReviewCommands _review;
    private readonly AiAssistant _assistant;
    private VoxSettings _settings = new() { AiCloudProvider = AiCloudProvider.Claude };
    private VoxSettings? _saved;
    private RunPolicy _policy = RunPolicy.Normal;

    private sealed class FakeCapture : IScreenCapture
    {
        public bool IsAvailable { get; set; } = true;
        public List<ScreenRect> Captured { get; } = new();

        public CapturedImage? Capture(ScreenRect bounds)
        {
            lock (Captured) Captured.Add(bounds);
            return new CapturedImage(bounds.Width, bounds.Height, new byte[bounds.Width * bounds.Height * 4], bounds);
        }
    }

    private sealed class FakeTargets : IOcrTargetSource
    {
        public ScreenRect? Navigator { get; set; } = new ScreenRect(100, 200, 30, 40);
        public ScreenRect? Window { get; set; } = new ScreenRect(0, 0, 80, 60);
        public VBufferNode? AskedFor { get; private set; }

        public Task<ScreenRect?> NavigatorBoundsAsync() => Task.FromResult(Navigator);
        public Task<ScreenRect?> WindowBoundsAsync() => Task.FromResult(Window);

        public Task<ScreenRect?> ElementBoundsAsync(VBufferNode node)
        {
            AskedFor = node;
            return Task.FromResult<ScreenRect?>(new ScreenRect(5, 6, 7, 8));
        }
    }

    private sealed class FakeContext : IAiContextSource
    {
        public AiTarget Navigator { get; set; } = new(false, "notepad");
        public AiTarget Window { get; set; } = new(false, "notepad");
        public string? Text { get; set; } = "Untitled - Notepad\nFile menu\nSave button";

        public Task<AiTarget> NavigatorTargetAsync() => Task.FromResult(Navigator);
        public Task<AiTarget> WindowTargetAsync() => Task.FromResult(Window);
        public Task<string?> WindowTextAsync() => Task.FromResult(Text);
    }

    private sealed class FakeTextPrompt : ITextPrompt
    {
        public string? Answer { get; set; } = "How do I save?";
        public List<(string Title, bool Secret, IReadOnlyList<string> History)> Asked { get; } = new();

        public Task<string?> AskAsync(string title, string label, bool secret = false, IReadOnlyList<string>? history = null)
        {
            Asked.Add((title, secret, history ?? []));
            return Task.FromResult(Answer);
        }
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

    public AiCommandsTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = AiTestSupport.Settings(() => _settings);
        var audio = new Mock<IAudioCuePlayer>();
        audio.Setup(a => a.Play(It.IsAny<string>())).Callback<string>(cue => { lock (_cues) _cues.Add(cue); });
        var navigation = new ObjectNavigationCommands(_uiaThread, new Focus(new MockNavigatorObject("Picture", "Image")), _queue,
            new AnnouncementBuilder(), audio.Object, settings, NullLogger<ObjectNavigationCommands>.Instance);
        _review = new ReviewCommands(_uiaThread, navigation, new NoText(), _queue, audio.Object, settings,
            NullLogger<ReviewCommands>.Instance);
        _assistant = new AiAssistant(_local, _cloud, new AiConsent(RunPolicy.Normal, settings), _consent, new DescriptionCache(),
            NullLogger<AiAssistant>.Instance);
        _audio = audio.Object;
    }

    private readonly IAudioCuePlayer _audio;

    private AiCommands Commands(AiAssistant? assistant = null) => new(assistant ?? _assistant, _capture, _targets, _context, _review,
        _textPrompt, null, _keys, _policy, _audio, _queue, AiTestSupport.Settings(() => _settings),
        NullLogger<AiCommands>.Instance, s => _saved = _settings = s, progressInterval: TimeSpan.FromMilliseconds(20),
        dialogSettleTime: TimeSpan.Zero);

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    private static readonly HashSet<string> Progress = ["Describing image", "Describing the page", "Thinking", "Summarizing"];

    private async Task<string> Run(Func<Task> command)
    {
        _engine.Clear();
        await command();
        return (await _engine.WaitForAsync(s => !Progress.Contains(s.Text))).Text;
    }

    [Fact]
    public async Task Describe_SaysTheDescription_AndReviewsIt()
    {
        _cloud.Answer = "A sunset over the sea.\nTwo boats in the distance.";
        var commands = Commands();

        Assert.Equal(_cloud.Answer, await Run(commands.DescribeAsync));

        var request = Assert.Single(_cloud.Requests);
        Assert.Equal(AiTask.DescribeImage, request.Task);
        Assert.Equal(new ScreenRect(100, 200, 30, 40), request.Image!.Bounds);
        Assert.Equal("A sunset over the sea.", await Run(() => _review.ReadAsync(TextUnit.Line)));
        Assert.Equal("Two boats in the distance.", await Run(() => _review.MoveAsync(TextUnit.Line, 1)));
    }

    [Fact]
    public async Task Browsing_DescribesTheGraphicAtTheCursor()
    {
        var graphic = new VBufferNode { Name = "Chart", ControlType = "Image", Parent = new VBufferNode() };
        var commands = Commands();
        commands.BrowseTarget = () => graphic;

        await Run(commands.DescribeAsync);

        Assert.Same(graphic, _targets.AskedFor);
        Assert.Equal(new ScreenRect(5, 6, 7, 8), Assert.Single(_cloud.Requests).Image!.Bounds);
    }

    [Fact]
    public async Task APasswordField_IsNeverSent()
    {
        _settings = _settings with { AiCloudConsent = AiConsentMode.Allow };
        _context.Navigator = new AiTarget(IsPassword: true);

        Assert.Equal("Password fields are never sent", await Run(Commands().DescribeAsync));
        Assert.Empty(_cloud.Requests);
    }

    [Fact]
    public async Task SecureScreens_DescribeNothing()
    {
        var assistant = new AiAssistant(_local, _cloud, new AiConsent(RunPolicy.Secure, AiTestSupport.Settings(() => _settings)),
            _consent, new DescriptionCache(), NullLogger<AiAssistant>.Instance);

        Assert.Equal("Not available on this screen", await Run(Commands(assistant).DescribeAsync));
        Assert.Empty(_capture.Captured);
        Assert.Empty(_consent.Questions);
    }

    [Fact]
    public async Task NothingSetUp_SaysHowToSetItUp()
    {
        _cloud.IsAvailable = false;

        Assert.StartsWith("No AI is set up", await Run(Commands().DescribeAsync));
        Assert.Empty(_capture.Captured);
    }

    [Fact]
    public async Task WhileWaiting_TheProgressCuePlays_AndPressingAgainCancels()
    {
        _settings = _settings with { AiCloudConsent = AiConsentMode.Allow };
        _cloud.Gate = new TaskCompletionSource();
        var commands = Commands();

        _engine.Clear();
        var describing = commands.DescribeAsync();
        await _engine.WaitForTextAsync("Describing image");
        await WaitUntil(() => { lock (_cues) return _cues.Count(c => c == "progress") >= 2; });
        Assert.True(commands.IsBusy);

        Assert.True(commands.TryHandle(NavigationCommand.DescribeObject));
        await _engine.WaitForTextAsync("Cancelled");
        await describing;

        Assert.False(commands.IsBusy);
        int cues;
        lock (_cues) cues = _cues.Count;
        await Task.Delay(100);
        lock (_cues) Assert.Equal(cues, _cues.Count);
        Assert.DoesNotContain(_engine.SpokenText, t => t.Contains("bicycle"));
    }

    [Fact]
    public async Task StopSpeech_CancelsQuietly()
    {
        _settings = _settings with { AiCloudConsent = AiConsentMode.Allow };
        _cloud.Gate = new TaskCompletionSource();
        var commands = Commands();

        var describing = commands.DescribeAsync();
        await WaitUntil(() => _cloud.Requests.Count == 1);
        Assert.True(commands.Cancel());
        await describing;

        Assert.False(commands.IsBusy);
        Assert.DoesNotContain("Cancelled", _engine.SpokenText);
    }

    [Fact]
    public async Task AskAboutScreen_SendsTheQuestion_TheWindowAndItsText()
    {
        _cloud.Answer = "Press Ctrl+S, or Alt, F, S.";
        var commands = Commands();

        Assert.Equal("Press Ctrl+S, or Alt, F, S.", await Run(commands.AskAboutScreenAsync));

        var request = Assert.Single(_cloud.Requests);
        Assert.Equal(AiTask.ExplainScreen, request.Task);
        Assert.Equal("How do I save?", request.Question);
        Assert.Contains("Save button", request.Context);
        Assert.Equal(new ScreenRect(0, 0, 80, 60), request.Image!.Bounds);
        Assert.Equal("Press Ctrl+S, or Alt, F, S.", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task AskAboutScreen_OffersEarlierQuestions()
    {
        var commands = Commands();
        await Run(commands.AskAboutScreenAsync);
        _textPrompt.Answer = "What is selected?";
        await Run(commands.AskAboutScreenAsync);

        Assert.Equal(["How do I save?"], _textPrompt.Asked[1].History);
    }

    [Fact]
    public async Task AskAboutScreen_InAPrivateApp_SaysNoBeforeAsking()
    {
        _context.Window = new AiTarget(ProcessName: "KeePass");

        _engine.Clear();
        await Commands().AskAboutScreenAsync();
        await _engine.WaitForTextAsync("This application is private: its windows are never sent");

        Assert.Empty(_textPrompt.Asked);
        Assert.Empty(_cloud.Requests);
    }

    [Fact]
    public async Task AskAboutScreen_Cancelled_DoesNothing()
    {
        _textPrompt.Answer = null;

        await Commands().AskAboutScreenAsync();

        Assert.Empty(_capture.Captured);
        Assert.Empty(_cloud.Requests);
    }

    private static VBufferDocument Page()
    {
        int id = 9000;
        var root = new MockElement { RuntimeId = [id++], ControlType = "Document", Name = "Gardening tips" };
        root.AddChild(new MockElement { RuntimeId = [id++], Name = "Home", ControlType = "Hyperlink" });
        var main = new MockElement { RuntimeId = [id++], ControlType = "Group", AriaRole = "main" };
        main.AddChild(new MockElement { RuntimeId = [id++], Name = "Growing tomatoes", ControlType = "Text", HeadingLevel = 1 });
        main.AddChild(new MockElement { RuntimeId = [id++], Name = "Tomatoes need sun, water and patience. Plant them after the last frost." });
        root.AddChild(main);
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public async Task SummarizePage_SendsThePageText()
    {
        _cloud.Answer = "How to grow tomatoes.";
        var commands = Commands();
        commands.BrowseDocument = Page;

        Assert.Equal("How to grow tomatoes.", await Run(commands.SummarizePageAsync));

        var request = Assert.Single(_cloud.Requests);
        Assert.Equal(AiTask.SummarizePage, request.Task);
        Assert.Null(request.Image);
        Assert.StartsWith("Gardening tips", request.Context);
        Assert.Contains("patience", request.Context);
    }

    [Fact]
    public async Task SummarizePage_WithoutAi_SummarisesFromThePage()
    {
        _cloud.IsAvailable = false;
        var commands = Commands();
        commands.BrowseDocument = Page;
        commands.MainContentKey = () => "Insert+Ctrl+M";

        var summary = await Run(commands.SummarizePageAsync);

        Assert.StartsWith("Gardening tips\nMain heading: Growing tomatoes", summary);
        Assert.Contains("Insert+Ctrl+M moves to the main content.", summary);
        Assert.Empty(_cloud.Requests);
    }

    [Fact]
    public async Task SummarizePage_Declined_SummarisesFromThePage()
    {
        _consent.Answer = false;
        var commands = Commands();
        commands.BrowseDocument = Page;

        var summary = await Run(commands.SummarizePageAsync);

        Assert.StartsWith("Gardening tips", summary);
        Assert.Empty(_cloud.Requests);
    }

    [Fact]
    public async Task SummarizePage_NotBrowsing_SaysSo()
    {
        Assert.Equal("Not on a page", await Run(Commands().SummarizePageAsync));
    }

    [Fact]
    public async Task SetKey_KeepsIt_AndChoosesClaude()
    {
        _settings = new VoxSettings();
        _textPrompt.Answer = "sk-ant-secret";

        Assert.StartsWith("AI provider key saved", await Run(Commands().SetKeyAsync));

        Assert.Equal("sk-ant-secret", _keys.Key);
        Assert.True(Assert.Single(_textPrompt.Asked).Secret);
        Assert.Equal(AiCloudProvider.Claude, _saved!.AiCloudProvider);
        // Choosing the provider never says yes to sending
        Assert.Equal(AiConsentMode.Ask, _saved.AiCloudConsent);
    }

    [Fact]
    public async Task SetKey_NotOnSecureScreens()
    {
        _policy = RunPolicy.Secure;

        Assert.Equal("Not available on this screen", await Run(Commands().SetKeyAsync));
        Assert.Empty(_textPrompt.Asked);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
