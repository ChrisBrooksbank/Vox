namespace Vox.Core.Buffer;

/// <summary>
/// Classifies the page elements quick navigation finds besides form controls (lists, graphics,
/// block quotes, frames, separators, embedded objects), by UIA control type or ARIA role.
///
/// Chromium reports plain &lt;ul&gt;/&lt;li&gt; as List/ListItem, but list boxes and their options use
/// the same control types, so those are told apart by ARIA role (listbox/option are form fields).
/// </summary>
public static class PageElements
{
    private static readonly HashSet<string> GraphicRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "img", "image", "graphics-document", "graphics-symbol"
    };

    private static readonly HashSet<string> FrameRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "iframe", "frame"
    };

    private static readonly HashSet<string> EmbeddedObjectRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "embeddedobject", "pluginobject", "application", "audio", "video"
    };

    /// <summary>A list (not a list box, tree or menu), by control type or ARIA role.</summary>
    public static bool IsList(VBufferNode node) =>
        (Is(node.ControlType, "List") || Is(node.AriaRole, "list") || Is(node.AriaRole, "directory"))
        && !FormControls.IsFormField(node.ControlType, node.AriaRole);

    /// <summary>A list item (not a list box option or tree item), by control type or ARIA role.</summary>
    public static bool IsListItem(VBufferNode node) =>
        (Is(node.ControlType, "ListItem") || Is(node.AriaRole, "listitem"))
        && !FormControls.IsFormField(node.ControlType, node.AriaRole);

    /// <summary>An image or other graphic, by control type or ARIA role.</summary>
    public static bool IsGraphic(VBufferNode node) =>
        Is(node.ControlType, "Image") || GraphicRoles.Contains(node.AriaRole ?? string.Empty);

    /// <summary>A block quote, by ARIA role (Chromium and Firefox report blockquote as a group).</summary>
    public static bool IsBlockQuote(VBufferNode node) =>
        Is(node.AriaRole, "blockquote");

    /// <summary>
    /// A frame: an element with the iframe/frame role, or a document nested in the page that
    /// isn't the content of such an element (so each frame is found once).
    /// </summary>
    public static bool IsFrame(VBufferNode node) =>
        FrameRoles.Contains(node.AriaRole ?? string.Empty)
        || (Is(node.ControlType, "Document") && node.Parent is { } parent
            && !FrameRoles.Contains(parent.AriaRole ?? string.Empty));

    /// <summary>A separator (a horizontal rule), by control type or ARIA role.</summary>
    public static bool IsSeparator(VBufferNode node) =>
        Is(node.ControlType, "Separator") || Is(node.AriaRole, "separator");

    /// <summary>An embedded object (plug-in, embedded application, audio or video player), by ARIA role.</summary>
    public static bool IsEmbeddedObject(VBufferNode node) =>
        EmbeddedObjectRoles.Contains(node.AriaRole ?? string.Empty);

    private static bool Is(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
