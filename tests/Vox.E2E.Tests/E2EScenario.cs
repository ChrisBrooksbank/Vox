namespace Vox.E2E.Tests;

/// <summary>
/// A scripted end-to-end run against a desktop app or a fault: what it does to the Vox it is
/// given and what it expects Vox to say (it throws <see cref="ScenarioFailedException"/> when Vox
/// doesn't). Run by the tests and, one at a time, by the interactive runner.
/// </summary>
public sealed record E2EScenario(string Name, string Description, Func<ScenarioContext, Task> RunAsync);

/// <summary>Every desktop and fault scenario, by name.</summary>
public static class E2EScenarios
{
    public static IReadOnlyList<E2EScenario> All { get; } = [.. DesktopScenarios.All, .. FaultScenarios.All];

    public static E2EScenario? Find(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
