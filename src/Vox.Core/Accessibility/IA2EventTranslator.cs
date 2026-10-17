using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

public enum IA2EventKind
{
    /// <summary><c>IA2_EVENT_TEXT_INSERTED</c> in a node.</summary>
    TextInserted,
    /// <summary><c>IA2_EVENT_TEXT_REMOVED</c> from a node.</summary>
    TextRemoved,
    /// <summary><c>IA2_EVENT_TEXT_ATTRIBUTE_CHANGED</c>: spelling errors, language, fonts.</summary>
    TextAttributeChanged,
    /// <summary><c>IA2_EVENT_OBJECT_ATTRIBUTE_CHANGED</c>: ARIA states and properties.</summary>
    ObjectAttributeChanged,
}

/// <summary>
/// A COM-free IAccessible2 event from a Firefox document, as <c>Vox.NativeHelper</c> reports it.
/// </summary>
/// <param name="UniqueId">IA2 unique id of the node the event is about.</param>
/// <param name="Text">The inserted or removed text.</param>
/// <param name="RegionText">The whole text of the enclosing live region after the change.</param>
/// <param name="Attributes">The node's object attributes (<c>live</c>, <c>container-live</c>, <c>container-atomic</c>...).</param>
/// <param name="Subtree">The node re-captured after a text/object attribute change.</param>
/// <param name="AncestorIds">Unique ids of the node's ancestors up to the document, nearest first.</param>
/// <param name="DocumentId">Unique id of the document.</param>
/// <param name="IsForeground">False for a window that is not the one in use.</param>
public sealed record IA2Event(
    IA2EventKind Kind,
    int UniqueId,
    string Text = "",
    string RegionText = "",
    IReadOnlyDictionary<string, string>? Attributes = null,
    IA2Node? Subtree = null,
    IReadOnlyList<int>? AncestorIds = null,
    int DocumentId = 0,
    bool IsForeground = true);

/// <summary>
/// Posts a Firefox page's IA2 live-region and attribute events to the pipeline like the UIA
/// subscriber does: live-region text as <see cref="LiveRegionChangedEvent"/>s, attribute changes as
/// <see cref="SubtreeChangedEvent"/>s (the buffer re-reads the node). Never logs text.
/// </summary>
public sealed class IA2EventTranslator
{
    private readonly IEventSink _sink;
    private readonly Func<DateTimeOffset> _clock;

    public IA2EventTranslator(IEventSink sink) : this(sink, () => DateTimeOffset.UtcNow) { }

    public IA2EventTranslator(IEventSink sink, Func<DateTimeOffset> clock)
    {
        _sink = sink;
        _clock = clock;
    }

    /// <summary>Politeness from the <c>live</c> / <c>container-live</c> attributes; Off when neither is set.</summary>
    public static LiveRegionPoliteness PolitenessOf(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null) return LiveRegionPoliteness.Off;
        var live = attributes.TryGetValue("container-live", out var c) && c.Length > 0 ? c
            : attributes.TryGetValue("live", out var l) ? l : "";
        return live switch
        {
            "assertive" => LiveRegionPoliteness.Assertive,
            "polite" => LiveRegionPoliteness.Polite,
            _ => LiveRegionPoliteness.Off,
        };
    }

    /// <summary>Handles one event; returns true when it posted something.</summary>
    public bool Handle(IA2Event e)
    {
        switch (e.Kind)
        {
            case IA2EventKind.TextInserted:
            case IA2EventKind.TextRemoved:
                return PostLive(e);
            case IA2EventKind.TextAttributeChanged:
            case IA2EventKind.ObjectAttributeChanged:
                return PostSubtree(e);
            default:
                return false;
        }
    }

    private bool PostLive(IA2Event e)
    {
        var politeness = PolitenessOf(e.Attributes);
        if (politeness == LiveRegionPoliteness.Off || !e.IsForeground)
            return false;

        var atomic = e.Attributes is not null
            && (e.Attributes.TryGetValue("container-atomic", out var ca) && ca == "true"
                || e.Attributes.TryGetValue("atomic", out var a) && a == "true");

        // Like UIA the monitor is given the region's text, which it diffs; a removal or an atomic
        // region needs the whole text, an insertion into a non-atomic region can be just the new text
        var text = e.Kind == IA2EventKind.TextRemoved || atomic || e.RegionText.Length > 0 ? e.RegionText : e.Text;

        _sink.Post(new LiveRegionChangedEvent(_clock(), text, politeness, "ia2:" + e.UniqueId));
        return true;
    }

    private bool PostSubtree(IA2Event e)
    {
        if (e.Subtree is null)
            return false;

        var snapshot = IA2ElementSnapshot.From(e.Subtree);
        int[] documentId = [e.DocumentId];
        var ancestors = (e.AncestorIds ?? []).Select(id => new[] { id }).ToList();
        _sink.Post(new SubtreeChangedEvent(_clock(), [e.UniqueId], snapshot, documentId, ancestors));
        return true;
    }
}
