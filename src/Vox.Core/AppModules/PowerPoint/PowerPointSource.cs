using Vox.Core.AppModules.Office;

namespace Vox.Core.AppModules.PowerPoint;

/// <summary>Reads PowerPoint's current slide and selects its shapes. Null when PowerPoint can't answer.</summary>
public interface IPowerPointSource
{
    /// <summary>The slide showing in a running slide show, else the slide in the editing window.</summary>
    Task<PowerPointSlide?> CurrentSlideAsync(IntPtr window);

    /// <summary>Selects the shape at <paramref name="index"/> (1-based) on the slide in the editing window; null when it can't.</summary>
    Task<string?> SelectShapeAsync(IntPtr window, int index);
}

/// <summary><see cref="IPowerPointSource"/> through PowerPoint's object model (late bound, on the Office thread).</summary>
public sealed class PowerPointObjectModelSource : IPowerPointSource
{
    private readonly IOfficeObjectModel _objectModel;

    public PowerPointObjectModelSource(IOfficeObjectModel objectModel)
    {
        _objectModel = objectModel;
    }

    public Task<PowerPointSlide?> CurrentSlideAsync(IntPtr window) =>
        _objectModel.RunAsync<PowerPointSlide>(OfficeApp.PowerPoint, window, app => ReadSlide((object)app));

    public Task<string?> SelectShapeAsync(IntPtr window, int index) =>
        _objectModel.RunAsync<string>(OfficeApp.PowerPoint, window, app =>
        {
            ((dynamic)app).ActiveWindow.View.Slide.Shapes(index).Select();
            return "selected";
        });

    /// <summary>The current slide of <paramref name="application"/> (a PowerPoint Application, or a fake of one).</summary>
    public static PowerPointSlide? ReadSlide(object application)
    {
        dynamic app = application;
        bool inShow = Try(() => (int)app.SlideShowWindows.Count > 0);
        dynamic slide = inShow ? app.SlideShowWindows(1).View.Slide : app.ActiveWindow.View.Slide;
        if (slide is null)
            return null;

        var shapes = new List<PowerPointShape>();
        string? title = null;
        dynamic collection = slide.Shapes;
        int count = (int)collection.Count;
        for (int i = 1; i <= count; i++)
        {
            dynamic shape = collection.Item(i);
            int type = Try(() => (int)shape.Type);
            var read = new PowerPointShape(
                Try(() => (string)shape.Name) ?? $"Shape {i}",
                type,
                Try(() => (bool)shape.HasTextFrame && (bool)shape.TextFrame.HasText ? (string)shape.TextFrame.TextRange.Text : null),
                Try(() => (string)shape.AlternativeText),
                type == 14 ? Try(() => (int)shape.PlaceholderFormat.Type) : 0);
            if (title is null && PowerPointFormatter.IsTitle(read))
                title = read.Text;
            shapes.Add(read);
        }

        // The notes page's body placeholder holds the speaker notes
        string? notes = Try(() => (string)slide.NotesPage.Shapes.Placeholders(2).TextFrame.TextRange.Text);
        return new PowerPointSlide(
            (int)slide.SlideIndex,
            Try(() => (int)app.ActivePresentation.Slides.Count),
            title, shapes, notes, inShow);
    }

    private static T? Try<T>(Func<T?> read)
    {
        try { return read(); }
        catch { return default; }
    }
}
