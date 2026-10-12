using Microsoft.Extensions.Logging;
using Vox.Sdk;

namespace Vox.SampleAddOn;

/// <summary>
/// The add-on's entry point: Vox creates it and calls <see cref="Initialize"/> once, where it adds
/// everything it brings. When the add-on is turned off Vox takes all of it back out and disposes it.
/// </summary>
public sealed class SampleAddOn : IAddOn, IDisposable
{
    private IAddOnHost? _host;

    public void Initialize(IAddOnHost host)
    {
        _host = host;
        var focusCounter = new FocusCounter();

        // A command everywhere, with its key
        host.AddCommand(new AddOnCommand("sayHello", "Say hello", "Says hello and how many times focus moved.",
            () => host.Speech.Speak($"Hello from the sample add-on. Focus moved {focusCounter.Count} times.", SpeechPriority.Interrupt))
        {
            Gestures = [KeyGesture.Parse("VoxKey+Ctrl+F12")],
        });

        // Support for one application
        host.AddAppModule(new NotepadModule(host.Speech));

        // A step every utterance goes through
        host.AddAnnouncementFilter(new AbbreviationFilter());

        // Sees every event, whatever application is in front
        host.AddGlobalPlugin(focusCounter);

        host.Logger.LogInformation("Sample add-on started on add-on API {Api}", host.ApiVersion);
    }

    public void Dispose() => _host?.Logger.LogInformation("Sample add-on stopped");
}

/// <summary>Notepad: Insert+Alt+F12 says how many words the text has.</summary>
public sealed class NotepadModule(ISpeechOutput speech) : AppModule
{
    private string? _text;

    public override string Id => "vox.sample.notepad";
    public override string Name => "Notepad";
    public override IReadOnlyCollection<string> ProcessNames => ["notepad"];

    public override IReadOnlyList<AddOnCommand> Commands =>
    [
        new AddOnCommand("countWords", "Count words", "Says how many words the text in Notepad has.", CountWords)
        {
            Gestures = [KeyGesture.Parse("VoxKey+Alt+F12")],
        },
    ];

    public override void OnEvent(VoxEvent evt)
    {
        // The focused edit's text (never a password's: Vox doesn't give it)
        if (evt is FocusEvent { Focus.Value: { } text })
            _text = text;
    }

    public override void OnDeactivated()
    {
        base.OnDeactivated();
        _text = null;
    }

    private void CountWords()
    {
        var words = (_text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        speech.Speak(words == 1 ? "1 word" : $"{words} words", SpeechPriority.Interrupt);
    }
}

/// <summary>Says abbreviations in full.</summary>
public sealed class AbbreviationFilter : IAnnouncementFilter
{
    public string? Filter(string text, AnnouncementInfo info) =>
        text.Contains("e.g.", StringComparison.Ordinal) ? text.Replace("e.g.", "for example", StringComparison.Ordinal) : null;
}

/// <summary>Counts focus changes.</summary>
public sealed class FocusCounter : GlobalPlugin
{
    private int _count;

    public int Count => _count;

    public override void OnEvent(VoxEvent evt)
    {
        if (evt is FocusEvent)
            Interlocked.Increment(ref _count);
    }
}
