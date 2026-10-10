using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Input;

public class CommandSearchTests
{
    private static readonly GestureEditor Keys = new(
    [
        new KeyBinding(KeyModifiers.None, 72, "Browse", "NextHeading"),
        new KeyBinding(KeyModifiers.Shift, 72, "Browse", "PrevHeading"),
        new KeyBinding(KeyModifiers.Insert, 84, "Any", "SayTitle"),
    ]);

    [Fact]
    public void NamesStartingWithTheQuery_ComeFirst_WithTheirKeys()
    {
        var found = CommandSearch.Find("next heading", Keys);

        Assert.Equal("Next heading: H (browse mode)", found[0].ToString());
        Assert.All(found, m => Assert.Contains("heading", m.Command.Name + m.Command.Description, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryWordMustMatch_InNameOrDescription()
    {
        var found = CommandSearch.Find("title window", Keys);

        Assert.Contains(found, m => m.ToString() == "Say window title: Insert+T");
        Assert.DoesNotContain(found, m => m.Command.Command == Vox.Core.Input.NavigationCommand.NextHeading);
    }

    [Fact]
    public void CommandsWithoutAKey_SaySo() =>
        Assert.Contains(CommandSearch.Find("settings", Keys), m => m.ToString() == "Settings: no key");

    [Fact]
    public void NothingTyped_FindsNothing() => Assert.Empty(CommandSearch.Find("  ", Keys));
}
