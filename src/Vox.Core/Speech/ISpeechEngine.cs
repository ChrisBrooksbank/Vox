namespace Vox.Core.Speech;

public interface ISpeechEngine
{
    bool IsSpeaking { get; }

    Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default);
    void Cancel();
    void SetRate(int wpm);
    void SetVoice(string voiceName);
    IReadOnlyList<string> GetAvailableVoices();
}
