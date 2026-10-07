using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Accessibility;

/// <summary>
/// The "where am I" commands, available everywhere: window title (Insert+T), focused element
/// (Insert+Tab), status bar (Insert+End), time (Insert+F12; twice: date), battery
/// (Insert+Shift+B) and the whole foreground window (Insert+B).
/// </summary>
public sealed class WhereAmICommands
{
    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly IForegroundWindow _foregroundWindow;
    private readonly SpeechQueue _speechQueue;
    private readonly AnnouncementBuilder _announcementBuilder;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger<WhereAmICommands> _logger;
    private readonly RepeatPressConfirmation _timePressedTwice = new(TimeSpan.FromMilliseconds(500));
    private FocusChangedEvent? _lastFocus;

    public WhereAmICommands(UIAThread uiaThread, UIAProvider uiaProvider, IForegroundWindow foregroundWindow,
        SpeechQueue speechQueue, AnnouncementBuilder announcementBuilder, IOptionsMonitor<VoxSettings> settings,
        ILogger<WhereAmICommands> logger)
    {
        _uiaThread = uiaThread;
        _uiaProvider = uiaProvider;
        _foregroundWindow = foregroundWindow;
        _speechQueue = speechQueue;
        _announcementBuilder = announcementBuilder;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Clock for the time command (tests).</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    /// <summary>Power state for the battery command (tests).</summary>
    public Func<PowerInfo> Power { get; init; } = ReadPower;

    public void HandleFocusChanged(FocusChangedEvent focus) => _lastFocus = focus;

    /// <summary>Runs <paramref name="command"/> if it is a where-am-I command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.SayTitle:
                var title = SafeForeground()?.Title.Trim();
                Speak(string.IsNullOrEmpty(title) ? "No title" : title);
                return true;

            case NavigationCommand.SayFocus:
                var focus = _lastFocus;
                var settings = _settings.CurrentValue;
                var text = focus is null ? null
                    : _announcementBuilder.Build(focus, VerbosityProfile.For(settings.VerbosityLevel), settings.AnnounceVisitedLinks);
                Speak(string.IsNullOrWhiteSpace(text) ? "No focus" : text);
                return true;

            case NavigationCommand.SayTime:
                var now = Now();
                Speak(_timePressedTwice.Press() ? WhereAmI.DateText(now) : WhereAmI.TimeText(now));
                return true;

            case NavigationCommand.SayBattery:
                Speak(WhereAmI.BatteryText(Power()));
                return true;

            case NavigationCommand.SayStatusBar:
                _ = ReadWindowAsync(window => WhereAmI.StatusBarText(window) ?? "No status bar");
                return true;

            case NavigationCommand.ReadWindow:
                _ = ReadWindowAsync(window => WhereAmI.WindowText(window));
                return true;

            default:
                return false;
        }
    }

    private ForegroundWindowInfo? SafeForeground()
    {
        try { return _foregroundWindow.Get(); }
        catch { return null; }
    }

    /// <summary>Captures the foreground window on the UIA thread and speaks what <paramref name="describe"/> makes of it.</summary>
    private async Task ReadWindowAsync(Func<Buffer.IVBufferElement, string> describe)
    {
        var hwnd = SafeForeground()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            Speak("No window");
            return;
        }
        try
        {
            var text = await _uiaThread.RunAsync(() =>
            {
                var window = _uiaProvider.Automation.ElementFromHandle(hwnd);
                var cached = _uiaProvider.WithDocumentCaptureTimeout(() => window.BuildUpdatedCache(_uiaProvider.SubtreeCacheRequest));
                return describe(UIAElementSnapshot.Capture(cached));
            }, UIAThread.DocumentTimeout).ConfigureAwait(false);
            Speak(text);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the foreground window");
            Speak("Not available");
        }
    }

    private void Speak(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));

    private static PowerInfo ReadPower()
    {
        var status = System.Windows.Forms.SystemInformation.PowerStatus;
        bool hasBattery = (status.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) == 0
            && status.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown;
        int? percent = status.BatteryLifePercent is >= 0 and <= 1 ? (int)Math.Round(status.BatteryLifePercent * 100) : null;
        return new PowerInfo(
            hasBattery,
            percent,
            Charging: (status.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) != 0,
            PluggedIn: status.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online);
    }
}
