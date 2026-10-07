using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Vox.Core.Input;
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
            NullLogger<FocusedTextMonitor>.Instance) { FallbackDelay = TimeSpan.FromMilliseconds(10) };
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
}
