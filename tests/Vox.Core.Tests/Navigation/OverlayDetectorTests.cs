using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class OverlayDetectorTests
{
    private static int _id = 5000;

    private static MockElement Text(string name) => new() { RuntimeId = [_id++], Name = name };
    private static MockElement Button(string name) => new() { RuntimeId = [_id++], Name = name, ControlType = "Button" };

    private static MockElement Group(params MockElement[] children)
    {
        var group = new MockElement { RuntimeId = [_id++], ControlType = "Group" };
        foreach (var child in children)
            group.AddChild(child);
        return group;
    }

    private static VBufferDocument Page(params MockElement[] children)
    {
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document", Name = "Shop" };
        root.AddChild(Text("Welcome to the shop"));
        root.AddChild(Button("Search"));
        foreach (var child in children)
            root.AddChild(child);
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public void CookieBanner_PrefersRejectOverClose()
    {
        var banner = Group(Text("We use cookies to improve your experience."),
            Button("Accept all"), Button("Close"), Button("Reject all"));
        var document = Page(banner);

        var overlay = Assert.IsType<Overlay>(OverlayDetector.Find(document));

        Assert.Equal(OverlayDetector.CookieBanner, overlay.Kind);
        Assert.Same(document.FindByRuntimeId(banner.RuntimeId), overlay.Container);
        Assert.Equal("Reject all", overlay.DismissButton!.Name);
    }

    [Fact]
    public void CookieBanner_NeverPicksAButtonThatAccepts()
    {
        var document = Page(Group(Text("This site uses cookies."), Button("Accept and close"), Button("Cookie settings")));

        var overlay = Assert.IsType<Overlay>(OverlayDetector.Find(document));

        Assert.Null(overlay.DismissButton);
        Assert.Equal(OverlayDetector.CookieBanner, OverlayDetector.Announcement(overlay, "Insert+Shift+D"));
    }

    [Fact]
    public void ModalDialog_ClosesWithItsCloseButton()
    {
        var dialog = new MockElement { RuntimeId = [_id++], ControlType = "Group", AriaRole = "dialog", AriaProperties = "modal=true", Name = "Newsletter" }
            .AddChild(Text("Sign up for offers"))
            .AddChild(Button("Subscribe"))
            .AddChild(Button("×"));
        var document = Page(dialog);

        var overlay = Assert.IsType<Overlay>(OverlayDetector.Find(document));

        Assert.Equal(OverlayDetector.Dialog, overlay.Kind);
        Assert.Equal("×", overlay.DismissButton!.Name);
        Assert.Equal("Dialog, Newsletter. Insert+Shift+D presses ×", OverlayDetector.Announcement(overlay, "Insert+Shift+D"));
    }

    [Fact]
    public void OrdinaryPage_HasNoOverlay()
    {
        Assert.Null(OverlayDetector.Find(Page(Group(Text("Our products"), Button("Close menu")))));
    }
}
