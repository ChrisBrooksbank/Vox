using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class MenuTrackerTests
{
    [Fact]
    public void MenuBar_OpenFileMenu_SubmenuAndLeave()
    {
        var menus = new MenuTracker();

        menus.Handle(MenuEventKind.ModeStart);
        menus.Handle(MenuEventKind.Opened);
        Assert.Equal("menu", menus.TakeContext());
        Assert.Null(menus.TakeContext()); // said once

        menus.Handle(MenuEventKind.Opened);
        Assert.Equal("submenu", menus.TakeContext());

        menus.Handle(MenuEventKind.Closed);
        menus.Handle(MenuEventKind.Closed);
        Assert.Null(menus.TakeContext()); // still in the menu bar

        menus.Handle(MenuEventKind.ModeEnd);
        Assert.Equal("leaving menu", menus.TakeContext());
        Assert.Equal(0, menus.Depth);
    }

    [Fact]
    public void ContextMenu_OpenAndClose()
    {
        var menus = new MenuTracker();

        menus.Handle(MenuEventKind.Opened);
        Assert.Equal("menu", menus.TakeContext());
        menus.Handle(MenuEventKind.Closed);
        Assert.Equal("leaving menu", menus.TakeContext());
    }

    [Fact]
    public void MenuModeWithoutAMenuOpened_EndsQuietlyOnlyIfNothingHappened()
    {
        var menus = new MenuTracker();

        menus.Handle(MenuEventKind.ModeEnd);

        Assert.Null(menus.TakeContext());
    }

    [Fact]
    public void ExtraCloseEvents_AreIgnored()
    {
        var menus = new MenuTracker();

        menus.Handle(MenuEventKind.Closed);

        Assert.Equal(0, menus.Depth);
        Assert.Null(menus.TakeContext());
    }
}
