namespace Vox.Core.Speech;

public enum SpeechPriority
{
    Interrupt = 0,
    High = 1,
    Normal = 2,
    Low = 3
}

public record Utterance(string Text, SpeechPriority Priority, string? SoundCue = null);
