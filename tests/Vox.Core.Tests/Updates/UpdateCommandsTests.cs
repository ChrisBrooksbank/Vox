using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Updates;
using Xunit;

namespace Vox.Core.Tests.Updates;

public sealed class UpdateCommandsTests : IDisposable
{
    private sealed class CaptureSink : IEventSink
    {
        private readonly List<ScreenReaderEvent> _events = new();
        public void Post(ScreenReaderEvent evt) { lock (_events) _events.Add(evt); }
        public List<T> OfType<T>() { lock (_events) return _events.OfType<T>().ToList(); }
    }

    private sealed class FakeWeb(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("pretend msi");

    private readonly RSA _releaseKey = RSA.Create(2048);
    private readonly List<string> _spoken = new();
    private readonly Mock<ISpeechEngine> _engine = new();
    private readonly SpeechQueue _speech;
    private readonly CaptureSink _sink = new();
    private readonly BrowseModeController _browse;
    private readonly Mock<IConfirmPresenter> _confirm = new();
    private readonly UpdateChecker _checker;
    private readonly List<string> _started = new();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly UpdateCommands _commands;

    public UpdateCommandsTests()
    {
        _engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (_spoken) _spoken.Add(u.Text); })
            .Returns(Task.CompletedTask);
        _speech = new SpeechQueue(_engine.Object, NullLogger<SpeechQueue>.Instance);

        var settings = new Mock<IOptionsMonitor<VoxSettings>>();
        settings.SetupGet(m => m.CurrentValue).Returns(new VoxSettings());
        var audio = new Mock<IAudioCuePlayer>();
        _browse = new BrowseModeController(
            _speech, audio.Object,
            new NavigationManager(_sink, NullLogger<NavigationManager>.Instance),
            new QuickNavHandler(audio.Object),
            new SayAllController(_speech, NullLogger<SayAllController>.Instance),
            new AnnouncementBuilder(),
            new TypingEchoHandler(_sink, () => TypingEchoMode.Characters, NullLogger<TypingEchoHandler>.Instance),
            settings.Object, _sink, new Mock<IBrowseDocumentActions>().Object, new Mock<IElementsListPresenter>().Object,
            NullLogger<BrowseModeController>.Instance);

        var hash = Convert.ToHexString(SHA256.HashData(Installer));
        var feed = $$"""
            { "releases": [ { "version": "1.2.0", "channel": "stable", "url": "https://example.com/Vox-1.2.0.msi", "sha256": "{{hash}}" } ] }
            """;
        var signature = Convert.ToBase64String(_releaseKey.SignData(Encoding.UTF8.GetBytes(feed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        _checker = new UpdateChecker(
            new HttpClient(new FakeWeb(new()
            {
                ["https://example.com/feed.json"] = Encoding.UTF8.GetBytes(feed),
                ["https://example.com/feed.json.sig"] = Encoding.UTF8.GetBytes(signature),
                ["https://example.com/Vox-1.2.0.msi"] = Installer,
            })),
            () => UpdateChannel.Stable, _releaseKey.ExportSubjectPublicKeyInfoPem(), new Uri("https://example.com/feed.json"),
            new Version(1, 0, 0), NullLogger<UpdateChecker>.Instance);

        _commands = new UpdateCommands(_checker, _confirm.Object, _browse, _sink, _speech, () => _exited.TrySetResult(),
            lastCheckFile: null, NullLogger<UpdateCommands>.Instance, startInstaller: _started.Add);
    }

    public void Dispose()
    {
        _checker.Dispose();
        foreach (var path in _started)
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        _speech.Dispose();
        _releaseKey.Dispose();
    }

    private async Task<PipelineWorkEvent> CheckAndWaitForTheQuestion()
    {
        Assert.True(_commands.TryHandle(NavigationCommand.CheckForUpdates));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_sink.OfType<PipelineWorkEvent>().Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        return Assert.Single(_sink.OfType<PipelineWorkEvent>());
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
    public async Task UpdateFound_AsksOnThePipelineThread_NotFromTheCheck()
    {
        _confirm.Setup(c => c.AskAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        var work = await CheckAndWaitForTheQuestion();

        // The check, on the thread pool, only posts the work: the question waits for the pipeline
        await Task.Delay(50);
        _confirm.Verify(c => c.AskAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        work.Work();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_sink.OfType<VoxDialogClosedEvent>().Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        _confirm.Verify(c => c.AskAsync("Update available", It.IsAny<string>()), Times.Once);
        Assert.Single(_sink.OfType<VoxDialogClosedEvent>());
    }

    [Fact]
    public async Task UpdateFound_WhileAnotherVoxDialogIsOpen_SaysSo()
    {
        var work = await CheckAndWaitForTheQuestion();
        Assert.True(_browse.BeginOwnDialog()); // e.g. the settings dialog

        work.Work();

        await WaitForSpeech("Close the open Vox dialog");
        _confirm.Verify(c => c.AskAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InstallerStarted_VoxClosesEvenWhenTheGoodbyeIsNotSpoken()
    {
        _confirm.Setup(c => c.AskAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var work = await CheckAndWaitForTheQuestion();
        // Paused speech or sleep mode: the goodbye is cancelled at once
        _speech.IsMuted = () => true;

        work.Work();

        var finished = await Task.WhenAny(_exited.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(_exited.Task, finished);
        Assert.Single(_started);
    }
}
