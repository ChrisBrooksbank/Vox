using System.Diagnostics;
using Vox.Core.Buffer;
using Xunit;
using Xunit.Abstractions;

namespace Vox.Core.Tests.Buffer;

/// <summary>
/// The buffer for a large page must be ready quickly: a 10,000-node synthetic page (sections of
/// headings, paragraphs with links, lists and tables) builds within the 500 ms budget.
/// </summary>
public class BufferBuildBudgetTests(ITestOutputHelper output)
{
    private const int BudgetMs = 500;

    /// <summary>A synthetic page of about <paramref name="nodeCount"/> elements.</summary>
    internal static MockElement SyntheticPage(int nodeCount)
    {
        int id = 1;
        var root = new MockElement { RuntimeId = [id++], ControlType = "Document", Name = "Synthetic page" };
        int count = 1;
        int section = 0;
        while (count < nodeCount)
        {
            section++;
            var region = new MockElement { RuntimeId = [id++], ControlType = "Group", AriaRole = section % 5 == 0 ? "region" : "" };
            region.AddChild(new MockElement { RuntimeId = [id++], Name = $"Section {section}", AriaRole = "heading", HeadingLevel = 2 });
            count += 2;

            for (int p = 0; p < 4; p++)
            {
                var paragraph = new MockElement { RuntimeId = [id++], ControlType = "Group" }
                    .AddChild(new MockElement { RuntimeId = [id++], Name = $"Paragraph {p} of section {section} says something about the topic, " })
                    .AddChild(new MockElement { RuntimeId = [id++], Name = $"link {section}.{p}", ControlType = "Hyperlink" })
                    .AddChild(new MockElement { RuntimeId = [id++], Name = " and then carries on for a while longer." });
                region.AddChild(paragraph);
                count += 4;
            }

            var list = new MockElement { RuntimeId = [id++], ControlType = "List" };
            for (int i = 0; i < 5; i++)
                list.AddChild(new MockElement { RuntimeId = [id++], ControlType = "ListItem" }
                    .AddChild(new MockElement { RuntimeId = [id++], Name = $"Item {i}" }));
            region.AddChild(list);
            count += 11;

            var table = new MockElement { RuntimeId = [id++], ControlType = "Table" };
            for (int r = 0; r < 3; r++)
            {
                var row = new MockElement { RuntimeId = [id++], ControlType = "DataItem", AriaRole = "row" };
                for (int c = 0; c < 3; c++)
                    row.AddChild(new MockElement { RuntimeId = [id++], ControlType = r == 0 ? "HeaderItem" : "DataItem", AriaRole = r == 0 ? "columnheader" : "cell" }
                        .AddChild(new MockElement { RuntimeId = [id++], Name = $"R{r}C{c}" }));
                table.AddChild(row);
            }
            region.AddChild(table);
            count += 1 + 3 * 7;

            root.AddChild(region);
        }
        return root;
    }

    [Fact]
    public void TenThousandNodePage_BuildsWithinBudget()
    {
        var page = SyntheticPage(10_000);
        new VBufferBuilder().Build(SyntheticPage(500)); // warm up (JIT)

        var stopwatch = Stopwatch.StartNew();
        var document = new VBufferBuilder().Build(page);
        stopwatch.Stop();

        output.WriteLine($"{document.AllNodes.Count} nodes in {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(document.AllNodes.Count >= 10_000);
        Assert.True(stopwatch.ElapsedMilliseconds < BudgetMs, $"Built in {stopwatch.ElapsedMilliseconds} ms");
    }
}
