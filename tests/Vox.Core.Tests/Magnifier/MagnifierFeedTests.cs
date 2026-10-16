using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Magnifier;
using Vox.Core.Ocr;
using Vox.Core.Pipeline;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Magnifier;

public sealed class MagnifierFeedTests : IDisposable
{
    private readonly FakeMagnifier _magnifier = new();
    private readonly TestOptionsMonitor<VoxSettings> _settings =
        new(new VoxSettings { MagnifierSmoothTracking = false, MagnifierEnabled = true });
    private readonly ManualTimeProvider _time = new();
    private readonly MagnifierController _controller;
    private readonly Dictionary<VBufferNode, TaskCompletionSource<ScreenRect?>> _lookups = [];
    private readonly List<VBufferNode> _lookedUp = [];
    private ScreenRect? _caret;
    private int _caretReads;
    private readonly MagnifierFeed _feed;

    public MagnifierFeedTests()
    {
        _controller = new MagnifierController(_magnifier, new FakeDisplays(), _settings, _settings.Set, _ => { },
            NullLogger<MagnifierController>.Instance, time: _time);
        _controller.Start();
        _feed = new MagnifierFeed(_controller, LookUp, () => { _caretReads++; return _caret; }, NullLogger.Instance);
    }

    public void Dispose() => _controller.Dispose();

    private Task<ScreenRect?> LookUp(VBufferNode node)
    {
        lock (_lookups)
        {
            _lookedUp.Add(node);
            var lookup = new TaskCompletionSource<ScreenRect?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _lookups[node] = lookup;
            return lookup.Task;
        }
    }

    private void Answer(VBufferNode node, ScreenRect? bounds)
    {
        lock (_lookups)
            _lookups[node].SetResult(bounds);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public void Focus_TheBoundsComeWithTheEvent()
    {
        _feed.FocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "OK", "Button", Bounds: new ScreenRect(100, 100, 40, 20)));

        Assert.Equal((0.0, 0.0), (_magnifier.Last!.Left, _magnifier.Last.Top));
    }

    [Fact]
    public void Caret_IsReadOnlyWhileFollowed()
    {
        _caret = new ScreenRect(1500, 500, 1, 18);
        _feed.CaretMoved();
        Assert.Equal(1, _caretReads);
        Assert.True(_magnifier.Last!.Left > 480);

        _settings.Set(_settings.CurrentValue with { MagnifierFollowCaret = false });
        _feed.CaretMoved();
        Assert.Equal(1, _caretReads);
    }

    [Fact]
    public async Task Typing_ReadsTheCaretAfterAMoment()
    {
        _caret = new ScreenRect(1500, 500, 1, 18);

        await _feed.KeyTypedAsync(TimeSpan.FromMilliseconds(1));

        Assert.Equal(1, _caretReads);
    }

    [Fact]
    public async Task BrowseCursor_OneLookupAtATime_TheNewestElementWins()
    {
        var first = new VBufferNode { Name = "first" };
        var second = new VBufferNode { Name = "second" };
        var third = new VBufferNode { Name = "third" };

        _feed.BrowseUpdated(first);
        _feed.BrowseUpdated(first); // the same element: nothing more
        _feed.BrowseUpdated(second);
        _feed.BrowseUpdated(third);
        Assert.Equal([first], _lookedUp);

        // The first answer is stale by now: the third element is looked up instead of it being followed
        Answer(first, new ScreenRect(5, 5, 10, 10));
        await Until(() => _lookedUp.Count == 2);
        Assert.Equal([first, third], _lookedUp);
        var before = _magnifier.Last;
        Assert.Equal((480.0, 270.0), (before!.Left, before.Top));

        Answer(third, new ScreenRect(1600, 900, 20, 20));
        await Until(() => _magnifier.Last!.Left == 960);
        Assert.Equal(540, _magnifier.Last!.Top);
    }

    [Fact]
    public void BrowseCursor_NotLookedUpWhileNotFollowed()
    {
        _settings.Set(_settings.CurrentValue with { MagnifierFollowBrowseCursor = false });

        _feed.BrowseUpdated(new VBufferNode());

        Assert.Empty(_lookedUp);
    }

    [Fact]
    public void ReviewCursor_FollowsTheRangesBounds()
    {
        _feed.ReviewMoved(new BoundedRange(new ScreenRect(100, 100, 50, 18)));

        Assert.Equal((0.0, 0.0), (_magnifier.Last!.Left, _magnifier.Last.Top));
    }

    [Fact]
    public void Pointer_TheFirstPositionIsWhereItRests_ThenMovesAreFollowed()
    {
        _feed.PointerPolled((1900, 1070));
        Assert.Equal(480, _magnifier.Last!.Left);

        _feed.PointerPolled((1910, 1075));
        Assert.Equal(960, _magnifier.Last!.Left);

        // After a pause the next position starts afresh
        _feed.PointerPaused();
        _feed.PointerPolled((5, 5));
        Assert.Equal(960, _magnifier.Last!.Left);
    }

    /// <summary>A text range that only knows where it is.</summary>
    private sealed class BoundedRange(ScreenRect bounds) : ITextRange
    {
        public string GetText(int maxLength = -1) => string.Empty;
        public bool IsDegenerate => true;
        public ITextRange ExpandToEnclosingUnit(TextUnit unit) => this;
        public (ITextRange Range, int Moved) Move(TextUnit unit, int count) => (this, 0);
        public int CompareEndpoints(TextEndpoint endpoint, ITextRange other, TextEndpoint otherEndpoint) => 0;
        public ITextRange WithEndpoint(TextEndpoint endpoint, ITextRange other, TextEndpoint otherEndpoint) => this;
        public TextAttributes GetAttributes() => new();
        public ScreenRect? GetBounds() => bounds;
    }
}
