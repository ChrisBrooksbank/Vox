using Xunit;
using Xunit.Abstractions;

namespace Vox.E2E.Tests;

/// <summary>The desktop and fault scenarios, each with a Vox of its own.</summary>
public class ScenarioRunTests(ITestOutputHelper output)
{
    public static TheoryData<string> Desktop() => [.. DesktopScenarios.All.Select(s => s.Name)];

    public static TheoryData<string> Faults() => [.. FaultScenarios.All.Select(s => s.Name)];

    [E2ETheory]
    [MemberData(nameof(Desktop))]
    public Task DesktopScenario(string name) => RunAsync(name);

    [E2ETheory]
    [MemberData(nameof(Faults))]
    public Task FaultScenario(string name) => RunAsync(name);

    private async Task RunAsync(string name)
    {
        var scenario = E2EScenarios.Find(name) ?? throw new ArgumentException($"No scenario {name}");
        await using var vox = await VoxHarness.StartAsync();
        var context = new ScenarioContext(vox, log: output.WriteLine);
        try
        {
            await scenario.RunAsync(context);
        }
        catch (ScenarioFailedException ex)
        {
            Assert.Fail($"{scenario.Name}: {ex.Message} Said since the last step: {context.Said}");
        }
    }
}

/// <summary>The scenario list itself (an ordinary unit test).</summary>
public class ScenarioListTests
{
    [Fact]
    public void Names_AreUniqueAndFindable()
    {
        var names = E2EScenarios.All.Select(s => s.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => Assert.NotNull(E2EScenarios.Find(n.ToUpperInvariant())));
        Assert.Contains("notepad", names);
        Assert.Contains("hung-provider", names);
    }
}
