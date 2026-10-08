using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Text;

namespace Vox.Core.Tests.TestSupport;

/// <summary>An in-memory object tree for navigator tests.</summary>
public sealed class MockNavigatorObject : INavigatorObject
{
    private readonly List<MockNavigatorObject> _children = new();

    public MockNavigatorObject(string name, string controlType = "Button", params MockNavigatorObject[] children)
    {
        Name = name;
        ControlType = controlType;
        foreach (var child in children)
        {
            child.Parent = this;
            _children.Add(child);
        }
    }

    public string Name { get; }
    public string ControlType { get; }
    public MockNavigatorObject? Parent { get; private set; }
    public IReadOnlyList<MockNavigatorObject> Children => _children;

    /// <summary>Text the object exposes (for review and copy).</summary>
    public string? Text { get; init; }

    /// <summary>How many times a relative was asked for (to check lookups stay bounded).</summary>
    public static int Lookups;

    public INavigatorObject? GetParent() { Lookups++; return Parent; }
    public INavigatorObject? GetFirstChild() { Lookups++; return _children.FirstOrDefault(); }
    public INavigatorObject? GetLastChild() { Lookups++; return _children.LastOrDefault(); }
    public INavigatorObject? GetNextSibling() => Sibling(+1);
    public INavigatorObject? GetPreviousSibling() => Sibling(-1);

    public FocusChangedEvent Describe() => new(DateTimeOffset.UtcNow, Name, ControlType);

    public ITextDocument GetText() => new StringTextDocument(Text ?? Name);

    /// <summary>Where the object is on screen (null: no location).</summary>
    public (int X, int Y)? ClickPoint { get; init; }

    public (int X, int Y)? GetClickPoint() => ClickPoint;

    /// <summary>Finds a descendant (or this object) by name.</summary>
    public MockNavigatorObject Find(string name) =>
        Name == name ? this : _children.Select(c => c.FindOrNull(name)).FirstOrDefault(c => c is not null)
            ?? throw new KeyNotFoundException(name);

    private MockNavigatorObject? FindOrNull(string name)
    {
        try { return Find(name); }
        catch (KeyNotFoundException) { return null; }
    }

    private MockNavigatorObject? Sibling(int step)
    {
        Lookups++;
        if (Parent is null)
            return null;
        int index = Parent._children.IndexOf(this) + step;
        return index >= 0 && index < Parent._children.Count ? Parent._children[index] : null;
    }

    public override string ToString() => Name;
}
