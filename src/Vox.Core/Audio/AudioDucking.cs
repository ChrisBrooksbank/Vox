using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;

namespace Vox.Core.Audio;

/// <summary>Lowers (ducks) other applications' audio.</summary>
public interface IAudioDucker
{
    void SetDucked(bool ducked);
}

/// <summary>
/// Follows the ducking setting: <see cref="AudioDuckingMode.Always"/> keeps other audio lowered,
/// <see cref="AudioDuckingMode.WhileSpeaking"/> lowers it from the start of speech until
/// <see cref="ReleaseDelay"/> after the last utterance ends (so a pause between utterances doesn't
/// make other audio jump up and down), <see cref="AudioDuckingMode.Off"/> never.
/// </summary>
public sealed class DuckingController : IDisposable
{
    public static readonly TimeSpan DefaultReleaseDelay = TimeSpan.FromMilliseconds(500);

    private readonly IAudioDucker _ducker;
    private readonly object _lock = new();
    private readonly System.Threading.Timer _releaseTimer;
    private AudioDuckingMode _mode = AudioDuckingMode.Off;
    private bool _ducked;
    private int _speaking;

    public DuckingController(IAudioDucker ducker, TimeSpan? releaseDelay = null)
    {
        _ducker = ducker;
        ReleaseDelay = releaseDelay ?? DefaultReleaseDelay;
        _releaseTimer = new System.Threading.Timer(_ => Release(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public TimeSpan ReleaseDelay { get; }

    public bool IsDucked
    {
        get { lock (_lock) return _ducked; }
    }

    public void SetMode(AudioDuckingMode mode)
    {
        lock (_lock)
        {
            _mode = mode;
            Set(mode == AudioDuckingMode.Always || (mode == AudioDuckingMode.WhileSpeaking && _speaking > 0));
        }
    }

    public void OnSpeechStarted()
    {
        lock (_lock)
        {
            _speaking++;
            if (_mode == AudioDuckingMode.WhileSpeaking)
            {
                _releaseTimer.Change(Timeout.Infinite, Timeout.Infinite);
                Set(true);
            }
        }
    }

    public void OnSpeechEnded()
    {
        lock (_lock)
        {
            _speaking = Math.Max(0, _speaking - 1);
            if (_mode == AudioDuckingMode.WhileSpeaking && _speaking == 0)
                _releaseTimer.Change(ReleaseDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Release()
    {
        lock (_lock)
        {
            if (_mode == AudioDuckingMode.WhileSpeaking && _speaking == 0)
                Set(false);
        }
    }

    // Caller holds _lock
    private void Set(bool ducked)
    {
        if (_ducked == ducked)
            return;
        _ducked = ducked;
        _ducker.SetDucked(ducked);
    }

    public void Dispose()
    {
        _releaseTimer.Dispose();
        lock (_lock)
            Set(false);
    }
}

/// <summary>
/// Ducking through the accessibility API Windows provides for screen readers
/// (AccSetRunningUtilityState). Windows honours it only for processes with UI access (signed,
/// installed builds; see docs/signing.md); elsewhere it does nothing. The call needs a window
/// of this process, so a hidden one is kept on its own thread.
/// </summary>
public sealed class Win32AudioDucker : IAudioDucker, IDisposable
{
    private const uint ANRUS_PRIORITY_AUDIO_ACTIVE = 0x4;
    private const uint ANRUS_PRIORITY_AUDIO_ACTIVE_NODUCK = 0x8;

    private readonly ILogger<Win32AudioDucker> _logger;
    private readonly Lazy<IntPtr> _window;
    private Thread? _windowThread;
    private System.Windows.Forms.NativeWindow? _nativeWindow;

    public Win32AudioDucker(ILogger<Win32AudioDucker> logger)
    {
        _logger = logger;
        _window = new Lazy<IntPtr>(CreateWindow);
    }

    public void SetDucked(bool ducked)
    {
        try
        {
            var hwnd = _window.Value;
            if (hwnd == IntPtr.Zero)
                return;
            int hr = AccSetRunningUtilityState(hwnd, ANRUS_PRIORITY_AUDIO_ACTIVE | ANRUS_PRIORITY_AUDIO_ACTIVE_NODUCK,
                ducked ? ANRUS_PRIORITY_AUDIO_ACTIVE : ANRUS_PRIORITY_AUDIO_ACTIVE_NODUCK);
            if (hr < 0)
                _logger.LogDebug("Audio ducking unavailable (HRESULT 0x{Hr:X8}); it needs UI access", hr);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Audio ducking failed");
        }
    }

    private IntPtr CreateWindow()
    {
        var created = new ManualResetEventSlim();
        IntPtr handle = IntPtr.Zero;
        _windowThread = new Thread(() =>
        {
            _nativeWindow = new System.Windows.Forms.NativeWindow();
            _nativeWindow.CreateHandle(new System.Windows.Forms.CreateParams { Caption = "Vox audio ducking" });
            handle = _nativeWindow.Handle;
            created.Set();
            System.Windows.Forms.Application.Run();
        })
        { IsBackground = true, Name = "Vox-Ducking" };
        if (OperatingSystem.IsWindows())
            _windowThread.SetApartmentState(ApartmentState.STA);
        _windowThread.Start();
        created.Wait(TimeSpan.FromSeconds(5));
        return handle;
    }

    public void Dispose()
    {
        if (_window.IsValueCreated)
            SetDucked(false);
    }

    [DllImport("oleacc.dll")]
    private static extern int AccSetRunningUtilityState(IntPtr hwndApp, uint mask, uint state);
}
