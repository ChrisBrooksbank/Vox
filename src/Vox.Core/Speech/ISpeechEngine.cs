namespace Vox.Core.Speech;

/// <summary>What a speech engine can do besides speaking at a rate in a voice.</summary>
[Flags]
public enum SpeechCapabilities
{
    None = 0,
    /// <summary><see cref="ISpeechEngine.SetPitch"/> changes the pitch.</summary>
    Pitch = 1,
    /// <summary><see cref="ISpeechEngine.SetVolume"/> changes the volume.</summary>
    Volume = 2,
    /// <summary>The engine takes markup commands (SSML or similar) within an utterance.</summary>
    Markup = 4,
}

/// <summary>An installed voice and the language it speaks (BCP 47; empty when unknown).</summary>
public sealed record SpeechVoice(string Name, string Language);

public interface ISpeechEngine
{
    /// <summary>Normal pitch on the 0–100 scale of <see cref="SetPitch"/>.</summary>
    public const int DefaultPitch = 50;

    bool IsSpeaking { get; }

    Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default);
    void Cancel();
    void SetRate(int wpm);
    /// <summary>Selects a voice by name; an empty name selects the engine's default voice.</summary>
    void SetVoice(string voiceName);
    IReadOnlyList<string> GetAvailableVoices();

    /// <summary>The name of the voice currently speaking, or null when unknown.</summary>
    string? CurrentVoice => null;

    /// <summary>The fastest rate Vox offers, in words per minute (engines may support less).</summary>
    public const int MaxSupportedWpm = 900;

    /// <summary>The slowest rate Vox offers, in words per minute.</summary>
    public const int MinSupportedWpm = 150;

    /// <summary>The fastest rate this engine speaks, in words per minute; faster rates are spoken at this one.</summary>
    int MaxRateWpm => 450;

    /// <summary>What the engine supports beyond rate and voice.</summary>
    SpeechCapabilities Capabilities => SpeechCapabilities.None;

    /// <summary>Sets the pitch, 0 (lowest) to 100 (highest), <see cref="DefaultPitch"/> normal. Ignored without <see cref="SpeechCapabilities.Pitch"/>.</summary>
    void SetPitch(int pitch) { }

    /// <summary>Sets the volume, 0 (silent) to 100 (loudest). Ignored without <see cref="SpeechCapabilities.Volume"/>.</summary>
    void SetVolume(int volume) { }

    /// <summary>The languages (BCP 47 tags) the installed voices speak.</summary>
    IReadOnlyList<string> GetLanguages() => [];

    /// <summary>The installed voices with their languages. Engines honour <see cref="Utterance.Voice"/> with one of these names.</summary>
    IReadOnlyList<SpeechVoice> GetVoiceDetails() => GetAvailableVoices().Select(v => new SpeechVoice(v, string.Empty)).ToList();
}
