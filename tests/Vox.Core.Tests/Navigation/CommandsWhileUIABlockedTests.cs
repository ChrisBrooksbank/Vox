using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
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

namespace Vox.Core.Tests.Navigation;

/// <summary>
/// Commands that don't need UIA must keep working while the UIA thread is stuck in a call to an
/// unresponsive application: the user must always be able to silence and quit Vox, and to keep
/// reading the buffer it already has.
/// </summary>
public class CommandsWhileUIABlockedTests : IDisposable
{
    private static readonly TimeSpan Responsive = TimeSpan.FromMilliseconds(500);

    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly ManualResetEventSlim _unblockUia = new();
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _speechQueue;
    private readonly EventPipeline _pipeline;
    private readonly BrowseModeController _controller;

    /// <summary>Document actions that, like the real tracker, run on the UIA thread.</summary>
    private sealed class UiaBackedActions(UIAThread uiaThread, ManualResetEventSlim unblock) : IBrowseDocumentActions
    {
        public Task<bool> ActivateAsync(VBufferNode node) =>
            uiaThread.RunAsync(() => { unblock.Wait(); return true; }, Timeout.InfiniteTimeSpan);

        public void RequestRecapture(int[]? runtimeId) { }
    }

    public CommandsWhileUIABlockedTests()
    {
        _speechQueue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var audio = Mock.Of<IAudioCuePlayer>();
        _pipeline = new EventPipeline(_speechQueue, audio, NullLogger<EventPipeline>.Instance);
        var navigationManager = new NavigationManager(_pipeline, NullLogger<NavigationManager>.Instance);
        var settings = Mock.Of<IOptionsMonitor<VoxSettings>>(m => m.CurrentValue == new VoxSettings());

        _controller = new BrowseModeController(
            _speechQueue, audio, navigationManager, new QuickNavHandler(audio),
            new SayAllController(_speechQueue, NullLogger<SayAllController>.Instance),
            new AnnouncementBuilder(),
            new TypingEchoHandler(_pipeline, () => TypingEchoMode.None, NullLogger<TypingEchoHandler>.Instance),
            settings, _pipeline, new UiaBackedActions(_uiaThread, _unblockUia), Mock.Of<IElementsListPresenter>(),
            NullLogger<BrowseModeController>.Instance);

        // Wired as in ScreenReaderService: commands are handled on the pipeline thread
        _pipeline.NavigationCommandReceived += (_, e) => _controller.HandleCommand(e.Command);
        _pipeline.DocumentChangedProcessed += (_, e) => _controller.HandleDocumentChanged(e);
    }

    public void Dispose()
    {
        _unblockUia.Set();
        _pipeline.Dispose();
        _speechQueue.Dispose();
        _uiaThread.Dispose();
        _unblockUia.Dispose();
    }

    private void Command(NavigationCommand command) =>
        _pipeline.Post(new NavigationCommandEvent(DateTimeOffset.UtcNow, command));

    private async Task LoadDocumentAndBlockUiaAsync()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var paragraph = new MockElement { RuntimeId = [4], ControlType = "Group" };
        paragraph.AddChild(new MockElement { RuntimeId = [2], Name = "Read more", ControlType = "Hyperlink" });
        root.AddChild(paragraph);
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Second line of text" });
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _controller.DocumentActiveChanged += (_, isActive) => { if (isActive) active.TrySetResult(); };
        _pipeline.Post(new DocumentChangedEvent(DateTimeOffset.UtcNow, new VBufferBuilder().Build(root), [2]));
        await active.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Activating the link goes to the (hung) application: the UIA thread is now stuck
        Command(NavigationCommand.ActivateElement);
        for (int i = 0; i < 100 && _uiaThread.CurrentWorkDuration is null; i++)
            await Task.Delay(10);
        Assert.NotNull(_uiaThread.CurrentWorkDuration);
    }

    [Fact]
    public async Task StopSpeech_WhileUiaBlocked_CancelsSpeechPromptly()
    {
        await LoadDocumentAndBlockUiaAsync();
        var cancelsBefore = _engine.CancelCount;

        Command(NavigationCommand.StopSpeech);

        var deadline = DateTime.UtcNow + Responsive;
        while (_engine.CancelCount == cancelsBefore && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(_engine.CancelCount > cancelsBefore, "StopSpeech did not cancel speech while UIA was blocked");
    }

    [Fact]
    public async Task Quit_WhileUiaBlocked_RequestsQuitPromptly()
    {
        await LoadDocumentAndBlockUiaAsync();
        var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _controller.QuitRequested += (_, _) => quit.TrySetResult();

        Command(NavigationCommand.Quit);
        Command(NavigationCommand.Quit);

        await quit.Task.WaitAsync(Responsive);
    }

    [Fact]
    public async Task ReadingTheBuffer_WhileUiaBlocked_StillSpeaks()
    {
        await LoadDocumentAndBlockUiaAsync();

        Command(NavigationCommand.NextLine);

        await _engine.WaitForAsync(s => s.Text.Contains("Second line of text"), Responsive);
    }
}
