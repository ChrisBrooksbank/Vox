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
    private volatile bool _isSpeaking;

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

        // Warm up the engine to avoid first-utterance delay
        _synthesizer.Volume = 0;
        _synthesizer.SpeakAsync(" ");
        _synthesizer.Volume = 100;
    }

    public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(utterance.Text))
            return;

        if (utterance.Priority == SpeechPriority.Interrupt)
        {
            _synthesizer.SpeakAsyncCancelAll();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new Prompt(utterance.Text);

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
            _synthesizer.SpeakAsyncCancelAll();
            tcs.TrySetCanceled(cancellationToken);
        });

        _synthesizer.SpeakAsync(prompt);

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

    public void Cancel()
    {
        _synthesizer.SpeakAsyncCancelAll();
    }

    public void SetRate(int wpm)
    {
        wpm = Math.Clamp(wpm, MinWpm, MaxWpm);
        _synthesizer.Rate = WpmToSapiRate(wpm);
        _logger.LogDebug("Speech rate set to {Wpm} WPM (SAPI rate {SapiRate})", wpm, _synthesizer.Rate);
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

    public void SetVoice(string voiceName)
    {
        try
        {
            _synthesizer.SelectVoice(voiceName);
            _logger.LogInformation("Voice set to {VoiceName}", voiceName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set voice {VoiceName}", voiceName);
        }
    }

    public IReadOnlyList<string> GetAvailableVoices()
    {
        return _synthesizer.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo.Name)
            .ToList();
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
