using Vox.Core.Configuration;
using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public class VoxMenuTests
{
    [Fact]
    public void TheMenuHasWhatTheSpecAsksFor()
    {
        var commands = VoxMenu.Items.Select(i => i.Command).ToList();

        Assert.Contains(NavigationCommand.OpenSettings, commands);
        Assert.Contains(NavigationCommand.OpenInputGestures, commands);
        Assert.Contains(NavigationCommand.ToggleSpeechViewer, commands);
        Assert.Contains(NavigationCommand.OpenUserGuide, commands);
        Assert.Contains(NavigationCommand.TogglePauseSpeech, commands);
        Assert.Equal(NavigationCommand.ExitVox, commands[^1]);
    }

    [Fact]
    public void EveryItemHasAUniqueAccessKey()
    {
        var keys = VoxMenu.Items.Select(i => char.ToLowerInvariant(i.Text[i.Text.IndexOf('&') + 1])).ToList();

        Assert.All(VoxMenu.Items, i => Assert.Contains('&', i.Text));
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void TheMenuOpensWithInsertN()
    {
        var map = KeyMap.LoadBuiltIn();

        Assert.True(map.TryResolveOutsideDocument(KeyModifiers.Insert, 78, out var command, out _));
        Assert.Equal(NavigationCommand.OpenVoxMenu, command);
    }
}
