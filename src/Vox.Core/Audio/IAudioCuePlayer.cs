namespace Vox.Core.Audio;

public interface IAudioCuePlayer
{
    void Play(string cueName);
    bool IsEnabled { get; set; }

    /// <summary>Plays a short sine tone (progress beeps, indentation tones). Optional: does nothing by default.</summary>
    void PlayTone(double frequencyHz, int durationMs) { }
}
