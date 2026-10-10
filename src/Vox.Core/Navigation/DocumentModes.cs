using Vox.Core.Buffer;
using Vox.Core.Pipeline;

namespace Vox.Core.Navigation;

/// <summary>
/// The mode a newly loaded document starts in. Web applications (Electron and WebView2 apps such
/// as VS Code or Teams, pages that are one <c>role="application"</c> or an editor) are used
/// through their own keys, so they start in Focus mode; so does any app the user listed in the
/// <c>AppDefaultModes</c> setting as Focus. Everything else starts in Browse mode.
/// </summary>
public static class DocumentModes
{
    // How deep below the document an application element may sit (inside body wrappers)
    private const int MaxApplicationDepth = 4;

    public static InteractionMode Initial(VBufferDocument document, string? processName,
        IReadOnlyDictionary<string, InteractionMode>? appModes)
    {
        if (!string.IsNullOrEmpty(processName) && appModes is not null)
        {
            foreach (var (app, mode) in appModes)
            {
                if (string.Equals(app, processName, StringComparison.OrdinalIgnoreCase))
                    return mode;
            }
        }
        return IsApplication(document) ? InteractionMode.Focus : InteractionMode.Browse;
    }

    /// <summary>
    /// True when the document is (or is almost wholly) an application or editor: an element near
    /// its root with role application, or role description "editor", holding at least half its text.
    /// </summary>
    public static bool IsApplication(VBufferDocument document)
    {
        int textLength = document.FlatText.Length;
        var level = new List<VBufferNode> { document.Root };
        for (int depth = 0; depth <= MaxApplicationDepth && level.Count > 0; depth++)
        {
            var next = new List<VBufferNode>();
            foreach (var node in level)
            {
                if (IsApplicationRole(node))
                {
                    var (start, end) = VBufferDocument.SubtreeSpan(node);
                    if (textLength == 0 || (end - start) * 2 >= textLength)
                        return true;
                }
                next.AddRange(node.Children);
            }
            level = next;
        }
        return false;
    }

    private static bool IsApplicationRole(VBufferNode node) =>
        node.AriaRole.Trim().Equals("application", StringComparison.OrdinalIgnoreCase)
        || node.RoleDescription.Contains("editor", StringComparison.OrdinalIgnoreCase);
}
