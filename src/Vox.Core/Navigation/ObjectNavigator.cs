using Vox.Core.Pipeline;
using Vox.Core.Text;

namespace Vox.Core.Navigation;

/// <summary>
/// An object in the tree the navigator moves through (a UIA element in the control view, or a
/// mock in tests). Implementations backed by UIA must only be used on the UIA thread.
/// </summary>
public interface INavigatorObject
{
    INavigatorObject? GetParent();
    INavigatorObject? GetFirstChild();
    INavigatorObject? GetLastChild();
    INavigatorObject? GetNextSibling();
    INavigatorObject? GetPreviousSibling();

    /// <summary>What the object is, in the shape <see cref="AnnouncementBuilder"/> speaks.</summary>
    FocusChangedEvent Describe();

    /// <summary>Gives the object keyboard focus; false when it can't take focus.</summary>
    bool SetFocus() => false;

    /// <summary>Runs the object's default action (press, toggle, select); false when it has none.</summary>
    bool Activate() => false;

    /// <summary>The object's text for review: its own text (an edit's or document's), or its name and value.</summary>
    ITextDocument GetText() => new StringTextDocument(ObjectNavigator.TextOf(Describe()));

    /// <summary>A screen point to click the object at (its clickable point or centre), or null when it has no location.</summary>
    (int X, int Y)? GetClickPoint() => null;
}

/// <summary>A navigator movement.</summary>
public enum NavigatorMove
{
    Parent,
    FirstChild,
    Next,
    Previous,
}

/// <summary>
/// The navigator object: a position in the object tree that moves independently of keyboard
/// focus (parent, first child, next and previous sibling), so users can reach objects they
/// can't Tab to. In simple review mode, layout-only objects (unnamed groups and panes) are
/// skipped: their children are treated as children of the nearest object that isn't skipped.
/// Not thread-safe; the UIA-backed navigator keeps it on the UIA thread.
/// </summary>
public sealed class ObjectNavigator
{
    // Relatives looked at per move before giving up (a huge or cyclic tree must not hang the UIA thread)
    private const int MaxLookups = 2000;
    private int _lookups;

    public INavigatorObject? Current { get; private set; }

    /// <summary>Skip layout-only objects (unnamed Group and Pane).</summary>
    public bool SimpleReview { get; set; }

    /// <summary>Puts the navigator on <paramref name="target"/> (e.g. the focused object).</summary>
    public void MoveTo(INavigatorObject? target) => Current = target;

    /// <summary>
    /// Moves the navigator. Returns the new object, or null at a boundary (no parent, child or
    /// sibling in that direction, or no navigator object at all), leaving the navigator where it was.
    /// </summary>
    public INavigatorObject? Move(NavigatorMove move)
    {
        if (Current is not { } current)
            return null;
        _lookups = 0;
        var target = SimpleReview
            ? move switch
            {
                NavigatorMove.Parent => VisibleParent(current),
                NavigatorMove.FirstChild => FirstVisibleChild(current, forward: true),
                NavigatorMove.Next => VisibleSibling(current, forward: true),
                NavigatorMove.Previous => VisibleSibling(current, forward: false),
                _ => null,
            }
            : move switch
            {
                NavigatorMove.Parent => current.GetParent(),
                NavigatorMove.FirstChild => current.GetFirstChild(),
                NavigatorMove.Next => current.GetNextSibling(),
                NavigatorMove.Previous => current.GetPreviousSibling(),
                _ => null,
            };
        if (target is not null)
            Current = target;
        return target;
    }

    /// <summary>An object that only lays out others: an unnamed group or pane.</summary>
    public static bool IsLayoutOnly(INavigatorObject obj)
    {
        var description = obj.Describe();
        return description.ControlType is "Group" or "Pane" && string.IsNullOrWhiteSpace(description.ElementName);
    }

    /// <summary>
    /// The text an object shows when it has no text of its own: its name and value (never a
    /// password's value), on separate lines.
    /// </summary>
    public static string TextOf(FocusChangedEvent description)
    {
        var name = description.ElementName.Trim();
        var value = description.IsPassword ? string.Empty : (description.Value ?? string.Empty).Trim();
        if (value.Length == 0 || value == name)
            return name;
        return name.Length == 0 ? value : name + "\n" + value;
    }

    private bool OverBudget() => ++_lookups > MaxLookups;

    /// <summary>The nearest ancestor that isn't skipped (the root is never skipped).</summary>
    private INavigatorObject? VisibleParent(INavigatorObject obj)
    {
        var parent = obj.GetParent();
        while (parent is not null && IsLayoutOnly(parent))
        {
            if (OverBudget())
                return null;
            var next = parent.GetParent();
            if (next is null)
                return parent;
            parent = next;
        }
        return parent;
    }

    /// <summary>The first (or last) descendant that isn't skipped, looking through skipped children.</summary>
    private INavigatorObject? FirstVisibleChild(INavigatorObject obj, bool forward)
    {
        for (var child = forward ? obj.GetFirstChild() : obj.GetLastChild();
             child is not null;
             child = forward ? child.GetNextSibling() : child.GetPreviousSibling())
        {
            if (OverBudget())
                return null;
            if (!IsLayoutOnly(child))
                return child;
            if (FirstVisibleChild(child, forward) is { } inner)
                return inner;
        }
        return null;
    }

    /// <summary>
    /// The next (or previous) object that isn't skipped: a sibling, a skipped sibling's
    /// descendant, or — when the parent is skipped — the parent's sibling, and so on.
    /// </summary>
    private INavigatorObject? VisibleSibling(INavigatorObject obj, bool forward)
    {
        var node = obj;
        while (true)
        {
            for (var sibling = forward ? node.GetNextSibling() : node.GetPreviousSibling();
                 sibling is not null;
                 sibling = forward ? sibling.GetNextSibling() : sibling.GetPreviousSibling())
            {
                if (OverBudget())
                    return null;
                if (!IsLayoutOnly(sibling))
                    return sibling;
                if (FirstVisibleChild(sibling, forward) is { } inner)
                    return inner;
            }
            var parent = node.GetParent();
            if (parent is null || !IsLayoutOnly(parent) || OverBudget())
                return null;
            node = parent;
        }
    }
}
