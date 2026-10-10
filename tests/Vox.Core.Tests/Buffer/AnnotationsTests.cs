using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class AnnotationsTests
{
    [Theory]
    [InlineData("comment", new int[0], Annotations.Comment)]
    [InlineData("insertion", new int[0], Annotations.Insertion)]
    [InlineData("deletion", new int[0], Annotations.Deletion)]
    [InlineData("mark", new int[0], Annotations.Highlight)]
    [InlineData("suggestion", new int[0], Annotations.Suggestion)]
    [InlineData("", new[] { 60003 }, Annotations.Comment)]
    [InlineData("", new[] { 60011 }, Annotations.Insertion)]
    [InlineData("", new[] { 60012 }, Annotations.Deletion)]
    [InlineData("", new[] { 60008 }, Annotations.Highlight)]
    [InlineData("", new[] { 60001 }, "")] // a spelling error is not a page annotation
    [InlineData("group", new int[0], "")]
    public void Kind_FromRoleOrUiaAnnotationTypes(string role, int[] types, string expected)
    {
        Assert.Equal(expected, Annotations.Kind(role, types));
    }

    private static int _id = 1000;

    private static MockElement Text(string name) => new() { RuntimeId = [_id++], Name = name };

    private static MockElement Marked(string role, params MockElement[] children)
    {
        var element = new MockElement { RuntimeId = [_id++], ControlType = "Group", AriaRole = role };
        foreach (var child in children)
            element.AddChild(child);
        return element;
    }

    // Paragraph: "The price is " <del>"10"</del> <ins>"12"</ins> " pounds"
    private static VBufferDocument PriceChange()
    {
        var paragraph = new MockElement { RuntimeId = [_id++], ControlType = "Group" }
            .AddChild(Text("The price is "))
            .AddChild(Marked("deletion", Text("10")))
            .AddChild(Marked("insertion", Text("12")))
            .AddChild(Text(" pounds"));
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document" }.AddChild(paragraph);
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public void Builder_KeepsInsertionsAndDeletionsOnTheirLine()
    {
        var document = PriceChange();

        Assert.Contains("The price is  10 12  pounds", document.FlatText.Replace("\n", " "));
        Assert.Single(document.FlatText.TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public void Transition_SaysWhatIsLeftAndEntered()
    {
        var document = PriceChange();
        VBufferNode At(string text) => document.FindNodeAtOffset(document.FlatText.IndexOf(text))!;

        Assert.Equal("deleted", Annotations.Transition(At("The price"), At("10")));
        Assert.Equal("end of deleted, inserted", Annotations.Transition(At("10"), At("12")));
        Assert.Equal("end of inserted", Annotations.Transition(At("12"), At(" pounds")));
        Assert.Null(Annotations.Transition(At("The price"), At(" pounds")));
    }

    [Fact]
    public void Transition_NestedAnnotations_LeavesInnermostFirst()
    {
        var inner = Text("note");
        var after = Text("after");
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document" }
            .AddChild(Marked("comment", Marked("mark", inner)))
            .AddChild(after);
        var document = new VBufferBuilder().Build(root);
        VBufferNode At(string text) => document.FindNodeAtOffset(document.FlatText.IndexOf(text))!;

        Assert.Equal("end of highlighted, end of comment", Annotations.Transition(At("note"), At("after")));
        Assert.Equal("comment, highlighted", Annotations.Transition(At("after"), At("note")));
    }

    [Fact]
    public void Cursor_MovingIntoADeletion_SaysDeleted()
    {
        var document = PriceChange();
        var cursor = new VBufferCursor(document, Mock.Of<IAudioCuePlayer>());
        var start = cursor.CurrentNode;
        cursor.MoveTo(document.FlatText.IndexOf("10"));

        Assert.Equal("deleted", Annotations.Transition(start, cursor.CurrentNode));
    }

    [Fact]
    public void ClickableElement_IsSaidOnEntry_ButNotItsDescendantsOrLinks()
    {
        var card = new MockElement { RuntimeId = [_id++], ControlType = "Group", IsInvokable = true }
            .AddChild(new MockElement { RuntimeId = [_id++], Name = "Card title", IsInvokable = true });
        var link = new MockElement { RuntimeId = [_id++], Name = "A link", ControlType = "Hyperlink", IsInvokable = true };
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document" }
            .AddChild(Text("before"))
            .AddChild(card)
            .AddChild(link);
        var document = new VBufferBuilder().Build(root);
        VBufferNode At(string text) => document.FindNodeAtOffset(document.FlatText.IndexOf(text))!;

        Assert.True(At("Card title").Parent!.IsClickable);
        Assert.False(At("Card title").IsClickable);
        Assert.False(At("A link").IsClickable);
        Assert.Equal("clickable", Annotations.Transition(At("before"), At("Card title")));
        Assert.Null(Annotations.Transition(At("Card title"), At("A link")));
    }

    [Fact]
    public void Figure_IsSaidOnEntryAndExit()
    {
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document" }
            .AddChild(Text("before"))
            .AddChild(Marked("figure", new MockElement { RuntimeId = [_id++], Name = "Chart", ControlType = "Image" }, Text("Sales by year")))
            .AddChild(Text("after"));
        var document = new VBufferBuilder().Build(root);
        VBufferNode At(string text) => document.FindNodeAtOffset(document.FlatText.IndexOf(text))!;

        Assert.Equal("figure", Annotations.Transition(At("before"), At("Chart")));
        Assert.Null(Annotations.Transition(At("Chart"), At("Sales by year")));
        Assert.Equal("out of figure", Annotations.Transition(At("Sales by year"), At("after")));
    }

    [Fact]
    public void Abbreviation_ExpansionIsSaidOnlyWithTheSetting()
    {
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document" }
            .AddChild(Text("before"))
            .AddChild(new MockElement { RuntimeId = [_id++], Name = "WHO", AriaRole = "abbr", Description = "World Health Organization" });
        var document = new VBufferBuilder().Build(root);
        VBufferNode At(string text) => document.FindNodeAtOffset(document.FlatText.IndexOf(text))!;

        Assert.Null(Annotations.Transition(At("before"), At("WHO")));
        Assert.Equal("World Health Organization", Annotations.Transition(At("before"), At("WHO"), expandAbbreviations: true));
    }
}
