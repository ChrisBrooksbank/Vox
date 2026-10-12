using System.Net;
using System.Text.RegularExpressions;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Input;

/// <summary>The user guide's "Vox for JAWS users" and "Vox for NVDA users" pages.</summary>
public partial class SwitchingGuideTests
{
    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "config");

    private static string GuidePage(string name) =>
        File.ReadAllText(Path.Combine(ConfigDirectory, "..", "..", "docs", "guide", name));

    [GeneratedRegex("<tr data-command=\"(\\w+)\">(.*?)</tr>")]
    private static partial Regex Row();

    [GeneratedRegex("<td data-layout=\"(\\w+)\">(.*?)</td>")]
    private static partial Regex LayoutCell();

    [GeneratedRegex("<kbd>(.*?)</kbd>")]
    private static partial Regex Kbd();

    [Theory]
    [InlineData("jaws.html")]
    [InlineData("nvda.html")]
    public void EveryVoxKeyOnThePage_RunsItsCommand_InThatLayout(string page)
    {
        var html = GuidePage(page);
        var rows = Row().Matches(html);
        Assert.NotEmpty(rows);

        foreach (Match row in rows)
        {
            var command = Enum.Parse<NavigationCommand>(row.Groups[1].Value);
            var cells = LayoutCell().Matches(row.Groups[2].Value);
            Assert.NotEmpty(cells);
            foreach (Match cell in cells)
            {
                var layout = Enum.Parse<KeyboardLayout>(cell.Groups[1].Value);
                // Every key of the command in that layout, by name, without the mode it works in
                var keys = new GestureEditor(KeyMap.LayoutBindings(ConfigDirectory, layout)).GesturesFor(command)
                    .Select(g => new Gesture(g.Modifiers, g.VkCode, "Any", g.IsKeypad).Describe()).ToHashSet();
                foreach (Match kbd in Kbd().Matches(cell.Groups[2].Value))
                {
                    var key = WebUtility.HtmlDecode(kbd.Groups[1].Value);
                    Assert.True(keys.Contains(key), $"{page}: {key} is not a {layout} key of {command} (its keys: {string.Join(", ", keys)})");
                }
            }
        }
    }

    [Fact]
    public void TheGuideLinksToBothPages_AndTheyLinkBack()
    {
        var guide = GuidePage("index.html");

        foreach (var page in new[] { "jaws.html", "nvda.html" })
        {
            Assert.Contains($"href=\"{page}\"", guide);
            var html = GuidePage(page);
            Assert.Contains("href=\"index.html\"", html);
            Assert.Contains("<link rel=\"stylesheet\" href=\"guide.css\">", html);
        }
        Assert.Contains($"href=\"{KeyboardReference.JawsFileName}\"", GuidePage("jaws.html"));
        Assert.Contains("href=\"jaws.html\"", KeyboardReference.JawsHtml(ConfigDirectory));
    }
}
