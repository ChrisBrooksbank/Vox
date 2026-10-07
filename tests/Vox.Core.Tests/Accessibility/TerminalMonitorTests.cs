using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class TerminalMonitorTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly FakeTerminal _terminal = new();
    private readonly TerminalMonitor _monitor;

    private sealed class FakeTerminal : IFocusedTextSource
    {
        public bool RaisesCaretEvents => true;
        public bool HasText => true;
        public bool IsTerminal { get; set; } = true;
        public string[] Lines { get; set; } = [];
        public ITextDocument? GetFocusedDocument() => null;
        public IReadOnlyList<string>? GetVisibleLines() => Lines;
    }

    public TerminalMonitorTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _monitor = new TerminalMonitor(_uiaThread, _terminal, _queue, NullLogger<TerminalMonitor>.Instance);
    }

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    private Task Edited() => _monitor.HandleTextEditedAsync(new TextEditedEvent(DateTimeOffset.UtcNow, [1]));

    [Fact]
    public async Task NewOutput_IsSpoken_ButNotWhatWasOnScreenAtFocus()
    {
        _terminal.Lines = ["old output", "$ "];
        await _monitor.HandleFocusChangedAsync();

        _terminal.Lines = ["old output", "$ make", "Build succeeded", "$ "];
        await Edited();

        var spoken = await _engine.WaitForAsync(s => s.Text.Contains("Build succeeded"));
        Assert.DoesNotContain("old output", spoken.Text);
        Assert.Equal(SpeechPriority.Normal, spoken.Priority);
    }

    [Fact]
    public async Task TypedCharacters_AreNotReadBackAsOutput()
    {
        _terminal.Lines = ["$ ", ""];
        await _monitor.HandleFocusChangedAsync();

        _monitor.HandleRawKey(new RawKeyEvent(DateTimeOffset.UtcNow, new KeyEvent { VkCode = 'L', IsKeyDown = true }));
        _terminal.Lines = ["$ l", ""];
        await Edited();
        await Task.Delay(150);

        Assert.Empty(_engine.Spoken);
    }

    [Fact]
    public async Task NotATerminal_NothingIsRead()
    {
        _terminal.IsTerminal = false;
        _terminal.Lines = ["something"];

        await Edited();
        await Task.Delay(50);

        Assert.Empty(_engine.Spoken);
    }

    [Fact]
    public async Task RapidOutput_IsBatched()
    {
        _terminal.Lines = ["$ "];
        await _monitor.HandleFocusChangedAsync();

        _terminal.Lines = ["$ ", "one"];
        await Edited();
        _terminal.Lines = ["$ ", "one", "two"];
        var second = Edited();
        _terminal.Lines = ["$ ", "one", "two", "three"];
        var third = Edited();
        await Task.WhenAll(second, third);

        await _engine.WaitForAsync(s => s.Text.Contains("three"));
        Assert.True(_engine.Spoken.Count <= 2, string.Join(" | ", _engine.SpokenText));
    }
}
