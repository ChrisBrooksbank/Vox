using Vox.Core.Buffer;

namespace Vox.Benchmarks;

/// <summary>A page element for the benchmarks (no UIA): what a captured snapshot looks like to the builder.</summary>
public sealed class SyntheticElement : IVBufferElement
{
    private readonly List<IVBufferElement> _children = [];

    public int[] RuntimeId { get; init; } = [];
    public string Name { get; init; } = string.Empty;
    public string ControlType { get; init; } = "Text";
    public string AriaRole { get; init; } = string.Empty;
    public string AriaProperties { get; init; } = string.Empty;
    public bool IsFocusable { get; init; }
    public int HeadingLevel { get; init; }

    public IReadOnlyList<IVBufferElement> GetChildren() => _children;

    public SyntheticElement Add(SyntheticElement child)
    {
        _children.Add(child);
        return this;
    }
}

/// <summary>
/// A synthetic page of about the given number of elements, built of sections like a real article
/// page: a heading, paragraphs with links, a list and a small table each.
/// </summary>
public static class SyntheticPage
{
    public static SyntheticElement Build(int elementCount)
    {
        int id = 1;
        var root = new SyntheticElement { RuntimeId = [id++], ControlType = "Document", Name = "Synthetic page" };
        int count = 1;
        for (int section = 1; count < elementCount; section++)
        {
            var region = new SyntheticElement { RuntimeId = [id++], ControlType = "Group", AriaRole = section % 5 == 0 ? "region" : "" };
            region.Add(new SyntheticElement { RuntimeId = [id++], Name = $"Section {section}", AriaRole = "heading", HeadingLevel = 2 });
            count += 2;
            for (int p = 0; p < 4; p++)
            {
                region.Add(new SyntheticElement { RuntimeId = [id++], ControlType = "Group" }
                    .Add(new SyntheticElement { RuntimeId = [id++], Name = $"Paragraph {p} of section {section} says something about the topic, " })
                    .Add(new SyntheticElement { RuntimeId = [id++], Name = $"link {section}.{p}", ControlType = "Hyperlink", IsFocusable = true })
                    .Add(new SyntheticElement { RuntimeId = [id++], Name = " and then carries on for a while longer." }));
                count += 4;
            }
            var list = new SyntheticElement { RuntimeId = [id++], ControlType = "List" };
            for (int i = 0; i < 5; i++)
                list.Add(new SyntheticElement { RuntimeId = [id++], ControlType = "ListItem" }.Add(new SyntheticElement { RuntimeId = [id++], Name = $"Item {i}" }));
            region.Add(list);
            count += 11;
            var table = new SyntheticElement { RuntimeId = [id++], ControlType = "Table" };
            for (int r = 0; r < 3; r++)
            {
                var row = new SyntheticElement { RuntimeId = [id++], ControlType = "DataItem", AriaRole = "row" };
                for (int c = 0; c < 3; c++)
                    row.Add(new SyntheticElement { RuntimeId = [id++], ControlType = r == 0 ? "HeaderItem" : "DataItem", AriaRole = r == 0 ? "columnheader" : "cell" }
                        .Add(new SyntheticElement { RuntimeId = [id++], Name = $"R{r}C{c}" }));
                table.Add(row);
            }
            region.Add(table);
            count += 22;
            root.Add(region);
        }
        return root;
    }
}
