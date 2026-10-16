using Vox.Core.Ocr;

namespace Vox.Core.Magnifier;

/// <summary>
/// What the magnifier shows at a moment: the view, the zoom, the magnified part of the screen
/// (its top-left corner, unmagnified virtual-screen pixels), the window of a docked or lens view
/// and the colour filter.
/// </summary>
public sealed record MagnifierFrame(
    MagnifierView View,
    double Zoom,
    double Left,
    double Top,
    ScreenRect? Window,
    MagnifierColorFilter Filter,
    bool Smoothing);

/// <summary>Whether the magnifier could start.</summary>
public enum MagnifierStartResult
{
    Started,
    /// <summary>Windows Magnifier is running: two magnifiers would fight over the screen.</summary>
    WindowsMagnifierRunning,
    /// <summary>The Magnification API can't be used here (no desktop, the API failed).</summary>
    Unavailable,
}

/// <summary>
/// Magnifies the screen. The Windows implementation (<see cref="WindowsMagnifier"/>) uses the
/// Magnification API on a thread of its own; tests use a fake. Thread-safe.
/// </summary>
public interface IMagnifier : IDisposable
{
    /// <summary>Starts magnifying (nothing changes on screen until <see cref="Show"/>).</summary>
    MagnifierStartResult Start();

    /// <summary>Stops magnifying and puts the screen back as it was.</summary>
    void Stop();

    bool IsRunning { get; }

    /// <summary>Shows <paramref name="frame"/> (ignored while not running).</summary>
    void Show(MagnifierFrame frame);
}

/// <summary>The monitors, in virtual-screen pixels (any thread).</summary>
public interface IDisplayLayout
{
    /// <summary>Every monitor; the primary one first.</summary>
    IReadOnlyList<ScreenRect> Displays { get; }
}
