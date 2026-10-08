using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;

namespace Vox.Core.Lifecycle;

/// <summary>
/// Sleep mode, per application: while an application in <see cref="VoxSettings.SleepApps"/> has
/// focus, Vox says nothing and leaves its keys alone (for self-voicing applications). The toggle
/// command adds or removes the focused application, saving the list.
/// <para>
/// <see cref="IsAsleepNow"/> looks at the foreground window (for the speech queue's mute gate);
/// <see cref="IsAsleepCached"/> is what it last found, for the keyboard hook, which must not
/// look up processes.
/// </para>
/// </summary>
public sealed class SleepMode
{
    private const int MaxCachedProcesses = 256;

    private readonly IForegroundWindow _foreground;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action<VoxSettings> _updateSettings;
    private readonly SpeechQueue _speechQueue;
    private readonly Func<int, string?> _processName;
    private readonly object _lock = new();
    private readonly Dictionary<int, string?> _names = new();
    private volatile bool _asleep;

    public SleepMode(IForegroundWindow foreground, IOptionsMonitor<VoxSettings> settings, Action<VoxSettings> updateSettings,
        SpeechQueue speechQueue, Func<int, string?>? processName = null)
    {
        _foreground = foreground;
        _settings = settings;
        _updateSettings = updateSettings;
        _speechQueue = speechQueue;
        _processName = processName ?? ProcessName;
    }

    /// <summary>Whether the focused application was asleep when last looked at (any thread, never blocks).</summary>
    public bool IsAsleepCached => _asleep;

    /// <summary>Whether the focused application is asleep now (and remembers it for <see cref="IsAsleepCached"/>).</summary>
    public bool IsAsleepNow()
    {
        var name = ForegroundProcess();
        var apps = _settings.CurrentValue.SleepApps;
        bool asleep = name is not null && apps.Contains(name, StringComparer.OrdinalIgnoreCase);
        _asleep = asleep;
        return asleep;
    }

    /// <summary>Runs <paramref name="command"/> if it is the sleep mode toggle; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.ToggleSleepMode)
            return false;
        Toggle();
        return true;
    }

    /// <summary>Puts the focused application to sleep, or wakes it.</summary>
    public void Toggle()
    {
        var name = ForegroundProcess();
        if (name is null)
        {
            Say("Not available");
            return;
        }
        var settings = _settings.CurrentValue;
        var apps = settings.SleepApps;
        if (apps.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _updateSettings(settings with { SleepApps = apps.Where(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).ToList() });
            IsAsleepNow();
            Say("Sleep mode off");
        }
        else
        {
            // Said before the application goes to sleep, which mutes Vox
            Say("Sleep mode on");
            _updateSettings(settings with { SleepApps = [.. apps, name] });
            IsAsleepNow();
        }
    }

    private string? ForegroundProcess()
    {
        ForegroundWindowInfo? window;
        try { window = _foreground.Get(); }
        catch { return null; }
        if (window is not { ProcessId: > 0 } info)
            return null;
        lock (_lock)
        {
            if (_names.TryGetValue(info.ProcessId, out var cached))
                return cached;
        }
        var name = _processName(info.ProcessId);
        lock (_lock)
        {
            if (_names.Count >= MaxCachedProcesses)
                _names.Clear();
            _names[info.ProcessId] = name;
        }
        return name;
    }

    private static string? ProcessName(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private void Say(string text) => _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
