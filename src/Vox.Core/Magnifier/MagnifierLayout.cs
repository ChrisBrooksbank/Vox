using Vox.Core.Ocr;

namespace Vox.Core.Magnifier;

/// <summary>Where the magnifier shows what it magnifies.</summary>
public enum MagnifierView
{
    /// <summary>The whole screen is magnified.</summary>
    FullScreen,
    /// <summary>A window along one edge of the screen shows the magnified view; the rest of the screen stays as it is.</summary>
    Docked,
    /// <summary>A window that moves with what is followed (the mouse, the focus) magnifies what is under it.</summary>
    Lens,
}

/// <summary>The edge of the screen the docked view sits along.</summary>
public enum MagnifierDockEdge
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>Where the docked and lens views' windows go on a display, as pure maths.</summary>
public static class MagnifierLayout
{
    public const int MinSizePercent = 10;
    public const int MaxSizePercent = 80;
    public const int SizeStepPercent = 5;

    /// <summary>A view size percentage kept between <see cref="MinSizePercent"/> and <see cref="MaxSizePercent"/>.</summary>
    public static int ClampSize(int percent) => Math.Clamp(percent, MinSizePercent, MaxSizePercent);

    /// <summary>
    /// The docked view's window on <paramref name="display"/>: the whole length of
    /// <paramref name="edge"/>, <paramref name="sizePercent"/> of the display deep.
    /// </summary>
    public static ScreenRect Docked(ScreenRect display, MagnifierDockEdge edge, int sizePercent)
    {
        int percent = ClampSize(sizePercent);
        int height = Math.Max(1, display.Height * percent / 100);
        int width = Math.Max(1, display.Width * percent / 100);
        return edge switch
        {
            MagnifierDockEdge.Bottom => new ScreenRect(display.Left, display.Bottom - height, display.Width, height),
            MagnifierDockEdge.Left => new ScreenRect(display.Left, display.Top, width, display.Height),
            MagnifierDockEdge.Right => new ScreenRect(display.Right - width, display.Top, width, display.Height),
            _ => new ScreenRect(display.Left, display.Top, display.Width, height),
        };
    }

    /// <summary>
    /// The lens's window: <paramref name="sizePercent"/> of <paramref name="display"/>'s width and
    /// height, centred on (<paramref name="x"/>, <paramref name="y"/>) and kept on the display.
    /// </summary>
    public static ScreenRect Lens(ScreenRect display, int x, int y, int sizePercent)
    {
        int percent = ClampSize(sizePercent);
        int width = Math.Max(1, display.Width * percent / 100);
        int height = Math.Max(1, display.Height * percent / 100);
        int left = Math.Clamp(x - width / 2, display.Left, display.Right - width);
        int top = Math.Clamp(y - height / 2, display.Top, display.Bottom - height);
        return new ScreenRect(left, top, width, height);
    }

    /// <summary>The display among <paramref name="displays"/> holding (<paramref name="x"/>, <paramref name="y"/>), else the nearest one.</summary>
    public static ScreenRect DisplayAt(IReadOnlyList<ScreenRect> displays, int x, int y)
    {
        if (displays.Count == 0)
            return new ScreenRect(0, 0, 1, 1);
        ScreenRect best = displays[0];
        long bestDistance = long.MaxValue;
        foreach (var display in displays)
        {
            long dx = x < display.Left ? display.Left - x : x >= display.Right ? x - display.Right + 1 : 0;
            long dy = y < display.Top ? display.Top - y : y >= display.Bottom ? y - display.Bottom + 1 : 0;
            long distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                best = display;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>The next edge for the docked view: top, bottom, left, right, then top again.</summary>
    public static MagnifierDockEdge NextEdge(MagnifierDockEdge edge) =>
        (MagnifierDockEdge)(((int)edge + 1) % Enum.GetValues<MagnifierDockEdge>().Length);

    /// <summary>The union of <paramref name="displays"/>: the virtual screen.</summary>
    public static ScreenRect VirtualScreen(IReadOnlyList<ScreenRect> displays)
    {
        if (displays.Count == 0)
            return new ScreenRect(0, 0, 1, 1);
        return ScreenRect.FromEdges(displays.Min(d => d.Left), displays.Min(d => d.Top),
            displays.Max(d => d.Right), displays.Max(d => d.Bottom));
    }
}
