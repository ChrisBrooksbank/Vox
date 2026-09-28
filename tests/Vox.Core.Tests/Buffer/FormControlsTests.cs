using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class FormControlsTests
{
    [Theory]
    [InlineData("Edit", null, true)]
    [InlineData("CheckBox", null, true)]
    [InlineData("List", null, false)]       // <ul>
    [InlineData("ListItem", null, false)]   // <li>
    [InlineData("List", "listbox", true)]
    [InlineData("ListItem", "option", true)]
    [InlineData("Hyperlink", null, false)]
    public void IsFormField(string controlType, string? role, bool expected)
    {
        Assert.Equal(expected, FormControls.IsFormField(controlType, role));
    }

    [Theory]
    [InlineData("Edit", null, true)]
    [InlineData("ComboBox", null, true)]
    [InlineData("List", null, false)]
    [InlineData("List", "listbox", true)]
    [InlineData("CheckBox", null, false)]
    [InlineData("Button", null, false)]
    public void NeedsFocusMode(string controlType, string? role, bool expected)
    {
        Assert.Equal(expected, FormControls.NeedsFocusMode(controlType, role));
    }

    [Fact]
    public void Document_FormFieldsIndex_ExcludesPlainListItems()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var list = new MockElement { RuntimeId = [2], ControlType = "List" };
        list.AddChild(new MockElement { RuntimeId = [3], Name = "Bullet", ControlType = "ListItem" });
        root.AddChild(list);
        root.AddChild(new MockElement { RuntimeId = [4], Name = "Email", ControlType = "Edit" });

        var doc = new VBufferBuilder().Build(root);

        var field = Assert.Single(doc.FormFields);
        Assert.Equal("Email", field.Name);
        Assert.DoesNotContain(doc.FocusableElements, n => n.ControlType == "ListItem");
    }
}

public class FormControlsRound6Tests
{
    [Theory]
    [InlineData("MenuItem", null)]
    [InlineData("Menu", null)]
    [InlineData("MenuBar", null)]
    [InlineData("TabItem", null)]
    [InlineData("Custom", "menuitem")]
    [InlineData("Custom", "menuitemcheckbox")]
    [InlineData("Custom", "menuitemradio")]
    [InlineData("Custom", "tab")]
    public void MenusAndTabs_NeedFocusMode(string controlType, string? role) =>
        Assert.True(FormControls.NeedsFocusMode(controlType, role));

    [Fact]
    public void SpokenValue_MultiLine_SpeaksOnlyTheFirstLine()
    {
        var node = new VBufferNode { Name = "Comment", ControlType = "Edit", Value = "First line\nSecond line\nThird" };
        Assert.Equal("First line…", FormControls.SpokenValue(node));
    }

    [Fact]
    public void SpokenValue_LongLine_IsCutAtAWordBoundary()
    {
        var node = new VBufferNode { Name = "Draft", ControlType = "Edit", Value = "one two three four five six" };
        Assert.Equal("one two…", FormControls.SpokenValue(node, maxLength: 10));
    }

    [Fact]
    public void SpokenValue_ShortValue_IsUnchanged()
    {
        var node = new VBufferNode { Name = "Search", ControlType = "Edit", Value = "hello" };
        Assert.Equal("hello", FormControls.SpokenValue(node));
    }

    [Fact]
    public void BufferValue_IsCapped_AndKeepsLines()
    {
        var huge = new VBufferNode { Name = "Log", ControlType = "Edit", Value = new string('x', 5000) };
        var value = FormControls.BufferValue(huge)!;
        Assert.Equal(FormControls.MaxBufferValueLength + 1, value.Length); // plus "…"

        var lines = new VBufferNode { Name = "Notes", ControlType = "Edit", Value = "a\r\nb" };
        Assert.Equal("a\nb", FormControls.BufferValue(lines));
    }
}
