using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.MathSpeech;
using Vox.Core.Tests.Buffer;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.MathSpeech;

public class MathMarkupTests
{
    private static int _id = 7000;

    private static MockElement Math(params MockElement[] children)
    {
        var math = new MockElement { RuntimeId = [_id++], ControlType = "Group", AriaRole = "math" };
        foreach (var child in children)
            math.AddChild(child);
        return math;
    }

    private static MockElement El(string tag, params MockElement[] children)
    {
        var element = new MockElement { RuntimeId = [_id++], ControlType = "Group", AriaRole = tag };
        foreach (var child in children)
            element.AddChild(child);
        return element;
    }

    private static MockElement Token(string tag, string text) => new() { RuntimeId = [_id++], Name = text, AriaRole = tag };

    private static VBufferDocument Page(MockElement math) =>
        new VBufferBuilder().Build(new MockElement { RuntimeId = [_id++], ControlType = "Document" }
            .AddChild(new MockElement { RuntimeId = [_id++], Name = "The formula " })
            .AddChild(math));

    [Fact]
    public void RebuildsMathMlFromTheTree()
    {
        var math = Math(El("mfrac", Token("mi", "a"), El("msup", Token("mi", "b"), Token("mn", "2"))), Token("mo", "<"));
        var document = Page(math);
        var node = document.FindByRuntimeId(math.RuntimeId)!;

        Assert.True(MathMarkup.IsMath(node));
        Assert.Equal("<math><mfrac><mi>a</mi><msup><mi>b</mi><mn>2</mn></msup></mfrac><mo>&lt;</mo></math>", MathMarkup.ToMathMl(node));
    }

    [Fact]
    public void MathMlInTheName_IsUsedAsIs()
    {
        var math = new MockElement { RuntimeId = [_id++], ControlType = "Image", AriaRole = "math", Name = "<math><mi>x</mi></math>" };
        var node = Page(math).FindByRuntimeId(math.RuntimeId)!;

        Assert.Equal("<math><mi>x</mi></math>", MathMarkup.ToMathMl(node));
    }

    [Fact]
    public void MathWithoutStructure_HasNoMathMl()
    {
        var math = Math(new MockElement { RuntimeId = [_id++], Name = "x squared" });
        var node = Page(math).FindByRuntimeId(math.RuntimeId)!;

        Assert.Null(MathMarkup.ToMathMl(node));
    }

    [Fact]
    public void Enclosing_FindsTheOutermostMath()
    {
        var inner = Token("mi", "y");
        var math = Math(El("mrow", inner));
        var document = Page(math);

        Assert.Same(document.FindByRuntimeId(math.RuntimeId), MathMarkup.Enclosing(document.FindByRuntimeId(inner.RuntimeId)));
        Assert.Null(MathMarkup.Enclosing(document.Root));
    }

    [Fact]
    public void Explorer_MapsArrowsToMathCatCommands_AndNeedsTheComponent()
    {
        var speech = new FakeMathSpeech();
        var explorer = new MathExplorer(speech);
        Assert.Null(explorer.Start("<math/>"));
        Assert.False(explorer.IsActive);

        speech.IsAvailable = true;
        Assert.Equal("x squared", explorer.Start("<math/>"));
        Assert.Equal((true, "x"), explorer.Handle(NavigationCommand.NextLine));
        Assert.Equal((true, "squared"), explorer.Handle(NavigationCommand.NextChar));
        Assert.Equal((false, null), explorer.Handle(NavigationCommand.NextHeading));
        Assert.Equal(["ZoomIn", "MoveNext"], speech.Commands);
    }
}
