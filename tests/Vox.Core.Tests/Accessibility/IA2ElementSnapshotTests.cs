using Xunit;
using Vox.Core.Accessibility;
using Vox.Core.Buffer;

namespace Vox.Core.Tests.Accessibility;

public class IA2ElementSnapshotTests
{
    private static IA2Node Node(int id, int role, string name = "", long states = 0, Dictionary<string, string>? attrs = null,
        Dictionary<string, IReadOnlyList<int>>? relations = null, params IA2Node[] children) => new()
    {
        UniqueId = id, Role = role, Name = name, States = states,
        Attributes = attrs ?? new(), Relations = relations ?? new(), Children = children,
    };

    [Fact]
    public void Heading_HasLevelAndAriaRole()
    {
        var e = IA2ElementSnapshot.From(Node(1, 0x414, "Title", attrs: new() { ["xml-roles"] = "heading", ["level"] = "2" }));
        Assert.Equal("heading", e.AriaRole);
        Assert.Equal(2, e.HeadingLevel);
    }

    [Fact]
    public void VisitedLink_IsVisited()
    {
        var e = IA2ElementSnapshot.From(Node(1, 30, "x", states: 0x800000));
        Assert.True(e.IsVisited);
        Assert.Equal("Hyperlink", e.ControlType);
    }

    [Fact]
    public void Password_HidesValue()
    {
        var node = Node(1, 42, states: 0x20000000) with { Value = "secret" };
        var e = IA2ElementSnapshot.From(node);
        Assert.True(e.IsPassword);
        Assert.Equal("", e.Value);
    }

    [Fact]
    public void AriaStates_AreCarriedInAriaProperties()
    {
        var e = IA2ElementSnapshot.From(Node(1, 30, attrs: new() { ["current"] = "page", ["sort"] = "ascending" }));
        Assert.Equal("page", AriaStates.Current(e.AriaProperties));
        Assert.Equal("ascending", AriaStates.Sort(e.AriaProperties));
    }

    [Fact]
    public void InvalidEntry_WithErrorMessageRelation()
    {
        var rel = new Dictionary<string, IReadOnlyList<int>> { ["errorMessage"] = new[] { 9 } };
        var e = IA2ElementSnapshot.From(Node(1, 42, states: 0x80L << 32, relations: rel));
        Assert.False(e.IsDataValidForForm);
        Assert.Equal(9, Assert.Single(e.ErrorMessageIds)[0]);
    }

    [Fact]
    public void CheckBox_ToggleState()
    {
        Assert.Equal(1, IA2ElementSnapshot.From(Node(1, 44, states: 0x10)).ToggleState);
        Assert.Equal(0, IA2ElementSnapshot.From(Node(1, 44)).ToggleState);
        Assert.Equal(2, IA2ElementSnapshot.From(Node(1, 44, states: 0x20)).ToggleState);
        Assert.Null(IA2ElementSnapshot.From(Node(1, 30)).ToggleState);
    }

    [Fact]
    public void Language_FromAttributeOrTextRun()
    {
        Assert.Equal("fr", IA2ElementSnapshot.From(Node(1, 41, attrs: new() { ["lang"] = "fr" })).Language);
        var withRun = Node(1, 41) with { TextRuns = [new IA2TextRun(0, 3, new Dictionary<string, string> { ["language"] = "de" })] };
        Assert.Equal("de", IA2ElementSnapshot.From(withRun).Language);
    }

    [Fact]
    public void Builder_FindsLandmarksAndHeadings_FromSnapshot()
    {
        var tree = Node(1, 15, "Page", children:
        [
            Node(2, 0x424, attrs: new() { ["xml-roles"] = "navigation" }, children: [Node(3, 41, "Menu")]),
            Node(4, 0x414, "Welcome", attrs: new() { ["xml-roles"] = "heading", ["level"] = "1" }),
        ]);
        var doc = new VBufferBuilder().Build(IA2ElementSnapshot.From(tree));
        Assert.Single(doc.Headings);
        Assert.Single(doc.Landmarks);
    }

    [Fact]
    public void DeepTree_DoesNotOverflow()
    {
        var node = Node(0, 41, "leaf");
        for (int i = 1; i < 5000; i++) node = Node(i, 20, children: [node]);
        Assert.NotNull(IA2ElementSnapshot.From(node));
    }
}
