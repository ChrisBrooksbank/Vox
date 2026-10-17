using Xunit;
using Vox.Core.Accessibility;

namespace Vox.Core.Tests.Accessibility;

public class IA2SnapshotReaderTests
{
    private const string Json = """
    {"id":1,"role":15,"states":65,"name":"Page","attributes":{"xml-roles":"document"},
     "children":[
       {"id":2,"role":22,"name":"Title","attributes":{"level":"2","xml-roles":"heading"},
        "textRuns":[{"start":0,"end":5,"attributes":{"language":"fr"}}]},
       {"id":3,"role":40,"name":"Email","value":"x","relations":{"errorMessage":[7,8],"describedby":[9]}}]}
    """;

    [Fact]
    public void Parses_tree_attributes_relations_and_runs()
    {
        var root = IA2SnapshotReader.TryParse(Json)!;
        Assert.Equal(1, root.UniqueId);
        Assert.Equal("document", root.Attribute("xml-roles"));
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("2", root.Children[0].Attribute("level"));
        Assert.Equal("fr", root.Children[0].TextRuns[0].Attributes["language"]);
        Assert.Equal([7, 8], root.Children[1].Relations["errorMessage"]);
        Assert.Equal("x", root.Children[1].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void Returns_null_for_non_snapshots(string text) => Assert.Null(IA2SnapshotReader.TryParse(text));

    [Fact]
    public void Missing_fields_default()
    {
        var n = IA2SnapshotReader.TryParse("{}")!;
        Assert.Equal("", n.Name);
        Assert.Empty(n.Children);
        Assert.Null(n.Attribute("level"));
    }
}
