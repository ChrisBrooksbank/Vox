namespace Vox.Core.Navigation;

/// <summary>A menu event from UIA.</summary>
public enum MenuEventKind
{
    /// <summary>A menu (or submenu, or context menu) opened.</summary>
    Opened,
    /// <summary>A menu closed.</summary>
    Closed,
    /// <summary>Menu mode started (the menu bar became active, e.g. with Alt).</summary>
    ModeStart,
    /// <summary>Menu mode ended.</summary>
    ModeEnd,
}

/// <summary>
/// Follows menus opening and closing and says so with the next focus announcement: "menu" when
/// one opens, "submenu" for one opened from it, "leaving menu" when the last one closes. Said as
/// context before the focused item (the focus announcement interrupts speech, so anything said
/// separately just before it would be cut off). Call on the pipeline thread.
/// </summary>
public sealed class MenuTracker
{
    private int _depth;
    private bool _menuMode;
    private string? _pending;

    public int Depth => _depth;

    public void Handle(MenuEventKind kind)
    {
        switch (kind)
        {
            case MenuEventKind.Opened:
                _depth++;
                _pending = _depth == 1 ? "menu" : "submenu";
                break;

            case MenuEventKind.Closed:
                if (_depth == 0)
                    break;
                _depth--;
                // A context menu has no menu mode: closing it leaves the menu
                if (_depth == 0 && !_menuMode)
                    _pending = "leaving menu";
                else if (_pending is "menu" or "submenu")
                    _pending = null; // opened and closed before anything was focused
                break;

            case MenuEventKind.ModeStart:
                _menuMode = true;
                break;

            case MenuEventKind.ModeEnd:
                bool wasInMenu = _menuMode || _depth > 0;
                _menuMode = false;
                _depth = 0;
                _pending = wasInMenu ? "leaving menu" : null;
                break;
        }
    }

    /// <summary>What to say before the next focus announcement (once), or null.</summary>
    public string? TakeContext()
    {
        var pending = _pending;
        _pending = null;
        return pending;
    }
}
