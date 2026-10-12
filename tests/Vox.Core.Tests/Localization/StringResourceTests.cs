using System.Text.RegularExpressions;
using System.Xml.Linq;
using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Localization;

/// <summary>Every string the code asks for by name is in Strings.resx (a missing one would be said as its name).</summary>
public partial class StringResourceTests
{
    private static string RepoPath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. parts]);

    private static HashSet<string> ResourceNames() =>
        XDocument.Load(RepoPath("src", "Vox.Core", "Localization", "Strings.resx"))
            .Root!.Elements("data").Select(d => (string)d.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"(?:VoxStrings\.(?:Get|Format|GetOrDefault|Has)|\bL)\(""([A-Za-z0-9_.]*[A-Za-z0-9_])""")]
    private static partial Regex LiteralLookup();

    [Fact]
    public void EveryNameUsedInTheCode_IsAResource()
    {
        var names = ResourceNames();
        var missing = Directory.EnumerateFiles(RepoPath("src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => LiteralLookup().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(n => !names.Contains(n))
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "Not in Strings.resx: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryCommand_HasANameAndDescription()
    {
        var names = ResourceNames();
        foreach (var command in Enum.GetValues<NavigationCommand>().Where(CommandCatalog.Has))
        {
            Assert.Contains($"Command.{command}.Name", names);
            Assert.Contains($"Command.{command}.Description", names);
        }
        foreach (var category in Enum.GetValues<CommandCategory>())
            Assert.Contains($"CommandCategory.{category}", names);
    }

    [Fact]
    public void EveryVoxMenuItem_HasItsString()
    {
        var names = ResourceNames();
        foreach (var item in Vox.Core.Configuration.VoxMenu.Items)
            Assert.Contains($"VoxMenu.{item.Command}", names);
    }
}
