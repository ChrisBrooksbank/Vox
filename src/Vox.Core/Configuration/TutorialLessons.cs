using Vox.Core.Input;

namespace Vox.Core.Configuration;

/// <summary>A step of a practice lesson: the command to try, how to ask for it, and what it does.</summary>
public sealed record LessonStep(NavigationCommand Command, string Instruction, string Done);

/// <summary>A practice lesson on one topic.</summary>
public sealed record Lesson(string Topic, string Introduction, IReadOnlyList<LessonStep> Steps);

/// <summary>
/// The practice lessons of the tutorial (rerunnable from the Vox menu): the user presses each
/// command's key and hears what it does. Keys are not written here: they come from the keymap in
/// use (layout and the user's own keys), so the lessons always teach the right ones.
/// </summary>
public static class TutorialLessons
{
    public static readonly IReadOnlyList<Lesson> All =
    [
        new("web pages",
            "On a web page, Vox reads in browse mode: letters move by element, and the arrow keys read line by line.",
            [
                new(NavigationCommand.NextHeading, "To move to the next heading", "That moves to the next heading. With Shift, it goes back."),
                new(NavigationCommand.NextLink, "To move to the next link", "That moves to the next link."),
                new(NavigationCommand.NextLandmark, "To move to the next landmark, such as the navigation or the main content", "That moves to the next landmark."),
                new(NavigationCommand.ElementsList, "To list the page's headings, links and landmarks", "That opens the elements list, to pick one by name."),
                new(NavigationCommand.ToggleMode, "To switch between browse mode and focus mode, where keys go to the page", "That switches mode. Vox also switches by itself in text boxes."),
                new(NavigationCommand.SayAll, "To read from where you are to the end", "That reads on until you press a key."),
                new(NavigationCommand.PageSummary, "To hear a summary of the page", "That says the page's title and how many headings, links and form fields it has."),
            ]),
        new("editing text",
            "In a text box or document, the arrow keys move the caret and Vox reads what it passes. These keys read without moving.",
            [
                new(NavigationCommand.ReadCurrentLine, "To read the current line", "That reads the line. Press it twice quickly to spell it."),
                new(NavigationCommand.ReadCurrentWord, "To read the current word", "That reads the word."),
                new(NavigationCommand.ReadCurrentChar, "To read the current character", "That reads the character."),
                new(NavigationCommand.ReadSelection, "To read the selected text", "That reads what is selected."),
                new(NavigationCommand.SayFocus, "To hear what has focus", "That says the focused control."),
                new(NavigationCommand.SayTitle, "To hear the window's title", "That says the title of the window in front."),
            ]),
        new("reviewing the screen",
            "The review cursor reads anywhere on the screen without moving the focus, and object navigation moves between controls.",
            [
                new(NavigationCommand.ReviewCurrentLine, "To read the line at the review cursor", "That reads the review cursor's line."),
                new(NavigationCommand.ReviewNextLine, "To move the review cursor to the next line", "That moves it down a line."),
                new(NavigationCommand.ReviewNextWord, "To move the review cursor to the next word", "That moves it to the next word."),
                new(NavigationCommand.NextReviewMode, "To change what is reviewed: the object, the document, or the whole screen", "That changes the review mode."),
                new(NavigationCommand.ReportNavigator, "To hear the navigator object", "That says the object the navigator is on."),
                new(NavigationCommand.NavigatorNext, "To move the navigator to the next object", "That moves to the next object."),
            ]),
    ];

    /// <summary>The key a lesson asks for: the command's first key, or null when it has none.</summary>
    public static Gesture? KeyFor(NavigationCommand command, GestureEditor keys) => keys.GesturesFor(command).FirstOrDefault();

    /// <summary>Whether a key pressed is <paramref name="gesture"/> (keypad keys as their keypad codes too).</summary>
    public static bool Matches(KeyEvent key, Gesture gesture)
    {
        if (key.Modifiers != gesture.Modifiers)
            return false;
        var (primary, fallback) = NumpadKeys.BindingCodes(key.VkCode, key.IsKeypad);
        return primary == gesture.VkCode || fallback == gesture.VkCode;
    }
}
