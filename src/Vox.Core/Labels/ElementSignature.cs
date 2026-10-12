using System.Text;
using Vox.Core.Buffer;

namespace Vox.Core.Labels;

/// <summary>
/// A stable name for an element of a page, under which its custom label is kept: its automation
/// id (the HTML id) when it has one, else its role, the roles of its nearest meaningful ancestors
/// and the last words of text before it. Built from what survives a reload or an update of
/// another part of the page; neither the element's position among its siblings nor its own
/// (unhelpful) name is part of it.
/// </summary>
public static class ElementSignature
{
    /// <summary>How many ancestors (generic containers left out) are part of the signature.</summary>
    public const int PathDepth = 5;

    /// <summary>How many words of the text before the element are part of the signature.</summary>
    public const int NearbyWords = 3;

    /// <summary>How much text before the element is looked at for those words.</summary>
    internal const int NearbyWindow = 200;

    /// <summary>The signature of <paramref name="node"/> in a built document with <paramref name="flatText"/>.</summary>
    public static string Of(VBufferNode node, string flatText)
    {
        if (!string.IsNullOrEmpty(node.AutomationId))
            return Compose(node.AutomationId, node.ControlType, node.AriaRole, [], string.Empty);
        int start = Math.Clamp(node.TextRange.Start, 0, flatText.Length);
        int from = Math.Max(0, start - NearbyWindow);
        return Compose(node.AutomationId, node.ControlType, node.AriaRole,
            AncestorKeys(node.Parent, []), Nearby(flatText.AsSpan(from, start - from)));
    }

    /// <summary>
    /// The signature of an element with these properties: "id:&lt;automation id&gt;", or
    /// "&lt;role&gt;|&lt;ancestor roles, nearest first&gt;|&lt;nearby words&gt;".
    /// </summary>
    public static string Compose(string? automationId, string controlType, string? ariaRole, IReadOnlyList<string> ancestorKeys, string nearby)
    {
        if (!string.IsNullOrWhiteSpace(automationId))
            return "id:" + automationId.Trim();
        return $"{RoleKey(controlType, ariaRole)}|{string.Join(">", ancestorKeys)}|{nearby}";
    }

    /// <summary>
    /// The roles of the nearest <see cref="PathDepth"/> meaningful ancestors, starting at
    /// <paramref name="parent"/> and going on with <paramref name="outerKeys"/> (those of the
    /// ancestors above a re-captured subtree) when the parent chain ends.
    /// </summary>
    public static IReadOnlyList<string> AncestorKeys(VBufferNode? parent, IReadOnlyList<string> outerKeys)
    {
        var keys = new List<string>(PathDepth);
        for (var node = parent; node is not null && keys.Count < PathDepth; node = node.Parent)
        {
            if (!IsGeneric(node.ControlType, node.AriaRole))
                keys.Add(RoleKey(node.ControlType, node.AriaRole));
        }
        for (int i = 0; i < outerKeys.Count && keys.Count < PathDepth; i++)
            keys.Add(outerKeys[i]);
        return keys;
    }

    /// <summary>The last <see cref="NearbyWords"/> words of <paramref name="before"/>, lower case, single-spaced.</summary>
    public static string Nearby(ReadOnlySpan<char> before)
    {
        var words = new List<string>(NearbyWords);
        int end = before.Length;
        while (words.Count < NearbyWords && end > 0)
        {
            while (end > 0 && char.IsWhiteSpace(before[end - 1]))
                end--;
            int start = end;
            while (start > 0 && !char.IsWhiteSpace(before[start - 1]))
                start--;
            if (start == end)
                break;
            words.Add(before[start..end].ToString().ToLowerInvariant());
            end = start;
        }
        words.Reverse();
        var nearby = string.Join(" ", words);
        return nearby.Length > 60 ? nearby[^60..] : nearby;
    }

    private static string RoleKey(string controlType, string? ariaRole) =>
        string.IsNullOrWhiteSpace(ariaRole) ? controlType : ariaRole.Trim().ToLowerInvariant();

    // Plain <div>s and <span>s come and go with a page's styling: not part of the path
    private static bool IsGeneric(string controlType, string? ariaRole) =>
        controlType is "Group" or "Custom" or "Pane" or "Text"
        && (string.IsNullOrWhiteSpace(ariaRole) || ariaRole is "generic" or "none" or "presentation" or "group" or "paragraph");
}

/// <summary>
/// The labels to apply while building part of a page: those of the site, plus what the builder
/// can't see of a re-captured subtree's surroundings (its ancestors and the text before it).
/// </summary>
internal sealed class LabelScope(SiteLabels labels, IReadOnlyList<string> outerAncestorKeys, string textBefore)
{
    public SiteLabels Labels => labels;

    /// <summary>The label of an element about to be built under <paramref name="parent"/>, with <paramref name="flatText"/> before it.</summary>
    public string? LabelFor(IVBufferElement element, VBufferNode? parent, StringBuilder flatText)
    {
        string signature;
        if (!string.IsNullOrWhiteSpace(element.AutomationId))
        {
            signature = ElementSignature.Compose(element.AutomationId, element.ControlType, element.AriaRole, [], string.Empty);
        }
        else
        {
            string before = flatText.Length >= ElementSignature.NearbyWindow
                ? flatText.ToString(flatText.Length - ElementSignature.NearbyWindow, ElementSignature.NearbyWindow)
                : textBefore + flatText.ToString();
            signature = ElementSignature.Compose(null, element.ControlType, element.AriaRole,
                ElementSignature.AncestorKeys(parent, outerAncestorKeys), ElementSignature.Nearby(before));
        }
        return labels.Get(signature);
    }
}

/// <summary>Which elements can be given a label: links, controls and graphics, not text or containers.</summary>
public static class CustomLabelTargets
{
    public static bool IsLabelable(VBufferNode node) =>
        node.Parent is not null
        && (node.IsLink || node.IsFocusable || PageElements.IsGraphic(node) || !string.IsNullOrEmpty(node.CustomLabel));

    /// <summary>
    /// The labelable element at or around <paramref name="node"/>: its nearest link or control
    /// (an icon inside a button names the button), else its nearest graphic; null when none.
    /// </summary>
    public static VBufferNode? Around(VBufferNode? node)
    {
        VBufferNode? graphic = null;
        for (; node?.Parent is not null; node = node.Parent)
        {
            if (node.IsLink || node.IsFocusable || !string.IsNullOrEmpty(node.CustomLabel))
                return node;
            graphic ??= PageElements.IsGraphic(node) ? node : null;
        }
        return graphic;
    }
}
