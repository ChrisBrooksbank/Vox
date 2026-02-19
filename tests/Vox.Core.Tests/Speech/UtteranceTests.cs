using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class UtteranceTests
{
    [Fact]
    public void Utterance_Record_HasCorrectProperties()
    {
        var utterance = new Utterance("Hello world", SpeechPriority.High, "browse_mode");

        Assert.Equal("Hello world", utterance.Text);
        Assert.Equal(SpeechPriority.High, utterance.Priority);
        Assert.Equal("browse_mode", utterance.SoundCue);
    }

    [Fact]
    public void Utterance_DefaultSoundCue_IsNull()
    {
        var utterance = new Utterance("Hello", SpeechPriority.Normal);
        Assert.Null(utterance.SoundCue);
    }

    [Theory]
    [InlineData(SpeechPriority.Interrupt, 0)]
    [InlineData(SpeechPriority.High, 1)]
    [InlineData(SpeechPriority.Normal, 2)]
    [InlineData(SpeechPriority.Low, 3)]
    public void SpeechPriority_Values_AreOrdered(SpeechPriority priority, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)priority);
    }

    [Fact]
    public void SpeechPriority_Interrupt_IsLowestNumericValue()
    {
        Assert.True(SpeechPriority.Interrupt < SpeechPriority.High);
        Assert.True(SpeechPriority.High < SpeechPriority.Normal);
        Assert.True(SpeechPriority.Normal < SpeechPriority.Low);
    }
}
