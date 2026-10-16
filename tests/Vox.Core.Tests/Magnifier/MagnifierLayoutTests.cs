using Vox.Core.Magnifier;
using Vox.Core.Ocr;
using Xunit;

namespace Vox.Core.Tests.Magnifier;

public class MagnifierLayoutTests
{
    private static readonly ScreenRect Display = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(MagnifierDockEdge.Top, 0, 0, 1920, 324)]
    [InlineData(MagnifierDockEdge.Bottom, 0, 756, 1920, 324)]
    [InlineData(MagnifierDockEdge.Left, 0, 0, 576, 1080)]
    [InlineData(MagnifierDockEdge.Right, 1344, 0, 576, 1080)]
    public void Docked_RunsTheLengthOfItsEdge(MagnifierDockEdge edge, int left, int top, int width, int height) =>
        Assert.Equal(new ScreenRect(left, top, width, height), MagnifierLayout.Docked(Display, edge, 30));

    [Fact]
    public void Docked_OnASecondMonitor_IsOnThatMonitor() =>
        Assert.Equal(new ScreenRect(1920, 0, 1280, 102),
            MagnifierLayout.Docked(new ScreenRect(1920, 0, 1280, 1024), MagnifierDockEdge.Top, 10));

    [Theory]
    [InlineData(1, MagnifierLayout.MinSizePercent)]
    [InlineData(99, MagnifierLayout.MaxSizePercent)]
    public void Sizes_AreKeptInRange(int asked, int kept) => Assert.Equal(kept, MagnifierLayout.ClampSize(asked));

    [Fact]
    public void Lens_IsCentredOnThePoint()
    {
        var lens = MagnifierLayout.Lens(Display, 960, 540, 20);

        Assert.Equal(new ScreenRect(768, 432, 384, 216), lens);
    }

    [Fact]
    public void Lens_NearAnEdge_StaysOnTheDisplay()
    {
        var lens = MagnifierLayout.Lens(Display, 1915, 5, 20);

        Assert.Equal(new ScreenRect(1920 - 384, 0, 384, 216), lens);
    }

    [Fact]
    public void DisplayAt_FindsTheMonitorHoldingThePoint_ElseTheNearest()
    {
        var primary = Display;
        var second = new ScreenRect(1920, 0, 1280, 1024);

        Assert.Equal(second, MagnifierLayout.DisplayAt([primary, second], 2000, 100));
        Assert.Equal(primary, MagnifierLayout.DisplayAt([primary, second], 100, 100));
        // Below the second monitor, past the primary's right edge: the second is nearer
        Assert.Equal(second, MagnifierLayout.DisplayAt([primary, second], 2500, 1050));
    }

    [Fact]
    public void VirtualScreen_IsEveryMonitorTogether() =>
        Assert.Equal(new ScreenRect(-1280, -200, 3200, 1280),
            MagnifierLayout.VirtualScreen([Display, new ScreenRect(-1280, -200, 1280, 1024)]));

    [Fact]
    public void NextEdge_GoesRoundEveryEdge()
    {
        var edge = MagnifierDockEdge.Top;
        var seen = new List<MagnifierDockEdge>();
        for (int i = 0; i < 4; i++)
        {
            seen.Add(edge);
            edge = MagnifierLayout.NextEdge(edge);
        }

        Assert.Equal(MagnifierDockEdge.Top, edge);
        Assert.Equal(Enum.GetValues<MagnifierDockEdge>().Order(), seen.Order());
    }
}

public class MagnifierZoomTests
{
    [Fact]
    public void Steps_RunFromOneToSixteenTimes()
    {
        Assert.Equal(MagnifierZoom.MinPercent, MagnifierZoom.StepsPercent[0]);
        Assert.Equal(MagnifierZoom.MaxPercent, MagnifierZoom.StepsPercent[^1]);
        Assert.Equal(MagnifierZoom.StepsPercent.Order(), MagnifierZoom.StepsPercent);
    }

    [Fact]
    public void InAndOut_MoveOneStep_AndStopAtTheEnds()
    {
        Assert.Equal(250, MagnifierZoom.In(200));
        Assert.Equal(175, MagnifierZoom.Out(200));
        // Between steps: the next one either way
        Assert.Equal(250, MagnifierZoom.In(220));
        Assert.Equal(200, MagnifierZoom.Out(220));
        Assert.Null(MagnifierZoom.In(1600));
        Assert.Null(MagnifierZoom.Out(100));
    }

    [Fact]
    public void Factor_IsClamped()
    {
        Assert.Equal(2.5, MagnifierZoom.Factor(250));
        Assert.Equal(16, MagnifierZoom.Factor(5000));
        Assert.Equal(1, MagnifierZoom.Factor(0));
    }
}

public class MagnifierColorMatricesTests
{
    private static readonly (float, float, float) White = (1, 1, 1);
    private static readonly (float, float, float) Black = (0, 0, 0);

    private static void Near((float R, float G, float B) expected, (float R, float G, float B) actual)
    {
        Assert.Equal(expected.R, actual.R, 3);
        Assert.Equal(expected.G, actual.G, 3);
        Assert.Equal(expected.B, actual.B, 3);
    }

    [Fact]
    public void EveryMatrixIsFiveByFive()
    {
        foreach (var filter in Enum.GetValues<MagnifierColorFilter>())
            Assert.Equal(25, MagnifierColorMatrices.For(filter).Length);
    }

    [Fact]
    public void None_LeavesColoursAlone() =>
        Near((0.2f, 0.4f, 0.6f), MagnifierColorMatrices.Apply(MagnifierColorMatrices.For(MagnifierColorFilter.None), (0.2f, 0.4f, 0.6f)));

    [Fact]
    public void Inverted_TurnsWhiteBlackAndBlackWhite()
    {
        var matrix = MagnifierColorMatrices.For(MagnifierColorFilter.Inverted);

        Near(Black, MagnifierColorMatrices.Apply(matrix, White));
        Near(White, MagnifierColorMatrices.Apply(matrix, Black));
        Near((0.8f, 0.6f, 0.4f), MagnifierColorMatrices.Apply(matrix, (0.2f, 0.4f, 0.6f)));
    }

    [Fact]
    public void Greyscale_GivesEveryChannelTheColoursBrightness()
    {
        var matrix = MagnifierColorMatrices.For(MagnifierColorFilter.Greyscale);

        var red = MagnifierColorMatrices.Apply(matrix, (1, 0, 0));
        Near((0.2126f, 0.2126f, 0.2126f), red);
        Near(White, MagnifierColorMatrices.Apply(matrix, White));
    }

    [Fact]
    public void InvertedGreyscale_TurnsWhiteBlackAndPureGreenDarkGrey()
    {
        var matrix = MagnifierColorMatrices.For(MagnifierColorFilter.InvertedGreyscale);

        Near(Black, MagnifierColorMatrices.Apply(matrix, White));
        var green = MagnifierColorMatrices.Apply(matrix, (0, 1, 0));
        Near((1 - 0.7152f, 1 - 0.7152f, 1 - 0.7152f), green);
    }

    [Fact]
    public void YellowOnBlack_DarkTextBecomesYellow_ALightPageBlack()
    {
        var matrix = MagnifierColorMatrices.For(MagnifierColorFilter.YellowOnBlack);

        Near((1, 1, 0), MagnifierColorMatrices.Apply(matrix, Black));
        Near(Black, MagnifierColorMatrices.Apply(matrix, White));
    }

    [Fact]
    public void Black_HidesEverything()
    {
        Near(Black, MagnifierColorMatrices.Apply(MagnifierColorMatrices.Black, White));
        Near(Black, MagnifierColorMatrices.Apply(MagnifierColorMatrices.Black, (0.3f, 0.9f, 0.1f)));
    }
}
