namespace Vox.Core.Audio;

public interface IAudioCuePlayer
{
    void Play(string cueName);
    bool IsEnabled { get; set; }
}
