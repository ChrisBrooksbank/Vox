using Vox.Core.Buffer;
using Vox.Core.Localization;

namespace Vox.Core.Navigation;

/// <summary>
/// PDFs opened in Edge (or Chrome): their viewer exposes the PDF's tag tree as an ordinary web
/// document, so a tagged PDF is browsed like a page. An untagged PDF has no structure (no
/// headings, lists or tables), and a scanned one no text at all; both are said on load, with
/// the offer to recognise the text on the screen (Insert+R, <see cref="Ocr.OcrCommands"/>).
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

    /// <summary>
    /// What to say when a PDF is loaded, or null for any other document. An untagged or scanned
    /// PDF also offers text recognition, with <paramref name="recognizeKey"/> (null: no offer).
    /// </summary>
    public static string? LoadAnnouncement(VBufferDocument document, string? recognizeKey = null)
    {
        if (!IsPdf(document))
            return null;
        string announcement;
        if (string.IsNullOrWhiteSpace(document.FlatText))
            announcement = VoxStrings.Get("Pdf.NoText");
        else if (IsStructured(document))
            return VoxStrings.Get("Pdf.Document");
        else
            announcement = VoxStrings.Get("Pdf.Untagged");
        return recognizeKey is null ? announcement : $"{announcement}. {VoxStrings.Format("Ocr.PdfOffer", recognizeKey)}";
    }

    private static bool IsStructured(VBufferDocument document) =>
        document.Headings.Count > 0 || document.Lists.Count > 0 || document.TableModels.Any(t => !t.IsLayoutTable);
}
