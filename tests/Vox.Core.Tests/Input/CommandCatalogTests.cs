using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Input;

public class CommandCatalogTests
{
    [Fact]
    public void EveryCommandHasANameDescriptionAndCategory()
    {
        var missing = Enum.GetValues<NavigationCommand>().Where(c => !CommandCatalog.Has(c)).ToList();
        Assert.True(missing.Count == 0, "No catalog entry for: " + string.Join(", ", missing));

        foreach (var info in CommandCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.Name), info.Command.ToString());
            Assert.EndsWith(".", info.Description);
        }
    }

    [Fact]
    public void NamesAreUnique() =>
        Assert.Equal(CommandCatalog.All.Count, CommandCatalog.All.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Fact]
    public void Describe_ReturnsTheEntry()
    {
        var info = CommandCatalog.Describe(NavigationCommand.NextHeading);

        Assert.Equal("Next heading", info.Name);
        Assert.Equal(CommandCategory.QuickNavigation, info.Category);
        Assert.Equal("Quick navigation", CommandCatalog.CategoryName(info.Category));
    }
}
