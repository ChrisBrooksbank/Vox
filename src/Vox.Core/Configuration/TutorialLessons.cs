using Vox.Core.Input;
using Vox.Core.Localization;

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
    /// <summary>The lessons, in the language in use (the Lesson.&lt;lesson&gt;.* strings).</summary>
    public static IReadOnlyList<Lesson> All =>
    [
        Make("WebPages",
            NavigationCommand.NextHeading, NavigationCommand.NextLink, NavigationCommand.NextLandmark,
            NavigationCommand.ElementsList, NavigationCommand.ToggleMode, NavigationCommand.SayAll, NavigationCommand.PageSummary),
        Make("EditingText",
            NavigationCommand.ReadCurrentLine, NavigationCommand.ReadCurrentWord, NavigationCommand.ReadCurrentChar,
            NavigationCommand.ReadSelection, NavigationCommand.SayFocus, NavigationCommand.SayTitle),
        Make("ReviewingScreen",
            NavigationCommand.ReviewCurrentLine, NavigationCommand.ReviewNextLine, NavigationCommand.ReviewNextWord,
            NavigationCommand.NextReviewMode, NavigationCommand.ReportNavigator, NavigationCommand.NavigatorNext),
    ];

    private static Lesson Make(string id, params NavigationCommand[] commands) => new(
        VoxStrings.Get($"Lesson.{id}.Topic"),
        VoxStrings.Get($"Lesson.{id}.Introduction"),
        commands.Select(c => new LessonStep(c, VoxStrings.Get($"Lesson.{id}.{c}.Instruction"), VoxStrings.Get($"Lesson.{id}.{c}.Done"))).ToList());

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
