using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class ForegroundWindowMonitorTests
{
    private sealed class FakeForeground : IForegroundWindow
    {
        public ForegroundWindowInfo? Window { get; set; }
        public ForegroundWindowInfo? Get() => Window;
    }

    private sealed class Sink : IEventSink
    {
        public List<ScreenReaderEvent> Events { get; } = new();
        public void Post(ScreenReaderEvent evt) => Events.Add(evt);
    }

    private static FocusChangedEvent Focus(string name) => new(DateTimeOffset.UtcNow, name, "Edit");

    [Fact]
    public void NewWindow_GivesItsTitleOnce()
    {
        var foreground = new FakeForeground { Window = new(1, "Untitled - Notepad", 10) };
        var sink = new Sink();
        var monitor = new ForegroundWindowMonitor(foreground, sink);

        Assert.Equal("Untitled - Notepad", monitor.FocusContext(Focus("Text editor")));
        Assert.Null(monitor.FocusContext(Focus("Text editor")));

        var changed = Assert.Single(sink.Events.OfType<ForegroundWindowChangedEvent>());
        Assert.Equal("Untitled - Notepad", changed.Title);
    }

    [Fact]
    public void SwitchingWindows_GivesEachTitle()
    {
        var foreground = new FakeForeground { Window = new(1, "One", 10) };
        var monitor = new ForegroundWindowMonitor(foreground, new Sink());
        monitor.FocusContext(Focus("x"));

        foreground.Window = new(2, "Two", 11);
        Assert.Equal("Two", monitor.FocusContext(Focus("x")));
        foreground.Window = new(1, "One", 10);
        Assert.Equal("One", monitor.FocusContext(Focus("x")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Calculator")] // the focused element is the window itself
    public void TitleLeftOut(string title)
    {
        var monitor = new ForegroundWindowMonitor(new FakeForeground { Window = new(1, title, 10) }, new Sink());

        Assert.Null(monitor.FocusContext(Focus("Calculator")));
    }

    [Fact]
    public async Task Pipeline_SaysTheWindowTitleBeforeTheFocusedControl()
    {
        var engine = new RecordingSpeechEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);
        using var pipeline = new EventPipeline(queue, Mock.Of<IAudioCuePlayer>(), NullLogger<EventPipeline>.Instance);
        var monitor = new ForegroundWindowMonitor(new FakeForeground { Window = new(5, "Save As", 10) }, pipeline);
        pipeline.FocusContextProvider = monitor.FocusContext;

        pipeline.Post(new FocusChangedEvent(DateTimeOffset.UtcNow, "File name", "Edit"));

        var spoken = await engine.WaitForAsync(s => s.Text.StartsWith("Save As"));
        Assert.Contains("File name", spoken.Text);
    }
}
