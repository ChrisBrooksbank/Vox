using Interop.UIAutomationClient;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

/// <summary>
/// Finds the object the navigator starts from. The UIA implementation must only be used on the
/// UIA thread.
/// </summary>
public interface INavigatorObjectSource
{
    /// <summary>The object with keyboard focus, or null when there is none.</summary>
    INavigatorObject? GetFocused();
}

/// <summary>
/// A UIA element in the control view as a navigator object. Every member makes UIA calls, so it
/// must only be used on the UIA thread that created it; relatives are fetched with the
/// provider's cache request, so <see cref="Describe"/> reads cached properties only.
/// </summary>
public sealed class UIANavigatorObject : INavigatorObject
{
    private readonly IUIAutomationElement _element;
    private readonly IUIAutomationTreeWalker _walker;
    private readonly IUIAutomationCacheRequest _cacheRequest;

    private UIANavigatorObject(IUIAutomationElement element, IUIAutomationTreeWalker walker, IUIAutomationCacheRequest cacheRequest)
    {
        _element = element;
        _walker = walker;
        _cacheRequest = cacheRequest;
    }

    /// <summary>The element itself, for actions on it (focus, invoke). UIA thread only.</summary>
    public IUIAutomationElement Element => _element;

    /// <summary>The focused element as a navigator object. UIA thread only.</summary>
    public static UIANavigatorObject? Focused(UIAProvider provider)
    {
        var automation = provider.Automation;
        var element = automation.GetFocusedElementBuildCache(provider.CacheRequest);
        return element is null ? null : new UIANavigatorObject(element, automation.ControlViewWalker, provider.CacheRequest);
    }

    public INavigatorObject? GetParent() => Wrap(_walker.GetParentElementBuildCache(_element, _cacheRequest));

    public INavigatorObject? GetFirstChild() => Wrap(_walker.GetFirstChildElementBuildCache(_element, _cacheRequest));

    public INavigatorObject? GetLastChild() => Wrap(_walker.GetLastChildElementBuildCache(_element, _cacheRequest));

    public INavigatorObject? GetNextSibling() => Wrap(_walker.GetNextSiblingElementBuildCache(_element, _cacheRequest));

    public INavigatorObject? GetPreviousSibling() => Wrap(_walker.GetPreviousSiblingElementBuildCache(_element, _cacheRequest));

    public FocusChangedEvent Describe() => UIAEventSubscriber.DescribeCached(_element);

    private UIANavigatorObject? Wrap(IUIAutomationElement? element) =>
        element is null ? null : new UIANavigatorObject(element, _walker, _cacheRequest);
}

/// <summary>Finds the focused UIA element. UIA thread only.</summary>
public sealed class UIANavigatorObjectSource(UIAProvider provider) : INavigatorObjectSource
{
    public INavigatorObject? GetFocused() => UIANavigatorObject.Focused(provider);
}
