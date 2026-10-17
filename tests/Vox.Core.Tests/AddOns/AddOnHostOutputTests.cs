using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AddOns;
using Vox.Core.Input;
using Vox.Sdk;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class AddOnHostOutputTests
{
    private sealed class NoSpeech : ISpeechOutput
    {
        public void Speak(string text, SpeechPriority priority = SpeechPriority.Normal) { }
        public void StopSpeaking() { }
    }

    private sealed class Recorder
    {
        public List<string> Earcons { get; } = [];
        public List<string> Messages { get; } = [];
        public List<string> Documents { get; } = [];
        public List<NavigationCommand> Commands { get; } = [];

        public AddOnIntegration Integration => new()
        {
            PlayEarcon = Earcons.Add,
            ShowBrailleMessage = Messages.Add,
            ShowDocument = text => { Documents.Add(text); return Task.CompletedTask; },
            RunCommand = Commands.Add,
        };
    }

    private static AddOnHost HostFor(Recorder recorder) =>
        new("x", () => Path.GetTempPath(), NullLogger.Instance, new NoSpeech(), () => null, work => work(), null, recorder.Integration);

    [Fact]
    public async Task Output_ReachesVox()
    {
        var recorder = new Recorder();
        var host = HostFor(recorder);

        host.PlayEarcon("boundary");
        host.ShowBrailleMessage("Saved");
        await host.ShowDocumentAsync("Line one\nLine two");

        Assert.Equal(["boundary"], recorder.Earcons);
        Assert.Equal(["Saved"], recorder.Messages);
        Assert.Equal(["Line one\nLine two"], recorder.Documents);
    }

    [Fact]
    public void RunCommand_RunsByName_AndRefusesUnknownAndQuit()
    {
        var recorder = new Recorder();
        var host = HostFor(recorder);
        var name = Enum.GetNames<NavigationCommand>().First(n => n != nameof(NavigationCommand.Quit));

        Assert.True(host.RunCommand(name));
        Assert.False(host.RunCommand("NoSuchCommand"));
        Assert.False(host.RunCommand("999"));
        Assert.False(host.RunCommand(nameof(NavigationCommand.Quit)));

        Assert.Single(recorder.Commands);
        Assert.Equal(Enum.Parse<NavigationCommand>(name), recorder.Commands[0]);
    }

    [Fact]
    public async Task Output_IsIgnoredWithoutIntegration_AndAfterStopping()
    {
        var bare = new AddOnHost("x", () => Path.GetTempPath(), NullLogger.Instance, new NoSpeech(), () => null, work => work());
        bare.PlayEarcon("boundary");
        await bare.ShowDocumentAsync("text");
        Assert.False(bare.RunCommand(Enum.GetNames<NavigationCommand>().First(n => n != nameof(NavigationCommand.Quit))));

        var recorder = new Recorder();
        var host = HostFor(recorder);
        host.Stop();
        host.PlayEarcon("boundary");
        host.ShowBrailleMessage("late");
        await host.ShowDocumentAsync("late");
        Assert.False(host.RunCommand(nameof(NavigationCommand.Quit)));

        Assert.Empty(recorder.Earcons);
        Assert.Empty(recorder.Messages);
        Assert.Empty(recorder.Documents);
        Assert.Empty(recorder.Commands);
    }
}
