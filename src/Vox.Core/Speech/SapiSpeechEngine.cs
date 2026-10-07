using System.Globalization;
using System.Security;
using Microsoft.Extensions.Logging;
using System.Speech.Synthesis;

namespace Vox.Core.Speech;

/// <summary>
/// SAPI5-based speech engine using System.Speech.Synthesis.SpeechSynthesizer.
/// Pre-inits at startup to avoid first-utterance delay.
/// Cancel-before-speak: every Interrupt-priority utterance calls SpeakAsyncCancelAll() first.
/// </summary>
public sealed class SapiSpeechEngine : ISpeechEngine, IDisposable
{
    private readonly SpeechSynthesizer _synthesizer;
    private readonly ILogger<SapiSpeechEngine> _logger;

    // SpeechSynthesizer isn't documented as thread-safe, and it is driven from the speech queue,
    // the pipeline (interrupts), the settings watcher and the wizard: serialize its method calls.
    // Event subscription is left outside the lock (delegate add/remove is already thread-safe),
    // so SpeakCompleted handlers can never deadlock against a call holding it.
    private readonly object _synthLock = new();
    private volatile bool _isSpeaking;
    // The voice chosen at startup, restored when the voice setting is cleared
    private readonly string? _defaultVoiceName;
    private volatile int _pitch = ISpeechEngine.DefaultPitch;
    private volatile int _volume = 100;

    // Supported WPM range (matches the first-run wizard)
    private const int MinWpm = 150;
    private const int MaxWpm = 450;
    private const int MinSapiRate = -10;
    private const int MaxSapiRate = 10;

    // SAPI rate is logarithmic: rate 0 is the voice's normal speed (~180 WPM for Microsoft voices),
    // +10 is about 3x faster and -10 about 3x slower.
    private const double BaseWpm = 180.0;

    public bool IsSpeaking => _isSpeaking;

    public SapiSpeechEngine(ILogger<SapiSpeechEngine> logger)
    {
        _logger = logger;
        _synthesizer = new SpeechSynthesizer();

        // Pre-init: speak an empty string to warm up the engine
        _synthesizer.SetOutputToDefaultAudioDevice();
        _synthesizer.SpeakStarted += (_, _) => _isSpeaking = true;
        _synthesizer.SpeakCompleted += (_, _) => _isSpeaking = false;

        // Select OneCore voice if available
        SelectOneCoreVoice();
        _defaultVoiceName = TryGetCurrentVoiceName();

        // Warm up the engine to avoid first-utterance delay
        _synthesizer.Volume = 0;
        _synthesizer.SpeakAsync(" ");
        _synthesizer.Volume = _volume;
    }

    // System.Speech has no pitch property: pitch is set per prompt with SSML prosody
    public SpeechCapabilities Capabilities => SpeechCapabilities.Pitch | SpeechCapabilities.Volume | SpeechCapabilities.Markup;

    public void SetPitch(int pitch)
    {
        _pitch = Math.Clamp(pitch, 0, 100);
        _logger.LogDebug("Speech pitch set to {Pitch}", _pitch);
    }

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        lock (_synthLock)
        {
            _synthesizer.Volume = _volume;
        }
        _logger.LogDebug("Speech volume set to {Volume}", _volume);
    }

    public IReadOnlyList<string> GetLanguages()
    {
        lock (_synthLock)
        {
            return _synthesizer.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo.Culture?.Name)
                .OfType<string>()
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// The relative SSML prosody pitch for a pitch on the 0–100 scale: 50 is the voice's normal
    /// pitch, 0 and 100 half an octave or so below and above (-50% to +50%).
    /// </summary>
    public static string PitchToProsody(int pitch)
    {
        int percent = Math.Clamp(pitch, 0, 100) - ISpeechEngine.DefaultPitch;
        return percent >= 0 ? $"+{percent}%" : $"{percent}%";
    }

    /// <summary>SSML speaking <paramref name="text"/> (escaped) at <paramref name="pitch"/>.</summary>
    public static string BuildPitchSsml(string text, int pitch, string language) =>
        "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" " +
        $"xml:lang=\"{SecurityElement.Escape(language)}\"><prosody pitch=\"{PitchToProsody(pitch)}\">" +
        $"{SecurityElement.Escape(text)}</prosody></speak>";

    /// <summary>A prompt for <paramref name="text"/>, through SSML only when the pitch isn't normal.</summary>
    private Prompt CreatePrompt(string text)
    {
        int pitch = _pitch;
        if (pitch == ISpeechEngine.DefaultPitch)
            return new Prompt(text);
        string language;
        lock (_synthLock)
        {
            try { language = _synthesizer.Voice?.Culture?.Name ?? CultureInfo.CurrentUICulture.Name; }
            catch { language = CultureInfo.CurrentUICulture.Name; }
        }
        if (string.IsNullOrEmpty(language))
            language = "en-US";
        return new Prompt(BuildPitchSsml(text, pitch, language), SynthesisTextFormat.Ssml);
    }

    public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(utterance.Text))
            return;

        if (utterance.Priority == SpeechPriority.Interrupt)
        {
            CancelAllPrompts();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = CreatePrompt(utterance.Text);

        void OnCompleted(object? sender, SpeakCompletedEventArgs e)
        {
            // Only handle completion for our specific prompt to avoid
            // race conditions with cancelled warm-up or prior speech.
            if (e.Prompt != prompt)
                return;

            _synthesizer.SpeakCompleted -= OnCompleted;
            if (e.Cancelled)
                tcs.TrySetCanceled();
            else if (e.Error != null)
                tcs.TrySetException(e.Error);
            else
                tcs.TrySetResult(true);
        }

        _synthesizer.SpeakCompleted += OnCompleted;

        using var registration = cancellationToken.Register(() =>
        {
            CancelAllPrompts();
            tcs.TrySetCanceled(cancellationToken);
        });

        // Check-and-submit atomically with CancelAllPrompts (same lock): if the registration
        // above already ran (or is running) on another thread, this sees the cancellation and
        // skips submitting — otherwise the prompt could be queued with SAPI just after the
        // cancel-all runs, so it would be spoken anyway despite the caller's token being
        // cancelled and having already observed a TaskCanceledException.
        lock (_synthLock)
        {
            if (!cancellationToken.IsCancellationRequested)
                _synthesizer.SpeakAsync(prompt);
        }

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            _synthesizer.SpeakCompleted -= OnCompleted;
            throw;
        }
    }

    public void Cancel() => CancelAllPrompts();

    private void CancelAllPrompts()
    {
        lock (_synthLock)
        {
            _synthesizer.SpeakAsyncCancelAll();
        }
    }

    public void SetRate(int wpm)
    {
        wpm = Math.Clamp(wpm, MinWpm, MaxWpm);
        int rate = WpmToSapiRate(wpm);
        lock (_synthLock)
        {
            _synthesizer.Rate = rate;
        }
        _logger.LogDebug("Speech rate set to {Wpm} WPM (SAPI rate {SapiRate})", wpm, rate);
    }

    /// <summary>
    /// Converts words per minute to a SAPI rate (-10..10): rate = 10 * log3(wpm / 180).
    /// </summary>
    public static int WpmToSapiRate(int wpm)
    {
        if (wpm <= 0) return MinSapiRate;
        var rate = 10.0 * Math.Log(wpm / BaseWpm, 3.0);
        return Math.Clamp((int)Math.Round(rate), MinSapiRate, MaxSapiRate);
    }

    /// <summary>Selects <paramref name="voiceName"/>; empty means the default voice chosen at startup.</summary>
    public void SetVoice(string voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName))
        {
            if (_defaultVoiceName is null)
                return;
            voiceName = _defaultVoiceName;
        }

        try
        {
            lock (_synthLock)
            {
                _synthesizer.SelectVoice(voiceName);
            }
            _logger.LogInformation("Voice set to {VoiceName}", voiceName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set voice {VoiceName}", voiceName);
        }
    }

    public string? CurrentVoice
    {
        get { lock (_synthLock) return TryGetCurrentVoiceName(); }
    }

    public IReadOnlyList<string> GetAvailableVoices()
    {
        lock (_synthLock)
        {
            return _synthesizer.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo.Name)
                .ToList();
        }
    }

    private string? TryGetCurrentVoiceName()
    {
        try { return _synthesizer.Voice?.Name; }
        catch { return null; }
    }

    private void SelectOneCoreVoice()
    {
        // OneCore voices have "MSTTS" or "Microsoft" prefix and are higher quality
        var voices = _synthesizer.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo)
            .ToList();

        var oneCore = voices.FirstOrDefault(v =>
            v.Name.Contains("OneCore", StringComparison.OrdinalIgnoreCase) ||
            (v.Name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) &&
             v.Name.Contains("Desktop", StringComparison.OrdinalIgnoreCase)));

        oneCore ??= voices.FirstOrDefault(v =>
            v.Name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase));

        if (oneCore != null)
        {
            try
            {
                _synthesizer.SelectVoice(oneCore.Name);
                _logger.LogInformation("Selected voice: {VoiceName}", oneCore.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not select preferred voice, using default");
            }
        }
    }

    public void Dispose()
    {
        _synthesizer.Dispose();
    }
}
