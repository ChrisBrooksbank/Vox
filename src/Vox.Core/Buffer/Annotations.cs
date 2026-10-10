namespace Vox.Core.Buffer;

/// <summary>
/// Annotations in a page: comments (role comment, or a UIA Comment annotation), suggested
/// insertions and deletions (&lt;ins&gt;, &lt;del&gt;, role insertion/deletion, tracked changes),
/// highlights (&lt;mark&gt;) and suggestions. Classified from the ARIA role and UIA
/// AnnotationTypes, and announced when the browse cursor enters or leaves one.
/// </summary>
public static class Annotations
{
    public const string Comment = "comment";
    public const string Insertion = "insertion";
    public const string Deletion = "deletion";
    public const string Highlight = "highlight";
    public const string Suggestion = "suggestion";

    // UIA AnnotationType identifiers (UIAutomationClient.h)
    private const int AnnotationType_Comment = 60003;
    private const int AnnotationType_Highlighted = 60008;
    private const int AnnotationType_InsertionChange = 60011;
    private const int AnnotationType_DeletionChange = 60012;

    /// <summary>The annotation kind for an ARIA role and UIA annotation types, or empty.</summary>
    public static string Kind(string? ariaRole, IReadOnlyList<int>? annotationTypes)
    {
        switch (ariaRole?.Trim().ToLowerInvariant())
        {
            case "comment": return Comment;
            case "insertion": return Insertion;
            case "deletion": return Deletion;
            case "mark": return Highlight;
            case "suggestion": return Suggestion;
        }
        if (annotationTypes is null)
            return string.Empty;
        if (annotationTypes.Contains(AnnotationType_Comment)) return Comment;
        if (annotationTypes.Contains(AnnotationType_InsertionChange)) return Insertion;
        if (annotationTypes.Contains(AnnotationType_DeletionChange)) return Deletion;
        if (annotationTypes.Contains(AnnotationType_Highlighted)) return Highlight;
        return string.Empty;
    }

    /// <summary>Kinds that mark up a run of text (and so stay on its line).</summary>
    public static bool IsInlineKind(string kind) => kind is Insertion or Deletion or Highlight;

    /// <summary>What entering an annotation of this kind says.</summary>
    public static string EnterText(string kind) => kind switch
    {
        Insertion => "inserted",
        Deletion => "deleted",
        Highlight => "highlighted",
        _ => kind,
    };

    /// <summary>What leaving it says.</summary>
    public static string ExitText(string kind) => kind switch
    {
        Insertion => "end of inserted",
        Deletion => "end of deleted",
        Highlight => "end of highlighted",
        _ => $"end of {kind}",
    };

    /// <summary>
    /// The annotations left and entered moving from <paramref name="from"/> to
    /// <paramref name="to"/> ("end of deleted, inserted"), or null when the same ones surround both.
    /// </summary>
    public static string? Transition(VBufferNode? from, VBufferNode? to)
    {
        if (from is null || to is null || ReferenceEquals(from, to))
            return null;
        var before = Around(from);
        var after = Around(to);
        int common = 0;
        while (common < before.Count && common < after.Count && ReferenceEquals(before[common], after[common]))
            common++;
        if (common == before.Count && common == after.Count)
            return null;

        var parts = new List<string>();
        for (int i = before.Count - 1; i >= common; i--)
            parts.Add(ExitText(before[i].Annotation));
        for (int i = common; i < after.Count; i++)
            parts.Add(EnterText(after[i].Annotation));
        return string.Join(", ", parts);
    }

    /// <summary>The annotations around <paramref name="node"/> (itself included), outermost first.</summary>
    public static IReadOnlyList<VBufferNode> Around(VBufferNode node)
    {
        var found = new List<VBufferNode>();
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n.Annotation.Length > 0)
                found.Add(n);
        }
        found.Reverse();
        return found;
    }
}
