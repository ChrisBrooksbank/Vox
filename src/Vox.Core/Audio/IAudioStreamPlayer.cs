using NAudio.Wave;

namespace Vox.Core.Audio;

/// <summary>Plays audio streams (speech) on the output device, mixed with earcons.</summary>
public interface IAudioStreamPlayer
{
    /// <summary>
    /// Starts playing <paramref name="source"/> (any rate, mono or stereo). It plays until it
    /// returns fewer samples than asked for; returning 0 stops it.
    /// </summary>
    void PlayStream(ISampleProvider source);
}
