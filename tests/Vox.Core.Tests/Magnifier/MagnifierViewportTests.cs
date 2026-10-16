using Vox.Core.Magnifier;
using Vox.Core.Ocr;
using Xunit;

namespace Vox.Core.Tests.Magnifier;

public class MagnifierViewportTests
{
    private static readonly ScreenRect Screen = new(0, 0, 1920, 1080);

    private static MagnifierViewport At(double zoom, ScreenRect? virtualScreen = null) =>
        MagnifierViewport.Create(virtualScreen ?? Screen, 1920, 1080, zoom);

    [Fact]
    public void Create_CentresOnTheScreen_WithASourceTheViewOverTheZoom()
    {
        var viewport = At(2);

        Assert.Equal(960, viewport.SourceWidth);
        Assert.Equal(540, viewport.SourceHeight);
        Assert.Equal((960.0, 540.0), viewport.Centre);
        Assert.Equal(new ScreenRect(480, 270, 960, 540), viewport.Source);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(40, 16)]
    [InlineData(double.NaN, 1)]
    public void Zoom_IsKeptBetweenOneAndSixteen(double asked, double kept) =>
        Assert.Equal(kept, At(asked).Zoom);

    [Fact]
    public void WithZoom_KeepsTheMiddleWhereItIs()
    {
        var viewport = At(2).CentreOnPoint(700, 400);

        var zoomed = viewport.WithZoom(4);

        Assert.Equal(4, zoomed.Zoom);
        Assert.Equal((700.0, 400.0), zoomed.Centre);
        Assert.Equal(480, zoomed.SourceWidth);
    }

    [Fact]
    public void CentreOnPoint_NearAnEdge_IsKeptOnTheScreen()
    {
        var viewport = At(4).CentreOnPoint(10, 1075);

        Assert.Equal(0, viewport.Left);
        Assert.Equal(1080 - viewport.SourceHeight, viewport.Top);
    }

    [Fact]
    public void CentreOn_PutsATargetInTheMiddle()
    {
        var viewport = At(2).CentreOn(new ScreenRect(1000, 600, 100, 20));

        Assert.Equal((1050.0, 610.0), viewport.Centre);
        Assert.True(viewport.Shows(new ScreenRect(1000, 600, 100, 20)));
    }

    [Fact]
    public void CentreOn_ATargetWiderThanTheView_ShowsItsStart()
    {
        var viewport = At(4).CentreOn(new ScreenRect(200, 500, 1200, 30));

        Assert.Equal(200, viewport.Left);
        Assert.Equal(515 - viewport.SourceHeight / 2, viewport.Top);
    }

    [Fact]
    public void KeepInView_ATargetAlreadyShown_DoesNotMove()
    {
        var viewport = At(2);

        var kept = viewport.KeepInView(new ScreenRect(900, 500, 10, 20), margin: 40);

        Assert.Equal(viewport, kept);
    }

    [Fact]
    public void KeepInView_ATargetPastTheRightEdge_MovesJustFarEnoughWithTheMargin()
    {
        var viewport = At(2); // shows 480..1440 across

        var kept = viewport.KeepInView(new ScreenRect(1500, 500, 2, 20), margin: 40);

        Assert.Equal(1542 - kept.SourceWidth, kept.Left);
        Assert.Equal(viewport.Top, kept.Top);
    }

    [Fact]
    public void KeepInView_ATargetAboveTheView_MovesUp()
    {
        var viewport = At(2); // shows 270..810 down

        var kept = viewport.KeepInView(new ScreenRect(700, 100, 2, 20), margin: 10);

        Assert.Equal(90, kept.Top);
    }

    [Fact]
    public void PanStep_MovesAQuarterOfTheView_AndIsNullAtTheEdge()
    {
        var viewport = At(2);

        var right = viewport.PanStep(PanDirection.Right);
        Assert.NotNull(right);
        Assert.Equal(viewport.Left + 240, right!.Left);

        var atLeft = viewport.WithOrigin(0, 0);
        Assert.Null(atLeft.PanStep(PanDirection.Left));
        Assert.Null(atLeft.PanStep(PanDirection.Up));
        Assert.NotNull(atLeft.PanStep(PanDirection.Down));
    }

    [Fact]
    public void TwoMonitors_TheViewMovesAcrossBothAndStaysInside()
    {
        // A second monitor to the left of the primary one
        var both = new ScreenRect(-1280, 0, 3200, 1080);
        var viewport = MagnifierViewport.Create(both, 1920, 1080, 2);

        var left = viewport.CentreOnPoint(-1270, 10);
        Assert.Equal(-1280, left.Left);
        Assert.Equal(0, left.Top);

        var right = viewport.CentreOnPoint(5000, 5000);
        Assert.Equal(1920 - right.SourceWidth, right.Left);
        Assert.Equal(1080 - right.SourceHeight, right.Top);
    }

    [Fact]
    public void ASourceLargerThanTheScreen_IsCentredOnIt()
    {
        // A tall, narrow view at 1×: wider than the screen is high
        var viewport = MagnifierViewport.Create(new ScreenRect(0, 0, 800, 600), 1000, 400, 1).CentreOnPoint(0, 0);

        Assert.Equal(-100, viewport.Left);
        Assert.Equal(0, viewport.Top);
    }

    [Fact]
    public void WithView_KeepsTheMiddleForANewSize()
    {
        var viewport = At(2).CentreOnPoint(800, 500);

        var docked = viewport.WithView(Screen, 1920, 324);

        Assert.Equal(162, docked.SourceHeight);
        Assert.Equal((800.0, 500.0), docked.Centre);
    }
}
