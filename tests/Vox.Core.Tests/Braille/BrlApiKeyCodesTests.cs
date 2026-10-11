using Vox.Core.Braille.Brltty;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrlApiKeyCodesTests
{
    private const ulong Cmd = BrlApiKeyCodes.TypeCommand;

    private static ulong Block(int block, int argument) => Cmd | ((ulong)block << 16) | (uint)argument;

    [Theory]
    [InlineData(1, "lineUp")]
    [InlineData(2, "lineDown")]
    [InlineData(9, "top")]
    [InlineData(10, "bottom")]
    [InlineData(23, "panLeft")]
    [InlineData(24, "panRight")]
    [InlineData(27, "lineBegin")]
    [InlineData(28, "lineEnd")]
    [InlineData(29, "home")]
    [InlineData(40, "toggleTether")]
    [InlineData(999, "cmd999")]
    public void BasicCommands_HaveOneName(int command, string name)
    {
        var args = BrlApiKeyCodes.Decode(Cmd | (uint)command);

        Assert.NotNull(args);
        Assert.Equal(new[] { name }, args.Keys);
        Assert.Null(args.RoutingCell);
    }

    [Fact]
    public void BasicCommand_IgnoresToggleFlags()
    {
        var args = BrlApiKeyCodes.Decode(Cmd | 40 | (0x0100UL << 32));

        Assert.Equal(new[] { "toggleTether" }, args!.Keys);
    }

    [Fact]
    public void Noop_IsIgnored()
    {
        Assert.Null(BrlApiKeyCodes.Decode(Cmd));
    }

    [Fact]
    public void RoutingKey_ReportsTheCell()
    {
        var args = BrlApiKeyCodes.Decode(Block(BrlApiKeyCodes.RouteBlock, 12));

        Assert.Equal(new[] { "routing" }, args!.Keys);
        Assert.Equal(12, args.RoutingCell);
    }

    [Fact]
    public void OtherCellBlocks_ReportTheirNameAndCell()
    {
        var args = BrlApiKeyCodes.Decode(Block(0x09, 3));

        Assert.Equal(new[] { "describeChar" }, args!.Keys);
        Assert.Equal(3, args.RoutingCell);
    }

    [Fact]
    public void PassDots_ReportsDots()
    {
        var args = BrlApiKeyCodes.Decode(Block(BrlApiKeyCodes.PassDotsBlock, 0x13)); // dots 1 2 5

        Assert.Equal(new[] { "dots", "dots125" }, args!.Keys);
        Assert.Null(args.RoutingCell);
    }

    [Fact]
    public void PassDots_WithSpace()
    {
        var args = BrlApiKeyCodes.Decode(Block(BrlApiKeyCodes.PassDotsBlock, BrlApiKeyCodes.DotChord | 0x09));

        Assert.Equal(new[] { "space", "dots", "dots14" }, args!.Keys);
    }

    [Fact]
    public void PassDots_SpaceAlone()
    {
        var args = BrlApiKeyCodes.Decode(Block(BrlApiKeyCodes.PassDotsBlock, BrlApiKeyCodes.DotChord));

        Assert.Equal(new[] { "space" }, args!.Keys);
    }

    [Fact]
    public void PassDots_WithControlModifier()
    {
        var args = BrlApiKeyCodes.Decode(Block(BrlApiKeyCodes.PassDotsBlock, 0x01) | (0x04UL << 32));

        Assert.Equal(new[] { "control", "dots", "dots1" }, args!.Keys);
    }

    [Theory]
    [InlineData(0xFF08u, "key_BackSpace")]
    [InlineData(0xFF09u, "key_Tab")]
    [InlineData(0xFF0Du, "key_Return")]
    [InlineData(0xFF1Bu, "key_Escape")]
    [InlineData(0xFF51u, "key_Left")]
    [InlineData(0xFF54u, "key_Down")]
    [InlineData(0xFFFFu, "key_Delete")]
    [InlineData(0xFFBEu, "key_F1")]
    [InlineData(0xFFC9u, "key_F12")]
    [InlineData(0x61u, "char_a")]
    [InlineData(0xE9u, "char_é")]
    [InlineData(0x01002260u, "char_≠")]
    public void Keysyms_HaveKeyOrCharNames(uint keysym, string name)
    {
        var args = BrlApiKeyCodes.Decode(keysym);

        Assert.Equal(new[] { name }, args!.Keys);
    }

    [Fact]
    public void Keysym_WithShift()
    {
        var args = BrlApiKeyCodes.Decode(0xFF09 | (0x01UL << 32));

        Assert.Equal(new[] { "shift", "key_Tab" }, args!.Keys);
    }

    [Fact]
    public void UnknownKeysymsAndBlocks_AreIgnored()
    {
        Assert.Null(BrlApiKeyCodes.Decode(0xFE03)); // an X dead key
        Assert.Null(BrlApiKeyCodes.Decode(Block(0x24, 0x1C))); // a raw XT scan code
    }

    [Fact]
    public void Tables_HaveNoDuplicateCodes()
    {
        Assert.Equal(BrlApiKeyCodes.Commands.Count, BrlApiKeyCodes.Commands.Select(c => c.Code).Distinct().Count());
        Assert.Equal(BrlApiKeyCodes.Commands.Count, BrlApiKeyCodes.Commands.Select(c => c.Name).Distinct().Count());
        Assert.Equal(BrlApiKeyCodes.Blocks.Count, BrlApiKeyCodes.Blocks.Select(b => b.Block).Distinct().Count());
    }
}
