using System.Text.RegularExpressions;

namespace Vox.E2E.Tests;

/// <summary>
/// Vox in Windows' own apps: Notepad, File Explorer and Settings. They check what Vox says, not
/// every word of it (the apps change with Windows updates, so no approved transcripts), and assume
/// an English (United States) Windows 11.
/// </summary>
public static partial class DesktopScenarios
{
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;
    private static readonly TimeSpan AppStart = TimeSpan.FromSeconds(20);

    public static readonly E2EScenario[] All =
    [
        new("notepad", "Notepad: the focused editor, reading lines as the caret moves, the current line, typing echo and deleting", NotepadAsync),
        new("explorer", "File Explorer: details-view rows with their column headers, moving through the list, first-letter navigation, into a folder and back", ExplorerAsync),
        new("settings", "Settings: moving through the categories, then a toggle switch on Accessibility > Visual effects turned and turned back", SettingsAsync),
    ];

    private static async Task NotepadAsync(ScenarioContext c)
    {
        await using var notepad = await NotepadSession.OpenAsync("First line\r\nSecond line\r\nThird line\r\n");
        c.Target = notepad.Window;
        await c.ExpectAsync(t => t.Contains(NotepadSession.FileTitle, IgnoreCase), "the window's title", AppStart);
        await c.QuietAsync();

        c.Mark();
        await c.PressAsync("Insert+Tab", "what has the focus");
        await c.ExpectAsync(t => t.Contains("edit", IgnoreCase) || t.Contains("document", IgnoreCase), "the text editor");

        // The line the caret moves to is read
        await c.PressAsync("Ctrl+Home");
        c.Mark();
        await c.PressAsync("Down");
        await c.ExpectSaidAsync("Second line");
        c.Mark();
        await c.PressAsync("Down");
        await c.ExpectSaidAsync("Third line");
        c.Mark();
        await c.PressAsync("Up");
        await c.ExpectSaidAsync("Second line");
        c.Mark();
        await c.PressAsync("Insert+Up", "read the current line");
        await c.ExpectSaidAsync("Second line");

        // Typing echo: each character, then the word when the space ends it
        await c.PressAsync("End");
        c.Mark();
        await c.TypeAsync(" here ");
        await c.ExpectAsync(t => t.Equals("h", IgnoreCase), "the typed character \"h\"");
        await c.ExpectAsync(t => t.Equals("here", IgnoreCase), "the typed word \"here\"");
        c.Mark();
        await c.PressAsync("Insert+Up");
        await c.ExpectSaidAsync("Second line here");

        // Deleting it again
        for (int i = 0; i < " here ".Length; i++)
            await c.PressAsync("Backspace");
        c.Mark();
        await c.PressAsync("Insert+Up");
        await c.ExpectAsync(t => t.Contains("Second line", IgnoreCase) && !t.Contains("here", IgnoreCase), "the line without the word");

        // Saved, so closing doesn't ask
        await c.PressAsync("Ctrl+S", "save");
    }

    private static async Task ExplorerAsync(ScenarioContext c)
    {
        const string folder = ExplorerSession.FolderName, subfolder = ExplorerSession.SubfolderName;
        await using var explorer = await ExplorerSession.OpenAsync();
        c.Target = explorer.Window;
        await c.ExpectAsync(t => t.Contains(folder, IgnoreCase), "the folder's name", AppStart);
        await c.QuietAsync();

        // The details view, whatever view the folder opened in
        await c.PressAsync("Ctrl+Shift+6", "details view");

        // Onto the first item (folders first): the focus may start outside the list
        bool onFirst = false;
        for (int i = 0; i < 8 && !onFirst; i++)
        {
            c.Mark();
            await c.PressAsync("Home");
            onFirst = c.Speech.SpokenText.Any(t => t.Contains(subfolder, IgnoreCase));
            if (!onFirst)
                await c.PressAsync("Tab", "towards the file list");
        }
        if (!onFirst)
            throw new ScenarioFailedException($"Never reached \"{subfolder}\" in the file list. Said: {c.Said}");

        // A details-view row: the name alone, the other columns with their headers (ExplorerModule)
        await c.ExpectAsync(t => t.Contains(subfolder, IgnoreCase) && t.Contains("Date modified", IgnoreCase)
            && t.Contains("File folder", IgnoreCase), "the folder's details row");

        c.Mark();
        await c.PressAsync("Down");
        await c.ExpectAsync(t => t.Contains("Alpha notes", IgnoreCase) && t.Contains("Type", IgnoreCase)
            && t.Contains("Text Document", IgnoreCase), "Alpha notes' details row");
        c.Mark();
        await c.PressAsync("Down");
        await c.ExpectSaidAsync("Beta notes");
        c.Mark();
        await c.PressAsync("End");
        await c.ExpectSaidAsync("Gamma notes");
        c.Mark();
        await c.PressAsync("Home");
        await c.ExpectSaidAsync(subfolder);

        // First-letter navigation is the list's own (not a web page: Vox passes the key)
        c.Mark();
        await c.PressAsync("B", "first letter");
        await c.ExpectSaidAsync("Beta notes");

        // Into the folder and back out, onto the folder again
        await c.PressAsync("Home");
        c.Mark();
        await c.PressAsync("Enter", "open the folder");
        await c.ExpectSaidAsync(subfolder);
        await c.QuietAsync();
        c.Mark();
        await c.PressAsync("Alt+Up", "up a level");
        await c.ExpectSaidAsync(folder);
        await c.ExpectSaidAsync(subfolder);
    }

    private static async Task SettingsAsync(ScenarioContext c)
    {
        await using var settings = await SettingsSession.OpenAsync("ms-settings:easeofaccess-visualeffects");
        c.Target = settings.Window;
        await c.ExpectAsync(t => t.Contains("Settings", IgnoreCase), "the Settings window", AppStart);
        await c.QuietAsync();

        // Back to the categories, on the one selected (the page is under Accessibility)
        await c.PressUntilAsync("Shift+Tab",
            t => t.Contains("Accessibility", IgnoreCase) && t.Contains("list item", IgnoreCase), 15, "the Accessibility category");
        c.Mark();
        await c.PressAsync("Up");
        await c.ExpectSaidAsync("Gaming");
        c.Mark();
        await c.PressAsync("Down");
        await c.ExpectSaidAsync("Accessibility");

        // Forward into the page, to its first toggle switch
        var toggle = await c.PressUntilAsync("Tab", t => ToggleState(t) is not null, 20, "a toggle switch");
        var first = ToggleState(toggle)!;
        int turns = 0;
        try
        {
            c.Mark();
            await c.PressAsync("Space", "turn it");
            turns++;
            await c.ExpectAsync(t => ToggleState(t) is { } state && state != first, "the toggle's new state");
            c.Mark();
            await c.PressAsync("Space", "turn it back");
            turns++;
            await c.ExpectAsync(t => ToggleState(t) == first, "the toggle's first state again");
        }
        finally
        {
            // Leave the setting as it was, if the scenario failed half-way
            if (turns % 2 == 1 && DesktopWindows.IsForeground(settings.Window))
                KeyNotation.Press("Space");
        }
    }

    /// <summary>"on" or "off" when <paramref name="text"/> describes a toggle switch's state, else null.</summary>
    internal static string? ToggleState(string text)
    {
        if (text.Contains("not checked", IgnoreCase))
            return "off";
        if (text.Contains("checked", IgnoreCase))
            return "on";
        var match = OnOff().Match(text);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"\b(on|off)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OnOff();
}
