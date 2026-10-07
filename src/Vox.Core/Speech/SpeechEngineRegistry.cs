using Microsoft.Extensions.Logging;

namespace Vox.Core.Speech;

/// <summary>A speech engine Vox can use: its id (as in settings), spoken name, and how to start it.</summary>
public sealed record SpeechEngineDescriptor(string Id, string DisplayName, Func<ISpeechEngine> Create);

/// <summary>
/// The installed speech engines, and the one in use. It is itself the <see cref="ISpeechEngine"/>
/// everything speaks through, forwarding to the current engine, so the engine can be switched at
/// runtime without restarting. Engines are listed in order of preference; the last is the
/// fallback (SAPI). An engine that fails to start, or fails while speaking, is replaced by the
/// next one that starts. Rate, pitch, volume and voice are re-applied to each new engine.
/// </summary>
public sealed class SpeechEngineRegistry : ISpeechEngine, IDisposable
{
    public const string OneCoreId = "OneCore";
    public const string SapiId = "SAPI";

    private readonly IReadOnlyList<SpeechEngineDescriptor> _engines;
    private readonly ILogger<SpeechEngineRegistry> _logger;
    private readonly object _lock = new();
    private ISpeechEngine? _current;
    private SpeechEngineDescriptor? _currentDescriptor;
    // What has been set, so a new engine starts the same way (null: never set)
    private int? _rate;
    private int _pitch = ISpeechEngine.DefaultPitch;
    private int _volume = 100;
    private string? _voice;

    public SpeechEngineRegistry(IReadOnlyList<SpeechEngineDescriptor> engines, ILogger<SpeechEngineRegistry> logger)
    {
        if (engines.Count == 0)
            throw new ArgumentException("At least one speech engine is needed", nameof(engines));
        _engines = engines;
        _logger = logger;
    }

    /// <summary>The engines, most preferred first.</summary>
    public IReadOnlyList<SpeechEngineDescriptor> Engines => _engines;

    /// <summary>The id of the engine in use (starting the preferred one if none is yet).</summary>
    public string CurrentId
    {
        get
        {
            lock (_lock)
            {
                EnsureEngine();
                return _currentDescriptor!.Id;
            }
        }
    }

    /// <summary>Raised (with the new engine's id) after the engine in use changes.</summary>
    public event EventHandler<string>? EngineChanged;

    /// <summary>
    /// Switches to the engine with <paramref name="id"/> (null or unknown: the preferred one); if
    /// it fails to start, to the next one after it that does. Returns the id of the engine now in use.
    /// </summary>
    public string Select(string? id)
    {
        string selected;
        lock (_lock)
        {
            int start = Math.Max(0, IndexOf(id));
            if (_currentDescriptor is not null && _currentDescriptor == _engines[start])
                return _currentDescriptor.Id;
            selected = StartFrom(start);
        }
        EngineChanged?.Invoke(this, selected);
        return selected;
    }

    private int IndexOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return 0;
        for (int i = 0; i < _engines.Count; i++)
        {
            if (string.Equals(_engines[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    /// <summary>Starts the first engine from <paramref name="index"/> on that starts (under the lock).</summary>
    private string StartFrom(int index)
    {
        for (int i = index; i < _engines.Count; i++)
        {
            var descriptor = _engines[i];
            ISpeechEngine engine;
            try
            {
                engine = descriptor.Create();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Speech engine {Engine} could not be started", descriptor.Id);
                continue;
            }
            Replace(descriptor, engine);
            _logger.LogInformation("Speech engine: {Engine}", descriptor.Id);
            return descriptor.Id;
        }
        throw new InvalidOperationException("No speech engine could be started");
    }

    private void Replace(SpeechEngineDescriptor descriptor, ISpeechEngine engine)
    {
        var previous = _current;
        if (_rate is { } rate)
            engine.SetRate(rate);
        engine.SetPitch(_pitch);
        engine.SetVolume(_volume);
        if (_voice is not null)
            engine.SetVoice(_voice);
        _current = engine;
        _currentDescriptor = descriptor;

        if (previous is not null)
        {
            try { previous.Cancel(); } catch { }
            (previous as IDisposable)?.Dispose();
        }
    }

    private ISpeechEngine EnsureEngine()
    {
        if (_current is null)
            StartFrom(0);
        return _current!;
    }

    private ISpeechEngine Current
    {
        get { lock (_lock) return EnsureEngine(); }
    }

    public bool IsSpeaking => Current.IsSpeaking;

    public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        var engine = Current;
        try
        {
            await engine.SpeakAsync(utterance, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && FallBackFrom(engine, ex) is { } fallback)
        {
            // Say it with the engine that took over, so nothing is lost
            await fallback.SpeakAsync(utterance, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// After <paramref name="failed"/> threw while speaking: switches to the next engine, unless
    /// it is the last one (or was already replaced). Returns the engine to retry with, or null.
    /// </summary>
    private ISpeechEngine? FallBackFrom(ISpeechEngine failed, Exception ex)
    {
        string selected;
        ISpeechEngine fallback;
        lock (_lock)
        {
            if (!ReferenceEquals(failed, _current))
                return _current; // already replaced: retry with the engine now in use
            int index = _engines.ToList().IndexOf(_currentDescriptor!);
            if (index >= _engines.Count - 1)
                return null;
            _logger.LogWarning(ex, "Speech engine {Engine} failed; switching to the next one", _currentDescriptor!.Id);
            try
            {
                selected = StartFrom(index + 1);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            fallback = _current!;
        }
        EngineChanged?.Invoke(this, selected);
        return fallback;
    }

    public void Cancel()
    {
        ISpeechEngine? engine;
        lock (_lock) engine = _current;
        engine?.Cancel();
    }

    public void SetRate(int wpm)
    {
        lock (_lock) _rate = wpm;
        Current.SetRate(wpm);
    }

    public void SetPitch(int pitch)
    {
        lock (_lock) _pitch = pitch;
        Current.SetPitch(pitch);
    }

    public void SetVolume(int volume)
    {
        lock (_lock) _volume = volume;
        Current.SetVolume(volume);
    }

    public void SetVoice(string voiceName)
    {
        lock (_lock) _voice = string.IsNullOrWhiteSpace(voiceName) ? null : voiceName;
        Current.SetVoice(voiceName);
    }

    public IReadOnlyList<string> GetAvailableVoices() => Current.GetAvailableVoices();

    public string? CurrentVoice => Current.CurrentVoice;

    public SpeechCapabilities Capabilities => Current.Capabilities;

    public int MaxRateWpm => Current.MaxRateWpm;

    public IReadOnlyList<string> GetLanguages() => Current.GetLanguages();

    public IReadOnlyList<SpeechVoice> GetVoiceDetails() => Current.GetVoiceDetails();

    public void Dispose()
    {
        ISpeechEngine? engine;
        lock (_lock)
        {
            engine = _current;
            _current = null;
            _currentDescriptor = null;
        }
        (engine as IDisposable)?.Dispose();
    }
}
