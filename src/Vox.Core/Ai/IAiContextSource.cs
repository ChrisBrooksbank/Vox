namespace Vox.Core.Ai;

/// <summary>
/// What the AI commands need to know about the screen besides a picture of it: whether the
/// navigator object or the focus is a password field, which application is in front, and the
/// text of the window's controls. Safe to call from any thread; UIA work runs on the UIA thread.
/// </summary>
public interface IAiContextSource
{
    /// <summary>The navigator object, for the rules that never send (is it a password field, whose is it).</summary>
    Task<AiTarget> NavigatorTargetAsync();

    /// <summary>The window in front, for the rules that never send (is the focus in a password field, whose is it).</summary>
    Task<AiTarget> WindowTargetAsync();

    /// <summary>The window in front: its title, then its controls and text, one per line; null when there is none.</summary>
    Task<string?> WindowTextAsync();
}
