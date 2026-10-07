using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// The message of a dialog: its static text that no control owns ("Do you want to save changes
/// to Untitled?"), read when the dialog opens, since focus lands on a button and the message
/// would otherwise go unheard. Text inside controls, lists, tables and documents is not dialog
/// text, nor is a label just before the field it names, nor the dialog's own title.
/// </summary>
public static class DialogText
{
    private static readonly HashSet<string> ContainersToSkip = new(StringComparer.Ordinal)
    {
        "List", "Tree", "Table", "DataGrid", "Document", "Menu", "MenuBar", "ToolBar", "StatusBar",
        "TitleBar", "Tab", "ComboBox", "Edit", "Hyperlink",
    };

    private static readonly HashSet<string> LabelledControls = new(StringComparer.Ordinal)
    {
        "Edit", "ComboBox", "Spinner", "Slider", "List", "Tree", "DataGrid", "Table",
    };

    /// <summary>The dialog's text, in reading order.</summary>
    public static IReadOnlyList<string> Collect(IVBufferElement dialog, int maxDepth = 8, int maxItems = 20)
    {
        var texts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { dialog.Name.Trim() };
        Walk(dialog, 0);
        return texts;

        void Walk(IVBufferElement element, int depth)
        {
            if (depth > maxDepth || texts.Count >= maxItems)
                return;
            var children = element.GetChildren();
            for (int i = 0; i < children.Count && texts.Count < maxItems; i++)
            {
                var child = children[i];
                if (child.ControlType == "Text")
                {
                    var name = child.Name.Trim();
                    bool isLabel = i + 1 < children.Count && LabelledControls.Contains(children[i + 1].ControlType);
                    if (name.Length > 0 && !child.IsFocusable && !isLabel && seen.Add(name))
                        texts.Add(name);
                    continue;
                }
                // Controls own their text (a button's caption), as do lists, tables and documents
                if (child.IsFocusable || ContainersToSkip.Contains(child.ControlType))
                    continue;
                Walk(child, depth + 1);
            }
        }
    }
}
