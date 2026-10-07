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

/// <summary>Whether other applications' audio is lowered while Vox speaks.</summary>
public enum AudioDuckingMode
{
    Off,
    WhileSpeaking,
    Always
}

/// <summary>How progress bars are reported.</summary>
public enum ProgressReporting
{
    /// <summary>Speak the percentage every 10 %.</summary>
    Every10Percent,
    /// <summary>Speak the percentage every 25 %.</summary>
    Every25Percent,
    /// <summary>A beep whose pitch rises with the percentage.</summary>
    Beep,
    Off
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
    /// <summary>How progress bars are reported.</summary>
    public ProgressReporting ProgressBars { get; init; } = ProgressReporting.Every10Percent;
    /// <summary>Also report progress bars of applications other than the foreground one.</summary>
    public bool ReportBackgroundProgress { get; init; } = false;
    /// <summary>Start Vox when the user signs in to Windows.</summary>
    public bool StartAtLogon { get; init; } = false;
    /// <summary>Output device for earcons and tones by name (null: the default device).</summary>
    public string? AudioOutputDevice { get; init; }
    /// <summary>Lower other applications' audio: off, while Vox speaks, or always (needs UI access).</summary>
    public AudioDuckingMode AudioDucking { get; init; } = AudioDuckingMode.Off;
    /// <summary>Keep the audio device awake between sounds so their start isn't clipped.</summary>
    public bool KeepAudioDeviceAwake { get; init; } = true;
    /// <summary>Object navigation skips layout-only objects (unnamed groups and panes).</summary>
    public bool SimpleReviewMode { get; init; } = true;
}
