using Vox.Core.AppModules.PowerPoint;
using Xunit;

namespace Vox.Core.Tests.AppModules.PowerPoint;

// Plain stand-ins for PowerPoint's object model, read late bound by PowerPointObjectModelSource
// as COM objects would be. PowerPoint's yes/no properties are MsoTriState numbers, not bools.

public sealed class FakePptApp
{
    public FakePptWindow ActiveWindow { get; } = new();
    public FakePptPresentation ActivePresentation { get; } = new();
}

public sealed class FakePptWindow
{
    public FakePptView View { get; } = new();
}

public sealed class FakePptView
{
    public FakePptSlide Slide { get; } = new();
}

public sealed class FakePptPresentation
{
    public FakePptCount Slides { get; } = new() { Count = 10 };
}

public sealed class FakePptCount
{
    public int Count { get; set; }
}

public sealed class FakePptSlide
{
    public int SlideIndex { get; set; } = 3;
    public FakePptShapes Shapes { get; } = new();
}

public sealed class FakePptShapes
{
    public List<FakePptShape> List { get; } = [];
    public int Count => List.Count;
    public FakePptShape Item(int index) => List[index - 1];
}

public sealed class FakePptShape
{
    public string Name { get; set; } = string.Empty;
    public int Type { get; set; } = 14; // msoPlaceholder
    public int HasTextFrame { get; set; } = -1; // msoTrue
    public FakePptTextFrame TextFrame { get; set; } = new();
    public string AlternativeText { get; set; } = string.Empty;
    public FakePptPlaceholder PlaceholderFormat { get; set; } = new();
}

public sealed class FakePptTextFrame
{
    public int HasText { get; set; } = -1;
    public FakePptText TextRange { get; set; } = new();
}

public sealed class FakePptText
{
    public string Text { get; set; } = string.Empty;
}

public sealed class FakePptPlaceholder
{
    public int Type { get; set; }
}

public class PowerPointObjectModelSourceTests
{
    private static FakePptShape TextShape(string name, string text, int placeholder) => new()
    {
        Name = name,
        TextFrame = new FakePptTextFrame { TextRange = new FakePptText { Text = text } },
        PlaceholderFormat = new FakePptPlaceholder { Type = placeholder },
    };

    [Fact]
    public void ReadSlide_ReadsTextWhenPowerPointSaysMsoTrue()
    {
        var app = new FakePptApp();
        app.ActiveWindow.View.Slide.Shapes.List.Add(TextShape("Title 1", "Results", placeholder: 1));
        app.ActiveWindow.View.Slide.Shapes.List.Add(TextShape("Content 2", "Up 5%", placeholder: 2));

        var slide = PowerPointObjectModelSource.ReadSlide(app)!;

        Assert.Equal("Results", slide.Title);
        Assert.Equal(["Results", "Up 5%"], slide.Shapes.Select(s => s.Text));
        Assert.Equal("Slide 3 of 10, Results", PowerPointFormatter.SlideChanged(slide));
    }

    [Fact]
    public void ReadSlide_NoTextFrameOrNoText_NoText()
    {
        var app = new FakePptApp();
        var noFrame = TextShape("Picture 3", "ignored", placeholder: 0);
        noFrame.HasTextFrame = 0; // msoFalse
        app.ActiveWindow.View.Slide.Shapes.List.Add(noFrame);
        var empty = TextShape("Content 4", "ignored", placeholder: 2);
        empty.TextFrame.HasText = 0;
        app.ActiveWindow.View.Slide.Shapes.List.Add(empty);

        var slide = PowerPointObjectModelSource.ReadSlide(app)!;

        Assert.All(slide.Shapes, s => Assert.Null(s.Text));
    }

    [Fact]
    public void IsTrue_AcceptsTriStatesAndBools()
    {
        Assert.True(PowerPointObjectModelSource.IsTrue(-1));
        Assert.True(PowerPointObjectModelSource.IsTrue(1));
        Assert.True(PowerPointObjectModelSource.IsTrue(true));
        Assert.False(PowerPointObjectModelSource.IsTrue(0));
        Assert.False(PowerPointObjectModelSource.IsTrue(false));
        Assert.False(PowerPointObjectModelSource.IsTrue(null));
    }
}
