using Vox.Core.Braille.Hid;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class HidChordTrackerTests
{
    // Data indices in HidBrailleLayoutTests.FortyCellLayout: dot1–dot8 = 0–7, space = 8,
    // panLeft = 9, panRight = 10, routing keys over cells 0–39 = 11–50.
    private const int Dot1 = 0, Dot2 = 1, Dot4 = 3, Dot5 = 4, Space = 8, PanLeft = 9, PanRight = 10;

    private static int Routing(int cell) => 11 + cell;

    private readonly HidChordTracker _tracker = new(HidBrailleLayoutTests.FortyCellLayout());

    [Fact]
    public void SingleKey_IsReportedWhenReleased()
    {
        Assert.Null(_tracker.Update([PanLeft]));
        Assert.True(_tracker.IsChordInProgress);

        var chord = _tracker.Update([]);

        Assert.NotNull(chord);
        Assert.Equal(["panLeft"], chord.Keys);
        Assert.Null(chord.RoutingCell);
        Assert.False(_tracker.IsChordInProgress);
    }

    [Fact]
    public void RoutingKeyAlone_ReportsItsCell()
    {
        Assert.Null(_tracker.Update([Routing(17)]));

        var chord = _tracker.Update([]);

        Assert.NotNull(chord);
        Assert.Equal(["routing"], chord.Keys);
        Assert.Equal(17, chord.RoutingCell);
    }

    [Fact]
    public void DotsChord_IsReportedOnlyAfterEveryKeyIsReleased()
    {
        // Dots 1, 2 and 5 (the letter h), pressed one after another and let go one by one.
        Assert.Null(_tracker.Update([Dot1]));
        Assert.Null(_tracker.Update([Dot1, Dot2]));
        Assert.Null(_tracker.Update([Dot1, Dot2, Dot5]));
        Assert.Null(_tracker.Update([Dot2, Dot5]));
        Assert.Null(_tracker.Update([Dot5]));

        var chord = _tracker.Update([]);

        Assert.NotNull(chord);
        Assert.Equal(["dot1", "dot2", "dot5"], chord.Keys);
    }

    [Fact]
    public void KeysPressedAfterAPartialRelease_JoinTheChord()
    {
        Assert.Null(_tracker.Update([Space, Dot1]));
        Assert.Null(_tracker.Update([Space]));
        Assert.Null(_tracker.Update([Space, Dot4]));

        var chord = _tracker.Update([]);

        Assert.NotNull(chord);
        Assert.Equal(["dot1", "space", "dot4"], chord.Keys);
    }

    [Fact]
    public void KeysPressedInOneReport_AreInDataIndexOrder()
    {
        Assert.Null(_tracker.Update([PanRight, Dot2, Space]));

        Assert.Equal(["dot2", "space", "panRight"], _tracker.Update([])!.Keys);
    }

    [Fact]
    public void KeyWithRoutingKey_ReportsBothAndTheCell()
    {
        Assert.Null(_tracker.Update([Space]));
        Assert.Null(_tracker.Update([Space, Routing(3)]));
        Assert.Null(_tracker.Update([Space]));

        var chord = _tracker.Update([]);

        Assert.NotNull(chord);
        Assert.Equal(["space", "routing"], chord.Keys);
        Assert.Equal(3, chord.RoutingCell);
    }

    [Fact]
    public void TwoRoutingKeys_ReportTheFirstCellOnce()
    {
        Assert.Null(_tracker.Update([Routing(5)]));
        Assert.Null(_tracker.Update([Routing(5), Routing(9)]));

        var chord = _tracker.Update([]);

        Assert.Equal(["routing"], chord!.Keys);
        Assert.Equal(5, chord.RoutingCell);
    }

    [Fact]
    public void DataIndicesThatAreNotKeys_AreIgnored()
    {
        Assert.Null(_tracker.Update([200]));
        Assert.False(_tracker.IsChordInProgress);
        Assert.Null(_tracker.Update([]));

        Assert.Null(_tracker.Update([200, PanLeft]));
        Assert.Equal(["panLeft"], _tracker.Update([200])!.Keys);
    }

    [Fact]
    public void RepeatedReports_DoNotRepeatKeys()
    {
        Assert.Null(_tracker.Update([Dot1]));
        Assert.Null(_tracker.Update([Dot1]));
        Assert.Null(_tracker.Update([Dot1]));

        Assert.Equal(["dot1"], _tracker.Update([])!.Keys);
        Assert.Null(_tracker.Update([]));
    }

    [Fact]
    public void NextChord_StartsEmpty()
    {
        _tracker.Update([Dot1]);
        _tracker.Update([]);

        _tracker.Update([Dot2]);
        var chord = _tracker.Update([]);

        Assert.Equal(["dot2"], chord!.Keys);
    }

    [Fact]
    public void Reset_DropsAChordInProgress()
    {
        _tracker.Update([Dot1]);

        _tracker.Reset();

        Assert.False(_tracker.IsChordInProgress);
        Assert.Null(_tracker.Update([]));
    }
}
