using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// PDFs opened in Edge (or Chrome): their viewer exposes the PDF's tag tree as an ordinary web
/// document, so a tagged PDF is browsed like a page. An untagged PDF has no structure (no
/// headings, lists or tables), and a scanned one no text at all; both are said on load.
/// </summary>
public static class PdfDocuments
{
    /// <summary>True when the document's address (Chromium reports it as the document's value) is a PDF.</summary>
    public static bool IsPdf(VBufferDocument document)
    {
        var url = document.Root.Value.Trim();
        if (url.Length == 0)
            return false;
        int end = url.IndexOfAny(['?', '#']);
        var path = end < 0 ? url : url[..end];
        return path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What to say when a PDF is loaded, or null for any other document.</summary>
    public static string? LoadAnnouncement(VBufferDocument document)
    {
        if (!IsPdf(document))
            return null;
        if (string.IsNullOrWhiteSpace(document.FlatText))
            return "PDF document with no text, probably a scanned image";
        bool structured = document.Headings.Count > 0 || document.Lists.Count > 0 || document.TableModels.Any(t => !t.IsLayoutTable);
        return structured ? "PDF document" : "Untagged PDF document: no headings or structure";
    }
}
