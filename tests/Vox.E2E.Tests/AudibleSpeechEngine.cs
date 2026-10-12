using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;

namespace Vox.E2E.Tests;

/// <summary>
/// Records every utterance (for the test's checks) and also speaks it aloud through
/// <paramref name="voice"/>, for watching a scenario run (<see cref="VoxHarnessOptions.Audible"/>).
/// It reports the recording engine's capabilities, so Vox sends it the same text either way.
/// </summary>
public sealed class AudibleSpeechEngine(RecordingSpeechEngine recording, ISpeechEngine voice) : ISpeechEngine, IDisposable
{
    public bool IsSpeaking => voice.IsSpeaking;

    public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        await recording.SpeakAsync(utterance, cancellationToken).ConfigureAwait(false);
        await voice.SpeakAsync(utterance with { Voice = null }, cancellationToken).ConfigureAwait(false);
    }

    public void Cancel()
    {
        recording.Cancel();
        voice.Cancel();
    }

    public void SetRate(int wpm)
    {
        recording.SetRate(wpm);
        voice.SetRate(wpm);
    }

    // The settings name the recording engine's voice; the real one keeps its default
    public void SetVoice(string voiceName) => recording.SetVoice(voiceName);

    public IReadOnlyList<string> GetAvailableVoices() => recording.GetAvailableVoices();

    public SpeechCapabilities Capabilities => recording.Capabilities;

    public void SetPitch(int pitch) => recording.SetPitch(pitch);

    public void SetVolume(int volume) => recording.SetVolume(volume);

    public void Dispose() => (voice as IDisposable)?.Dispose();
}
