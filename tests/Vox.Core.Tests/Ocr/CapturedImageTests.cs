using Vox.Core.Ocr;
using Xunit;

namespace Vox.Core.Tests.Ocr;

public class CapturedImageTests
{
    private static CapturedImage Image(int width, int height, ScreenRect bounds, Func<int, int, byte>? gray = null)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte value = gray?.Invoke(x, y) ?? 128;
            int i = (y * width + x) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        return new CapturedImage(width, height, pixels, bounds);
    }

    [Fact]
    public void ToScreen_OffsetsByWhereTheImageWasTaken()
    {
        var image = Image(100, 50, new ScreenRect(200, 300, 100, 50));

        Assert.Equal(new ScreenRect(210, 305, 20, 10), image.ToScreen(10, 5, 20, 10));
    }

    [Fact]
    public void ToScreen_OnAScaledCopy_MapsBackToTheScreen()
    {
        var image = Image(100, 50, new ScreenRect(200, 300, 100, 50)).Scaled(2);

        Assert.Equal(200, image.Width);
        Assert.Equal(100, image.Height);
        Assert.Equal(new ScreenRect(210, 305, 20, 10), image.ToScreen(20, 10, 40, 20));
    }

    [Fact]
    public void Scaled_KeepsAPlainImagePlain()
    {
        var scaled = Image(3, 2, new ScreenRect(0, 0, 3, 2)).Scaled(2);

        Assert.All(scaled.Pixels.Where((_, i) => i % 4 != 3), b => Assert.Equal(128, b));
        Assert.Equal(new ScreenRect(0, 0, 3, 2), scaled.Bounds);
    }

    [Fact]
    public void Scaled_Blends_BetweenPixels()
    {
        // A black pixel beside a white one: enlarged, the edge is a step from dark to light
        var scaled = Image(2, 1, new ScreenRect(0, 0, 2, 1), (x, _) => x == 0 ? (byte)0 : (byte)255).Scaled(2);

        var row = Enumerable.Range(0, 4).Select(x => scaled.Pixels[x * 4]).ToList();
        Assert.Equal(0, row[0]);
        Assert.True(row[1] > 0 && row[1] < row[2]);
        Assert.Equal(255, row[3]);
    }

    [Fact]
    public void Scaled_ByOne_IsTheSameImage()
    {
        var image = Image(4, 4, new ScreenRect(0, 0, 4, 4));

        Assert.Same(image, image.Scaled(1));
    }

    [Fact]
    public void Hash_IsTheSameForTheSamePicture_AndDiffersOtherwise()
    {
        var bounds = new ScreenRect(0, 0, 4, 4);

        Assert.Equal(Image(4, 4, bounds).Hash(), Image(4, 4, new ScreenRect(50, 50, 4, 4)).Hash());
        Assert.NotEqual(Image(4, 4, bounds).Hash(), Image(4, 4, bounds, (x, y) => (byte)(x + y)).Hash());
        Assert.NotEqual(Image(4, 2, bounds).Hash(), Image(2, 4, bounds).Hash());
    }

    [Fact]
    public void Pixels_MustBeFourBytesEach() =>
        Assert.Throws<ArgumentException>(() => new CapturedImage(2, 2, new byte[12], new ScreenRect(0, 0, 2, 2)));

    [Fact]
    public void ScreenRect_Intersect_ClipsToTheScreen()
    {
        var screen = new ScreenRect(0, 0, 1920, 1080);

        Assert.Equal(new ScreenRect(1900, 1000, 20, 80), new ScreenRect(1900, 1000, 200, 200).Intersect(screen));
        Assert.True(new ScreenRect(2000, 0, 10, 10).Intersect(screen).IsEmpty);
        Assert.Equal((15, 25), new ScreenRect(10, 20, 10, 10).Center);
    }

    [Theory]
    [InlineData(400, 100, 10000, 2.0)]   // a toolbar: enlarged
    [InlineData(1920, 1080, 10000, 1.0)] // a whole screen: big enough already
    [InlineData(4000, 1000, 2000, 0.5)]  // larger than the engine takes: reduced
    public void Scaling_FitsTheEngine(int width, int height, int maxDimension, double expected) =>
        Assert.Equal(expected, OcrScaling.FactorFor(width, height, maxDimension));

    [Fact]
    public void ScreenCapture_IsNeverAvailableOnSecureScreens()
    {
        var capture = new GdiScreenCapture(Vox.Core.Lifecycle.RunPolicy.Secure);

        Assert.False(capture.IsAvailable);
        Assert.Throws<InvalidOperationException>(() => capture.Capture(new ScreenRect(0, 0, 10, 10)));
        Assert.True(new GdiScreenCapture(Vox.Core.Lifecycle.RunPolicy.Normal).IsAvailable);
    }
}
