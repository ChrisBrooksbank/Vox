using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Vox.Core.Magnifier;

namespace Vox.Core.ScreenCurtain;

/// <summary>
/// The screen curtain over <c>MagSetFullscreenColorEffect</c> with a matrix that turns every
/// colour black. The Magnification API is initialised on a thread of its own that lives while
/// the curtain is on; the colour effect belongs to the process, so Windows drops it with the
/// process if Vox crashes (<see cref="ClearLeftover"/> is the watchdog's belt and braces).
/// </summary>
public sealed class WindowsScreenCurtain : IScreenCurtain
{
    private readonly ILogger? _logger;
    private readonly object _lock = new();
    private Thread? _thread;
    private ManualResetEventSlim? _release;
    private bool _active;

    public WindowsScreenCurtain(ILogger? logger = null) => _logger = logger;

    public bool IsActive
    {
        get { lock (_lock) return _active; }
    }

    public bool Apply()
    {
        lock (_lock)
        {
            if (_active)
                return true;
            var applied = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            bool ok = false;
            var thread = new Thread(() =>
            {
                try
                {
                    if (!MagInitialize())
                        return;
                    var effect = new MAGCOLOREFFECT { transform = MagnifierColorMatrices.Black };
                    ok = MagSetFullscreenColorEffect(ref effect);
                    applied.Set();
                    if (ok)
                        release.Wait();
                    var identity = new MAGCOLOREFFECT { transform = MagnifierColorMatrices.Identity };
                    MagSetFullscreenColorEffect(ref identity);
                    MagUninitialize();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "The screen curtain failed");
                }
                finally
                {
                    applied.Set();
                }
            })
            { IsBackground = true, Name = "Vox screen curtain" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            applied.Wait(TimeSpan.FromSeconds(5));
            if (!ok)
            {
                release.Set();
                return false;
            }
            _thread = thread;
            _release = release;
            _active = true;
            return true;
        }
    }

    public void Remove()
    {
        Thread? thread;
        lock (_lock)
        {
            if (!_active)
                return;
            _active = false;
            _release?.Set();
            thread = _thread;
            _thread = null;
            _release = null;
        }
        thread?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Remove();

    /// <summary>Resets the colour effect from outside the curtain's own process (used by Vox.Watchdog after a crash).</summary>
    public static void ClearLeftover()
    {
        try
        {
            if (!MagInitialize())
                return;
            var identity = new MAGCOLOREFFECT { transform = MagnifierColorMatrices.Identity };
            MagSetFullscreenColorEffect(ref identity);
            MagUninitialize();
        }
        catch (Exception)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MAGCOLOREFFECT
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] transform;
    }

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);
}
