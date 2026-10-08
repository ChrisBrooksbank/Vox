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

public class LegacyStateTests
{
    [Theory]
    [InlineData(0x800000, true)]    // STATE_SYSTEM_TRAVERSED
    [InlineData(0x800004, true)]
    [InlineData(0x100000, false)]   // focusable only
    [InlineData(0, false)]
    public void IsTraversed_ReadsVisitedBit(int state, bool expected) =>
        Assert.Equal(expected, UIAElementSnapshot.IsTraversed(state));
}

public class DescriptionTests
{
    [Theory]
    [InlineData(" Opens in a new window ", "Tooltip", "Opens in a new window")]
    [InlineData(null, " Tooltip ", "Tooltip")]
    [InlineData("  ", "Tooltip", "Tooltip")]
    [InlineData(null, null, "")]
    [InlineData("", " ", "")]
    public void DescriptionFrom_PrefersFullDescription(string? fullDescription, string? helpText, string expected) =>
        Assert.Equal(expected, UIAElementSnapshot.DescriptionFrom(fullDescription, helpText));
}
