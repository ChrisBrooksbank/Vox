using Moq;
using Vox.Core.Braille;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrailleDisplayTests
{
    [Fact]
    public void FakeDisplay_PadsShortLinesAndCutsLongOnes()
    {
        var display = new FakeBrailleDisplay(cellCount: 4);

        display.Write(BrailleDots.FromUnicode("⠁⠃"), cursorPosition: 1);
        display.Write(BrailleDots.FromUnicode("⠁⠃⠉⠙⠑"), cursorPosition: 4);

        Assert.Equal(2, display.Writes.Count);
        Assert.Equal(new byte[] { 0x01, 0x03, 0, 0 }, display.Writes[0].Cells);
        Assert.Equal(1, display.Writes[0].CursorPosition);
        Assert.Equal("⠁⠃⠉⠙", display.Current!.Unicode);
        Assert.Null(display.Current.CursorPosition); // the cursor cell was cut off
    }

    [Fact]
    public void FakeDisplay_IgnoresWritesAndHasNoCellsWhileDisconnected()
    {
        var display = new FakeBrailleDisplay(cellCount: 40) { IsConnected = false };

        display.Write(new byte[] { 0x01 }, null);

        Assert.Equal(0, display.CellCount);
        Assert.Empty(display.Writes);
        Assert.Null(display.Current);
    }

    [Fact]
    public void FakeDisplay_ReportsChordsAndRoutingKeys()
    {
        var display = new FakeBrailleDisplay(cellCount: 20);
        var pressed = new List<BrailleKeyEventArgs>();
        display.KeyPressed += (_, e) => pressed.Add(e);

        display.Press("space", "dot1", "dot4");
        display.PressRouting(7);

        Assert.Equal(["space", "dot1", "dot4"], pressed[0].Keys);
        Assert.Null(pressed[0].RoutingCell);
        Assert.Equal(["routing"], pressed[1].Keys);
        Assert.Equal(7, pressed[1].RoutingCell);
        Assert.Throws<ArgumentOutOfRangeException>(() => display.PressRouting(20));
    }

    [Fact]
    public void Display_CanBeMocked()
    {
        var display = new Mock<IBrailleDisplay>();

        display.Object.Write(new byte[] { 0x01 }, 0);

        display.Verify(d => d.Write(It.Is<ReadOnlyMemory<byte>>(c => c.Length == 1), 0), Times.Once);
    }

    [Fact]
    public void NoBrailleDisplay_IsNeverConnected()
    {
        IBrailleDisplay display = new NoBrailleDisplay();
        display.KeyPressed += (_, _) => throw new InvalidOperationException();

        display.Write(new byte[] { 0x01 }, 0);

        Assert.False(display.IsConnected);
        Assert.Equal(0, display.CellCount);
    }
}
