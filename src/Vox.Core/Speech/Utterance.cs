namespace Vox.Core.Speech;

public enum SpeechPriority
{
    Interrupt = 0,
    High = 1,
    Normal = 2,
    Low = 3
}

/// <param name="SoundCue">A cue played just before the text is spoken (an earcon name, or <see cref="Utterance.CapitalCue"/>).</param>
/// <param name="PitchOffset">Added to the pitch setting (0–100 scale) for this utterance only.</param>
public record Utterance(string Text, SpeechPriority Priority, string? SoundCue = null, int PitchOffset = 0)
{
    /// <summary>The <see cref="SoundCue"/> for a capital letter: a short high beep.</summary>
    public const string CapitalCue = "capital";

    /// <summary>The text's language (BCP 47, e.g. "fr-FR"), when known: from the page it was read on.</summary>
    public string? Language { get; init; }

    /// <summary>A voice to say this utterance in instead of the current one (chosen for its <see cref="Language"/>).</summary>
    public string? Voice { get; init; }
}
