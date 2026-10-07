using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class CapitalsRuleTests
{
    private static Utterance Apply(string text, CapitalIndication characters, CapitalIndication words = CapitalIndication.Off) =>
        new CapitalsRule(() => new VoxSettings { CapitalsForCharacters = characters, CapitalsForWords = words })
            .ApplyTo(new Utterance(text, SpeechPriority.Interrupt));

    [Fact]
    public void Character_Pitch_RaisesThePitch()
    {
        var spoken = Apply("A", CapitalIndication.Pitch);

        Assert.Equal("A", spoken.Text);
        Assert.Equal(CapitalsRule.PitchRaise, spoken.PitchOffset);
    }

    [Fact]
    public void Character_SayCap()
    {
        Assert.Equal("cap A", Apply("A", CapitalIndication.SayCap).Text);
    }

    [Fact]
    public void Character_Beep_AddsTheCue()
    {
        var spoken = Apply("Q", CapitalIndication.Beep);

        Assert.Equal("Q", spoken.Text);
        Assert.Equal(Utterance.CapitalCue, spoken.SoundCue);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1")]
    [InlineData(".")]
    public void Character_NotACapital_IsUnchanged(string text)
    {
        var utterance = new Utterance(text, SpeechPriority.Interrupt);
        Assert.Equal(utterance, new CapitalsRule(() => new VoxSettings { CapitalsForCharacters = CapitalIndication.SayCap }).ApplyTo(utterance));
    }

    [Fact]
    public void Character_Off_IsUnchanged()
    {
        var spoken = Apply("A", CapitalIndication.Off);

        Assert.Equal(("A", 0, (string?)null), (spoken.Text, spoken.PitchOffset, spoken.SoundCue));
    }

    [Fact]
    public void Words_UseTheirOwnSetting()
    {
        // The character setting doesn't apply to a word
        Assert.Equal(0, Apply("Hello", CapitalIndication.Pitch).PitchOffset);

        Assert.Equal(CapitalsRule.PitchRaise, Apply("Hello", CapitalIndication.Off, CapitalIndication.Pitch).PitchOffset);
        Assert.Equal("cap Hello", Apply("Hello", CapitalIndication.Off, CapitalIndication.SayCap).Text);
        Assert.Equal("all caps NASA", Apply("NASA", CapitalIndication.Off, CapitalIndication.SayCap).Text);
        Assert.Equal("hello", Apply("hello", CapitalIndication.Off, CapitalIndication.SayCap).Text);
    }

    [Fact]
    public void Lines_SayCapBeforeEachCapitalizedWord()
    {
        Assert.Equal("cap Tom met all caps NASA staff", Apply("Tom met NASA staff", CapitalIndication.Off, CapitalIndication.SayCap).Text);
    }

    [Fact]
    public void Lines_PitchAndBeep_DoNotApply()
    {
        var spoken = Apply("Tom met NASA staff", CapitalIndication.Off, CapitalIndication.Pitch);

        Assert.Equal(0, spoken.PitchOffset);
        Assert.Null(spoken.SoundCue);
    }

    [Fact]
    public async Task Queue_PlaysTheCueBeforeSpeaking()
    {
        var engine = new RecordingSpeechEngine();
        var cues = new List<(string Cue, int SpokenBefore)>();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance)
        {
            TextProcessor = new TextProcessor([new CapitalsRule(() => new VoxSettings { CapitalsForCharacters = CapitalIndication.Beep })]),
        };
        queue.CuePlayer = cue => cues.Add((cue, engine.SpokenText.Count));

        queue.Enqueue(new Utterance("B", SpeechPriority.Interrupt));

        await engine.WaitForTextAsync("B");
        Assert.Equal([(Utterance.CapitalCue, 0)], cues);
    }
}
