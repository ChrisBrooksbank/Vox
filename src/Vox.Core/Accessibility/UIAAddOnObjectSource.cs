using Vox.Core.AddOns;
using Vox.Core.Navigation;

namespace Vox.Core.Accessibility;

/// <summary>Add-on object access over the UIA thread, its timeouts and the navigator.</summary>
public sealed class UIAAddOnObjectSource(UIAThread uiaThread, INavigatorObjectSource focus, ObjectNavigationCommands navigation)
    : IAddOnObjectSource
{
    public Task<T> RunAsync<T>(Func<T> work) => uiaThread.RunAsync(work);

    public INavigatorObject? GetFocused() => focus.GetFocused();

    public INavigatorObject? GetNavigator() => navigation.CurrentObject();
}
