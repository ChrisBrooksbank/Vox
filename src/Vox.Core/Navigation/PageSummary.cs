using System.Globalization;
using Vox.Core.Buffer;
using Vox.Core.Localization;

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
        parts.Add(title.Length > 0 ? title : VoxStrings.Get("PageDigest.Untitled"));
        if (LanguageName(document.Root.Language) is { } language)
            parts.Add(VoxStrings.Format("PageSummary.Language", language));

        int layoutTables = document.TableModels.Count(t => t.IsLayoutTable);
        int headings = document.Headings.Count;
        parts.Add(Count(headings, VoxStrings.Get("PageSummary.NoHeadings"),
            VoxStrings.Get("PageSummary.OneHeading"), VoxStrings.Format("PageSummary.Headings", headings)));
        int links = document.Links.Count;
        parts.Add(Count(links, VoxStrings.Get("PageSummary.NoLinks"),
            VoxStrings.Get("PageSummary.OneLink"), VoxStrings.Format("PageSummary.Links", links)));
        int landmarks = document.Landmarks.Count;
        parts.Add(Count(landmarks, VoxStrings.Get("PageSummary.NoLandmarks"),
            VoxStrings.Get("PageSummary.OneLandmark"), VoxStrings.Format("PageSummary.Landmarks", landmarks)));
        int formFields = document.FormFields.Count;
        parts.Add(Count(formFields, VoxStrings.Get("PageSummary.NoFormFields"),
            VoxStrings.Get("PageSummary.OneFormField"), VoxStrings.Format("PageSummary.FormFields", formFields)));
        int tables = Math.Max(0, document.Tables.Count - layoutTables);
        parts.Add(Count(tables, VoxStrings.Get("PageSummary.NoTables"),
            VoxStrings.Get("PageSummary.OneTable"), VoxStrings.Format("PageSummary.Tables", tables)));
        return string.Join(", ", parts);
    }

    /// <summary>The URL of the link at or around <paramref name="node"/> (Chromium reports it as the link's value).</summary>
    public static string LinkUrlText(VBufferNode? node)
    {
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n.IsLink)
                return n.Value.Trim() is { Length: > 0 } url ? url : VoxStrings.Get("PageSummary.LinkHasNoAddress");
        }
        return VoxStrings.Get("PageSummary.NotOnLink");
    }

    private static string Count(int count, string none, string one, string many) =>
        count switch
        {
            0 => none,
            1 => one,
            _ => many,
        };

    private static string? LanguageName(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        try { return CultureInfo.GetCultureInfo(tag).EnglishName; }
        catch (CultureNotFoundException) { return tag; }
    }
}
