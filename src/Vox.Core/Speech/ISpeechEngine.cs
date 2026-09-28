namespace Vox.Core.Speech;

public interface ISpeechEngine
{
    bool IsSpeaking { get; }

    Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default);
    void Cancel();
    void SetRate(int wpm);
    /// <summary>Selects a voice by name; an empty name selects the engine's default voice.</summary>
    void SetVoice(string voiceName);
    IReadOnlyList<string> GetAvailableVoices();
}
