namespace Vox.Core.Braille.Hid;

/// <summary>
/// One input button capability of a HID device, flattened from <c>HIDP_BUTTON_CAPS</c>: the data
/// indices <paramref name="DataIndexMin"/>…<paramref name="DataIndexMax"/> carry the usages
/// <paramref name="UsageMin"/>…<paramref name="UsageMax"/> in order (when there are more data indices
/// than usages the last usage repeats, as for a row of routing keys that all have the Router Key usage).
/// </summary>
/// <param name="UsagePage">The buttons' usage page (0x41 for braille).</param>
/// <param name="LinkCollection">The index of the collection the buttons are in (groups a router set).</param>
/// <param name="LinkUsagePage">The usage page of that collection.</param>
/// <param name="LinkUsage">The usage of that collection (a router set: 0xFA–0xFC).</param>
public sealed record HidButtonRange(
    ushort UsagePage,
    ushort LinkCollection,
    ushort LinkUsagePage,
    ushort LinkUsage,
    ushort UsageMin,
    ushort UsageMax,
    ushort DataIndexMin,
    ushort DataIndexMax);

/// <summary>A key on a HID braille display, as found from an input data index.</summary>
/// <param name="Usage">The key's usage on the braille page.</param>
/// <param name="Name">The key name reported in <see cref="BrailleKeyEventArgs.Keys"/>.</param>
/// <param name="RoutingCell">For a routing key, the cell it is over (0-based); null for other keys.</param>
public readonly record struct HidBrailleKey(ushort Usage, string Name, int? RoutingCell);

/// <summary>
/// The parts of a HID braille display Vox uses, free of hid.dll so they can be tested: the output
/// report that carries the cells (report id and length, the cell usage, its link collection, cell count
/// and bits per cell) and the map from input data index to key.
/// </summary>
public sealed class HidBrailleLayout
{
    private readonly Dictionary<int, HidBrailleKey> _keys;

    private HidBrailleLayout(
        byte outputReportId, int outputReportLength, ushort cellUsage, ushort cellLinkCollection,
        int cellCount, int cellBitSize, Dictionary<int, HidBrailleKey> keys)
    {
        OutputReportId = outputReportId;
        OutputReportLength = outputReportLength;
        CellUsage = cellUsage;
        CellLinkCollection = cellLinkCollection;
        CellCount = cellCount;
        CellBitSize = cellBitSize;
        _keys = keys;
    }

    /// <summary>The report id of the output report holding the cells (0 when the device uses no report ids).</summary>
    public byte OutputReportId { get; }

    /// <summary>The length of an output report in bytes, including the report id byte.</summary>
    public int OutputReportLength { get; }

    /// <summary>The cell usage: <see cref="HidBrailleUsages.EightDotCell"/> or <see cref="HidBrailleUsages.SixDotCell"/>.</summary>
    public ushort CellUsage { get; }

    /// <summary>The link collection of the cell value capability (passed to <c>HidP_SetUsageValueArray</c>).</summary>
    public ushort CellLinkCollection { get; }

    /// <summary>The number of cells: the cell value capability's report count.</summary>
    public int CellCount { get; }

    /// <summary>Bits per cell in the report (8 on every known display).</summary>
    public int CellBitSize { get; }

    /// <summary>True when the display shows 6-dot cells only.</summary>
    public bool IsSixDot => CellUsage == HidBrailleUsages.SixDotCell;

    /// <summary>The keys the input reports can carry, by data index.</summary>
    public IReadOnlyDictionary<int, HidBrailleKey> Keys => _keys;

    /// <summary>
    /// The layout of a display whose cells go in output report <paramref name="outputReportId"/>, from
    /// its input button capabilities. Only buttons on the braille page become keys; routing keys are
    /// numbered by their data index order within their collection, as NVDA's hidBrailleStandard does.
    /// </summary>
    public static HidBrailleLayout Create(
        byte outputReportId,
        int outputReportLength,
        ushort cellUsage,
        ushort cellLinkCollection,
        int cellCount,
        int cellBitSize,
        IEnumerable<HidButtonRange> inputButtons)
    {
        if (cellUsage is not (HidBrailleUsages.EightDotCell or HidBrailleUsages.SixDotCell))
            throw new ArgumentOutOfRangeException(nameof(cellUsage), "Not a braille cell usage.");
        if (cellCount < 0)
            throw new ArgumentOutOfRangeException(nameof(cellCount));
        if (cellBitSize is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(cellBitSize), "Cells must be 1 to 8 bits.");

        var keys = new Dictionary<int, HidBrailleKey>();
        // Routing keys per link collection: (router set, data indices).
        var routers = new Dictionary<ushort, (int Set, SortedSet<int> Indices)>();

        foreach (var range in inputButtons)
        {
            if (range.UsagePage != HidBrailleUsages.UsagePage || range.DataIndexMax < range.DataIndexMin)
                continue;
            for (int dataIndex = range.DataIndexMin; dataIndex <= range.DataIndexMax; dataIndex++)
            {
                var offset = dataIndex - range.DataIndexMin;
                var usage = (ushort)Math.Min(range.UsageMin + offset, Math.Max(range.UsageMin, range.UsageMax));
                if (usage == HidBrailleUsages.RouterKey)
                {
                    if (!routers.TryGetValue(range.LinkCollection, out var router))
                    {
                        router = (HidBrailleUsages.RouterSetOf(range.LinkUsagePage, range.LinkUsage), new SortedSet<int>());
                        routers[range.LinkCollection] = router;
                    }
                    router.Indices.Add(dataIndex);
                }
                else
                {
                    keys.TryAdd(dataIndex, new HidBrailleKey(usage, HidBrailleUsages.KeyName(usage), null));
                }
            }
        }

        foreach (var (set, indices) in routers.Values)
        {
            var name = HidBrailleUsages.RoutingKeyNameFor(set);
            var cell = 0;
            foreach (var dataIndex in indices)
                keys.TryAdd(dataIndex, new HidBrailleKey(HidBrailleUsages.RouterKey, name, cell++));
        }

        return new HidBrailleLayout(
            outputReportId, outputReportLength, cellUsage, cellLinkCollection, cellCount, cellBitSize, keys);
    }

    /// <summary>The key behind input data index <paramref name="dataIndex"/>, if it is one.</summary>
    public bool TryGetKey(int dataIndex, out HidBrailleKey key) => _keys.TryGetValue(dataIndex, out key);

    /// <summary>
    /// One value per cell for the cell usage: <paramref name="cells"/> padded with blank cells or cut to
    /// <see cref="CellCount"/>, with dots 7 and 8 dropped on a 6-dot display.
    /// </summary>
    public byte[] CellValues(ReadOnlySpan<byte> cells)
    {
        var values = new byte[CellCount];
        var count = Math.Min(cells.Length, CellCount);
        var mask = IsSixDot ? (byte)0x3F : (byte)0xFF;
        for (var i = 0; i < count; i++)
            values[i] = (byte)(cells[i] & mask);
        return values;
    }

    /// <summary>
    /// The usage value array <c>HidP_SetUsageValueArray</c> takes for <paramref name="cells"/>: the
    /// <see cref="CellValues"/> packed <see cref="CellBitSize"/> bits each, least significant bit first
    /// (with 8-bit cells, one byte per cell).
    /// </summary>
    public byte[] UsageValueArray(ReadOnlySpan<byte> cells)
    {
        var values = CellValues(cells);
        if (CellBitSize == 8)
            return values;

        var packed = new byte[(CellCount * CellBitSize + 7) / 8];
        var valueMask = (1 << CellBitSize) - 1;
        var bit = 0;
        foreach (var value in values)
        {
            var v = value & valueMask;
            for (var b = 0; b < CellBitSize; b++, bit++)
            {
                if ((v & (1 << b)) != 0)
                    packed[bit / 8] |= (byte)(1 << (bit % 8));
            }
        }
        return packed;
    }

    /// <summary>
    /// How many data indices a button capability without a usage range covers, starting at
    /// <paramref name="dataIndex"/>: its report count when Windows gives one (0 on older versions),
    /// never running into the next capability's first index (<paramref name="otherStarts"/>, from
    /// every input button and value capability) or past <paramref name="totalDataIndices"/>.
    /// Data indices are dense (0 to the total − 1), so the next start bounds this one.
    /// </summary>
    public static int NotRangeDataIndexCount(
        int dataIndex, int reportCount, IEnumerable<int> otherStarts, int totalDataIndices)
    {
        var limit = totalDataIndices > dataIndex ? totalDataIndices : dataIndex + 1;
        foreach (var start in otherStarts)
        {
            if (start > dataIndex && start < limit)
                limit = start;
        }
        var span = limit - dataIndex;
        if (reportCount > 0)
            span = Math.Min(span, reportCount);
        return Math.Max(1, span);
    }
}
