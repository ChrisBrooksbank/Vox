using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class DeveloperInfoTests
{
    [Fact]
    public void Format_OneLinePerField_NoneForMissing()
    {
        var info = new DeveloperInfo("Save", "Button", null, "Win32", "Button", "1", 4242, "notepad", [42, 7, 1]);

        var lines = info.Format().Split(Environment.NewLine);

        Assert.Equal("Name: Save", lines[0]);
        Assert.Equal("ARIA role: none", lines[2]);
        Assert.Equal("Process: notepad (4242)", lines[6]);
        Assert.Equal("Runtime id: 42.7.1", lines[7]);
    }
}
