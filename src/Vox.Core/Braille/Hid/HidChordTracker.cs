namespace Vox.Core.Braille.Hid;

/// <summary>
/// Turns the keys held in each input report into chords: keys accumulate while any of them is held
/// (pressing another before letting go of all adds it to the chord), and the chord is reported once
/// every key has been released. Not thread-safe: the driver's read thread is its only caller.
/// </summary>
public sealed class HidChordTracker
{
    private readonly HidBrailleLayout _layout;
    private readonly HashSet<int> _down = new();
    private readonly List<HidBrailleKey> _chord = new();
    private readonly HashSet<string> _chordNames = new(StringComparer.Ordinal);

    public HidChordTracker(HidBrailleLayout layout)
    {
        _layout = layout;
    }

    /// <summary>True while at least one key is held.</summary>
    public bool IsChordInProgress => _down.Count > 0;

    /// <summary>
    /// Takes the data indices that are on in the latest input report (those that aren't keys, such as
    /// values, are ignored) and returns the chord when this report released its last key, else null.
    /// The chord's keys are in the order pressed (keys pressed in the same report by data index), each
    /// name once; its routing cell is the first routing key's.
    /// </summary>
    public BrailleKeyEventArgs? Update(IEnumerable<int> dataIndicesOn)
    {
        var on = new SortedSet<int>();
        foreach (var dataIndex in dataIndicesOn)
        {
            if (_layout.TryGetKey(dataIndex, out _))
                on.Add(dataIndex);
        }

        foreach (var dataIndex in on)
        {
            if (_down.Add(dataIndex))
            {
                _layout.TryGetKey(dataIndex, out var key);
                if (_chordNames.Add(key.Name))
                    _chord.Add(key);
            }
        }
        _down.IntersectWith(on);

        if (_down.Count > 0 || _chord.Count == 0)
            return null;

        var chord = new BrailleKeyEventArgs(
            _chord.Select(k => k.Name).ToList(),
            _chord.FirstOrDefault(k => k.RoutingCell is not null).RoutingCell);
        Reset();
        return chord;
    }

    /// <summary>Forgets any chord in progress (after the device is reopened).</summary>
    public void Reset()
    {
        _down.Clear();
        _chord.Clear();
        _chordNames.Clear();
    }
}
