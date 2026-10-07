using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class ShellAnnouncementsTests
{
    [Theory]
    [InlineData("explorer", true)]
    [InlineData("SearchHost", true)]
    [InlineData("StartMenuExperienceHost", true)]
    [InlineData("notepad", false)]
    [InlineData("chrome", false)]
    public void IsShellName(string name, bool expected)
    {
        Assert.Equal(expected, ShellProcesses.IsShellName(name));
    }

    [Fact]
    public void IsShell_UnknownProcess_IsFalse()
    {
        Assert.False(ShellProcesses.IsShell(-1));
        Assert.False(ShellProcesses.IsShell(Environment.ProcessId)); // the test host isn't the shell
    }

    [Fact]
    public async Task ToolTip_IsSpoken_AndARepeatWithinTwoSecondsIsNot()
    {
        var engine = new RecordingSpeechEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);
        using var pipeline = new EventPipeline(queue, Mock.Of<IAudioCuePlayer>(), NullLogger<EventPipeline>.Instance);
        var now = DateTimeOffset.UtcNow;

        pipeline.Post(new ToolTipOpenedEvent(now, "Bold (Ctrl+B)"));
        pipeline.Post(new ToolTipOpenedEvent(now.AddSeconds(1), "Bold (Ctrl+B)"));
        pipeline.Post(new ToolTipOpenedEvent(now.AddSeconds(1), "Italic (Ctrl+I)"));
        pipeline.Post(new ToolTipOpenedEvent(now.AddSeconds(4), "Bold (Ctrl+B)"));

        await engine.WaitForAsync(s => s.Text.Contains("Italic (Ctrl+I)"));
        await Task.Delay(150);
        // Normal utterances close together are joined into one, so count mentions
        var all = string.Join(" | ", engine.SpokenText);
        Assert.Equal(2, all.Split("Bold (Ctrl+B)").Length - 1);
    }
}
