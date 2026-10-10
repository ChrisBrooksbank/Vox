using System.Globalization;
using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// What a page is and holds, said by the page summary command: its title, language, and how
/// many headings, links, landmarks, form fields and (data) tables it has.
/// </summary>
public static class PageSummary
{
    public static string Describe(VBufferDocument document)
    {
        var parts = new List<string>();
        var title = document.Root.Name.Trim();
        parts.Add(title.Length > 0 ? title : "Untitled page");
        if (LanguageName(document.Root.Language) is { } language)
            parts.Add($"language {language}");

        int layoutTables = document.TableModels.Count(t => t.IsLayoutTable);
        parts.Add(Count(document.Headings.Count, "heading"));
        parts.Add(Count(document.Links.Count, "link"));
        parts.Add(Count(document.Landmarks.Count, "landmark"));
        parts.Add(Count(document.FormFields.Count, "form field"));
        parts.Add(Count(Math.Max(0, document.Tables.Count - layoutTables), "table"));
        return string.Join(", ", parts);
    }

    /// <summary>The URL of the link at or around <paramref name="node"/> (Chromium reports it as the link's value).</summary>
    public static string LinkUrlText(VBufferNode? node)
    {
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n.IsLink)
                return n.Value.Trim() is { Length: > 0 } url ? url : "Link has no address";
        }
        return "Not on a link";
    }

    private static string Count(int count, string noun) =>
        count switch
        {
            0 => $"no {noun}s",
            1 => $"1 {noun}",
            _ => $"{count} {noun}s",
        };

    private static string? LanguageName(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        try { return CultureInfo.GetCultureInfo(tag).EnglishName; }
        catch (CultureNotFoundException) { return tag; }
    }
}
