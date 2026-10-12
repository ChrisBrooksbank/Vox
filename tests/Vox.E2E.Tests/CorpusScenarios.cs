namespace Vox.E2E.Tests;

/// <summary>A scenario on a corpus page: the keys pressed once it has loaded (its title said).</summary>
public sealed record Scenario(string Name, string Page, string Title, string[] Keys);

/// <summary>
/// The scenarios run on every corpus page (tests/pages), in each browser. What Vox says is
/// checked against the approved transcript <c>&lt;name&gt;.&lt;browser&gt;</c>.
/// </summary>
public static class CorpusScenarios
{
    public static readonly Scenario[] All =
    [
        new("headings-landmarks", "headings-landmarks.html", "Headings and landmarks",
            ["H", "H", "H", "Shift+H", "D", "D", "K", "L", "I", "I", "Q", "S", "Insert+Shift+I"]),
        new("accordion", "apg-accordion.html", "Accordion", ["B", "Enter", "B", "Enter", "Down"]),
        new("tabs", "apg-tabs.html", "Tabs", ["Tab", "Right", "Right", "Escape", "Down"]),
        new("disclosure", "apg-disclosure.html", "Disclosure", ["B", "Enter", "Down", "Down"]),
        new("menu-button", "apg-menu-button.html", "Menu button", ["B", "Enter", "Down", "Down", "Escape"]),
        new("combobox", "apg-combobox.html", "Combo box", ["E", "Enter", "B", "Down", "Down", "Enter", "Escape", "C"]),
        new("listbox", "apg-listbox.html", "List box", ["Tab", "Down", "Down", "Up"]),
        new("checkbox-radio", "apg-checkbox-radio.html", "Check boxes and radio buttons", ["X", "X", "Enter", "X", "R", "R", "B", "Enter"]),
        new("switch-slider", "apg-switch-slider.html", "Switch and slider", ["Tab", "Space", "Tab", "Right", "Tab", "Right", "Right"]),
        new("tables", "tables.html", "Tables", ["T", "Ctrl+Alt+Right", "Ctrl+Alt+Down", "Ctrl+Alt+Down", "Ctrl+Alt+Down", "T", "T", "T"]),
        new("form-errors", "form-errors.html", "Form with errors", ["F", "F", "F", "F", "F", "F"]),
        new("live-regions", "live-regions.html", "Live regions", ["B", "Enter", "B", "Enter"]),
        new("dialogs", "dialogs.html", "Dialogs", ["B", "Enter", "Down", "Down", "Escape", "B", "B", "Enter"]),
        new("cookie-banner", "cookie-banner.html", "Cookie banner", ["Insert+Shift+D"]),
        new("annotations", "annotations.html", "Annotations, figures and abbreviations", ["Down", "Down", "Down", "Down", "Down", "Down"]),
        new("math", "math.html", "Math", ["Down", "Down", "Down", "Insert+Alt+M"]),
        new("long-page", "long-page.html", "Long page", ["Insert+Shift+I", "H", "H", "Ctrl+End", "Shift+H"]),
    ];

    public static Scenario Find(string name) => All.Single(s => s.Name == name);

    /// <summary>How long a corpus page may take to load (its title said).</summary>
    public static readonly TimeSpan PageLoad = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Opens the scenario's page in <paramref name="browser"/>, waits for its title, and presses its
    /// keys, each once Vox has gone quiet after the last. What was said from the first key on stays
    /// in <c>context.Speech</c>, for the transcript. Returns the browser, still open (closing it
    /// moves the focus, which Vox would talk about), for the caller to close after checking.
    /// </summary>
    public static async Task<BrowserSession> RunAsync(ScenarioContext context, Scenario scenario, Browser browser)
    {
        var session = BrowserSession.Open(scenario.Page, browser);
        try
        {
            await context.ExpectAsync(t => t.Contains(scenario.Title), $"the title \"{scenario.Title}\"", PageLoad);
            context.Target = await session.WindowAsync(TimeSpan.FromSeconds(5));
            await context.QuietAsync();
            context.Mark();

            foreach (var key in scenario.Keys)
                await context.PressAsync(key);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }
}
