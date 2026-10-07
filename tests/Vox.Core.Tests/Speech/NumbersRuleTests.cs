using Vox.Core.Configuration;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class NumbersRuleTests
{
    private static string Apply(string text, NumberReading reading) =>
        new NumbersRule(() => reading).Apply(text, new Utterance(text, SpeechPriority.Normal));

    [Theory]
    [InlineData("Room 1200, floor 3", "Room 1 2 0 0, floor 3")]
    [InlineData("pi is 3.14", "pi is 3.1 4")]
    [InlineData("no numbers", "no numbers")]
    [InlineData("٤٥", "٤ ٥")]
    public void Digits_ReadsNumbersDigitByDigit(string text, string expected)
    {
        Assert.Equal(expected, Apply(text, NumberReading.Digits));
    }

    [Fact]
    public void Words_LeavesNumbersToTheSynthesizer()
    {
        Assert.Equal("Room 1200", Apply("Room 1200", NumberReading.Words));
    }
}
