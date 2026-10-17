using Vox.Core.Touch;
using Xunit;

namespace Vox.Core.Tests.Touch;

public class GestureRecognizerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly List<TouchGesture> _gestures = [];
    // One pixel is one millimetre
    private readonly GestureRecognizer _r = new(pixelsPerMm: 1);

    public GestureRecognizerTests() => _r.Recognized += _gestures.Add;

    private static TouchPoint P(int id, int x, int y, TouchPhase phase, int ms) => new(id, x, y, phase, T0.AddMilliseconds(ms));

    [Fact]
    public void FastDrag_IsAFlickInTheDominantDirection()
    {
        _r.Process(P(1, 100, 100, TouchPhase.Down, 0));
        _r.Process(P(1, 120, 100, TouchPhase.Move, 20));
        _r.Process(P(1, 140, 102, TouchPhase.Move, 40));
        _r.Process(P(1, 140, 102, TouchPhase.Up, 60));
        Assert.Equal([new TouchGesture(TouchGestureKind.FlickRight, 1, 100, 100)], _gestures);
    }

    [Theory]
    [InlineData(-40, 0, TouchGestureKind.FlickLeft)]
    [InlineData(0, 40, TouchGestureKind.FlickDown)]
    [InlineData(0, -40, TouchGestureKind.FlickUp)]
    public void Flick_Directions(int dx, int dy, TouchGestureKind expected)
    {
        _r.Process(P(1, 200, 200, TouchPhase.Down, 0));
        _r.Process(P(1, 200 + dx / 2, 200 + dy / 2, TouchPhase.Move, 20));
        _r.Process(P(1, 200 + dx, 200 + dy, TouchPhase.Up, 40));
        Assert.Equal(expected, Assert.Single(_gestures).Kind);
    }

    [Fact]
    public void ShortFastDrag_IsNotAFlick()
    {
        _r.Process(P(1, 100, 100, TouchPhase.Down, 0));
        _r.Process(P(1, 105, 100, TouchPhase.Up, 20));
        Assert.Empty(_gestures);
    }

    [Fact]
    public void SlowDrag_Explores_AndNeverFlicks()
    {
        _r.Process(P(1, 100, 100, TouchPhase.Down, 0));
        _r.Process(P(1, 103, 100, TouchPhase.Move, 100));
        _r.Process(P(1, 150, 100, TouchPhase.Move, 400));
        _r.Process(P(1, 150, 100, TouchPhase.Up, 410));
        Assert.NotEmpty(_gestures);
        Assert.All(_gestures, g => Assert.Equal(TouchGestureKind.Explore, g.Kind));
    }

    [Fact]
    public void ExploreIsLimitedToEvery60ms_AndNeedsTwoMillimetres()
    {
        _r.Process(P(1, 100, 100, TouchPhase.Down, 0));
        _r.Process(P(1, 105, 100, TouchPhase.Move, 100)); // first explore
        _r.Process(P(1, 109, 100, TouchPhase.Move, 120)); // too soon
        _r.Process(P(1, 109, 100, TouchPhase.Move, 200)); // 4 mm and 100 ms after the first
        _r.Process(P(1, 109, 100, TouchPhase.Move, 300)); // not moved since the last report
        _r.Process(P(1, 110, 100, TouchPhase.Move, 400)); // moved under 2 mm
        Assert.Equal(2, _gestures.Count);
        Assert.Equal(109, _gestures[1].X);
    }

    [Fact]
    public void FingerHeldStill_ExploresOnTick()
    {
        _r.Process(P(1, 50, 60, TouchPhase.Down, 0));
        _r.Tick(T0.AddMilliseconds(100));
        Assert.Empty(_gestures);
        _r.Tick(T0.AddMilliseconds(130));
        Assert.Equal([new TouchGesture(TouchGestureKind.Explore, 1, 50, 60)], _gestures);
        _r.Tick(T0.AddMilliseconds(200));
        Assert.Single(_gestures);
    }

    [Fact]
    public void SingleTap_DoesNothing_DoubleTapIsRecognised()
    {
        _r.Process(P(1, 10, 10, TouchPhase.Down, 0));
        _r.Process(P(1, 10, 10, TouchPhase.Up, 80));
        Assert.Empty(_gestures);
        _r.Process(P(1, 12, 10, TouchPhase.Down, 200));
        _r.Process(P(1, 12, 10, TouchPhase.Up, 270));
        Assert.Equal(TouchGestureKind.DoubleTap, Assert.Single(_gestures).Kind);
    }

    [Fact]
    public void TapsTooFarApartOrTooSlow_AreNotADoubleTap()
    {
        _r.Process(P(1, 10, 10, TouchPhase.Down, 0));
        _r.Process(P(1, 10, 10, TouchPhase.Up, 50));
        _r.Process(P(1, 40, 10, TouchPhase.Down, 150));
        _r.Process(P(1, 40, 10, TouchPhase.Up, 200));
        _r.Process(P(1, 40, 10, TouchPhase.Down, 700));
        _r.Process(P(1, 40, 10, TouchPhase.Up, 750));
        Assert.Empty(_gestures);
    }

    [Fact]
    public void TwoFingerTap_IsRecognisedAtOnce()
    {
        _r.Process(P(1, 10, 10, TouchPhase.Down, 0));
        _r.Process(P(2, 60, 10, TouchPhase.Down, 30));
        _r.Process(P(1, 10, 10, TouchPhase.Up, 100));
        _r.Process(P(2, 60, 10, TouchPhase.Up, 110));
        Assert.Equal(new TouchGesture(TouchGestureKind.Tap, 2, 35, 10), Assert.Single(_gestures));
    }

    [Fact]
    public void ThreeFingerFlick_MovesTogether()
    {
        for (int i = 0; i < 3; i++)
            _r.Process(P(i, 100, 50 + i * 30, TouchPhase.Down, i * 10));
        for (int i = 0; i < 3; i++)
            _r.Process(P(i, 150, 50 + i * 30, TouchPhase.Move, 60));
        for (int i = 0; i < 3; i++)
            _r.Process(P(i, 150, 50 + i * 30, TouchPhase.Up, 80));
        var gesture = Assert.Single(_gestures);
        Assert.Equal(TouchGestureKind.FlickRight, gesture.Kind);
        Assert.Equal(3, gesture.Fingers);
    }

    [Fact]
    public void FingerJoiningLate_CancelsTheGesture()
    {
        _r.Process(P(1, 10, 10, TouchPhase.Down, 0));
        _r.Process(P(2, 60, 10, TouchPhase.Down, 300));
        _r.Process(P(1, 10, 10, TouchPhase.Up, 350));
        _r.Process(P(2, 60, 10, TouchPhase.Up, 360));
        Assert.Empty(_gestures);
    }

    [Fact]
    public void Cancel_EndsEverythingWithoutAGesture()
    {
        _r.Process(P(1, 100, 100, TouchPhase.Down, 0));
        _r.Process(P(1, 140, 100, TouchPhase.Move, 30));
        _r.Process(P(0, 0, 0, TouchPhase.Cancel, 40));
        _r.Process(P(1, 140, 100, TouchPhase.Up, 50));
        Assert.Empty(_gestures);
    }
}
