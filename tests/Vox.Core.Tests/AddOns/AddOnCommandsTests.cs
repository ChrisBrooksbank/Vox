using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.AddOns;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class AddOnCommandsTests : IDisposable
{
    /// <summary>Runs pipeline work at once, as if on the pipeline thread.</summary>
    private sealed class RunningSink : IEventSink
    {
        public BrowseModeController? Browse { get; set; }

        public void Post(ScreenReaderEvent evt)
        {
            if (evt is PipelineWorkEvent work)
                work.Work();
            else if (evt is VoxDialogClosedEvent)
                Browse?.EndOwnDialog(false);
        }
    }

    private readonly AddOnTestFiles _files = new();
    private readonly SpeechQueue _speech = new(new RecordingSpeechEngine(), NullLogger<SpeechQueue>.Instance);
    private readonly List<string> _said = [];
    private readonly List<string> _questions = [];
    private readonly Mock<IAddOnsPresenter> _addOnsDialog = new();
    private readonly RestartRequest _restart = new();
    private readonly FakeLoader _loader = new();
    private bool _answer = true;
    private int _stops;

    public AddOnCommandsTests()
    {
        _speech.UtteranceQueued += (_, u) => { lock (_said) _said.Add(u.Text); };
    }

    public void Dispose()
    {
        _speech.Dispose();
        _files.Dispose();
    }

    private (AddOnCommands Commands, AddOnManager Manager) Create(RunPolicy? policy = null)
    {
        var sink = new RunningSink();
        var settings = new Mock<IOptionsMonitor<VoxSettings>>();
        settings.SetupGet(m => m.CurrentValue).Returns(new VoxSettings());
        var browse = new BrowseModeController(_speech, new Mock<IAudioCuePlayer>().Object, new NavigationManager(sink, NullLogger<NavigationManager>.Instance),
            new QuickNavHandler(new Mock<IAudioCuePlayer>().Object), new SayAllController(_speech, NullLogger<SayAllController>.Instance),
            new AnnouncementBuilder(), new TypingEchoHandler(sink, () => TypingEchoMode.None, NullLogger<TypingEchoHandler>.Instance),
            settings.Object, sink, new Mock<IBrowseDocumentActions>().Object, new Mock<IElementsListPresenter>().Object,
            NullLogger<BrowseModeController>.Instance);
        sink.Browse = browse;
        var confirm = new Mock<IConfirmPresenter>();
        confirm.Setup(c => c.AskAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, q) => _questions.Add(q))
            .ReturnsAsync(() => _answer);
        var manager = new AddOnManager(_files.AddOnsDirectory, policy ?? RunPolicy.Normal, _loader, AddOnTrustStore.Empty);
        var store = new AddOnStore(new HttpClient(), () => AddOnStore.DefaultCatalogueUrl, AddOnTrustStore.Empty, policy ?? RunPolicy.Normal,
            NullLogger<AddOnStore>.Instance);
        var commands = new AddOnCommands(manager, store, _addOnsDialog.Object, new Mock<IAddOnStorePresenter>().Object, confirm.Object,
            new OwnDialogRunner(browse, sink), sink, _speech, _restart, () => _stops++, NullLogger<AddOnCommands>.Instance);
        return (commands, manager);
    }

    [Fact]
    public async Task InstallingAPackage_AsksFirst_WarningWhenItIsUnsigned()
    {
        var (commands, manager) = Create();

        var message = await commands.InstallFromFileAsync(_files.Package());

        var question = Assert.Single(_questions);
        Assert.Contains("Install Add-on com.example.test 1.0.0 by Example?", question);
        Assert.Contains("Not signed", question);
        Assert.Contains("trust", question);
        Assert.Equal("Add-on com.example.test 1.0.0 installed.", message);
        Assert.Equal(AddOnState.Running, Assert.Single(manager.List()).State);
    }

    [Fact]
    public async Task SayingNo_InstallsNothing()
    {
        var (commands, manager) = Create();
        _answer = false;

        var message = await commands.InstallFromFileAsync(_files.Package());

        Assert.Equal(string.Empty, message);
        Assert.Empty(manager.List());
    }

    [Fact]
    public async Task AnIncompatiblePackage_IsRefusedWithoutAsking()
    {
        var (commands, manager) = Create();

        var message = await commands.InstallFromFileAsync(_files.Package(_files.Source(minApi: "9.0")));

        Assert.Empty(_questions);
        Assert.Contains("needs a newer Vox", message);
        Assert.Empty(manager.List());
    }

    [Fact]
    public async Task ADamagedPackage_IsRefused_AndSaidSo()
    {
        var (commands, _) = Create();
        var package = _files.Package();
        AddOnTestFiles.Replace(package, "Test.dll", "changed");

        var message = await commands.InstallFromFileAsync(package);

        Assert.StartsWith("Not installed:", message);
        Assert.Contains(_said, s => s.StartsWith("Not installed:"));
    }

    [Fact]
    public async Task TurningAnAddOnOffAndOn_IsSaid()
    {
        var (commands, manager) = Create();
        await commands.InstallFromFileAsync(_files.Package());

        Assert.Equal("Add-on com.example.test turned off.", await commands.SetEnabledAsync("com.example.test", false));
        Assert.Equal(AddOnState.Disabled, Assert.Single(manager.List()).State);
        Assert.Equal("Add-on com.example.test turned on.", await commands.SetEnabledAsync("com.example.test", true));
        Assert.Equal(AddOnState.Running, Assert.Single(manager.List()).State);
    }

    [Fact]
    public void OnTheSecureScreens_TheDialogIsntShown()
    {
        var (commands, _) = Create(RunPolicy.Secure);

        Assert.True(commands.TryHandle(NavigationCommand.ManageAddOns));

        _addOnsDialog.Verify(p => p.ShowAsync(It.IsAny<IAddOnActions>()), Times.Never);
        Assert.Contains(_said, s => s.Contains("not available on this screen"));
    }

    [Fact]
    public async Task Restart_GoesToSafeMode_AndBackFromIt()
    {
        var (commands, _) = Create();
        commands.TryHandle(NavigationCommand.RestartWithoutAddOns);
        await Remote.RemoteTestSupportWait.UntilAsync(() => _stops == 1);
        Assert.True(_restart.SafeMode);

        var (safe, _) = Create(RunPolicy.FromArgs(["--safe"]));
        safe.TryHandle(NavigationCommand.RestartWithoutAddOns);
        await Remote.RemoteTestSupportWait.UntilAsync(() => _stops == 2);
        Assert.False(_restart.SafeMode);
    }

    [Fact]
    public void TheDetails_ListTheCommandsAndTheirKeys()
    {
        var info = new AddOnInfo(AddOnManifestTests.Valid(), AddOnState.Running, null, AddOnTrust.Unsigned, null,
            [new AddOnCommandInfo("Say hello", "Says hello.", [Sdk.KeyGesture.Parse("VoxKey+Ctrl+F12")])]);

        var details = AddOnCommands.Describe(info);

        Assert.Contains("Test add-on, version 1.2.0", details);
        Assert.Contains("Say hello: Insert+Ctrl+F12. Says hello.", details);
        Assert.Contains("Not signed.", details);
        Assert.Equal("Test add-on 1.2.0, running", AddOnCommands.ListLine(info));
    }
}
