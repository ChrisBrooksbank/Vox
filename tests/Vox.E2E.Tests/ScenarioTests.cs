using System.Text.Json;
using Xunit;

namespace Vox.E2E.Tests;

/// <summary>The scenario definitions themselves (ordinary unit tests: no desktop needed).</summary>
public class ScenarioTests
{
    [Fact]
    public void EveryCorpusPageHasAScenario_AndEveryScenarioAPage()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(BrowserSession.PagesDirectory, "manifest.json")));
        var pages = manifest.RootElement.GetProperty("pages").EnumerateArray()
            .ToDictionary(p => p.GetProperty("file").GetString()!, p => p.GetProperty("title").GetString()!);

        Assert.Equal(pages.Keys.Order(), CorpusScenarios.All.Select(s => s.Page).Distinct().Order());
        foreach (var scenario in CorpusScenarios.All)
            Assert.Equal(pages[scenario.Page], scenario.Title);
        Assert.Equal(CorpusScenarios.All.Length, CorpusScenarios.All.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void EveryScenarioKeyCanBePressed()
    {
        foreach (var key in CorpusScenarios.All.SelectMany(s => s.Keys))
            KeyNotation.Parse(key); // throws on an unknown key
    }

    [Theory]
    [InlineData("H", 0x48, new ushort[0])]
    [InlineData("Shift+H", 0x48, new ushort[] { 0x10 })]
    [InlineData("Insert+Shift+D", 0x44, new ushort[] { 0x2D, 0x10 })]
    [InlineData("Ctrl+Alt+Right", 0x27, new ushort[] { 0x11, 0x12 })]
    [InlineData("F7", 0x76, new ushort[0])]
    [InlineData("Insert+F12", 0x7B, new ushort[] { 0x2D })]
    public void KeyNotation_Parses(string notation, int key, ushort[] modifiers)
    {
        var parsed = KeyNotation.Parse(notation);

        Assert.Equal(key, parsed.Key);
        Assert.Equal(modifiers, parsed.Modifiers);
    }

    [Fact]
    public void KeyNotation_RejectsUnknownKeys() =>
        Assert.Throws<FormatException>(() => KeyNotation.Parse("Ctrl+Hyper"));
}
