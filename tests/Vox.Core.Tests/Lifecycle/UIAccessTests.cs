using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class UIAccessTests
{
    [Fact]
    public void IsGranted_TestHost_HasNoUIAccess()
    {
        // The test host is neither signed for UI access nor in a secure location
        Assert.NotEqual(true, UIAccess.IsGranted());
    }

    [Theory]
    [InlineData(true, "granted")]
    [InlineData(false, "No UI access")]
    [InlineData(null, "unknown")]
    public void Describe(bool? granted, string expected)
    {
        Assert.Contains(expected, UIAccess.Describe(granted));
    }
}
