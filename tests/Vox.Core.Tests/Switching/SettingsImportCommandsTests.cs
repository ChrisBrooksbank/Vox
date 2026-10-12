using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class SettingsImportCommandsTests : IDisposable
{
    private sealed class CaptureSink : IEventSink
    {
        private readonly List<ScreenReaderEvent> _events = new();
        public void Post(ScreenReaderEvent evt) { lock (_events) _events.Add(evt); }
        public List<T> OfType<T>() { lock (_events) return _events.OfType<T>().ToList(); }
    }

    private sealed class FakePresenter : ISettingsImportPresenter
    {
        public ImportSource? Choice { get; set; }
        public List<ImportPreview> Reviewed { get; } = new();
        public List<string> Shown { get; } = new();

        public Task<ImportSource?> ReviewAsync(IReadOnlyList<ImportPreview> previews)
        {
            Reviewed.AddRange(previews);
            return Task.FromResult(Choice);
        }

        public Task ShowAsync(string title, string report)
        {
            Shown.Add(report);
            return Task.CompletedTask;
        }
    }

    private static readonly string Samples = Path.Combine(AppContext.BaseDirectory, "Switching", "Samples");
    private readonly string _vox = Path.Combine(Path.GetTempPath(), "vox-import-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _spoken = new();
    private readonly SpeechQueue _speech;
    private readonly CaptureSink _sink = new();
    private readonly FakePresenter _presenter = new();
    private readonly List<VoxSettings> _saved = new();
    private int _keyMapReloads;
    private readonly BrowseModeController _browse;
    private readonly Mock<IOptionsMonitor<VoxSettings>> _settings = new();

    public SettingsImportCommandsTests()
    {
        var engine = new Mock<ISpeechEngine>();
        engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (_spoken) _spoken.Add(u.Text); })
            .Returns(Task.CompletedTask);
        _speech = new SpeechQueue(engine.Object, NullLogger<SpeechQueue>.Instance);
        _settings.SetupGet(m => m.CurrentValue).Returns(new VoxSettings());
        var audio = new Mock<IAudioCuePlayer>();
        _browse = new BrowseModeController(
            _speech, audio.Object,
            new NavigationManager(_sink, NullLogger<NavigationManager>.Instance),
            new QuickNavHandler(audio.Object),
            new SayAllController(_speech, NullLogger<SayAllController>.Instance),
            new AnnouncementBuilder(),
            new TypingEchoHandler(_sink, () => TypingEchoMode.Characters, NullLogger<TypingEchoHandler>.Instance),
            _settings.Object, _sink, new Mock<IBrowseDocumentActions>().Object, new Mock<IElementsListPresenter>().Object,
            NullLogger<BrowseModeController>.Instance);
    }

    public void Dispose()
    {
        _speech.Dispose();
        if (Directory.Exists(_vox))
            Directory.Delete(_vox, recursive: true);
    }

    private SettingsImportCommands Commands(string nvda = "nvda-folder", string jaws = "jaws-root", bool secure = false)
    {
        var import = secure ? null : new SettingsImport(
            new ImportLocations(Path.Combine(Samples, nvda), Path.Combine(Samples, jaws), _vox, Path.Combine(_vox, "settings.json")),
            engine => engine == SpeechEngineRegistry.OneCoreId ? ["Microsoft David"] : null,
            layout => KeyMap.LayoutBindings(_vox, layout));
        return new SettingsImportCommands(import, _presenter, _settings.Object, _saved.Add, () => _keyMapReloads++,
            _browse, _sink, NullLogger<SettingsImportCommands>.Instance, _speech);
    }

    private async Task WaitForSpeech(string text)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            lock (_spoken)
                if (_spoken.Any(s => s.Contains(text))) return;
            await Task.Delay(10);
        }
        lock (_spoken)
            throw new Xunit.Sdk.XunitException($"\"{text}\" not said. Spoken: {string.Join(" | ", _spoken)}");
    }

    [Fact]
    public async Task Review_ShowsEachSourcesReport_AndImportsTheChosenOne()
    {
        _presenter.Choice = ImportSource.Jaws;

        await Commands().ReviewAndImportAsync([ImportSource.Nvda, ImportSource.Jaws]);

        Assert.Equal([ImportSource.Nvda, ImportSource.Jaws], _presenter.Reviewed.Select(p => p.Source));
        Assert.Contains("Nothing has been changed yet", _presenter.Reviewed[0].Report);
        Assert.Equal(PunctuationLevel.Most, Assert.Single(_saved).PunctuationLevel);
        Assert.Equal(1, _keyMapReloads);
        Assert.True(File.Exists(Path.Combine(_vox, KeyMap.UserFileName)));
        Assert.Single(_sink.OfType<VoxDialogClosedEvent>());
        await WaitForSpeech("Settings from JAWS brought over");
    }

    [Fact]
    public async Task Review_Cancelled_ChangesNothing()
    {
        _presenter.Choice = null;

        await Commands().ReviewAndImportAsync([ImportSource.Nvda]);

        Assert.Empty(_saved);
        Assert.Equal(0, _keyMapReloads);
        Assert.False(Directory.Exists(_vox));
        Assert.Single(_sink.OfType<VoxDialogClosedEvent>());
    }

    [Fact]
    public async Task Command_OnSecureScreens_IsRefused()
    {
        Assert.True(Commands(secure: true).TryHandle(NavigationCommand.ImportSettings));

        await WaitForSpeech("can't be imported here");
        Assert.Empty(_presenter.Reviewed);
    }

    [Fact]
    public async Task Command_NothingFound_SaysSo()
    {
        Assert.True(Commands("missing", "missing").TryHandle(NavigationCommand.ImportSettings));

        await WaitForSpeech("No NVDA or JAWS settings");
    }

    [Fact]
    public void OtherCommands_AreNotHandled()
    {
        Assert.False(Commands().TryHandle(NavigationCommand.SayTime));
    }

    [Fact]
    public void WizardImport_SavesWithoutAsking_AndReturnsTheReport()
    {
        var commands = Commands();

        var report = commands.Import(ImportSource.Nvda);

        Assert.NotNull(report);
        Assert.Equal("Microsoft David", Assert.Single(_saved).VoiceName);
        Assert.Empty(_presenter.Reviewed);

        commands.ShowReport(report);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_presenter.Shown.Count == 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
        Assert.Contains("These settings have been brought over", Assert.Single(_presenter.Shown));
    }
}
