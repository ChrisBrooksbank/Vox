using System.Net;
using System.Text;
using Vox.Core.Buffer;

namespace Vox.Core.MathSpeech;

/// <summary>
/// Finds math in the buffer and gets its MathML back. Chromium exposes a <c>&lt;math&gt;</c>
/// element (and <c>role="math"</c>) with the ARIA role "math", and its MathML elements with
/// their tag names as roles (mi, mn, mo, mfrac...), from which the MathML is rebuilt. Math
/// written as MathML text in its name or description (MathJax's alternative text) is used as is.
/// </summary>
public static class MathMarkup
{
    private static readonly HashSet<string> TokenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "mi", "mn", "mo", "mtext", "ms",
    };

    private static readonly HashSet<string> LayoutTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "mrow", "mfrac", "msqrt", "mroot", "msub", "msup", "msubsup", "munder", "mover",
        "munderover", "mtable", "mtr", "mtd", "mfenced", "menclose", "mstyle", "mpadded",
        "mphantom", "mmultiscripts", "mprescripts", "none", "semantics",
    };

    /// <summary>True for a math element (&lt;math&gt; or role math).</summary>
    public static bool IsMath(VBufferNode node) =>
        node.AriaRole.Trim().Equals("math", StringComparison.OrdinalIgnoreCase);

    /// <summary>The math element <paramref name="node"/> is in (itself included), or null.</summary>
    public static VBufferNode? Enclosing(VBufferNode? node)
    {
        VBufferNode? found = null;
        for (var n = node; n is not null; n = n.Parent)
        {
            if (IsMath(n))
                found = n; // the outermost: nested role=math is still one expression
        }
        return found;
    }

    /// <summary>The MathML of a math element, or null when it has no structure to rebuild.</summary>
    public static string? ToMathMl(VBufferNode math)
    {
        foreach (var text in new[] { math.Name, math.Description })
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith("<math", StringComparison.OrdinalIgnoreCase))
                return trimmed;
        }

        if (!HasMathElements(math))
            return null;
        var builder = new StringBuilder();
        builder.Append("<math>");
        foreach (var child in math.Children)
            Append(child, builder);
        builder.Append("</math>");
        return builder.ToString();
    }

    private static bool HasMathElements(VBufferNode math)
    {
        var stack = new Stack<VBufferNode>(math.Children);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (Tag(node) is not null)
                return true;
            foreach (var child in node.Children)
                stack.Push(child);
        }
        return false;
    }

    private static string? Tag(VBufferNode node)
    {
        var role = node.AriaRole.Trim();
        return TokenTags.Contains(role) || LayoutTags.Contains(role) ? role.ToLowerInvariant() : null;
    }

    private static void Append(VBufferNode node, StringBuilder builder)
    {
        var tag = Tag(node);
        if (tag is not null && TokenTags.Contains(tag))
        {
            builder.Append('<').Append(tag).Append('>')
                .Append(WebUtility.HtmlEncode(TokenText(node)))
                .Append("</").Append(tag).Append('>');
            return;
        }

        if (tag is null)
        {
            // An element without a MathML role: its text, or its children grouped
            if (node.Children.Count == 0)
            {
                var text = node.Name.Trim();
                if (text.Length > 0)
                    builder.Append("<mtext>").Append(WebUtility.HtmlEncode(text)).Append("</mtext>");
                return;
            }
            tag = "mrow";
        }

        builder.Append('<').Append(tag).Append('>');
        foreach (var child in node.Children)
            Append(child, builder);
        builder.Append("</").Append(tag).Append('>');
    }

    /// <summary>A token's text: its name, or its text children's (Chromium puts it in a text child).</summary>
    private static string TokenText(VBufferNode node)
    {
        if (node.Children.Count == 0)
            return node.Name.Trim();
        var text = new StringBuilder();
        foreach (var child in node.Children)
            text.Append(TokenText(child));
        return text.ToString();
    }
}
