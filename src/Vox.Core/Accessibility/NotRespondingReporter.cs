using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

/// <summary>
/// Tells the user when an application stops responding to UIA: when a UIA call times out, posts
/// <see cref="AppNotRespondingEvent"/> for the foreground application (the one the user is
/// working in, and almost always the one being asked), at most once per application per
/// <see cref="RepeatInterval"/>.
/// </summary>
public sealed class NotRespondingReporter : IDisposable
{
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(10);

    private readonly UIAThread _uiaThread;
    private readonly IForegroundApp _foregroundApp;
    private readonly IEventSink _eventSink;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<NotRespondingReporter> _logger;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly object _lock = new();
    private readonly Dictionary<int, DateTimeOffset> _lastReported = new();

    public NotRespondingReporter(UIAThread uiaThread, IForegroundApp foregroundApp, IEventSink eventSink,
        ILogger<NotRespondingReporter> logger)
        : this(uiaThread, foregroundApp, eventSink, logger, () => DateTimeOffset.UtcNow)
    {
    }

    public NotRespondingReporter(UIAThread uiaThread, IForegroundApp foregroundApp, IEventSink eventSink,
        ILogger<NotRespondingReporter> logger, Func<DateTimeOffset> clock)
    {
        _uiaThread = uiaThread;
        _foregroundApp = foregroundApp;
        _eventSink = eventSink;
        _logger = logger;
        _clock = clock;
        _uiaThread.CallTimedOut += OnCallTimedOut;
    }

    private void OnCallTimedOut(object? sender, TimeSpan timeout) => ReportForeground();

    /// <summary>Reports the foreground application as not responding (throttled).</summary>
    public void ReportForeground()
    {
        ForegroundAppInfo? app;
        try
        {
            app = _foregroundApp.Get();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not find the foreground application");
            return;
        }
        if (app is { } info)
            Report(info.ProcessId, info.Name);
    }

    /// <summary>
    /// Posts <see cref="AppNotRespondingEvent"/> for the process unless it was reported less than
    /// <see cref="RepeatInterval"/> ago. Returns whether it was posted. Vox itself is never reported.
    /// </summary>
    public bool Report(int processId, string appName)
    {
        if (processId == _ownProcessId)
            return false;

        var now = _clock();
        lock (_lock)
        {
            if (_lastReported.TryGetValue(processId, out var last) && now - last < RepeatInterval)
                return false;
            _lastReported[processId] = now;
        }

        _logger.LogInformation("{App} (process {ProcessId}) is not responding to UIA", appName, processId);
        _eventSink.Post(new AppNotRespondingEvent(now, processId, appName));
        return true;
    }

    public void Dispose()
    {
        _uiaThread.CallTimedOut -= OnCallTimedOut;
    }
}
