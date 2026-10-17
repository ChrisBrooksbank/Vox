namespace Vox.Sdk;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct VoxRect(int Left, int Top, int Width, int Height);

/// <summary>
/// A read-only view of an object on the screen (a control, window or pane), as it was when it was
/// asked for. Name, role, value and bounds are a snapshot; moving to a relative asks the
/// application again, off the calling thread, so await the result (it may be null at the edge of
/// the tree, or when the application doesn't answer in time). Since API 1.1.
/// </summary>
public interface IVoxObject
{
    /// <summary>The object's name (empty when it has none).</summary>
    string Name { get; }

    /// <summary>What the object is, e.g. "Button", "Edit", "Window".</summary>
    string Role { get; }

    /// <summary>The object's value, or null when it has none. Always null for a password field.</summary>
    string? Value { get; }

    /// <summary>Where the object is on the screen, or null when it has no location.</summary>
    VoxRect? Bounds { get; }

    Task<IVoxObject?> GetParentAsync();
    Task<IVoxObject?> GetFirstChildAsync();
    Task<IVoxObject?> GetNextAsync();
    Task<IVoxObject?> GetPreviousAsync();

    /// <summary>The object's children in order, at most <paramref name="maxCount"/> of them (1 to 1000).</summary>
    Task<IReadOnlyList<IVoxObject>> GetChildrenAsync(int maxCount = 100);
}
