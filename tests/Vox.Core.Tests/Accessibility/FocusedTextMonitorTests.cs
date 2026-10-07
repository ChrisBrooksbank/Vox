using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class FocusedTextMonitorTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly FakeSource _source = new();
    private readonly FocusedTextMonitor _monitor;

    private sealed class FakeSource : IFocusedTextSource
    {
        public bool RaisesCaretEvents { get; set; } = true;
        public bool HasText { get; set; } = true;
        public ITextDocument? Document { get; set; }
        public ITextDocument? GetFocusedDocument() => Document;
    }

    public FocusedTextMonitorTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _monitor = new FocusedTextMonitor(_uiaThread, _source, new TextCaretTracker(), _queue,
            NullLogger<FocusedTextMonitor>.Instance)
        {
            FallbackDelay = TimeSpan.FromMilliseconds(10),
            PrimeDelay = TimeSpan.Zero,
        };
    }

    public void Dispose()
    {
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    private static RawKeyEvent KeyDown(int vk, KeyModifiers modifiers = KeyModifiers.None) =>
        new(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vk, Modifiers = modifiers, IsKeyDown = true });

    [Fact]
    public async Task TextPatternControl_CaretEventAfterCaretKey_ReadsTheLine()
    {
        _source.Document = new StringTextDocument("first\nsecond line", caret: 6);

        await _monitor.HandleRawKey(KeyDown(CaretKeys.VK_DOWN));
        await _monitor.HandleCaretMovedAsync(new CaretMovedEvent(DateTimeOffset.UtcNow, [1]));

        var spoken = await _engine.WaitForTextAsync("second line");
        Assert.Equal(SpeechPriority.Interrupt, spoken.Priority);
    }

    [Fact]
    public async Task ValueOnlyControl_ReadsAfterTheCaretKeyWithoutAnyCaretEvent()
    {
        _source.RaisesCaretEvents = false;
        _source.Document = UIAFocusedTextSource.FromValue("hello world", 6, 6);

        await _monitor.HandleRawKey(KeyDown(CaretKeys.VK_RIGHT, KeyModifiers.Ctrl));

        await _engine.WaitForTextAsync("world");
    }

    [Fact]
    public async Task ControlWithoutText_SaysNothing()
    {
        _source.HasText = false;
        _source.RaisesCaretEvents = false;

        await _monitor.HandleRawKey(KeyDown(CaretKeys.VK_RIGHT));
        await Task.Delay(50);

        Assert.Empty(_engine.Spoken);
    }

    [Fact]
    public void FromValue_CaretAtTheSelectionEnd()
    {
        var document = UIAFocusedTextSource.FromValue("hello world", 0, 5);

        Assert.Equal(5, document.Caret);
        Assert.Equal("hello", Assert.Single(document.GetSelection()).GetText());
    }

    [Fact]
    public async Task TextPatternControl_BackspaceThenTextEvent_SaysTheDeletedCharacter()
    {
        _source.Document = new StringTextDocument("hello", 5);
        await _monitor.HandleFocusChanged(); // primes the caret line

        await _monitor.HandleRawKey(KeyDown(0x08));
        _source.Document = new StringTextDocument("hell", 4);
        await _monitor.HandleCaretMovedAsync(new CaretMovedEvent(DateTimeOffset.UtcNow, [1]));
        await _monitor.HandleTextEditedAsync(new TextEditedEvent(DateTimeOffset.UtcNow, [1]));

        await _engine.WaitForTextAsync("o");
    }

    [Fact]
    public async Task ValueOnlyControl_DeleteIsReadAfterTheKey()
    {
        _source.RaisesCaretEvents = false;
        _source.Document = UIAFocusedTextSource.FromValue("abc", 1, 1);
        await _monitor.HandleFocusChanged();

        _source.Document = UIAFocusedTextSource.FromValue("ac", 1, 1); // the control deletes "b" right away
        await _monitor.HandleRawKey(KeyDown(0x2E));

        await _engine.WaitForTextAsync("b");
    }

    [Theory]
    [InlineData(TextReadKind.Character, 6, null, "w")]
    [InlineData(TextReadKind.Word, 6, null, "world")]
    [InlineData(TextReadKind.Line, 13, null, "second line")]
    [InlineData(TextReadKind.Selection, 0, 5, "hello")]
    [InlineData(TextReadKind.Selection, 3, null, "No selection")]
    public void Describe_ReadCommands(TextReadKind kind, int caret, int? selectionEnd, string expected)
    {
        var document = selectionEnd is { } end
            ? new StringTextDocument("hello world\nsecond line", end, 0, end)
            : new StringTextDocument("hello world\nsecond line", caret);

        Assert.Equal(expected, FocusedTextMonitor.Describe(document, kind));
    }

    [Fact]
    public async Task ReadAsync_SpeaksTheLineOfTheFocusedControl()
    {
        _source.Document = new StringTextDocument("one\ntwo", 5);

        await _monitor.ReadAsync(TextReadKind.Line);

        await _engine.WaitForTextAsync("two");
    }
}
