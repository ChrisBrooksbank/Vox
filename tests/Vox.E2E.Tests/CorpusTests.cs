using Xunit;

namespace Vox.E2E.Tests;

/// <summary>Every corpus scenario, in Edge and Chrome, checked against its approved transcript.</summary>
public class CorpusTests
{
    public static TheoryData<string, Browser> Cases()
    {
        var data = new TheoryData<string, Browser>();
        foreach (var scenario in CorpusScenarios.All)
        {
            foreach (var browser in Enum.GetValues<Browser>())
                data.Add(scenario.Name, browser);
        }
        return data;
    }

    [E2ETheory]
    [MemberData(nameof(Cases))]
    public async Task Scenario(string name, Browser browser)
    {
        // A browser that isn't installed has nothing to test
        if (!BrowserSession.IsInstalled(browser))
            return;

        await using var vox = await VoxHarness.StartAsync();
        using var session = await CorpusScenarios.RunAsync(new ScenarioContext(vox), CorpusScenarios.Find(name), browser);

        Transcript.Verify(vox.Speech, $"{name}.{browser.ToString().ToLowerInvariant()}");
    }
}
