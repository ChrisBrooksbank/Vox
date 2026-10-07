using System.Globalization;
using Vox.Core.Navigation;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class WhereAmITests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void TimeAndDate()
    {
        var now = new DateTime(2026, 10, 7, 15, 45, 0);

        Assert.Equal("3:45 PM", WhereAmI.TimeText(now, English));
        Assert.Equal("Wednesday, October 7, 2026", WhereAmI.DateText(now, English));
    }

    [Theory]
    [InlineData(true, 85, false, false, "85 percent, on battery")]
    [InlineData(true, 40, true, true, "40 percent, charging")]
    [InlineData(true, 100, false, true, "100 percent, plugged in")]
    [InlineData(false, null, false, true, "No battery, plugged in")]
    public void Battery(bool hasBattery, int? percent, bool charging, bool pluggedIn, string expected)
    {
        Assert.Equal(expected, WhereAmI.BatteryText(new PowerInfo(hasBattery, percent, charging, pluggedIn)));
    }

    private static MockElement Notepad() =>
        new MockElement { Name = "Untitled - Notepad", ControlType = "Window" }
            .AddChild(new MockElement { Name = "Text editor", ControlType = "Document", IsFocusable = true })
            .AddChild(new MockElement { ControlType = "StatusBar" }
                .AddChild(new MockElement { Name = "Ln 1, Col 1", ControlType = "Text" })
                .AddChild(new MockElement { Name = "100%", ControlType = "Text" })
                .AddChild(new MockElement { Name = "UTF-8", ControlType = "Text" }));

    [Fact]
    public void StatusBar_ReadsItsTexts()
    {
        Assert.Equal("Ln 1, Col 1, 100%, UTF-8", WhereAmI.StatusBarText(Notepad()));
    }

    [Fact]
    public void StatusBar_None()
    {
        Assert.Null(WhereAmI.StatusBarText(new MockElement { ControlType = "Window" }));
    }

    [Fact]
    public void WindowText_ReadsTitleTextAndControls()
    {
        var dialog = new MockElement { Name = "Save As", ControlType = "Window" }
            .AddChild(new MockElement { Name = "File name:", ControlType = "Text" })
            .AddChild(new MockElement { Name = "File name:", ControlType = "Edit", IsFocusable = true })
            .AddChild(new MockElement { Name = "Save", ControlType = "Button", IsFocusable = true }
                .AddChild(new MockElement { Name = "Save", ControlType = "Text" }));

        var text = WhereAmI.WindowText(dialog);

        Assert.StartsWith("Save As. File name:. File name:, edit", text);
        Assert.EndsWith("Save, button", text);
    }
}
