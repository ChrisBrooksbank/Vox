using Vox.Core.Pipeline;

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
/// can't Tab to. Not thread-safe; the UIA-backed navigator keeps it on the UIA thread.
/// </summary>
public sealed class ObjectNavigator
{
    public INavigatorObject? Current { get; private set; }

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
        var target = move switch
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
}
