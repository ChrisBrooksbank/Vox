namespace Vox.Core.Braille;

/// <summary>
/// Where braille's routing keys and line moves act: the caret of the focused control, the review
/// cursor, the browse cursor and the focused object. Implementations hand the work to the thread
/// that owns it (UIA, pipeline) and return at once.
/// </summary>
public interface IBrailleRouter
{
    /// <summary>Moves the caret of the focused control to <paramref name="offsetInLine"/> in its current line.</summary>
    void MoveCaret(int offsetInLine);

    /// <summary>Moves the review cursor to <paramref name="offsetInLine"/> in its current line.</summary>
    void MoveReviewCursor(int offsetInLine);

    /// <summary>Moves the browse cursor to <paramref name="offset"/> in the page; with <paramref name="activate"/>, activates what is there.</summary>
    void RouteBrowse(int offset, bool activate);

    /// <summary>Activates the focused object (its default action).</summary>
    void ActivateFocus();

    /// <summary>Runs a command as if its key was pressed (line moves, chord commands).</summary>
    void RunCommand(Input.NavigationCommand command);
}
