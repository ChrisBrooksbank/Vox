using Microsoft.Extensions.Logging;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Speech;

namespace Vox.Core.Trial;

/// <summary>Offers trial mode in the first-run wizard.</summary>
public interface ITrialOffer
{
    /// <summary>The other screen reader running now, when trial mode could step in for it; else null.</summary>
    RunningScreenReader? FindRunning();

    /// <summary>Begins trial mode; what to say about it.</summary>
    string Begin();

    /// <summary>The key that ends the trial, as people read it.</summary>
    string ReturnKey { get; }
}

/// <summary>
/// Trial mode (<c>--trial</c>, or chosen in the first-run wizard): trying Vox without losing the
/// screen reader one is used to. Beginning it stops a running NVDA or Narrator (so the two don't
/// talk over each other) and records it in <c>trial.json</c>; the return key (Insert+Ctrl+F4,
/// <see cref="NavigationCommand.ReturnToScreenReader"/>) or the Vox menu starts it again and quits
/// Vox, leaving the trial. Vox only quits once the other screen reader has started; Narrator is
/// started when none was running. Vox.Watchdog reads the record too (after repeated crashes).
/// Never on secure screens. Thread-safe.
/// </summary>
public sealed class TrialMode : ITrialOffer
{
    private readonly IScreenReaderControl _control;
    private readonly string? _recordPath;
    private readonly RunPolicy _policy;
    private readonly Action _quit;
    private readonly ILogger<TrialMode> _logger;
    private readonly SpeechQueue? _speech;
    private readonly TimeProvider _time;
    private readonly object _lock = new();

    public TrialMode(IScreenReaderControl control, string? recordPath, RunPolicy policy, Action quit, ILogger<TrialMode> logger,
        SpeechQueue? speech = null, TimeProvider? time = null)
    {
        _control = control;
        _recordPath = policy.IsSecure ? null : recordPath;
        _policy = policy;
        _quit = quit;
        _logger = logger;
        _speech = speech;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The return key as people read it ("Insert+Ctrl+F4"). Set by the host.</summary>
    public Func<string?>? ReturnKeyName { get; set; }

    public string ReturnKey => ReturnKeyName?.Invoke() ?? "Insert+Ctrl+F4";

    /// <summary>Trial mode can be used here (not on secure screens).</summary>
    public bool IsAvailable => _recordPath is not null;

    /// <summary>The trial in progress, or null.</summary>
    public TrialRecord? Current => TrialRecord.Read(_recordPath);

    /// <summary>A trial is in progress.</summary>
    public bool IsActive => Current is not null;

    /// <summary>Raised when a trial begins or ends.</summary>
    public event EventHandler<bool>? ActiveChanged;

    public RunningScreenReader? FindRunning() => IsAvailable && !IsActive ? SafeFind() : null;

    /// <summary>
    /// Begins trial mode: stops the other screen reader running, if one is, and records it. A
    /// trial already in progress goes on. Null when trial mode isn't available here.
    /// </summary>
    public TrialRecord? BeginTrial()
    {
        if (!IsAvailable)
            return null;
        TrialRecord record;
        lock (_lock)
        {
            if (Current is { } current)
                return current;
            var running = SafeFind();
            if (running is not null && !_control.Stop(running))
                _logger.LogWarning("{ScreenReader} did not stop for the trial", running.Kind);
            record = new TrialRecord(running?.Kind, string.IsNullOrEmpty(running?.ExecutablePath) ? null : running.ExecutablePath,
                _time.GetUtcNow());
            if (!record.Write(_recordPath!))
                _logger.LogWarning("Could not record the trial");
        }
        _logger.LogInformation("Trial mode began (in place of {ScreenReader})", record.ScreenReader?.ToString() ?? "no screen reader");
        ActiveChanged?.Invoke(this, true);
        return record;
    }

    /// <summary>Begins trial mode and says what happened (the wizard and <c>--trial</c>).</summary>
    public string Begin()
    {
        var record = BeginTrial();
        if (record is null)
            return VoxStrings.Get("Trial.NotHere");
        var key = ReturnKey;
        return record.ScreenReader is { } previous
            ? VoxStrings.Format("Trial.Began", Name(previous), key)
            : VoxStrings.Format("Trial.BeganAlone", key, Name(OtherScreenReader.Narrator));
    }

    /// <summary>Runs <paramref name="command"/> if it is trial mode's; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.ReturnToScreenReader)
            return false;
        _ = ReturnAsync();
        return true;
    }

    /// <summary>
    /// Ends the trial: starts the screen reader it stepped in for (Narrator when there was none)
    /// and quits Vox. Vox keeps running, and says so, when it can't be started.
    /// </summary>
    public async Task<bool> ReturnAsync()
    {
        if (!IsAvailable)
        {
            Say(VoxStrings.Get("Trial.NotHere"));
            return false;
        }
        if (Current is not { } record)
        {
            Say(VoxStrings.Get("Trial.NotInTrial"));
            return false;
        }
        var name = Name(record.ReturnTo);
        if (_speech is not null)
        {
            // Said before the other screen reader starts talking
            try
            {
                await _speech.EnqueueAndWaitAsync(new Utterance(VoxStrings.Format("Trial.Returning", name), SpeechPriority.Interrupt))
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
        if (!_control.Start(record.ReturnTo, record.ScreenReader == record.ReturnTo ? record.ExecutablePath : null))
        {
            Say(VoxStrings.Format("Trial.CouldNotStart", name));
            return false;
        }
        TrialRecord.Delete(_recordPath);
        _logger.LogInformation("Trial mode ended: back to {ScreenReader}", record.ReturnTo);
        ActiveChanged?.Invoke(this, false);
        _quit();
        return true;
    }

    /// <summary>A screen reader's name as people know it (product names, not translated).</summary>
    public static string Name(OtherScreenReader kind) => kind switch
    {
        OtherScreenReader.Nvda => "NVDA",
        _ => "Narrator",
    };

    private RunningScreenReader? SafeFind()
    {
        try
        {
            return _control.FindRunning();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look for other screen readers");
            return null;
        }
    }

    private void Say(string text) => _speech?.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
