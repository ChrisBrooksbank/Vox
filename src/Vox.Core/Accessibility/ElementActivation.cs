using Vox.Core.Navigation;

namespace Vox.Core.Accessibility;

/// <summary>How <see cref="ElementActivation.Activate"/> activated an element.</summary>
public enum ActivationOutcome
{
    /// <summary>Nothing worked.</summary>
    None,
    /// <summary>Through a UIA pattern (Invoke, Toggle, SelectionItem, ExpandCollapse, a default action).</summary>
    Pattern,
    /// <summary>A mouse click at its clickable point (for click handlers with no accessible action).</summary>
    Clicked,
    /// <summary>It was given focus.</summary>
    Focused,
}

/// <summary>
/// Activates an element the way a user would: its pattern when it has one, else a left click at
/// its clickable point (scrolled into view first, the pointer put back afterwards), else focus.
/// UIA thread (the object makes UIA calls).
/// </summary>
public static class ElementActivation
{
    public static ActivationOutcome Activate(INavigatorObject target, IMouseInput? mouse)
    {
        if (target.Activate())
            return ActivationOutcome.Pattern;

        if (mouse is not null)
        {
            target.ScrollIntoView();
            if (target.GetClickPoint() is { } point)
            {
                var previous = mouse.Position;
                mouse.MoveTo(point.X, point.Y);
                mouse.Press(MouseButton.Left);
                mouse.Release(MouseButton.Left);
                if (previous is { } back)
                    mouse.MoveTo(back.X, back.Y);
                return ActivationOutcome.Clicked;
            }
        }

        return target.SetFocus() ? ActivationOutcome.Focused : ActivationOutcome.None;
    }
}
