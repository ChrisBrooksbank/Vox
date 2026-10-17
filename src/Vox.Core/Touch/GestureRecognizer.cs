namespace Vox.Core.Touch;

/// <summary>
/// Turns touch points into gestures. Pure and not thread-safe (the caller serialises); time comes
/// from the points' timestamps and from <see cref="Tick"/>, so tests script it with no clock.
/// </summary>
public sealed class GestureRecognizer
{
    private sealed class Finger
    {
        public int StartX, StartY, X, Y, PrevX, PrevY;
        public DateTimeOffset Start, PrevTime, Last;
        public double MaxMoveMm;
    }

    private readonly TouchThresholds _t;
    private readonly double _pxPerMm;
    private readonly Dictionary<int, Finger> _fingers = new();
    private readonly List<Finger> _ended = [];
    private DateTimeOffset _gestureStart;
    private int _maxFingers;
    private bool _cancelled;
    private bool _exploring;
    private int _exploreX, _exploreY;
    private DateTimeOffset _lastExplore;
    private (int Fingers, int X, int Y, DateTimeOffset At)? _lastTap;

    public GestureRecognizer(TouchThresholds? thresholds = null, double pixelsPerMm = 96 / 25.4)
    {
        _t = thresholds ?? new TouchThresholds();
        _pxPerMm = pixelsPerMm;
    }

    /// <summary>Raised for each gesture recognised.</summary>
    public event Action<TouchGesture>? Recognized;

    public void Process(TouchPoint p)
    {
        switch (p.Phase)
        {
            case TouchPhase.Down: Down(p); break;
            case TouchPhase.Move: Move(p); break;
            case TouchPhase.Up: Up(p); break;
            default: Reset(); break;
        }
    }

    /// <summary>Time has passed with fingers still down: a finger held in place starts exploring.</summary>
    public void Tick(DateTimeOffset now)
    {
        if (_fingers.Count != 1 || _cancelled || _exploring || _maxFingers != 1)
            return;
        var f = _fingers.Values.First();
        if (now - f.Start >= _t.ExploreHold && f.MaxMoveMm < _t.TapMaxMoveMm)
            Explore(f, now);
    }

    private void Down(TouchPoint p)
    {
        if (_fingers.Count == 0)
        {
            _ended.Clear();
            _gestureStart = p.Timestamp;
            _maxFingers = 0;
            _cancelled = false;
            _exploring = false;
        }
        else if (p.Timestamp - _gestureStart > _t.FingersDownWithin || _exploring)
        {
            // A finger joining late changes the gesture's finger count
            _cancelled = true;
        }
        _fingers[p.Id] = new Finger
        {
            StartX = p.X, StartY = p.Y, X = p.X, Y = p.Y, PrevX = p.X, PrevY = p.Y,
            Start = p.Timestamp, PrevTime = p.Timestamp, Last = p.Timestamp,
        };
        _maxFingers = Math.Max(_maxFingers, _fingers.Count);
    }

    private void Move(TouchPoint p)
    {
        if (!_fingers.TryGetValue(p.Id, out var f))
            return;
        f.PrevX = f.X; f.PrevY = f.Y; f.PrevTime = f.Last;
        f.X = p.X; f.Y = p.Y; f.Last = p.Timestamp;
        f.MaxMoveMm = Math.Max(f.MaxMoveMm, Mm(f.X - f.StartX, f.Y - f.StartY));
        if (_cancelled || _maxFingers != 1 || _fingers.Count != 1)
            return;

        if (_exploring)
        {
            if (p.Timestamp - _lastExplore >= _t.ExploreInterval && Mm(f.X - _exploreX, f.Y - _exploreY) >= _t.ExploreMinMoveMm)
                Explore(f, p.Timestamp);
            return;
        }
        if (f.MaxMoveMm < _t.ExploreMinMoveMm)
            return;
        double seconds = Math.Max((f.Last - f.PrevTime).TotalSeconds, 0.001);
        double speed = Mm(f.X - f.PrevX, f.Y - f.PrevY) / seconds;
        if (p.Timestamp - f.Start >= _t.ExploreHold || speed < _t.ExploreMaxSpeedMmPerSecond)
            Explore(f, p.Timestamp);
    }

    private void Explore(Finger f, DateTimeOffset now)
    {
        _exploring = true;
        _exploreX = f.X;
        _exploreY = f.Y;
        _lastExplore = now;
        Recognized?.Invoke(new TouchGesture(TouchGestureKind.Explore, 1, f.X, f.Y));
    }

    private void Up(TouchPoint p)
    {
        if (!_fingers.TryGetValue(p.Id, out var f))
            return;
        f.Last = p.Timestamp;
        f.X = p.X;
        f.Y = p.Y;
        f.MaxMoveMm = Math.Max(f.MaxMoveMm, Mm(f.X - f.StartX, f.Y - f.StartY));
        _ended.Add(f);
        _fingers.Remove(p.Id);
        // The gesture is judged when the last finger lifts
        if (_fingers.Count == 0)
            Finish(p.Timestamp);
    }

    private void Finish(DateTimeOffset now)
    {
        var all = _ended.ToList();
        _ended.Clear();
        if (_cancelled || _exploring || all.Count != _maxFingers)
            return;

        int fingers = _maxFingers;
        double dx = all.Average(f => f.X - f.StartX), dy = all.Average(f => f.Y - f.StartY);
        int cx = (int)all.Average(f => f.StartX), cy = (int)all.Average(f => f.StartY);
        var duration = now - _gestureStart;
        double distance = Mm(dx, dy);

        if (duration <= _t.TapMaxDuration && all.All(f => f.MaxMoveMm < _t.TapMaxMoveMm))
        {
            if (_lastTap is { } last && last.Fingers == fingers && now - last.At <= _t.DoubleTapWindow
                && Mm(cx - last.X, cy - last.Y) <= _t.DoubleTapMaxDistanceMm)
            {
                _lastTap = null;
                Recognized?.Invoke(new TouchGesture(TouchGestureKind.DoubleTap, fingers, cx, cy));
                return;
            }
            _lastTap = (fingers, cx, cy, now);
            // One finger's tap does nothing alone; with more fingers it is a gesture at once
            if (fingers > 1)
                Recognized?.Invoke(new TouchGesture(TouchGestureKind.Tap, fingers, cx, cy));
            return;
        }

        if (distance >= _t.FlickMinDistanceMm && distance / Math.Max(duration.TotalSeconds, 0.001) > _t.FlickMinSpeedMmPerSecond)
        {
            var kind = Math.Abs(dx) >= Math.Abs(dy)
                ? (dx > 0 ? TouchGestureKind.FlickRight : TouchGestureKind.FlickLeft)
                : (dy > 0 ? TouchGestureKind.FlickDown : TouchGestureKind.FlickUp);
            _lastTap = null;
            Recognized?.Invoke(new TouchGesture(kind, fingers, cx, cy));
        }
    }

    private void Reset()
    {
        _fingers.Clear();
        _ended.Clear();
        _cancelled = false;
        _exploring = false;
        _maxFingers = 0;
    }

    private double Mm(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy) / _pxPerMm;
}
