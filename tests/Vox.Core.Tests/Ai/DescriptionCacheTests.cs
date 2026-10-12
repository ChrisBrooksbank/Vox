using Vox.Core.Ai;
using Xunit;

namespace Vox.Core.Tests.Ai;

public class DescriptionCacheTests
{
    [Fact]
    public void TheKey_IsWhatWasAsked()
    {
        var image = AiTestSupport.Image(seed: 1);
        var request = new AiRequest(AiTask.DescribeImage, image);
        var key = DescriptionCache.KeyFor(request, "Claude");

        Assert.Equal(key, DescriptionCache.KeyFor(new AiRequest(AiTask.DescribeImage, AiTestSupport.Image(seed: 1)), "Claude"));
        Assert.NotEqual(key, DescriptionCache.KeyFor(new AiRequest(AiTask.DescribeImage, AiTestSupport.Image(seed: 2)), "Claude"));
        Assert.NotEqual(key, DescriptionCache.KeyFor(request, "llava"));
        Assert.NotEqual(key, DescriptionCache.KeyFor(request with { Question = "What colour is it?" }, "Claude"));
        Assert.NotEqual(key, DescriptionCache.KeyFor(request with { Language = "fr" }, "Claude"));
        Assert.NotEqual(key, DescriptionCache.KeyFor(request with { Task = AiTask.ExplainScreen }, "Claude"));
    }

    [Fact]
    public void TheLeastRecentlyUsed_GoesFirst()
    {
        var cache = new DescriptionCache(capacity: 2);
        cache.Add("a", "first");
        cache.Add("b", "second");
        Assert.True(cache.TryGet("a", out _));

        cache.Add("c", "third");

        Assert.True(cache.TryGet("a", out var a));
        Assert.Equal("first", a);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void AddingAgain_Replaces()
    {
        var cache = new DescriptionCache();
        cache.Add("a", "old");
        cache.Add("a", "new");

        Assert.True(cache.TryGet("a", out var text));
        Assert.Equal("new", text);
        Assert.Equal(1, cache.Count);
    }
}
