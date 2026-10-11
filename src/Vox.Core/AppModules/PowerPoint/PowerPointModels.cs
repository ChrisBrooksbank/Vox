namespace Vox.Core.AppModules.PowerPoint;

/// <summary>A shape on a slide: its name, kind (MsoShapeType, and for placeholders PpPlaceholderType), text and alternative text.</summary>
public sealed record PowerPointShape(string Name, int Type, string? Text = null, string? AltText = null, int PlaceholderType = 0);

/// <summary>
/// The slide in view (or showing, in a slide show): its number, the presentation's slide count,
/// title, shapes (in order) and speaker notes.
/// </summary>
public sealed record PowerPointSlide(
    int Number,
    int Count,
    string? Title,
    IReadOnlyList<PowerPointShape> Shapes,
    string? Notes = null,
    bool InSlideShow = false);

/// <summary>Composes what is said for PowerPoint slides and shapes.</summary>
public static class PowerPointFormatter
{
    /// <summary>"Slide 3 of 10, Quarterly results".</summary>
    public static string SlideChanged(PowerPointSlide slide)
    {
        var heading = $"Slide {slide.Number} of {slide.Count}";
        return string.IsNullOrWhiteSpace(slide.Title) ? heading : $"{heading}, {Clean(slide.Title)}";
    }

    /// <summary>In a slide show: the slide, then the text of every shape but the title.</summary>
    public static string SlideShowSlide(PowerPointSlide slide)
    {
        var parts = new List<string> { SlideChanged(slide) };
        foreach (var shape in slide.Shapes)
        {
            if (IsTitle(shape))
                continue;
            var text = !string.IsNullOrWhiteSpace(shape.Text) ? Clean(shape.Text)
                : !string.IsNullOrWhiteSpace(shape.AltText) ? $"{TypeName(shape)}, {Clean(shape.AltText)}"
                : null;
            if (text is not null)
                parts.Add(text);
        }
        return string.Join(". ", parts);
    }

    /// <summary>A shape moved to: "Title 1, title, Quarterly results" / "Picture 3, picture, A bar chart of sales".</summary>
    public static string Shape(PowerPointShape shape, int index, int count)
    {
        var parts = new List<string> { Clean(shape.Name), TypeName(shape) };
        if (!string.IsNullOrWhiteSpace(shape.Text))
            parts.Add(Clean(shape.Text));
        else if (!string.IsNullOrWhiteSpace(shape.AltText))
            parts.Add(Clean(shape.AltText));
        parts.Add($"{index} of {count}");
        return string.Join(", ", parts.Where(p => p.Length > 0));
    }

    /// <summary>The speaker notes command.</summary>
    public static string Notes(PowerPointSlide slide) =>
        string.IsNullOrWhiteSpace(slide.Notes) ? $"Slide {slide.Number} has no notes" : $"Notes: {Clean(slide.Notes)}";

    /// <summary>Whether a shape is the slide's title placeholder (ppPlaceholderTitle, ppPlaceholderCenterTitle).</summary>
    public static bool IsTitle(PowerPointShape shape) => shape.Type == 14 && shape.PlaceholderType is 1 or 3;

    /// <summary>A name for a shape's MsoShapeType (and placeholder kind).</summary>
    public static string TypeName(PowerPointShape shape) => shape.Type switch
    {
        14 => shape.PlaceholderType switch
        {
            1 or 3 => "title",
            4 => "subtitle",
            2 or 7 => "text placeholder",
            18 => "picture placeholder",
            8 => "chart placeholder",
            12 => "table placeholder",
            _ => "placeholder",
        },
        1 => "shape",
        3 => "chart",
        6 => "group",
        7 => "equation",
        13 => "picture",
        16 => "media",
        17 => "text box",
        19 => "table",
        24 => "SmartArt",
        28 => "graphic",
        _ => "object",
    };

    // PowerPoint ends paragraphs with \r and lines with \v
    private static string Clean(string text) => string.Join(" ",
        text.Split(['\r', '\n', '\v'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
