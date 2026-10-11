using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Braille;
using Vox.Core.Configuration;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Braille;

public sealed class BrailleDisplayManagerTests : IDisposable
{
    private sealed class FakeDriver(string id, int cells = 40) : IBrailleDisplayDriver
    {
        public string Id => id;
        public string Name => id + " display";
        public bool IsConnected { get; private set; }
        public int CellCount => IsConnected ? cells : 0;

        /// <summary>Whether a display is there to be found.</summary>
        public bool Available { get; set; }
        public bool ThrowOnConnect { get; set; }
        public int ConnectAttempts { get; private set; }
        public int Disconnects { get; private set; }
        public bool Disposed { get; private set; }

        public event EventHandler<BrailleKeyEventArgs>? KeyPressed;
        public event EventHandler? Disconnected;

        public bool TryConnect()
        {
            ConnectAttempts++;
            if (ThrowOnConnect)
                throw new IOException("device error");
            IsConnected = Available;
            return IsConnected;
        }

        public void Disconnect()
        {
            Disconnects++;
            IsConnected = false;
        }

        public void Write(ReadOnlyMemory<byte> cells, int? cursorPosition) { }

        /// <summary>The display was unplugged.</summary>
        public void Lose()
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public void Press(params string[] keys) => KeyPressed?.Invoke(this, new BrailleKeyEventArgs(keys));

        public void Dispose() => Disposed = true;
    }

    private readonly FakeDriver _hid = new("HID", 40);
    private readonly FakeDriver _brltty = new("BRLTTY", 20);
    private readonly TestOptionsMonitor<VoxSettings> _settings = new(new VoxSettings());
    private BrailleDisplayManager? _manager;
    private int _changes;

    public void Dispose() => _manager?.Dispose();

    private BrailleDisplayManager Manager(string? setting = null)
    {
        _settings.Set(_settings.CurrentValue with { BrailleDisplay = setting });
        _manager = new BrailleDisplayManager([_hid, _brltty], _settings, NullLogger<BrailleDisplayManager>.Instance, new ManualTimeProvider());
        _manager.DisplayChanged += (_, _) => _changes++;
        return _manager;
    }

    [Fact]
    public void BeforeAScan_ThereIsNoDisplay()
    {
        var manager = Manager();

        Assert.IsType<NoBrailleDisplay>(manager.Current);
        Assert.False(manager.Current.IsConnected);
        Assert.Equal(new IBrailleDisplayDriver[] { _hid, _brltty }, manager.Drivers);
    }

    [Fact]
    public void Automatic_TriesDriversInOrder()
    {
        _brltty.Available = true;
        var manager = Manager();

        manager.Scan();

        Assert.Same(_brltty, manager.Current);
        Assert.Equal(1, _hid.ConnectAttempts);
        Assert.Equal(1, _brltty.ConnectAttempts);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Automatic_TakesTheFirstThatConnects()
    {
        _hid.Available = true;
        _brltty.Available = true;
        var manager = Manager();

        manager.Scan();

        Assert.Same(_hid, manager.Current);
        Assert.Equal(0, _brltty.ConnectAttempts);
    }

    [Fact]
    public void ADriverThatThrows_IsSkipped()
    {
        _hid.ThrowOnConnect = true;
        _brltty.Available = true;
        var manager = Manager();

        manager.Scan();

        Assert.Same(_brltty, manager.Current);
    }

    [Fact]
    public void NothingFound_NoChange()
    {
        var manager = Manager();

        manager.Scan();

        Assert.IsType<NoBrailleDisplay>(manager.Current);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void Setting_PicksOneDriver()
    {
        _hid.Available = true;
        _brltty.Available = true;
        var manager = Manager("brltty");

        manager.Scan();

        Assert.Same(_brltty, manager.Current);
        Assert.Equal(0, _hid.ConnectAttempts);
    }

    [Fact]
    public void None_ConnectsNothing()
    {
        _hid.Available = true;
        var manager = Manager(BrailleDisplayManager.NoneId);

        manager.Scan();

        Assert.IsType<NoBrailleDisplay>(manager.Current);
        Assert.Equal(0, _hid.ConnectAttempts);
        Assert.Equal(0, _brltty.ConnectAttempts);
    }

    [Fact]
    public void AConnectedDisplay_IsNotLookedForAgain()
    {
        _hid.Available = true;
        var manager = Manager();
        manager.Scan();

        manager.Scan();

        Assert.Equal(1, _hid.ConnectAttempts);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Disconnected_LetsGo_ThenAScanReconnects()
    {
        _hid.Available = true;
        var manager = Manager();
        manager.Scan();

        _hid.Lose();

        Assert.IsType<NoBrailleDisplay>(manager.Current);
        Assert.Equal(2, _changes);

        manager.Scan();

        Assert.Same(_hid, manager.Current);
        Assert.Equal(3, _changes);
        Assert.Equal(2, _hid.ConnectAttempts);
    }

    [Fact]
    public void DisconnectedFromAnotherDriver_IsIgnored()
    {
        _hid.Available = true;
        var manager = Manager();
        manager.Scan();

        _brltty.Lose();

        Assert.Same(_hid, manager.Current);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void SettingChangeAwayFromTheCurrentDriver_DisconnectsIt()
    {
        _hid.Available = true;
        var manager = Manager();
        manager.Scan();

        _settings.Set(_settings.CurrentValue with { BrailleDisplay = "BRLTTY" });

        Assert.Equal(1, _hid.Disconnects);
        Assert.IsType<NoBrailleDisplay>(manager.Current);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void SettingChangeToNone_DisconnectsIt()
    {
        _hid.Available = true;
        var manager = Manager();
        manager.Scan();

        _settings.Set(_settings.CurrentValue with { BrailleDisplay = BrailleDisplayManager.NoneId });
        manager.Scan();

        Assert.Equal(1, _hid.Disconnects);
        Assert.IsType<NoBrailleDisplay>(manager.Current);
        Assert.Equal(1, _hid.ConnectAttempts);
    }

    [Fact]
    public void SettingChangeThatStillAllowsTheCurrentDriver_KeepsIt()
    {
        _hid.Available = true;
        var manager = Manager();
        manager.Scan();

        _settings.Set(_settings.CurrentValue with { BrailleDisplay = "hid" });

        Assert.Equal(0, _hid.Disconnects);
        Assert.Same(_hid, manager.Current);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Keys_AreForwardedOnlyFromTheCurrentDriver()
    {
        _hid.Available = true;
        var manager = Manager();
        manager.Scan();
        var keys = new List<IReadOnlyList<string>>();
        manager.KeyPressed += (_, e) => keys.Add(e.Keys);

        _hid.Press("dot1");
        _brltty.Press("dot2");

        Assert.Equal(["dot1"], Assert.Single(keys));
    }

    [Fact]
    public void Keys_BeforeAnyDisplay_AreNotForwarded()
    {
        var manager = Manager();
        var keys = 0;
        manager.KeyPressed += (_, _) => keys++;

        _hid.Press("dot1");

        Assert.Equal(0, keys);
    }

    [Fact]
    public void Dispose_DisposesTheDrivers_AndStopsScanning()
    {
        _hid.Available = true;
        var manager = Manager();

        manager.Dispose();
        manager.Scan();

        Assert.True(_hid.Disposed);
        Assert.True(_brltty.Disposed);
        Assert.Equal(0, _hid.ConnectAttempts);
    }

    [Fact]
    public void Start_ScansAtOnceAndAgainOnTheInterval()
    {
        var time = new ManualTimeProvider();
        _manager = new BrailleDisplayManager([_hid, _brltty], _settings, NullLogger<BrailleDisplayManager>.Instance, time);

        _manager.Start();
        time.Advance(TimeSpan.Zero);
        Assert.Equal(1, _hid.ConnectAttempts);

        _hid.Available = true;
        time.Advance(BrailleDisplayManager.ScanInterval);

        Assert.Same(_hid, _manager.Current);
        Assert.Equal(2, _hid.ConnectAttempts);
    }
}
