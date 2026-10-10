using Xunit;
using static Vox.E2E.Tests.KeyInjector;

namespace Vox.E2E.Tests;

/// <summary>Browse-mode heading navigation on headings-landmarks.html.</summary>
public class HeadingNavigationTests
{
    private static readonly TimeSpan PageLoad = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan KeyResponse = TimeSpan.FromSeconds(5);

    [E2EFact]
    public async Task H_MovesThroughTheHeadings()
    {
        await using var vox = await VoxHarness.StartAsync();
        using var browser = BrowserSession.Open("headings-landmarks.html");

        // The page's own window gets focus and Vox says its title
        await vox.Speech.WaitForAsync(s => s.Text.Contains("Headings and landmarks"), PageLoad);
        // Give the buffer a moment after the focus announcement (it is built in the background)
        await Task.Delay(1000);
        vox.Speech.Clear();

        Press('H');
        await vox.Speech.WaitForAsync(s => s.Text.Contains("Headings and landmarks") && s.Text.Contains("heading level 1"), KeyResponse);

        Press('H');
        await vox.Speech.WaitForAsync(s => s.Text.Contains("Section two") && s.Text.Contains("heading level 2"), KeyResponse);

        Press('H', Shift);
        await vox.Speech.WaitForAsync(s => s.Text.Contains("Headings and landmarks") && s.Text.Contains("heading level 1"), KeyResponse);

        // Exactly what was said, as approved
        Transcript.Verify(vox.Speech, "headings-h");
    }
}
