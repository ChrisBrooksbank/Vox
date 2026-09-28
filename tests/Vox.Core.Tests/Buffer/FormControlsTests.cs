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
