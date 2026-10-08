using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class SpeechHistoryCommandsTests : IDisposable
{
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly SpeechHistory _history = new();
    private readonly Mock<IAudioCuePlayer> _audio = new();
    private readonly Mock<IClipboard> _clipboard = new();
    private long _now = 10_000;
    private readonly SpeechHistoryCommands _commands;

    public SpeechHistoryCommandsTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        // As in the app: what is said goes into the history unless it is a reviewed entry
        _queue.UtteranceStarted += (_, u) => { if (u.RecordInHistory) _history.Add(u.Text); };
        _clipboard.Setup(c => c.SetText(It.IsAny<string>())).Returns(true);
        _commands = new SpeechHistoryCommands(_history, _queue, _audio.Object, _clipboard.Object, () => _now);
        foreach (var text in new[] { "one", "two", "three" })
            _history.Add(text);
    }

    public void Dispose() => _queue.Dispose();

    private async Task<string> Press(NavigationCommand command)
    {
        _now += 1000; // not a double press
        _engine.Clear();
        Assert.True(_commands.TryHandle(command));
        return (await _engine.WaitForAsync(_ => true)).Text;
    }

    [Fact]
    public async Task Previous_StartsWithTheLatest_ThenGoesBack()
    {
        Assert.Equal("three", await Press(NavigationCommand.SpeechHistoryPrevious));
        Assert.Equal("two", await Press(NavigationCommand.SpeechHistoryPrevious));
        Assert.Equal("one", await Press(NavigationCommand.SpeechHistoryPrevious));
        Assert.Equal("two", await Press(NavigationCommand.SpeechHistoryNext));
    }

    [Fact]
    public async Task Ends_PlayTheBoundaryCue_AndRepeatTheEntry()
    {
        await Press(NavigationCommand.SpeechHistoryPrevious);
        await Press(NavigationCommand.SpeechHistoryPrevious);
        await Press(NavigationCommand.SpeechHistoryPrevious);

        Assert.Equal("one", await Press(NavigationCommand.SpeechHistoryPrevious));
        _audio.Verify(a => a.Play("boundary"), Times.Once);

        Assert.Equal("two", await Press(NavigationCommand.SpeechHistoryNext));
        Assert.Equal("three", await Press(NavigationCommand.SpeechHistoryNext));
        Assert.Equal("three", await Press(NavigationCommand.SpeechHistoryNext));
        _audio.Verify(a => a.Play("boundary"), Times.Exactly(2));
    }

    [Fact]
    public async Task ReviewedEntries_AreNotAddedAgain()
    {
        await Press(NavigationCommand.SpeechHistoryPrevious);
        await Press(NavigationCommand.SpeechHistoryPrevious);

        Assert.Equal(["one", "two", "three"], _history.Entries);
    }

    [Fact]
    public async Task SomethingNewSaid_StartsAgainFromTheLatest()
    {
        await Press(NavigationCommand.SpeechHistoryPrevious);
        await Press(NavigationCommand.SpeechHistoryPrevious);

        _history.Add("four");

        Assert.Equal("four", await Press(NavigationCommand.SpeechHistoryPrevious));
    }

    [Fact]
    public async Task PressedTwice_CopiesTheEntry()
    {
        await Press(NavigationCommand.SpeechHistoryPrevious);
        await Press(NavigationCommand.SpeechHistoryPrevious);

        _now += 200;
        _engine.Clear();
        _commands.TryHandle(NavigationCommand.SpeechHistoryPrevious);

        await _engine.WaitForTextAsync("Copied");
        _clipboard.Verify(c => c.SetText("two"));
    }

    [Fact]
    public async Task OnlyTheLast100_CanBeReviewed()
    {
        _history.Clear();
        for (int i = 1; i <= 150; i++)
            _history.Add($"entry {i}");

        for (int i = 0; i < SpeechHistoryCommands.ReviewDepth - 1; i++)
            await Press(NavigationCommand.SpeechHistoryPrevious);

        Assert.Equal("entry 51", await Press(NavigationCommand.SpeechHistoryPrevious));
        Assert.Equal("entry 51", await Press(NavigationCommand.SpeechHistoryPrevious));
    }

    [Fact]
    public async Task EmptyHistory_SaysSo()
    {
        _history.Clear();

        Assert.Equal("No speech history", await Press(NavigationCommand.SpeechHistoryNext));
    }

    [Fact]
    public void OtherCommands_AreNotHandled()
    {
        Assert.False(_commands.TryHandle(NavigationCommand.SayAll));
    }
}
