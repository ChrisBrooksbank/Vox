using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Input;

public class KeyboardReferenceTests
{
    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "config");

    [Fact]
    public void ListsEveryCommandWithItsKeysInBothLayouts()
    {
        var html = KeyboardReference.Html(ConfigDirectory);

        foreach (var info in CommandCatalog.All)
            Assert.Contains($"<th scope=\"row\">{System.Net.WebUtility.HtmlEncode(info.Name)}</th>", html);
        Assert.Contains("<tr><th scope=\"row\">Next heading</th><td><kbd>H (browse mode)</kbd></td><td><kbd>H (browse mode)</kbd></td>", html);
        Assert.Contains("<h2>Quick navigation</h2>", html);
        Assert.StartsWith("<!doctype html>", html);
    }

    [Fact]
    public void LaptopColumn_HasNoKeypadKeys()
    {
        var html = KeyboardReference.Html(ConfigDirectory);
        var laptopCells = html.Split("<tr>").Skip(1)
            .Select(row => row.Split("<td>"))
            .Where(cells => cells.Length >= 3)
            .Select(cells => cells[2]);

        Assert.DoesNotContain(laptopCells, cell => cell.Contains("Numpad"));
    }

    [Fact]
    public void TheGuideLinksToTheReference_AndTheReferenceBack()
    {
        var guide = File.ReadAllText(Path.Combine(ConfigDirectory, "..", "..", "docs", "guide", "index.html"));

        Assert.Contains("href=\"keyboard.html\"", guide);
        Assert.Contains("href=\"index.html\"", KeyboardReference.Html(ConfigDirectory));
    }
}
