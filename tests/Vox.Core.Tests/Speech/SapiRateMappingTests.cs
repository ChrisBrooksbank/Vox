using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class SapiRateMappingTests
{
    [Theory]
    [InlineData(180, 0)]
    [InlineData(200, 1)]
    [InlineData(150, -2)]
    [InlineData(450, 8)]
    [InlineData(540, 10)]
    [InlineData(60, -10)]
    [InlineData(10000, 10)]
    public void WpmToSapiRate_IsLogarithmicAroundNormalSpeed(int wpm, int expected)
    {
        Assert.Equal(expected, SapiSpeechEngine.WpmToSapiRate(wpm));
    }

    [Fact]
    public void WpmToSapiRate_IsMonotonic()
    {
        int previous = int.MinValue;
        for (int wpm = 150; wpm <= 450; wpm += 10)
        {
            var rate = SapiSpeechEngine.WpmToSapiRate(wpm);
            Assert.True(rate >= previous);
            previous = rate;
        }
    }
}
