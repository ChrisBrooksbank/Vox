using Vox.Core.Ocr;

namespace Vox.Core.Magnifier;

/// <summary>
/// What the magnifier shows, as pure maths: the zoom and the part of the screen magnified (the
/// source, in unmagnified virtual-screen pixels), kept inside the virtual screen across every
/// monitor. The view is <see cref="ViewWidth"/> by <see cref="ViewHeight"/> pixels of output (the
/// primary display for full-screen magnification, the window for the docked and lens views), so
/// the source is that size divided by the zoom. Immutable: every move returns a new viewport.
/// </summary>
public sealed record MagnifierViewport
{
    public const double MinZoom = 1.0;
    public const double MaxZoom = 16.0;

    private MagnifierViewport(ScreenRect virtualScreen, int viewWidth, int viewHeight, double zoom, double left, double top)
    {
        VirtualScreen = virtualScreen;
        ViewWidth = Math.Max(1, viewWidth);
        ViewHeight = Math.Max(1, viewHeight);
        Zoom = Math.Clamp(double.IsFinite(zoom) ? zoom : MinZoom, MinZoom, MaxZoom);
        (Left, Top) = ClampOrigin(left, top);
    }

    /// <summary>
    /// A viewport over <paramref name="virtualScreen"/> (every monitor) showing a view of
    /// <paramref name="viewWidth"/> by <paramref name="viewHeight"/> pixels at <paramref name="zoom"/>,
    /// centred on the middle of <paramref name="centreOn"/> (the virtual screen when null).
    /// </summary>
    public static MagnifierViewport Create(ScreenRect virtualScreen, int viewWidth, int viewHeight, double zoom, ScreenRect? centreOn = null)
    {
        var around = centreOn ?? virtualScreen;
        var viewport = new MagnifierViewport(virtualScreen, viewWidth, viewHeight, zoom, around.Left, around.Top);
        return viewport.CentreOnPoint(around.Left + around.Width / 2.0, around.Top + around.Height / 2.0);
    }

    /// <summary>Every monitor together, in virtual-screen pixels.</summary>
    public ScreenRect VirtualScreen { get; }

    /// <summary>The width of the magnified view, in screen pixels.</summary>
    public int ViewWidth { get; }

    /// <summary>The height of the magnified view, in screen pixels.</summary>
    public int ViewHeight { get; }

    /// <summary>How many times larger the view shows the screen (1 to 16).</summary>
    public double Zoom { get; }

    /// <summary>The left edge of the magnified part of the screen, in unmagnified pixels.</summary>
    public double Left { get; }

    /// <summary>The top edge of the magnified part of the screen, in unmagnified pixels.</summary>
    public double Top { get; }

    /// <summary>The width of the magnified part of the screen, in unmagnified pixels.</summary>
    public double SourceWidth => ViewWidth / Zoom;

    /// <summary>The height of the magnified part of the screen, in unmagnified pixels.</summary>
    public double SourceHeight => ViewHeight / Zoom;

    /// <summary>The middle of the magnified part of the screen.</summary>
    public (double X, double Y) Centre => (Left + SourceWidth / 2, Top + SourceHeight / 2);

    /// <summary>The magnified part of the screen, rounded to whole pixels.</summary>
    public ScreenRect Source => new((int)Math.Round(Left), (int)Math.Round(Top),
        (int)Math.Round(SourceWidth), (int)Math.Round(SourceHeight));

    /// <summary>The same view at another zoom, keeping its middle where it is (as far as the screen's edges allow).</summary>
    public MagnifierViewport WithZoom(double zoom)
    {
        var (x, y) = Centre;
        return new MagnifierViewport(VirtualScreen, ViewWidth, ViewHeight, zoom, Left, Top).CentreOnPoint(x, y);
    }

    /// <summary>The same view over another screen or of another size (the monitors or the window changed).</summary>
    public MagnifierViewport WithView(ScreenRect virtualScreen, int viewWidth, int viewHeight)
    {
        var (x, y) = Centre;
        return new MagnifierViewport(virtualScreen, viewWidth, viewHeight, Zoom, Left, Top).CentreOnPoint(x, y);
    }

    /// <summary>The view moved to show the magnified part of the screen from (<paramref name="left"/>, <paramref name="top"/>).</summary>
    public MagnifierViewport WithOrigin(double left, double top) =>
        new(VirtualScreen, ViewWidth, ViewHeight, Zoom, left, top);

    /// <summary>The view with (<paramref name="x"/>, <paramref name="y"/>) in its middle.</summary>
    public MagnifierViewport CentreOnPoint(double x, double y) =>
        WithOrigin(x - SourceWidth / 2, y - SourceHeight / 2);

    /// <summary>
    /// The view with <paramref name="target"/> in its middle. A target too wide or too tall for the
    /// view has its start (left or top edge) shown instead, so the beginning of a line or a long
    /// control is what is seen.
    /// </summary>
    public MagnifierViewport CentreOn(ScreenRect target)
    {
        double left = target.Width <= SourceWidth ? target.Left + target.Width / 2.0 - SourceWidth / 2 : target.Left;
        double top = target.Height <= SourceHeight ? target.Top + target.Height / 2.0 - SourceHeight / 2 : target.Top;
        return WithOrigin(left, top);
    }

    /// <summary>
    /// The view moved as little as it must to show all of <paramref name="target"/>, with
    /// <paramref name="margin"/> pixels around it where they fit (the caret and the mouse pointer
    /// are followed this way, so the view doesn't jump with each character typed). A target the
    /// view already shows doesn't move it.
    /// </summary>
    public MagnifierViewport KeepInView(ScreenRect target, int margin = 0)
    {
        return WithOrigin(Follow(Left, SourceWidth, target.Left, target.Right, margin),
            Follow(Top, SourceHeight, target.Top, target.Bottom, margin));

        static double Follow(double start, double size, double from, double to, int margin)
        {
            // Never more margin than leaves room for the target itself
            double room = Math.Max(0, Math.Min(margin, (size - (to - from)) / 2));
            if (to - from > size)
                return from;
            if (from - room < start)
                return from - room;
            if (to + room > start + size)
                return to + room - size;
            return start;
        }
    }

    /// <summary>The view moved by (<paramref name="dx"/>, <paramref name="dy"/>) unmagnified pixels.</summary>
    public MagnifierViewport Pan(double dx, double dy) => WithOrigin(Left + dx, Top + dy);

    /// <summary>
    /// The view moved a quarter of its width or height towards <paramref name="direction"/>; null
    /// when it is already at that edge of the screen.
    /// </summary>
    public MagnifierViewport? PanStep(PanDirection direction)
    {
        var moved = direction switch
        {
            PanDirection.Left => Pan(-SourceWidth / 4, 0),
            PanDirection.Right => Pan(SourceWidth / 4, 0),
            PanDirection.Up => Pan(0, -SourceHeight / 4),
            _ => Pan(0, SourceHeight / 4),
        };
        return moved.Left == Left && moved.Top == Top ? null : moved;
    }

    /// <summary>Whether the view shows all of <paramref name="target"/>.</summary>
    public bool Shows(ScreenRect target) =>
        target.Left >= Left && target.Top >= Top && target.Right <= Left + SourceWidth && target.Bottom <= Top + SourceHeight;

    /// <summary>The origin kept inside the virtual screen (centred when the source is larger than the screen).</summary>
    private (double Left, double Top) ClampOrigin(double left, double top) =>
        (Clamp(left, VirtualScreen.Left, VirtualScreen.Width, SourceWidth),
            Clamp(top, VirtualScreen.Top, VirtualScreen.Height, SourceHeight));

    private static double Clamp(double value, int start, int length, double size)
    {
        if (!double.IsFinite(value))
            value = start;
        return size >= length ? start + (length - size) / 2 : Math.Clamp(value, start, start + length - size);
    }
}

/// <summary>A direction the magnified view is panned in.</summary>
public enum PanDirection
{
    Left,
    Right,
    Up,
    Down,
}
