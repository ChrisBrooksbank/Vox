using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;

namespace Vox.Core.Braille;

/// <summary>
/// Finds and keeps a braille display: the driver the <c>BrailleDisplay</c> setting names, or with
/// none named each driver in turn (HID braille first, then BRLTTY). While no display is connected
/// it looks again every <see cref="ScanInterval"/>, so a display plugged in later is found; a lost
/// display (unplugged, BRLTTY stopped) is let go and looked for again. "None" turns braille off.
/// </summary>
public sealed class BrailleDisplayManager : IBrailleDisplaySource, IDisposable
{
    /// <summary>The setting value that turns braille off.</summary>
    public const string NoneId = "None";

    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(3);

    private static readonly NoBrailleDisplay NoDisplay = new();

    private readonly IReadOnlyList<IBrailleDisplayDriver> _drivers;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger<BrailleDisplayManager> _logger;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly IDisposable? _settingsSubscription;
    private IBrailleDisplayDriver? _current;
    private string? _driverSetting;
    private ITimer? _scanTimer;
    private int _scanning;
    private bool _started;
    private bool _disposed;

    public BrailleDisplayManager(IEnumerable<IBrailleDisplayDriver> drivers, IOptionsMonitor<VoxSettings> settings,
        ILogger<BrailleDisplayManager> logger, TimeProvider? time = null)
    {
        _drivers = drivers.ToList();
        _settings = settings;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _driverSetting = settings.CurrentValue.BrailleDisplay;
        _settingsSubscription = settings.OnChange(OnSettingsChanged);
        foreach (var driver in _drivers)
        {
            driver.KeyPressed += OnDriverKeyPressed;
            driver.Disconnected += OnDriverDisconnected;
        }
    }

    public IBrailleDisplay Current
    {
        get { lock (_lock) return _current is { IsConnected: true } current ? current : NoDisplay; }
    }

    /// <summary>The drivers Vox has, in the order they are tried.</summary>
    public IReadOnlyList<IBrailleDisplayDriver> Drivers => _drivers;

    public event EventHandler? DisplayChanged;
    public event EventHandler<BrailleKeyEventArgs>? KeyPressed;

    /// <summary>Starts looking for a display (now, then every <see cref="ScanInterval"/> while none is connected).</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed)
                return;
            _started = true;
            _scanTimer = _time.CreateTimer(_ => Scan(), null, TimeSpan.Zero, ScanInterval);
        }
    }

    /// <summary>Looks for a display once, if none is connected (the timer calls this; tests call it directly).</summary>
    public void Scan()
    {
        // One scan at a time: a slow driver must not pile up scans
        if (Interlocked.Exchange(ref _scanning, 1) == 1)
            return;
        try
        {
            IReadOnlyList<IBrailleDisplayDriver> candidates;
            lock (_lock)
            {
                if (_disposed || _current is { IsConnected: true })
                    return;
                candidates = Candidates(_driverSetting);
            }
            foreach (var driver in candidates)
            {
                bool connected;
                try
                {
                    connected = driver.TryConnect();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Braille driver {Driver} failed to connect", driver.Id);
                    continue;
                }
                if (!connected)
                    continue;
                lock (_lock)
                {
                    // The setting changed while connecting
                    if (_disposed || !Candidates(_driverSetting).Contains(driver))
                    {
                        driver.Disconnect();
                        return;
                    }
                    _current = driver;
                }
                _logger.LogInformation("Braille display connected: {Driver}, {Cells} cells", driver.Name, driver.CellCount);
                DisplayChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
        finally
        {
            Volatile.Write(ref _scanning, 0);
        }
    }

    /// <summary>The drivers the setting allows, in the order they are tried.</summary>
    private IReadOnlyList<IBrailleDisplayDriver> Candidates(string? setting)
    {
        if (string.Equals(setting, NoneId, StringComparison.OrdinalIgnoreCase))
            return [];
        if (string.IsNullOrEmpty(setting))
            return _drivers;
        return _drivers.Where(d => string.Equals(d.Id, setting, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void OnDriverKeyPressed(object? sender, BrailleKeyEventArgs e)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(sender, _current))
                return;
        }
        KeyPressed?.Invoke(this, e);
    }

    private void OnDriverDisconnected(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(sender, _current))
                return;
            _current = null;
        }
        _logger.LogInformation("Braille display disconnected");
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsChanged(VoxSettings settings, string? _)
    {
        IBrailleDisplayDriver? dropped = null;
        lock (_lock)
        {
            if (_disposed || string.Equals(settings.BrailleDisplay, _driverSetting, StringComparison.OrdinalIgnoreCase))
                return;
            _driverSetting = settings.BrailleDisplay;
            if (_current is not null && !Candidates(_driverSetting).Contains(_current))
            {
                dropped = _current;
                _current = null;
            }
        }
        if (dropped is not null)
        {
            dropped.Disconnect();
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
        if (_started)
            ThreadPool.QueueUserWorkItem(_ => Scan());
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _scanTimer?.Dispose();
            _current = null;
        }
        _settingsSubscription?.Dispose();
        foreach (var driver in _drivers)
        {
            driver.KeyPressed -= OnDriverKeyPressed;
            driver.Disconnected -= OnDriverDisconnected;
            try { driver.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Error closing braille driver {Driver}", driver.Id); }
        }
    }
}
