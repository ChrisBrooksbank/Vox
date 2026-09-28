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
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class BrowseModeControllerTests : IDisposable
{
    private sealed class CaptureSink : IEventSink
    {
        private readonly List<ScreenReaderEvent> _events = new();
        public void Post(ScreenReaderEvent evt) { lock (_events) _events.Add(evt); }
        public List<T> OfType<T>() { lock (_events) return _events.OfType<T>().ToList(); }
    }

    private readonly List<Utterance> _spoken = new();
    private readonly Mock<ISpeechEngine> _engine = new();
    private readonly SpeechQueue _speechQueue;
    private readonly Mock<IAudioCuePlayer> _audio = new();
    private readonly CaptureSink _sink = new();
    private readonly NavigationManager _navigationManager;
    private readonly QuickNavHandler _quickNav;
    private readonly Mock<IBrowseDocumentActions> _actions = new();
    private readonly Mock<IElementsListPresenter> _presenter = new();
    private VoxSettings _settings = new();
    private readonly BrowseModeController _controller;

    public BrowseModeControllerTests()
    {
        _engine
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (_spoken) _spoken.Add(u); })
            .Returns(Task.CompletedTask);
        _speechQueue = new SpeechQueue(_engine.Object, NullLogger<SpeechQueue>.Instance);

        _navigationManager = new NavigationManager(_sink, NullLogger<NavigationManager>.Instance);
        _quickNav = new QuickNavHandler(_audio.Object);

        var settingsMonitor = new Mock<IOptionsMonitor<VoxSettings>>();
        settingsMonitor.SetupGet(m => m.CurrentValue).Returns(() => _settings);

        _controller = new BrowseModeController(
            _speechQueue,
            _audio.Object,
            _navigationManager,
            _quickNav,
            new SayAllController(_speechQueue, NullLogger<SayAllController>.Instance),
            new AnnouncementBuilder(),
            new TypingEchoHandler(_sink, () => TypingEchoMode.Characters, NullLogger<TypingEchoHandler>.Instance),
            settingsMonitor.Object,
            _sink,
            _actions.Object,
            _presenter.Object,
            NullLogger<BrowseModeController>.Instance);
    }

    public void Dispose() => _speechQueue.Dispose();

    // Document: H1 "Welcome" / text "Intro text" / link "Read more" / edit "Search"
    private static VBufferDocument BuildDocument()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Welcome", AriaRole = "heading", AriaProperties = "level=1" });
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Intro text" });
        var paragraph = new MockElement { RuntimeId = [6], ControlType = "Group" };
        paragraph.AddChild(new MockElement { RuntimeId = [4], Name = "Read more", ControlType = "Hyperlink" });
        root.AddChild(paragraph);
        root.AddChild(new MockElement { RuntimeId = [5], Name = "Search", ControlType = "Edit" });
        return new VBufferBuilder().Build(root);
    }

    private VBufferDocument LoadDocument(int[]? focusedId = null)
    {
        var doc = BuildDocument();
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, doc, focusedId));
        return doc;
    }

    private async Task<Utterance> WaitForSpeech(Func<Utterance, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            lock (_spoken)
            {
                var found = _spoken.FirstOrDefault(match);
                if (found is not null) return found;
            }
            await Task.Delay(10);
        }
        lock (_spoken)
            throw new Xunit.Sdk.XunitException("Expected speech not found. Spoken: " + string.Join(" | ", _spoken.Select(u => u.Text)));
    }

    // -------------------------------------------------------------------------
    // Document lifecycle
    // -------------------------------------------------------------------------

    [Fact]
    public void DocumentChanged_ActivatesAndDeactivates()
    {
        var changes = new List<bool>();
        _controller.DocumentActiveChanged += (_, active) => changes.Add(active);

        LoadDocument();
        Assert.True(_controller.IsDocumentActive);

        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, null));
        Assert.False(_controller.IsDocumentActive);
        Assert.Null(_quickNav.CurrentDocument);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void DocumentChanged_WithFocusedElement_PositionsCursorThere()
    {
        var doc = LoadDocument(focusedId: [4]);

        Assert.Same(doc.FindByRuntimeId([4]), _quickNav.CurrentNode);
        Assert.Equal(doc.FindByRuntimeId([4])!.TextRange.Start, _controller.Cursor!.TextOffset);
    }

    // -------------------------------------------------------------------------
    // Navigation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QuickNav_AnnouncesWithInterruptPriority_AndMovesCursor()
    {
        var doc = LoadDocument();

        _controller.HandleCommand(NavigationCommand.NextLink);

        var utterance = await WaitForSpeech(u => u.Text.Contains("Read more"));
        Assert.Equal(SpeechPriority.Interrupt, utterance.Priority);
        Assert.Equal(doc.FindByRuntimeId([4])!.TextRange.Start, _controller.Cursor!.TextOffset);
    }

    [Fact]
    public async Task QuickNav_UsesVerbosityFromSettings()
    {
        _settings = new VoxSettings { VerbosityLevel = VerbosityLevel.Advanced };
        LoadDocument();

        _controller.HandleCommand(NavigationCommand.NextHeading);

        var utterance = await WaitForSpeech(u => u.Text.Contains("Welcome"));
        Assert.DoesNotContain("heading level", utterance.Text);
    }

    [Fact]
    public async Task NextLine_AfterQuickNav_ReadsFollowingLine()
    {
        LoadDocument();
        _controller.HandleCommand(NavigationCommand.NextHeading); // Welcome

        _controller.HandleCommand(NavigationCommand.NextLine);

        await WaitForSpeech(u => u.Text == "Intro text");
        Assert.Equal([3], _quickNav.CurrentNode!.UIARuntimeId);
    }

    [Fact]
    public async Task ReadCurrentLine_SpeaksLineAtCursor()
    {
        LoadDocument(focusedId: [3]);

        _controller.HandleCommand(NavigationCommand.ReadCurrentLine);

        await WaitForSpeech(u => u.Text == "Intro text");
    }

    [Fact]
    public void BrowseCommand_InFocusMode_IsIgnored()
    {
        LoadDocument();
        _navigationManager.SwitchTo(InteractionMode.Focus);

        _controller.HandleCommand(NavigationCommand.NextLink);

        Assert.Null(_quickNav.CurrentNode);
    }

    [Fact]
    public void FocusChanged_MovesVirtualCursorToFocusedElement()
    {
        var doc = LoadDocument();

        _controller.HandleFocusChanged(new FocusChangedEvent(
            DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));

        Assert.Same(doc.FindByRuntimeId([4]), _quickNav.CurrentNode);
    }

    [Fact]
    public void StopSpeech_CancelsSpeech()
    {
        _controller.HandleCommand(NavigationCommand.StopSpeech);

        _engine.Verify(e => e.Cancel(), Times.AtLeastOnce);
    }

    // -------------------------------------------------------------------------
    // Activation and Elements List
    // -------------------------------------------------------------------------

    [Fact]
    public void ActivateElement_ActivatesCurrentNode()
    {
        LoadDocument(focusedId: [4]);
        _actions.Setup(a => a.ActivateAsync(It.IsAny<VBufferNode>())).ReturnsAsync(true);

        _controller.HandleCommand(NavigationCommand.ActivateElement);

        _actions.Verify(a => a.ActivateAsync(It.Is<VBufferNode>(n => n.Name == "Read more")), Times.Once);
        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
    }

    [Fact]
    public void ActivateElement_OnEditField_SwitchesToFocusMode()
    {
        LoadDocument(focusedId: [5]);
        _actions.Setup(a => a.ActivateAsync(It.IsAny<VBufferNode>())).ReturnsAsync(true);

        _controller.HandleCommand(NavigationCommand.ActivateElement);

        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);
        _actions.Verify(a => a.ActivateAsync(It.Is<VBufferNode>(n => n.Name == "Search")), Times.Once);
    }

    [Fact]
    public async Task ElementsList_DeactivatesBrowseKeysWhileOpen_AndJumpsToSelection()
    {
        var doc = LoadDocument();
        var selection = new TaskCompletionSource<VBufferNode?>();
        _presenter.Setup(p => p.ShowAsync(It.IsAny<VBufferDocument>())).Returns(selection.Task);

        _controller.HandleCommand(NavigationCommand.ElementsList);
        Assert.False(_controller.IsDocumentActive);

        selection.SetResult(doc.FindByRuntimeId([2]));
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_sink.OfType<ElementsListClosedEvent>().Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        var closed = Assert.Single(_sink.OfType<ElementsListClosedEvent>());

        _controller.HandleElementsListClosed(closed);

        Assert.True(_controller.IsDocumentActive);
        Assert.Same(doc.FindByRuntimeId([2]), _quickNav.CurrentNode);
        await WaitForSpeech(u => u.Text.Contains("Welcome"));
    }

    // -------------------------------------------------------------------------
    // Typing echo and incremental updates
    // -------------------------------------------------------------------------

    private static RawKeyEvent KeyUp(int vk) =>
        new(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vk, IsKeyDown = false });

    [Fact]
    public void RawKey_InBrowseModeOverDocument_IsNotEchoed()
    {
        LoadDocument();

        _controller.HandleRawKey(KeyUp(0x41));

        Assert.Empty(_sink.OfType<TypingEchoEvent>());
    }

    [Fact]
    public void RawKey_WithoutDocument_IsEchoed()
    {
        _controller.HandleRawKey(KeyUp(0x41));

        Assert.Single(_sink.OfType<TypingEchoEvent>());
    }

    [Fact]
    public void SubtreeChanged_KeepsCurrentElement()
    {
        LoadDocument(focusedId: [4]);
        var newIntro = new MockElement { RuntimeId = [3], Name = "A much longer introduction" };

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [3], newIntro));

        var doc = _quickNav.CurrentDocument!;
        Assert.Contains("A much longer introduction", doc.FlatText);
        Assert.Same(doc.FindByRuntimeId([4]), _quickNav.CurrentNode);
        Assert.Equal(doc.FindByRuntimeId([4])!.TextRange.Start, _controller.Cursor!.TextOffset);
    }

    // -------------------------------------------------------------------------
    // Round 2 fixes
    // -------------------------------------------------------------------------

    [Fact]
    public void PasswordField_EchoesStarsInsteadOfCharacters()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(
            DateTimeOffset.UtcNow, "Password", "Edit", RuntimeId: [9], IsPassword: true));

        _controller.HandleRawKey(KeyUp(0x41));

        var echo = Assert.Single(_sink.OfType<TypingEchoEvent>());
        Assert.Equal("star", echo.Text);
    }

    [Fact]
    public async Task ElementsList_FocusEventsDuringAndAfterDialog_DoNotUndoJump()
    {
        var doc = LoadDocument(focusedId: [4]);
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));
        var selection = new TaskCompletionSource<VBufferNode?>();
        _presenter.Setup(p => p.ShowAsync(It.IsAny<VBufferDocument>())).Returns(selection.Task);

        _controller.HandleCommand(NavigationCommand.ElementsList);

        // Focus moves into the dialog's list...
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Elements", "List", RuntimeId: [99, 1]));
        selection.SetResult(doc.FindByRuntimeId([2]));
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_sink.OfType<ElementsListClosedEvent>().Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        _controller.HandleElementsListClosed(_sink.OfType<ElementsListClosedEvent>()[0]);

        // ...then returns to the link that was focused before the dialog opened
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));

        Assert.Same(doc.FindByRuntimeId([2]), _quickNav.CurrentNode);
        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
    }

    [Fact]
    public async Task SayAll_FinishedEarlier_DoesNotPullCursorBackAfterFocusMove()
    {
        var doc = LoadDocument();
        _controller.HandleCommand(NavigationCommand.SayAll);

        // Let Say All read to the end
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!_spoken.Any(u => u.Text.Contains("Search")) && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        await Task.Delay(100);

        // User moves focus to the link, then navigates to the next form field from there
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));
        _controller.HandleCommand(NavigationCommand.ReadCurrentLine);

        await WaitForSpeech(u => u.Text == "Read more");
        Assert.Same(doc.FindByRuntimeId([4]), _quickNav.CurrentNode);
    }

    [Theory]
    [InlineData(NavigationCommand.SayAll)]
    [InlineData(NavigationCommand.ReadCurrentLine)]
    [InlineData(NavigationCommand.ElementsList)]
    [InlineData(NavigationCommand.ToggleMode)]
    public async Task DocumentCommand_WithoutDocument_SaysNotInADocument(NavigationCommand command)
    {
        _controller.HandleCommand(command);

        await WaitForSpeech(u => u.Text == "Not in a document");
        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
    }

    [Fact]
    public void FocusOutsideDocument_DoesNotSwitchMode()
    {
        LoadDocument();
        _navigationManager.SwitchTo(InteractionMode.Focus);

        // Focus moves to another application's control (not in the buffer)
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Notepad", "Document", RuntimeId: [500]));

        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);
    }

    [Fact]
    public void DocumentChanged_ResetsToBrowseModeSilently()
    {
        LoadDocument();
        _navigationManager.SwitchTo(InteractionMode.Focus);
        var posted = _sink.OfType<ModeChangedEvent>().Count;

        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, null));

        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
        Assert.Equal(posted, _sink.OfType<ModeChangedEvent>().Count);
    }

    [Fact]
    public async Task PropertyChanged_OnFocusedElement_AnnouncesExpandedAndValue()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Menu", "Button", RuntimeId: [7]));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30070, 1));
        await WaitForSpeech(u => u.Text == "expanded");

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Size", "ComboBox", RuntimeId: [8]));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [8], 30045, "Large"));
        await WaitForSpeech(u => u.Text == "Large");
    }

    [Fact]
    public async Task PropertyChanged_ValueOfEditOrOtherElement_IsNotAnnounced()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Name", "Edit", RuntimeId: [7]));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30045, "typed text"));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [8], 30070, 1));

        await Task.Delay(200);
        lock (_spoken)
        {
            Assert.DoesNotContain(_spoken, u => u.Text == "typed text");
            Assert.DoesNotContain(_spoken, u => u.Text == "expanded");
        }
    }

    [Fact]
    public async Task ElementSelected_WithoutFocusChange_IsAnnounced()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Fruit", "List", RuntimeId: [7]));

        _controller.HandleElementSelected(new ElementSelectedEvent(DateTimeOffset.UtcNow, [7, 2], "Banana"));

        await WaitForSpeech(u => u.Text == "Banana");
    }

    [Fact]
    public void SubtreeChanged_UnknownElement_RequestsFullRecapture()
    {
        LoadDocument();

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(
            DateTimeOffset.UtcNow, [777], new MockElement { RuntimeId = [777], Name = "New" }));

        _actions.Verify(a => a.RequestRecapture(null), Times.Once);
    }

    [Fact]
    public void SubtreeChanged_TextAppearsUnderNamedAncestor_RequestsAncestorRecapture()
    {
        // Button "Submit" with an empty child: the button emits its own name
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var button = new MockElement { RuntimeId = [2], Name = "Submit", ControlType = "Button" };
        button.AddChild(new MockElement { RuntimeId = [3], Name = "" });
        root.AddChild(button);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, new VBufferBuilder().Build(root)));

        // The child gains text: the button must no longer emit its own name
        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(
            DateTimeOffset.UtcNow, [3], new MockElement { RuntimeId = [3], Name = "Send" }));

        _actions.Verify(a => a.RequestRecapture(It.Is<int[]>(id => id.SequenceEqual(new[] { 2 }))), Times.Once);
    }
}
