using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Labels;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Labels;

public sealed class CustomLabelCommandsTests : IDisposable
{
    private sealed class CaptureSink : IEventSink
    {
        private readonly List<ScreenReaderEvent> _events = new();
        public void Post(ScreenReaderEvent evt) { lock (_events) _events.Add(evt); }
        public List<T> OfType<T>() { lock (_events) return _events.OfType<T>().ToList(); }
    }

    private sealed class FakePrompt : ITextPrompt
    {
        public string? Answer { get; set; }
        public List<(string Label, IReadOnlyList<string> History)> Asked { get; } = new();
        public Task<string?> AskAsync(string title, string label, bool secret = false, IReadOnlyList<string>? history = null)
        {
            Asked.Add((label, history ?? []));
            return Task.FromResult(Answer);
        }
    }

    private sealed class FakePresenter : ICustomLabelsPresenter
    {
        public Func<CustomLabelsEditor, IReadOnlyList<CustomLabel>?> Edit { get; set; } = _ => null;
        public CustomLabelsEditor? Shown { get; private set; }
        public Task<IReadOnlyList<CustomLabel>?> EditAsync(CustomLabelsEditor editor)
        {
            Shown = editor;
            return Task.FromResult(Edit(editor));
        }
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxLabelCommands_" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _spoken = new();
    private readonly SpeechQueue _speech;
    private readonly CaptureSink _sink = new();
    private readonly FakePrompt _prompt = new();
    private readonly FakePresenter _presenter = new();
    private readonly NavigationManager _navigation;
    private readonly BrowseModeController _browse;
    private int _recaptures;

    public CustomLabelCommandsTests()
    {
        var engine = new Mock<ISpeechEngine>();
        engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (_spoken) _spoken.Add(u.Text); })
            .Returns(Task.CompletedTask);
        _speech = new SpeechQueue(engine.Object, NullLogger<SpeechQueue>.Instance);
        var settings = new Mock<IOptionsMonitor<VoxSettings>>();
        settings.SetupGet(m => m.CurrentValue).Returns(new VoxSettings());
        var audio = new Mock<IAudioCuePlayer>();
        _navigation = new NavigationManager(_sink, NullLogger<NavigationManager>.Instance);
        _browse = new BrowseModeController(
            _speech, audio.Object, _navigation, new QuickNavHandler(audio.Object),
            new SayAllController(_speech, NullLogger<SayAllController>.Instance),
            new AnnouncementBuilder(),
            new TypingEchoHandler(_sink, () => TypingEchoMode.Characters, NullLogger<TypingEchoHandler>.Instance),
            settings.Object, _sink, new Mock<IBrowseDocumentActions>().Object, new Mock<IElementsListPresenter>().Object,
            NullLogger<BrowseModeController>.Instance);
    }

    public void Dispose()
    {
        _speech.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private CustomLabelStore Store(bool secure = false) => new(secure ? null : Path.Combine(_directory, CustomLabelStore.FileName));

    private CustomLabelCommands Commands(CustomLabelStore store) =>
        new(store, _browse, _prompt, _presenter, _sink, () => _recaptures++, NullLogger<CustomLabelCommands>.Instance, _speech);

    // Document / unnamed button holding an icon / link "Help"
    private static MockElement Page(string address = "https://mail.example.com/inbox")
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Value = address };
        var button = new MockElement { RuntimeId = [2], ControlType = "Button", IsFocusable = true };
        button.AddChild(new MockElement { RuntimeId = [3], Name = "ic_send.svg", ControlType = "Image" });
        root.AddChild(button);
        root.AddChild(new MockElement { RuntimeId = [4], Name = "Help", ControlType = "Hyperlink", IsFocusable = true });
        return root;
    }

    private VBufferDocument Load(ICustomLabels? labels = null, string address = "https://mail.example.com/inbox")
    {
        var document = new VBufferBuilder { Labels = labels }.Build(Page(address));
        _browse.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, document, null));
        return document;
    }

    private async Task WaitFor(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        lock (_spoken)
            Assert.True(condition(), $"{what}. Spoken: {string.Join(" | ", _spoken)}");
    }

    private bool Said(string text)
    {
        lock (_spoken) return _spoken.Any(s => s.Contains(text));
    }

    [Fact]
    public async Task Label_AsksForTheButtonsLabel_KeepsItForTheSite_AndCapturesThePageAgain()
    {
        var store = Store();
        var document = Load();
        _prompt.Answer = " Send ";

        Assert.True(Commands(store).TryHandle(NavigationCommand.LabelElement));

        await WaitFor(() => store.All.Count == 1, "labelled");
        var label = store.All[0];
        Assert.Equal("https://mail.example.com", label.Site);
        Assert.Equal("Send", label.Label);
        Assert.Equal(ElementSignature.Of(document.FindByRuntimeId([2])!, document.FlatText), label.Signature);
        Assert.Equal("Label for button ic_send.svg:", _prompt.Asked[0].Label.Replace("&", string.Empty));
        Assert.Equal(1, _recaptures);
        Assert.Single(_sink.OfType<VoxDialogClosedEvent>());
        await WaitFor(() => Said("Labelled Send"), "confirmation said");

        // The page built again has it, at the cursor and for focus announcements
        _browse.EndOwnDialog();
        Load(store);
        Assert.Equal("Send", _browse.CustomLabelFor([2]));
        Assert.Null(_browse.CustomLabelFor([4]));
        Assert.Equal([2], _browse.LabelTarget()!.Value.Node.UIARuntimeId);

        // Labelling it again offers the current label
        _prompt.Answer = "Send message";
        Assert.Equal("Send", _browse.Cursor!.CurrentNode!.CustomLabel);
        Commands(store).TryHandle(NavigationCommand.LabelElement);
        await WaitFor(() => store.All is [{ Label: "Send message" }], "relabelled");
        Assert.Equal(["Send"], _prompt.Asked[1].History);
    }

    [Fact]
    public async Task Label_InFocusMode_LabelsTheFocusedElement()
    {
        var store = Store();
        Load();
        _browse.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Help", "Hyperlink", RuntimeId: [4]));
        _navigation.SwitchTo(InteractionMode.Focus, "test", announce: false);
        _prompt.Answer = "Help centre";

        Commands(store).TryHandle(NavigationCommand.LabelElement);

        await WaitFor(() => store.All.Count == 1, "labelled");
        Assert.Equal("Hyperlink|Document|ic_send.svg", store.All[0].Signature);
    }

    [Fact]
    public async Task Label_Cancelled_KeepsNothing()
    {
        var store = Store();
        Load();
        _prompt.Answer = null;

        Commands(store).TryHandle(NavigationCommand.LabelElement);

        await WaitFor(() => _sink.OfType<VoxDialogClosedEvent>().Count == 1, "dialog closed");
        Assert.Empty(store.All);
        Assert.Equal(0, _recaptures);
    }

    [Fact]
    public async Task Label_IsRefused_OnSecureScreens_OutsidePages_AndOffTheWeb()
    {
        Commands(Store(secure: true)).TryHandle(NavigationCommand.LabelElement);
        await WaitFor(() => Said("not available here"), "refused on secure screens");

        Commands(Store()).TryHandle(NavigationCommand.LabelElement);
        await WaitFor(() => Said("Nothing to label here"), "nothing to label without a page");

        Load(address: "file:///C:/page.html");
        Commands(Store()).TryHandle(NavigationCommand.LabelElement);
        await WaitFor(() => Said("only be given on web sites"), "refused off the web");
        Assert.Empty(_prompt.Asked);
    }

    [Fact]
    public async Task Manage_KeepsWhatTheDialogReturns_TheCurrentSiteFirst()
    {
        var store = Store();
        store.SetAll([new CustomLabel("https://a.org", "id:x", "Other"), new CustomLabel("https://mail.example.com", "id:y", "Mine")]);
        Load();
        _presenter.Edit = editor =>
        {
            editor.Delete(1);
            return editor.Labels;
        };

        Assert.True(Commands(store).TryHandle(NavigationCommand.ManageLabels));

        await WaitFor(() => store.All.Count == 1, "saved");
        Assert.Equal("https://mail.example.com", _presenter.Shown!.CurrentSite);
        Assert.Equal("Mine", store.All[0].Label);
        Assert.Equal(1, _recaptures);
    }

    [Fact]
    public async Task Manage_WithNoLabels_StillOpens_ToImportAPack()
    {
        var store = Store();
        _presenter.Edit = editor =>
        {
            editor.Merge([new CustomLabel("https://mail.example.com", "id:send", "Send")]);
            return editor.Labels;
        };

        Commands(store).TryHandle(NavigationCommand.ManageLabels);

        await WaitFor(() => store.All.Count == 1, "imported labels kept");
        Assert.NotNull(_presenter.Shown);
    }
}
