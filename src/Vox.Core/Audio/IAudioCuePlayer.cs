namespace Vox.Core.Audio;

public interface IAudioCuePlayer
{
    void Play(string cueName);
    bool IsEnabled { get; set; }

    /// <summary>Plays a short sine tone (progress beeps, indentation tones). Optional: does nothing by default.</summary>
    void PlayTone(double frequencyHz, int durationMs) { }

    /// <summary>The output device's name (null: the default device). Optional.</summary>
    string? OutputDevice { get => null; set { } }

    /// <summary>Keep the output device open between sounds. Optional.</summary>
    bool KeepAwake { get => false; set { } }
}
