using Vox.Core.Navigation;
using Vox.Sdk;

namespace Vox.Core.AddOns;

/// <summary>Where add-ons get objects from: runs reads on the UIA thread (with its timeouts) and finds the focused and navigator objects.</summary>
public interface IAddOnObjectSource
{
    /// <summary>Runs <paramref name="work"/> on the UIA thread.</summary>
    Task<T> RunAsync<T>(Func<T> work);

    /// <summary>The focused object. UIA thread only.</summary>
    INavigatorObject? GetFocused();

    /// <summary>The navigator object. UIA thread only.</summary>
    INavigatorObject? GetNavigator();
}

/// <summary>
/// An <see cref="INavigatorObject"/> as an add-on sees it: a snapshot taken on the UIA thread
/// (never a password's value), with relatives fetched on that thread too. A failed or timed-out
/// read gives null / nothing rather than an exception.
/// </summary>
public sealed class VoxObject : IVoxObject
{
    private const int MaxChildren = 1000;
    private readonly INavigatorObject _object;
    private readonly IAddOnObjectSource _source;

    private VoxObject(INavigatorObject target, IAddOnObjectSource source)
    {
        _object = target;
        _source = source;
        var description = target.Describe();
        Name = description.ElementName ?? string.Empty;
        Role = description.ControlType ?? string.Empty;
        Value = description.IsPassword ? null : description.Value;
        Bounds = target.GetBounds() is { IsEmpty: false } b ? new VoxRect(b.Left, b.Top, b.Width, b.Height) : null;
    }

    public string Name { get; }
    public string Role { get; }
    public string? Value { get; }
    public VoxRect? Bounds { get; }

    /// <summary>Wraps an object; call on the UIA thread.</summary>
    public static IVoxObject? Wrap(INavigatorObject? target, IAddOnObjectSource source) =>
        target is null ? null : new VoxObject(target, source);

    public Task<IVoxObject?> GetParentAsync() => Relative(o => o.GetParent());
    public Task<IVoxObject?> GetFirstChildAsync() => Relative(o => o.GetFirstChild());
    public Task<IVoxObject?> GetNextAsync() => Relative(o => o.GetNextSibling());
    public Task<IVoxObject?> GetPreviousAsync() => Relative(o => o.GetPreviousSibling());

    public async Task<IReadOnlyList<IVoxObject>> GetChildrenAsync(int maxCount = 100)
    {
        var limit = Math.Clamp(maxCount, 1, MaxChildren);
        try
        {
            return await _source.RunAsync<IReadOnlyList<IVoxObject>>(() =>
            {
                var children = new List<IVoxObject>();
                for (var child = _object.GetFirstChild(); child is not null && children.Count < limit; child = child.GetNextSibling())
                    children.Add(new VoxObject(child, _source));
                return children;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private async Task<IVoxObject?> Relative(Func<INavigatorObject, INavigatorObject?> find)
    {
        try
        {
            return await _source.RunAsync(() => Wrap(find(_object), _source)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
