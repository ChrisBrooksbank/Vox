using Vox.Core.Ai;
using Vox.Core.Buffer;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Ai;

public class PageQuestionTests
{
    // Document: heading "Opening hours", text "Mon to Fri 9 to 5", link "Contact us", button "Book"
    private static VBufferDocument Page()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Value = "https://museum.example.org/" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Opening hours", AriaRole = "heading", AriaProperties = "level=2" });
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Mon to Fri 9 to 5" });
        root.AddChild(new MockElement { RuntimeId = [4], Name = "Contact us", ControlType = "Hyperlink", IsFocusable = true });
        root.AddChild(new MockElement { RuntimeId = [5], Name = "Book", ControlType = "Button", IsFocusable = true });
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public void ThePage_IsSentAsNumberedLines()
    {
        var question = PageQuestion.For(Page());

        Assert.StartsWith("[1] Opening hours\n", question.Context);
        // Elements inside a line have numbers of their own
        Assert.Equal("[1] Opening hours\n[2] Mon to Fri 9 to 5 [3] Contact us\n[4] Book\n", question.Context);
        Assert.Equal(4, question.LineCount);
    }

    [Fact]
    public void AnAnswer_NamingALine_GivesItsElement()
    {
        var document = Page();
        var question = PageQuestion.For(document);
        int line = LineOf(question, "Contact us");

        var (text, target, targetText) = question.Read($"Use the contact link [{line}].\nGO TO: [{line}]");

        Assert.Equal("Use the contact link.", text);
        Assert.NotNull(target);
        Assert.Equal([4], target!.UIARuntimeId);
        Assert.Equal("Contact us", targetText);
        Assert.Equal(3, line);
    }

    [Theory]
    [InlineData("It doesn't say.\nGO TO: [42]")]
    [InlineData("It doesn't say.\nGO TO: [0]")]
    [InlineData("It doesn't say.")]
    public void AnAnswer_NamingNoLineOfThePage_GivesNone(string answer)
    {
        var (text, target, _) = PageQuestion.For(Page()).Read(answer);

        Assert.Equal("It doesn't say.", text);
        Assert.Null(target);
    }

    [Theory]
    [InlineData("go to: 2")]
    [InlineData("GO TO [2].")]
    [InlineData("  GO TO:[2]  ")]
    public void TheGoToLine_IsReadLeniently(string line)
    {
        var (text, target, _) = PageQuestion.For(Page()).Read("Here.\n" + line);

        Assert.Equal("Here.", text);
        Assert.NotNull(target);
    }

    [Fact]
    public void WhileAModalDialogIsOpen_OnlyItIsSent()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Value = "https://example.com/" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Behind the dialog" });
        var dialog = new MockElement { RuntimeId = [3], ControlType = "Group", AriaRole = "dialog", AriaProperties = "modal=true" };
        dialog.AddChild(new MockElement { RuntimeId = [4], Name = "Accept the terms?" });
        root.AddChild(dialog);
        var document = new VBufferBuilder().Build(root);
        Assert.NotNull(document.ModalScope);

        var context = PageQuestion.For(document).Context;

        Assert.Contains("Accept the terms?", context);
        Assert.DoesNotContain("Behind the dialog", context);
    }

    private static int LineOf(PageQuestion question, string text)
    {
        var context = question.Context;
        int at = context.IndexOf("] " + text, StringComparison.Ordinal);
        int open = context.LastIndexOf('[', at);
        return int.Parse(context[(open + 1)..at]);
    }
}
