using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class PageElementsTests
{
    private static VBufferNode Node(string controlType, string ariaRole = "", VBufferNode? parent = null) =>
        new() { ControlType = controlType, AriaRole = ariaRole, Parent = parent };

    [Theory]
    [InlineData("List", "", true)]
    [InlineData("List", "list", true)]
    [InlineData("Group", "list", true)]
    [InlineData("List", "listbox", false)]
    [InlineData("Tree", "tree", false)]
    [InlineData("Text", "", false)]
    public void IsList_ExcludesListBoxesAndTrees(string controlType, string ariaRole, bool expected) =>
        Assert.Equal(expected, PageElements.IsList(Node(controlType, ariaRole)));

    [Theory]
    [InlineData("ListItem", "", true)]
    [InlineData("ListItem", "listitem", true)]
    [InlineData("ListItem", "option", false)]
    [InlineData("TreeItem", "treeitem", false)]
    public void IsListItem_ExcludesOptions(string controlType, string ariaRole, bool expected) =>
        Assert.Equal(expected, PageElements.IsListItem(Node(controlType, ariaRole)));

    [Theory]
    [InlineData("Image", "", true)]
    [InlineData("Group", "img", true)]
    [InlineData("Group", "graphics-document", true)]
    [InlineData("Text", "", false)]
    public void IsGraphic(string controlType, string ariaRole, bool expected) =>
        Assert.Equal(expected, PageElements.IsGraphic(Node(controlType, ariaRole)));

    [Theory]
    [InlineData("Group", "blockquote", true)]
    [InlineData("Group", "", false)]
    public void IsBlockQuote(string controlType, string ariaRole, bool expected) =>
        Assert.Equal(expected, PageElements.IsBlockQuote(Node(controlType, ariaRole)));

    [Theory]
    [InlineData("Separator", "", true)]
    [InlineData("Group", "separator", true)]
    [InlineData("Text", "", false)]
    public void IsSeparator(string controlType, string ariaRole, bool expected) =>
        Assert.Equal(expected, PageElements.IsSeparator(Node(controlType, ariaRole)));

    [Theory]
    [InlineData("Pane", "application", true)]
    [InlineData("Group", "video", true)]
    [InlineData("Pane", "embeddedobject", true)]
    [InlineData("Pane", "", false)]
    public void IsEmbeddedObject(string controlType, string ariaRole, bool expected) =>
        Assert.Equal(expected, PageElements.IsEmbeddedObject(Node(controlType, ariaRole)));

    [Fact]
    public void IsFrame_TheRootDocumentIsNot()
    {
        Assert.False(PageElements.IsFrame(Node("Document")));
    }

    [Fact]
    public void IsFrame_ANestedDocumentIs()
    {
        var root = Node("Document");
        Assert.True(PageElements.IsFrame(Node("Document", parent: root)));
    }

    [Fact]
    public void IsFrame_AnIframeIsFoundOnceNotAgainForItsDocument()
    {
        var root = Node("Document");
        var iframe = Node("Pane", "iframe", root);

        Assert.True(PageElements.IsFrame(iframe));
        Assert.False(PageElements.IsFrame(Node("Document", parent: iframe)));
    }
}
