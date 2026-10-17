using Xunit;
using Vox.Core.Accessibility;
using Vox.Core.Pipeline;

namespace Vox.Core.Tests.Accessibility;

public class IA2EventTranslatorTests
{
    private sealed class Sink : IEventSink
    {
        public List<ScreenReaderEvent> Events { get; } = [];
        public void Post(ScreenReaderEvent evt) => Events.Add(evt);
    }

    private static Dictionary<string, string> Attrs(string key, string value) => new() { [key] = value };

    [Fact]
    public void Insert_IntoPoliteRegion_PostsLiveRegionEvent()
    {
        var sink = new Sink();
        var posted = new IA2EventTranslator(sink).Handle(
            new IA2Event(IA2EventKind.TextInserted, 7, Text: "Saved", Attributes: Attrs("container-live", "polite")));

        Assert.True(posted);
        var live = Assert.IsType<LiveRegionChangedEvent>(Assert.Single(sink.Events));
        Assert.Equal("Saved", live.Text);
        Assert.Equal(LiveRegionPoliteness.Polite, live.Politeness);
        Assert.Equal("ia2:7", live.SourceId);
    }

    [Fact]
    public void Insert_IntoAssertiveAtomicRegion_PostsWholeRegionText()
    {
        var sink = new Sink();
        new IA2EventTranslator(sink).Handle(new IA2Event(IA2EventKind.TextInserted, 7, Text: "3",
            RegionText: "Errors: 3",
            Attributes: new Dictionary<string, string> { ["live"] = "assertive", ["container-atomic"] = "true" }));

        var live = Assert.IsType<LiveRegionChangedEvent>(Assert.Single(sink.Events));
        Assert.Equal("Errors: 3", live.Text);
        Assert.Equal(LiveRegionPoliteness.Assertive, live.Politeness);
    }

    [Fact]
    public void Removal_PostsRegionTextSoTheMonitorSeesItEmpty()
    {
        var sink = new Sink();
        new IA2EventTranslator(sink).Handle(new IA2Event(IA2EventKind.TextRemoved, 7, Text: "Saved",
            Attributes: Attrs("live", "polite")));

        Assert.Equal("", Assert.IsType<LiveRegionChangedEvent>(Assert.Single(sink.Events)).Text);
    }

    [Fact]
    public void NotALiveRegion_OrBackgroundWindow_PostsNothing()
    {
        var sink = new Sink();
        var t = new IA2EventTranslator(sink);
        Assert.False(t.Handle(new IA2Event(IA2EventKind.TextInserted, 1, Text: "x")));
        Assert.False(t.Handle(new IA2Event(IA2EventKind.TextInserted, 1, Text: "x", Attributes: Attrs("live", "off"))));
        Assert.False(t.Handle(new IA2Event(IA2EventKind.TextInserted, 1, Text: "x", Attributes: Attrs("live", "polite"), IsForeground: false)));
        Assert.Empty(sink.Events);
    }

    [Theory]
    [InlineData(IA2EventKind.TextAttributeChanged)]
    [InlineData(IA2EventKind.ObjectAttributeChanged)]
    public void AttributeChange_PostsSubtreeUpdateWithAncestors(IA2EventKind kind)
    {
        var sink = new Sink();
        var node = new IA2Node { UniqueId = 12, Role = 30, Name = "Link" };
        var posted = new IA2EventTranslator(sink).Handle(new IA2Event(kind, 12, Subtree: node, AncestorIds: [11, 10], DocumentId: 10));

        Assert.True(posted);
        var sub = Assert.IsType<SubtreeChangedEvent>(Assert.Single(sink.Events));
        Assert.Equal([12], sub.RuntimeId);
        Assert.Equal([10], sub.DocumentRuntimeId);
        Assert.Equal("Link", sub.NewSubtree!.Name);
        Assert.Equal(2, sub.AncestorRuntimeIds!.Count);
        Assert.Equal([11], sub.AncestorRuntimeIds[0]);
    }

    [Fact]
    public void AttributeChange_WithoutSubtree_PostsNothing()
    {
        var sink = new Sink();
        Assert.False(new IA2EventTranslator(sink).Handle(new IA2Event(IA2EventKind.TextAttributeChanged, 1)));
        Assert.Empty(sink.Events);
    }
}
