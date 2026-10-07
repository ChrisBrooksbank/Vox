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
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class ReviewCommandsTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly Mock<IAudioCuePlayer> _audio = new();
    private readonly TextSource _text = new();
    private readonly ObjectNavigationCommands _navigation;
    private readonly ReviewCommands _review;

    private readonly MockNavigatorObject _window = new("Notepad", "Window",
        new MockNavigatorObject("Text editor", "Edit") { Text = "Dear Sam\nThanks for the letter" },
        new MockNavigatorObject("Status bar", "StatusBar"));

    private sealed class Focus(INavigatorObject focused) : INavigatorObjectSource
    {
        public INavigatorObject? GetFocused() => focused;
    }

    private sealed class TextSource : IReviewTextSource
    {
        public ITextDocument? Focused { get; set; }
        public Dictionary<IntPtr, string> Windows { get; } = new();
        public IntPtr ForegroundWindow { get; set; }
        public int WindowCaptures { get; private set; }

        public ITextDocument? GetFocusedText() => Focused;

        public ITextDocument? GetWindowText(IntPtr window)
        {
            WindowCaptures++;
            return Windows.TryGetValue(window, out var text) ? new StringTextDocument(text) : null;
        }
    }

    public ReviewCommandsTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings());
        _navigation = new ObjectNavigationCommands(_uiaThread, new Focus(_window.Find("Text editor")), _queue,
            new AnnouncementBuilder(), _audio.Object, settings, NullLogger<ObjectNavigationCommands>.Instance);
        _review = new ReviewCommands(_uiaThread, _navigation, _text, _queue, _audio.Object, settings, NullLogger<ReviewCommands>.Instance);
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
        return (await _engine.WaitForAsync(_ => true)).Text;
    }

    /// <summary>Switches review mode and waits for the mode to be said.</summary>
    private async Task SwitchTo(ReviewMode mode)
    {
        while (_review.Mode != mode)
        {
            _engine.Clear();
            _review.SwitchMode(+1);
            await _engine.WaitForAsync(s => s.Text.EndsWith("review"));
        }
    }

    [Fact]
    public async Task ObjectReview_ReadsTheNavigatorObjectsText()
    {
        Assert.Equal("Dear Sam", await Run(() => _review.ReadAsync(TextUnit.Line)));
        Assert.Equal("Thanks for the letter", await Run(() => _review.MoveAsync(TextUnit.Line, 1)));
        Assert.Equal("for", await Run(() => _review.MoveAsync(TextUnit.Word, 1)));
        Assert.Equal("f", await Run(() => _review.ReadAsync(TextUnit.Character)));
    }

    [Fact]
    public async Task ObjectReview_MovingTheNavigator_ReviewsTheNewObject()
    {
        await Run(() => _review.MoveAsync(TextUnit.Line, 1));
        await Run(() => _navigation.MoveAsync(NavigatorMove.Next));

        Assert.Equal("Status bar", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task Boundary_PlaysCueAndRereads()
    {
        Assert.Equal("Dear Sam", await Run(() => _review.MoveAsync(TextUnit.Line, -1)));
        _audio.Verify(a => a.Play("boundary"), Times.Once);
    }

    [Fact]
    public async Task TopAndBottom()
    {
        Assert.True(_review.TryHandle(NavigationCommand.ReviewBottom));
        await _engine.WaitForTextAsync("Thanks for the letter");
        _engine.Clear();
        Assert.True(_review.TryHandle(NavigationCommand.ReviewTop));
        await _engine.WaitForTextAsync("Dear Sam");
    }

    [Fact]
    public async Task DocumentReview_ReadsTheFocusedText_AndFollowsFocusChanges()
    {
        _text.Focused = new StringTextDocument("one\ntwo", caret: 4);
        await SwitchTo(ReviewMode.Document);

        Assert.Equal("two", await Run(() => _review.ReadAsync(TextUnit.Line)));

        _text.Focused = new StringTextDocument("other field");
        Assert.Equal("two", await Run(() => _review.ReadAsync(TextUnit.Line)));
        _review.HandleFocusChanged();
        Assert.Equal("other field", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task DocumentReview_NoText_SaysSo()
    {
        await SwitchTo(ReviewMode.Document);

        Assert.Equal("No text", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task ScreenReview_ReadsTheForegroundWindow_CapturedOncePerWindow()
    {
        _text.ForegroundWindow = 1;
        _text.Windows[1] = "Notepad\nFile, menu item\nEdit, menu item";
        _text.Windows[2] = "Calculator";
        await SwitchTo(ReviewMode.Screen);

        Assert.Equal("Notepad", await Run(() => _review.ReadAsync(TextUnit.Line)));
        Assert.Equal("File, menu item", await Run(() => _review.MoveAsync(TextUnit.Line, 1)));
        Assert.Equal(1, _text.WindowCaptures);

        _text.ForegroundWindow = 2;
        Assert.Equal("Calculator", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task SwitchMode_SaysTheModeAndStopsAtTheEnds()
    {
        Task<string> Switch(int direction) => Run(() => { _review.SwitchMode(direction); return Task.CompletedTask; });

        _review.SwitchMode(-1);
        _audio.Verify(a => a.Play("boundary"), Times.Once);
        Assert.Equal(ReviewMode.Object, _review.Mode);

        Assert.Equal("Document review", await Switch(+1));
        Assert.Equal("Screen review", await Switch(+1));
        _review.SwitchMode(+1);
        _audio.Verify(a => a.Play("boundary"), Times.Exactly(2));
        Assert.Equal("Document review", await Switch(-1));
        Assert.Equal("Object review", await Switch(-1));
    }

    [Theory]
    [InlineData(NavigationCommand.ReviewPrevLine)]
    [InlineData(NavigationCommand.ReviewCurrentLine)]
    [InlineData(NavigationCommand.ReviewNextLine)]
    [InlineData(NavigationCommand.ReviewPrevWord)]
    [InlineData(NavigationCommand.ReviewCurrentWord)]
    [InlineData(NavigationCommand.ReviewNextWord)]
    [InlineData(NavigationCommand.ReviewPrevChar)]
    [InlineData(NavigationCommand.ReviewCurrentChar)]
    [InlineData(NavigationCommand.ReviewNextChar)]
    [InlineData(NavigationCommand.ReviewTop)]
    [InlineData(NavigationCommand.ReviewBottom)]
    [InlineData(NavigationCommand.NextReviewMode)]
    [InlineData(NavigationCommand.PrevReviewMode)]
    public void ReviewCommands_AreHandled(NavigationCommand command) => Assert.True(_review.TryHandle(command));

    [Fact]
    public void OtherCommands_AreNotHandled() => Assert.False(_review.TryHandle(NavigationCommand.SayAll));
}

public class ReviewSpellingTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly ReviewCommands _review;

    private sealed class Focus(INavigatorObject focused) : INavigatorObjectSource
    {
        public INavigatorObject? GetFocused() => focused;
    }

    public ReviewSpellingTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings());
        var edit = new MockNavigatorObject("Text editor", "Edit") { Text = "Vox reads" };
        var navigation = new ObjectNavigationCommands(_uiaThread, new Focus(edit), _queue, new AnnouncementBuilder(),
            Mock.Of<IAudioCuePlayer>(), settings, NullLogger<ObjectNavigationCommands>.Instance);
        _review = new ReviewCommands(_uiaThread, navigation, Mock.Of<IReviewTextSource>(), _queue,
            Mock.Of<IAudioCuePlayer>(), settings, NullLogger<ReviewCommands>.Instance);
    }

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    [Fact]
    public async Task CurrentWord_Twice_Spells_ThreeTimes_SpellsPhonetically()
    {
        _review.TryHandle(NavigationCommand.ReviewCurrentWord);
        await _engine.WaitForTextAsync("Vox");
        _review.TryHandle(NavigationCommand.ReviewCurrentWord);
        await _engine.WaitForTextAsync("cap v, o, x");
        _review.TryHandle(NavigationCommand.ReviewCurrentWord);
        await _engine.WaitForTextAsync("cap Victor, Oscar, X-ray");
    }

    [Fact]
    public async Task CurrentChar_Twice_IsPhonetic()
    {
        _review.TryHandle(NavigationCommand.ReviewCurrentChar);
        _review.TryHandle(NavigationCommand.ReviewCurrentChar);

        await _engine.WaitForTextAsync("cap Victor");
    }

    [Fact]
    public async Task CurrentLine_Twice_Spells()
    {
        _review.TryHandle(NavigationCommand.ReviewCurrentLine);
        _review.TryHandle(NavigationCommand.ReviewCurrentLine);

        await _engine.WaitForTextAsync("cap v, o, x, Space, r, e, a, d, s");
    }
}

public class ReviewFollowingTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly ObjectNavigationCommands _navigation;
    private readonly ReviewCommands _review;
    private readonly FocusSource _focus = new();
    private readonly TextSource _text = new();
    private VoxSettings _settings = new();

    private readonly MockNavigatorObject _window = new("Mail", "Window",
        new MockNavigatorObject("To", "Edit") { Text = "sam@example.com" },
        new MockNavigatorObject("Body", "Edit") { Text = "Hello Sam\nSee you soon" },
        new MockNavigatorObject("Send", "Button"));

    private sealed class FocusSource : INavigatorObjectSource
    {
        public INavigatorObject? Focused { get; set; }
        public INavigatorObject? GetFocused() => Focused;
    }

    private sealed class TextSource : IReviewTextSource
    {
        public ITextDocument? Focused { get; set; }
        public IntPtr ForegroundWindow => 1;
        public ITextDocument? GetFocusedText() => Focused;
        public ITextDocument? GetWindowText(IntPtr window) => null;
    }

    public ReviewFollowingTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var settings = new Mock<IOptionsMonitor<VoxSettings>>();
        settings.SetupGet(m => m.CurrentValue).Returns(() => _settings);
        _focus.Focused = _window.Find("To");
        _navigation = new ObjectNavigationCommands(_uiaThread, _focus, _queue, new AnnouncementBuilder(),
            Mock.Of<IAudioCuePlayer>(), settings.Object, NullLogger<ObjectNavigationCommands>.Instance);
        _review = new ReviewCommands(_uiaThread, _navigation, _text, _queue, Mock.Of<IAudioCuePlayer>(),
            settings.Object, NullLogger<ReviewCommands>.Instance);
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
        return (await _engine.WaitForAsync(_ => true)).Text;
    }

    [Fact]
    public async Task Navigator_FollowsFocus()
    {
        Assert.Equal("sam@example.com", await Run(() => _review.ReadAsync(TextUnit.Line)));

        _focus.Focused = _window.Find("Body");
        _navigation.HandleFocusChanged();

        Assert.Equal("Hello Sam", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task Navigator_DoesNotFollowFocus_WhenTurnedOff()
    {
        _settings = _settings with { ReviewFollowsFocus = false };
        await Run(() => _review.ReadAsync(TextUnit.Line));

        _focus.Focused = _window.Find("Body");
        _navigation.HandleFocusChanged();

        Assert.Equal("sam@example.com", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task Review_FollowsTheCaret_BackToTheFocusedObject()
    {
        _focus.Focused = _window.Find("Body");
        await Run(() => _navigation.MoveAsync(NavigatorMove.Next)); // to Send
        Assert.Equal("Send", await Run(() => _review.ReadAsync(TextUnit.Line)));

        _review.HandleCaretMoved();

        Assert.Equal("Hello Sam", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task DocumentReview_FollowsTheCaret_OnlyWhenTurnedOn()
    {
        var document = new MovableCaretDocument("one\ntwo\nthree");
        _text.Focused = document;
        while (_review.Mode != ReviewMode.Document)
        {
            _engine.Clear();
            _review.SwitchMode(+1);
            await _engine.WaitForAsync(s => s.Text.EndsWith("review"));
        }
        Assert.Equal("one", await Run(() => _review.ReadAsync(TextUnit.Line)));

        document.Caret = 4;
        _review.HandleCaretMoved();
        Assert.Equal("two", await Run(() => _review.ReadAsync(TextUnit.Line)));

        _settings = _settings with { ReviewFollowsCaret = false };
        document.Caret = 8;
        _review.HandleCaretMoved();
        Assert.Equal("two", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    [Fact]
    public async Task Browsing_ReviewIsTetheredToTheBuffer()
    {
        var buffer = new object();
        ReviewTether? tether = new(new StringTextDocument("Heading\nParagraph\nLink", caret: 8), buffer, 8);
        _review.BrowseTether = () => tether;

        Assert.Equal("Paragraph", await Run(() => _review.ReadAsync(TextUnit.Line)));
        Assert.Equal("Link", await Run(() => _review.MoveAsync(TextUnit.Line, 1)));

        // The browse cursor didn't move: the review keeps its own position
        Assert.Equal("Link", await Run(() => _review.ReadAsync(TextUnit.Line)));

        // The browse cursor moved: the review goes to it
        tether = new(new StringTextDocument("Heading\nParagraph\nLink", caret: 0), buffer, 0);
        Assert.Equal("Heading", await Run(() => _review.ReadAsync(TextUnit.Line)));

        // Leaving browse mode: back to the navigator object
        tether = null;
        Assert.Equal("sam@example.com", await Run(() => _review.ReadAsync(TextUnit.Line)));
    }

    /// <summary>A document whose caret the test moves.</summary>
    private sealed class MovableCaretDocument(string text) : ITextDocument
    {
        public int Caret { get; set; }
        private StringTextDocument Current => new(text, Caret);
        public ITextRange DocumentRange => Current.DocumentRange;
        public ITextRange? GetCaret() => Current.GetCaret();
        public IReadOnlyList<ITextRange> GetSelection() => Current.GetSelection();
    }
}
