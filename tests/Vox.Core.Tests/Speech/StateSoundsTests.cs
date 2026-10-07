using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class StateSoundsTests
{
    private static Utterance Speak(string announcement) =>
        new StateSoundsRule().ApplyTo(new Utterance(announcement, SpeechPriority.Interrupt));

    private static string Announce(VBufferNode node, bool sounds) =>
        new AnnouncementBuilder { StatesAsSounds = () => sounds }.Build(node, VerbosityLevel.Beginner);

    private static readonly VBufferNode CheckBox = new() { Name = "Remember me", ControlType = "CheckBox", ToggleState = 1 };

    [Fact]
    public void Off_StatesAreSpoken()
    {
        var spoken = Speak(Announce(CheckBox, sounds: false));

        Assert.Equal("Remember me, check box, checked", spoken.Text);
        Assert.Null(spoken.SoundCue);
    }

    [Fact]
    public void On_TheStateBecomesItsSound()
    {
        var spoken = Speak(Announce(CheckBox, sounds: true));

        Assert.Equal("Remember me, check box", spoken.Text);
        Assert.Equal(StateSounds.Checked, spoken.SoundCue);
    }

    [Theory]
    [InlineData(0, StateSounds.NotChecked)]
    [InlineData(2, StateSounds.HalfChecked)]
    public void ToggleStates_HaveTheirOwnSounds(int toggleState, string cue)
    {
        var node = new VBufferNode { Name = "Bold", ControlType = "CheckBox", ToggleState = toggleState };

        Assert.Equal(cue, Speak(Announce(node, sounds: true)).SoundCue);
    }

    [Fact]
    public void Expanded_AndSelected()
    {
        var combo = new VBufferNode { Name = "Country", ControlType = "ComboBox", IsExpandable = true, IsExpanded = false };
        var item = new VBufferNode { Name = "France", ControlType = "ListItem", IsSelected = true };

        Assert.Equal(StateSounds.Collapsed, Speak(Announce(combo, sounds: true)).SoundCue);
        Assert.Equal(StateSounds.Selected, Speak(Announce(item, sounds: true)).SoundCue);
    }

    [Fact]
    public void ASecondState_IsSpokenAsAWord()
    {
        var spoken = Speak($"Menu, {StateSounds.Mark(StateSounds.Expanded, "expanded")}, {StateSounds.Mark(StateSounds.Checked, "checked")}");

        Assert.Equal("Menu, checked", spoken.Text);
        Assert.Equal(StateSounds.Expanded, spoken.SoundCue);
    }

    [Fact]
    public void AnUtteranceWithACueAlready_SpeaksItsStates()
    {
        var spoken = new StateSoundsRule().ApplyTo(
            new Utterance($"A, {StateSounds.Mark(StateSounds.Checked, "checked")}", SpeechPriority.Interrupt, SoundCue: "capital"));

        Assert.Equal("A, checked", spoken.Text);
        Assert.Equal("capital", spoken.SoundCue);
    }

    [Fact]
    public void TextWithoutMarks_IsUnchanged()
    {
        var utterance = new Utterance("Read more, link", SpeechPriority.Interrupt);

        Assert.Same(utterance, new StateSoundsRule().ApplyTo(utterance));
    }

    [Fact]
    public void AStateOnItsOwn_IsJustTheSound()
    {
        var spoken = Speak(StateSounds.Mark(StateSounds.Checked, "checked"));

        Assert.Equal(string.Empty, spoken.Text);
        Assert.Equal(StateSounds.Checked, spoken.SoundCue);
    }
}
