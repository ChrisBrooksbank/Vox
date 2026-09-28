using Vox.Core.Accessibility;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class UIAElementSnapshotTests
{
    [Theory]
    [InlineData(80050, 0)] // HeadingLevel_None
    [InlineData(80051, 1)]
    [InlineData(80053, 3)]
    [InlineData(80059, 9)]
    [InlineData(80060, 0)]
    [InlineData(0, 0)]
    public void HeadingLevelFromUia_MapsUiaConstants(int value, int expected)
    {
        Assert.Equal(expected, UIAElementSnapshot.HeadingLevelFromUia(value));
    }

    [Fact]
    public void HeadingLevelFromUia_NonIntValue_IsZero()
    {
        Assert.Equal(0, UIAElementSnapshot.HeadingLevelFromUia(null));
        Assert.Equal(0, UIAElementSnapshot.HeadingLevelFromUia("80052"));
    }
}
