using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class NotRespondingReporterTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly List<AppNotRespondingEvent> _posted = new();
    private readonly Mock<IForegroundApp> _foreground = new();
    private DateTimeOffset _now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed class Sink(List<AppNotRespondingEvent> posted) : IEventSink
    {
        public void Post(ScreenReaderEvent evt)
        {
            if (evt is AppNotRespondingEvent e)
                lock (posted) posted.Add(e);
        }
    }

    public void Dispose() => _uiaThread.Dispose();

    private NotRespondingReporter CreateReporter() =>
        new(_uiaThread, _foreground.Object, new Sink(_posted), NullLogger<NotRespondingReporter>.Instance, () => _now);

    [Fact]
    public void Report_SameProcessWithinTenSeconds_SpokenOnce()
    {
        using var reporter = CreateReporter();

        Assert.True(reporter.Report(1234, "Slow App"));
        _now += TimeSpan.FromSeconds(9);
        Assert.False(reporter.Report(1234, "Slow App"));
        _now += TimeSpan.FromSeconds(1);
        Assert.True(reporter.Report(1234, "Slow App"));

        Assert.Equal(2, _posted.Count);
    }

    [Fact]
    public void Report_DifferentProcesses_ThrottledSeparately()
    {
        using var reporter = CreateReporter();

        Assert.True(reporter.Report(1, "One"));
        Assert.True(reporter.Report(2, "Two"));
    }

    [Fact]
    public void Report_OwnProcess_NeverReported()
    {
        using var reporter = CreateReporter();

        Assert.False(reporter.Report(Environment.ProcessId, "Vox"));
        Assert.Empty(_posted);
    }

    [Fact]
    public async Task UIACallTimeout_ReportsTheForegroundApp()
    {
        _foreground.Setup(f => f.Get()).Returns(new ForegroundAppInfo(4321, "Hung Browser"));
        using var reporter = CreateReporter();
        using var release = new ManualResetEventSlim();

        await Assert.ThrowsAsync<UIATimeoutException>(() =>
            _uiaThread.RunAsync(() => release.Wait(), TimeSpan.FromMilliseconds(50)));
        release.Set();
        // The timeout is raised to the caller and to the reporter independently
        for (int i = 0; i < 200 && _posted.Count == 0; i++)
            await Task.Delay(10);

        var evt = Assert.Single(_posted);
        Assert.Equal(4321, evt.ProcessId);
        Assert.Equal("Hung Browser", evt.AppName);
    }

    [Fact]
    public async Task Pipeline_SpeaksAppNotRespondingAtHighPriority()
    {
        var engine = new RecordingSpeechEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);
        using var pipeline = new EventPipeline(queue, Mock.Of<IAudioCuePlayer>(), NullLogger<EventPipeline>.Instance);

        pipeline.Post(new AppNotRespondingEvent(DateTimeOffset.UtcNow, 99, "Notepad"));

        var spoken = await engine.WaitForTextAsync("Notepad not responding");
        Assert.Equal(SpeechPriority.High, spoken.Priority);
    }
}
