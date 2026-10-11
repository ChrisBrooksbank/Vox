using System.Runtime.InteropServices;
using Vox.Core.Braille;
using Vox.Core.Braille.Hid;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class HidBrailleLayoutTests
{
    private const ushort Page = HidBrailleUsages.UsagePage;

    /// <summary>A 40-cell display: dots 1–8 and space at 0–8, pan keys at 9–10, 40 routing keys at 11–50.</summary>
    internal static HidBrailleLayout FortyCellLayout(ushort cellUsage = HidBrailleUsages.EightDotCell) =>
        HidBrailleLayout.Create(
            outputReportId: 2, outputReportLength: 41, cellUsage, cellLinkCollection: 1,
            cellCount: 40, cellBitSize: 8,
            [
                new HidButtonRange(Page, 2, Page, HidBrailleUsages.FaceControls,
                    HidBrailleUsages.Dot1, HidBrailleUsages.Space, 0, 8),
                new HidButtonRange(Page, 3, Page, HidBrailleUsages.FaceControls,
                    HidBrailleUsages.PanLeft, HidBrailleUsages.PanRight, 9, 10),
                // A router set: one Router Key usage repeated for 40 data indices.
                new HidButtonRange(Page, 4, Page, HidBrailleUsages.RouterSet1,
                    HidBrailleUsages.RouterKey, HidBrailleUsages.RouterKey, 11, 50),
            ]);

    [Theory]
    [InlineData(HidBrailleUsages.Dot1, "dot1")]
    [InlineData(HidBrailleUsages.Dot8, "dot8")]
    [InlineData(HidBrailleUsages.Space, "space")]
    [InlineData(HidBrailleUsages.LeftSpace, "leftSpace")]
    [InlineData(HidBrailleUsages.RightSpace, "rightSpace")]
    [InlineData(HidBrailleUsages.JoystickCenter, "joystickCenter")]
    [InlineData(HidBrailleUsages.JoystickUp, "joystickUp")]
    [InlineData(HidBrailleUsages.JoystickRight, "joystickRight")]
    [InlineData(HidBrailleUsages.DPadCenter, "dpadCenter")]
    [InlineData(HidBrailleUsages.DPadDown, "dpadDown")]
    [InlineData(HidBrailleUsages.DPadLeft, "dpadLeft")]
    [InlineData(HidBrailleUsages.PanLeft, "panLeft")]
    [InlineData(HidBrailleUsages.PanRight, "panRight")]
    [InlineData(HidBrailleUsages.RockerUp, "rockerUp")]
    [InlineData(HidBrailleUsages.RockerDown, "rockerDown")]
    [InlineData(HidBrailleUsages.RockerPress, "rockerPress")]
    [InlineData(HidBrailleUsages.RouterKey, "routing")]
    [InlineData(0x222, "braille0222")]
    public void KeyName_NamesBrailleUsages(ushort usage, string name)
    {
        Assert.Equal(name, HidBrailleUsages.KeyName(usage));
    }

    [Fact]
    public void Usages_MatchTheHidBraillePage()
    {
        Assert.Equal(0x41, HidBrailleUsages.UsagePage);
        Assert.Equal(0x03, HidBrailleUsages.EightDotCell);
        Assert.Equal(0x04, HidBrailleUsages.SixDotCell);
        Assert.Equal(0xFA, HidBrailleUsages.RouterSet1);
        Assert.Equal(0x100, HidBrailleUsages.RouterKey);
        Assert.Equal(0x209, HidBrailleUsages.Space);
        Assert.Equal(0x21E, HidBrailleUsages.RockerPress);
    }

    [Fact]
    public void Create_NamesKeysByDataIndexInUsageOrder()
    {
        var layout = FortyCellLayout();

        Assert.True(layout.TryGetKey(0, out var dot1));
        Assert.Equal(new HidBrailleKey(HidBrailleUsages.Dot1, "dot1", null), dot1);
        Assert.True(layout.TryGetKey(7, out var dot8));
        Assert.Equal("dot8", dot8.Name);
        Assert.True(layout.TryGetKey(8, out var space));
        Assert.Equal("space", space.Name);
        Assert.True(layout.TryGetKey(10, out var panRight));
        Assert.Equal("panRight", panRight.Name);
        Assert.False(layout.TryGetKey(51, out _));
        Assert.Equal(51, layout.Keys.Count);
    }

    [Fact]
    public void Create_NumbersRoutingKeysWithinTheirRouterSet()
    {
        var layout = FortyCellLayout();

        Assert.True(layout.TryGetKey(11, out var first));
        Assert.Equal(new HidBrailleKey(HidBrailleUsages.RouterKey, "routing", 0), first);
        Assert.True(layout.TryGetKey(30, out var middle));
        Assert.Equal(19, middle.RoutingCell);
        Assert.True(layout.TryGetKey(50, out var last));
        Assert.Equal(39, last.RoutingCell);
    }

    [Fact]
    public void Create_NamesSecondAndThirdRouterSets()
    {
        var layout = HidBrailleLayout.Create(0, 21, HidBrailleUsages.EightDotCell, 1, 20, 8,
        [
            new HidButtonRange(Page, 5, Page, HidBrailleUsages.RouterSet1, HidBrailleUsages.RouterKey, HidBrailleUsages.RouterKey, 0, 19),
            new HidButtonRange(Page, 6, Page, HidBrailleUsages.RouterSet2, HidBrailleUsages.RouterKey, HidBrailleUsages.RouterKey, 20, 39),
            new HidButtonRange(Page, 7, Page, HidBrailleUsages.RouterSet3, HidBrailleUsages.RouterKey, HidBrailleUsages.RouterKey, 40, 41),
        ]);

        layout.TryGetKey(3, out var set1);
        layout.TryGetKey(23, out var set2);
        layout.TryGetKey(41, out var set3);
        Assert.Equal(new HidBrailleKey(HidBrailleUsages.RouterKey, "routing", 3), set1);
        Assert.Equal(new HidBrailleKey(HidBrailleUsages.RouterKey, "routing2", 3), set2);
        Assert.Equal(new HidBrailleKey(HidBrailleUsages.RouterKey, "routing3", 1), set3);
    }

    [Fact]
    public void Create_NumbersRoutingKeysSplitOverSeveralCapabilitiesInOneCollection()
    {
        var layout = HidBrailleLayout.Create(0, 5, HidBrailleUsages.EightDotCell, 1, 4, 8,
        [
            new HidButtonRange(Page, 2, Page, HidBrailleUsages.RouterSet1, HidBrailleUsages.RouterKey, HidBrailleUsages.RouterKey, 12, 13),
            new HidButtonRange(Page, 2, Page, HidBrailleUsages.RouterSet1, HidBrailleUsages.RouterKey, HidBrailleUsages.RouterKey, 10, 11),
        ]);

        Assert.Equal(new[] { 0, 1, 2, 3 }, new[] { 10, 11, 12, 13 }.Select(i => layout.Keys[i].RoutingCell!.Value));
    }

    [Fact]
    public void Create_IgnoresButtonsOnOtherUsagePages()
    {
        var layout = HidBrailleLayout.Create(0, 5, HidBrailleUsages.EightDotCell, 1, 4, 8,
        [
            new HidButtonRange(0x09, 1, Page, HidBrailleUsages.FaceControls, 1, 4, 0, 3),
            new HidButtonRange(Page, 1, Page, HidBrailleUsages.FaceControls, HidBrailleUsages.Dot1, HidBrailleUsages.Dot1, 4, 4),
        ]);

        Assert.Single(layout.Keys);
        Assert.Equal("dot1", layout.Keys[4].Name);
    }

    [Fact]
    public void Create_RejectsANonCellUsage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HidBrailleLayout.Create(0, 5, HidBrailleUsages.RouterKey, 1, 4, 8, []));
    }

    [Fact]
    public void CellValues_PadsShortLinesWithBlankCells()
    {
        var layout = FortyCellLayout();

        var values = layout.CellValues(BrailleDots.FromUnicode("⠁⠃⣿"));

        Assert.Equal(40, values.Length);
        Assert.Equal(new byte[] { 0x01, 0x03, 0xFF }, values[..3]);
        Assert.All(values[3..], v => Assert.Equal(0, v));
    }

    [Fact]
    public void CellValues_CutsLongLines()
    {
        var layout = FortyCellLayout();
        var cells = Enumerable.Range(1, 50).Select(i => (byte)i).ToArray();

        var values = layout.CellValues(cells);

        Assert.Equal(cells[..40], values);
    }

    [Fact]
    public void CellValues_DropsDots7And8OnASixDotDisplay()
    {
        var layout = FortyCellLayout(HidBrailleUsages.SixDotCell);

        var values = layout.CellValues(new byte[] { 0xFF, BrailleDots.Dots78, 0x2D });

        Assert.True(layout.IsSixDot);
        Assert.Equal(new byte[] { 0x3F, 0x00, 0x2D }, values[..3]);
    }

    [Fact]
    public void UsageValueArray_IsOneBytePerCellForEightBitCells()
    {
        var layout = FortyCellLayout();

        var array = layout.UsageValueArray(new byte[] { 0x81, 0x42 });

        Assert.Equal(40, array.Length);
        Assert.Equal(new byte[] { 0x81, 0x42, 0 }, array[..3]);
    }

    [Fact]
    public void UsageValueArray_PacksNarrowerCellsLeastSignificantBitFirst()
    {
        var layout = HidBrailleLayout.Create(0, 4, HidBrailleUsages.SixDotCell, 1, 3, 6, []);

        // 0x3F, 0x01, 0x20 in 6 bits each: bits 0-5 = 111111, 6-11 = 100000, 12-17 = 000001.
        var array = layout.UsageValueArray(new byte[] { 0xFF, 0x01, 0x20 });

        Assert.Equal(new byte[] { 0x7F, 0x00, 0x02 }, array);
    }

    [Theory]
    [InlineData(11, 40, 51, 40)]   // the report count, when Windows gives one
    [InlineData(11, 0, 51, 40)]    // no report count: up to the total
    [InlineData(11, 0, 60, 40)]    // ... or to the next capability (51)
    [InlineData(11, 50, 60, 40)]   // never past the next capability
    [InlineData(3, 0, 4, 1)]
    [InlineData(70, 0, 0, 1)]      // at least one
    public void NotRangeDataIndexCount_StopsAtTheNextCapability(int dataIndex, int reportCount, int total, int expected)
    {
        var starts = new[] { 0, 9, dataIndex, 51 };

        Assert.Equal(expected, HidBrailleLayout.NotRangeDataIndexCount(dataIndex, reportCount, starts, total));
    }

    [Theory]
    [InlineData("HidpCaps", 64)]
    [InlineData("HidpButtonCaps", 72)]
    [InlineData("HidpValueCaps", 72)]
    [InlineData("HidpData", 8)]
    [InlineData("HidpRange", 16)]
    [InlineData("HiddAttributes", 12)]
    public void NativeStructs_HaveTheSdkSizes(string name, int size)
    {
        Assert.Equal(size, Marshal.SizeOf(NativeType(name)));
    }

    [Theory]
    [InlineData("HidpButtonCaps", "LinkCollection", 6)]
    [InlineData("HidpButtonCaps", "IsRange", 12)]
    [InlineData("HidpButtonCaps", "ReportCount", 16)]
    [InlineData("HidpButtonCaps", "Range", 56)]
    [InlineData("HidpValueCaps", "BitSize", 18)]
    [InlineData("HidpValueCaps", "ReportCount", 20)]
    [InlineData("HidpValueCaps", "UnitsExp", 32)]
    [InlineData("HidpValueCaps", "Range", 56)]
    [InlineData("HidpRange", "DataIndexMin", 12)]
    [InlineData("HidpCaps", "NumberLinkCollectionNodes", 44)]
    [InlineData("HidpCaps", "NumberInputDataIndices", 50)]
    public void NativeStructs_HaveTheSdkFieldOffsets(string name, string field, int offset)
    {
        Assert.Equal(offset, (int)Marshal.OffsetOf(NativeType(name), field));
    }

    [Fact]
    public void SpDeviceInterfaceData_HasTheSdkSize()
    {
        Assert.Equal(IntPtr.Size == 8 ? 32 : 28, Marshal.SizeOf(NativeType("SpDeviceInterfaceData")));
    }

    private static Type NativeType(string name) =>
        typeof(HidBrailleDisplay).Assembly.GetType($"Vox.Core.Braille.Hid.HidNative+{name}", throwOnError: true)!;
}
