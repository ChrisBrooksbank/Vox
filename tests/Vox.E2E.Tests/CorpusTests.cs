using Xunit;

namespace Vox.E2E.Tests;

/// <summary>Every corpus scenario, in Edge and Chrome, checked against its approved transcript.</summary>
public class CorpusTests
{
    private static readonly TimeSpan PageLoad = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(8);

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
        var scenario = CorpusScenarios.Find(name);

        await using var vox = await VoxHarness.StartAsync();
        using var session = BrowserSession.Open(scenario.Page, browser);
        await vox.Speech.WaitForAsync(s => s.Text.Contains(scenario.Title), PageLoad);
        await SpeechWaits.QuietAsync(vox.Speech, Quiet, MaxWait);
        vox.Speech.Clear();

        foreach (var key in scenario.Keys)
        {
            KeyNotation.Press(key);
            await SpeechWaits.QuietAsync(vox.Speech, Quiet, MaxWait);
        }

        Transcript.Verify(vox.Speech, $"{name}.{browser.ToString().ToLowerInvariant()}");
    }
}
