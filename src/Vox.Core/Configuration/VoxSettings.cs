namespace Vox.Core.Configuration;

public enum VerbosityLevel
{
    Beginner,
    Intermediate,
    Advanced
}

public enum TypingEchoMode
{
    None,
    Characters,
    Words,
    Both
}

public enum ModifierKey
{
    Insert,
    CapsLock
}

/// <summary>How a misspelled word is reported when the caret enters it.</summary>
public enum SpellingErrorReporting
{
    Speech,
    Earcon,
    Off
}

public record VoxSettings
{
    public VerbosityLevel VerbosityLevel { get; init; } = VerbosityLevel.Beginner;
    public int SpeechRateWpm { get; init; } = 200;
    public string? VoiceName { get; init; }
    public TypingEchoMode TypingEchoMode { get; init; } = TypingEchoMode.Both;
    public bool AudioCuesEnabled { get; init; } = true;
    public bool AnnounceVisitedLinks { get; init; } = true;
    public ModifierKey ModifierKey { get; init; } = ModifierKey.Insert;
    /// <summary>Browse-mode lines longer than this are split at a word boundary (0 = never).</summary>
    public int MaxLineLength { get; init; } = 100;
    public bool FirstRunCompleted { get; init; } = false;
    /// <summary>How a misspelled word is reported when the caret enters it in an edit control.</summary>
    public SpellingErrorReporting SpellingErrors { get; init; } = SpellingErrorReporting.Speech;
}
