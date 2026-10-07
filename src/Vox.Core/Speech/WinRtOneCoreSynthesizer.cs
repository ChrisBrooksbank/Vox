using Windows.Media.SpeechSynthesis;

namespace Vox.Core.Speech;

/// <summary>
/// <see cref="IOneCoreSynthesizer"/> over Windows.Media.SpeechSynthesis. Creating it fails where
/// OneCore speech isn't available (the engine registry then falls back to SAPI).
/// </summary>
public sealed class WinRtOneCoreSynthesizer : IOneCoreSynthesizer, IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly object _lock = new();

    public WinRtOneCoreSynthesizer()
    {
        // Less silence after sentences and punctuation, as screen reader users expect
        _synthesizer.Options.AppendedSilence = SpeechAppendedSilence.Min;
        _synthesizer.Options.PunctuationSilence = SpeechPunctuationSilence.Min;
    }

    public IReadOnlyList<OneCoreVoice> Voices =>
        SpeechSynthesizer.AllVoices.Select(v => new OneCoreVoice(v.DisplayName, v.Language)).ToList();

    public string? DefaultVoice => SpeechSynthesizer.DefaultVoice?.DisplayName;

    public async Task<byte[]> SynthesizeAsync(string text, OneCoreOptions options, CancellationToken cancellationToken)
    {
        Windows.Foundation.IAsyncOperation<SpeechSynthesisStream> operation;
        // Options belong to the synthesizer: set them and start synthesis together
        lock (_lock)
        {
            var voice = options.Voice is null
                ? SpeechSynthesizer.DefaultVoice
                : SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == options.Voice) ?? SpeechSynthesizer.DefaultVoice;
            if (voice is not null && _synthesizer.Voice?.Id != voice.Id)
                _synthesizer.Voice = voice;
            _synthesizer.Options.SpeakingRate = options.SpeakingRate;
            _synthesizer.Options.AudioPitch = options.Pitch;
            _synthesizer.Options.AudioVolume = options.Volume;
            operation = _synthesizer.SynthesizeTextToStreamAsync(text);
        }

        using var stream = await operation.AsTask(cancellationToken).ConfigureAwait(false);
        using var input = stream.AsStreamForRead();
        using var bytes = new MemoryStream();
        await input.CopyToAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes.ToArray();
    }

    public void Dispose() => _synthesizer.Dispose();
}
