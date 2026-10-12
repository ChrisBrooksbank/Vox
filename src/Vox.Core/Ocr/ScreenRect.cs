namespace Vox.Core.Ocr;

/// <summary>A rectangle on the screen, in pixels (virtual-screen coordinates).</summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    /// <summary>The rectangle has no area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The middle of the rectangle (where to click it).</summary>
    public (int X, int Y) Center => (Left + Width / 2, Top + Height / 2);

    public static ScreenRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    /// <summary>The part of this rectangle inside <paramref name="other"/> (empty when they don't overlap).</summary>
    public ScreenRect Intersect(ScreenRect other) =>
        FromEdges(Math.Max(Left, other.Left), Math.Max(Top, other.Top),
            Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
}
