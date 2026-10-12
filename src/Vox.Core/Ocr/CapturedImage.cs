using System.Security.Cryptography;

namespace Vox.Core.Ocr;

/// <summary>
/// A picture of part of the screen: 32-bit BGRA pixels, top row first, <see cref="Width"/> × 4
/// bytes per row. <see cref="Bounds"/> is where on the screen it was taken from; a scaled copy
/// keeps the original bounds, so positions in it map back to the screen.
/// </summary>
public sealed class CapturedImage
{
    public CapturedImage(int width, int height, byte[] pixels, ScreenRect bounds)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "An image needs at least one pixel");
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Expected 4 bytes (BGRA) per pixel", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
        Bounds = bounds;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRA pixels, top row first.</summary>
    public byte[] Pixels { get; }

    /// <summary>Where on the screen the image was taken from.</summary>
    public ScreenRect Bounds { get; }

    /// <summary>A hash of the picture (size and pixels), to recognise the same image again.</summary>
    public string Hash()
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(BitConverter.GetBytes(Width));
        sha.AppendData(BitConverter.GetBytes(Height));
        sha.AppendData(Pixels);
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    /// <summary>
    /// Maps a rectangle in this image's pixels to the screen (through <see cref="Bounds"/>, so it
    /// works on a scaled copy too).
    /// </summary>
    public ScreenRect ToScreen(double x, double y, double width, double height)
    {
        double scaleX = Bounds.Width / (double)Width;
        double scaleY = Bounds.Height / (double)Height;
        int left = Bounds.Left + (int)Math.Round(x * scaleX);
        int top = Bounds.Top + (int)Math.Round(y * scaleY);
        int right = Bounds.Left + (int)Math.Round((x + width) * scaleX);
        int bottom = Bounds.Top + (int)Math.Round((y + height) * scaleY);
        return ScreenRect.FromEdges(left, top, right, bottom);
    }

    /// <summary>
    /// A copy <paramref name="factor"/> times the size (bilinear), with the same
    /// <see cref="Bounds"/>. Text recognition reads small screen text much better enlarged.
    /// </summary>
    public CapturedImage Scaled(double factor)
    {
        int width = Math.Max(1, (int)Math.Round(Width * factor));
        int height = Math.Max(1, (int)Math.Round(Height * factor));
        if (width == Width && height == Height)
            return this;

        var source = Pixels;
        var result = new byte[width * height * 4];
        double stepX = Width / (double)width;
        double stepY = Height / (double)height;
        for (int y = 0; y < height; y++)
        {
            // Pixel centres map to pixel centres
            double sy = Math.Clamp((y + 0.5) * stepY - 0.5, 0, Height - 1);
            int y0 = (int)sy;
            int y1 = Math.Min(y0 + 1, Height - 1);
            double fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Clamp((x + 0.5) * stepX - 0.5, 0, Width - 1);
                int x0 = (int)sx;
                int x1 = Math.Min(x0 + 1, Width - 1);
                double fx = sx - x0;
                int target = (y * width + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    double top = source[(y0 * Width + x0) * 4 + c] * (1 - fx) + source[(y0 * Width + x1) * 4 + c] * fx;
                    double bottom = source[(y1 * Width + x0) * 4 + c] * (1 - fx) + source[(y1 * Width + x1) * 4 + c] * fx;
                    result[target + c] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
                }
            }
        }
        return new CapturedImage(width, height, result, Bounds);
    }
}
