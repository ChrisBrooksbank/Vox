namespace Vox.Core.Touch;

public enum TouchPhase { Down, Move, Up, Cancel }

/// <summary>One finger's position (screen pixels) at one moment.</summary>
public readonly record struct TouchPoint(int Id, int X, int Y, TouchPhase Phase, DateTimeOffset Timestamp);

public enum TouchGestureKind { Explore, DoubleTap, Tap, FlickRight, FlickLeft, FlickUp, FlickDown }

/// <summary>A recognised gesture; X/Y is where it happened (the explored point for Explore).</summary>
public readonly record struct TouchGesture(TouchGestureKind Kind, int Fingers, int X, int Y);

/// <summary>Recognition thresholds, in millimetres and milliseconds (design: docs/technical/touch.md).</summary>
public sealed record TouchThresholds
{
    public double ExploreMaxSpeedMmPerSecond { get; init; } = 150;
    public TimeSpan ExploreHold { get; init; } = TimeSpan.FromMilliseconds(120);
    public TimeSpan ExploreInterval { get; init; } = TimeSpan.FromMilliseconds(60);
    public double ExploreMinMoveMm { get; init; } = 2;
    public double FlickMinDistanceMm { get; init; } = 12;
    public double FlickMinSpeedMmPerSecond { get; init; } = 150;
    public TimeSpan TapMaxDuration { get; init; } = TimeSpan.FromMilliseconds(250);
    public double TapMaxMoveMm { get; init; } = 3;
    public TimeSpan DoubleTapWindow { get; init; } = TimeSpan.FromMilliseconds(350);
    public double DoubleTapMaxDistanceMm { get; init; } = 8;
    public TimeSpan FingersDownWithin { get; init; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>Where touch points come from. Start and Stop may be called repeatedly.</summary>
public interface ITouchSource : IDisposable
{
    event Action<TouchPoint>? PointReceived;
    /// <summary>Starts delivering points; false when touch cannot be captured (the source logs why).</summary>
    bool Start();
    void Stop();
}
