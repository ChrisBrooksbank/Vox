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

/// <summary>How much punctuation and how many symbols are spoken.</summary>
public enum PunctuationLevel
{
    None,
    Some,
    Most,
    All
}

/// <summary>How capital letters are indicated.</summary>
public enum CapitalIndication
{
    Off,
    /// <summary>Spoken at a higher pitch.</summary>
    Pitch,
    /// <summary>"cap" is said first ("all caps" for a word in capitals).</summary>
    SayCap,
    /// <summary>A short high beep first.</summary>
    Beep
}

/// <summary>How numbers are read.</summary>
public enum NumberReading
{
    /// <summary>As the synthesizer says them ("twelve hundred").</summary>
    Words,
    /// <summary>Digit by digit ("1 2 0 0").</summary>
    Digits
}

/// <summary>Which set of key bindings is used.</summary>
public enum KeyboardLayout
{
    /// <summary>NVDA's desktop layout: review and object navigation on the numeric keypad.</summary>
    Desktop,
    /// <summary>NVDA's laptop layout: no numeric keypad, meant for CapsLock as the screen reader key.</summary>
    Laptop
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

/// <summary>What mouse tracking reports under the pointer.</summary>
public enum MouseTextUnit
{
    /// <summary>The object (name and type).</summary>
    Object,
    /// <summary>The line of text, or the object where there is no text.</summary>
    Line,
    /// <summary>The word, or the object where there is no text.</summary>
    Word
}

public record VoxSettings
{
    public VerbosityLevel VerbosityLevel { get; init; } = VerbosityLevel.Beginner;
    public int SpeechRateWpm { get; init; } = 200;
    /// <summary>Speech pitch, 0 (lowest) to 100 (highest); 50 is the voice's normal pitch.</summary>
    public int SpeechPitch { get; init; } = 50;
    /// <summary>Speech volume, 0 to 100.</summary>
    public int SpeechVolume { get; init; } = 100;
    /// <summary>How much punctuation is spoken.</summary>
    public PunctuationLevel PunctuationLevel { get; init; } = PunctuationLevel.Some;
    /// <summary>How a capital letter is indicated when reading by character.</summary>
    public CapitalIndication CapitalsForCharacters { get; init; } = CapitalIndication.Pitch;
    /// <summary>How capitals are indicated when reading a word or line (only "cap" applies within a line).</summary>
    public CapitalIndication CapitalsForWords { get; init; } = CapitalIndication.Off;
    /// <summary>Numbers as words or digit by digit.</summary>
    public NumberReading Numbers { get; init; } = NumberReading.Words;
    /// <summary>Text in another language (a page's lang) is read with a voice for that language.</summary>
    public bool AutoLanguageSwitching { get; init; } = true;
    /// <summary>The voice to use for a language (BCP 47 tag or just the language, e.g. "fr"), overriding the automatic choice.</summary>
    public Dictionary<string, string> LanguageVoices { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Applications (process names) in sleep mode: Vox neither speaks nor takes keys while they have focus.</summary>
    public List<string> SleepApps { get; init; } = [];
    public string? VoiceName { get; init; }
    /// <summary>Speech engine by id ("OneCore", "eSpeak" when installed, "SAPI"); null: the preferred engine that starts (OneCore, then SAPI).</summary>
    public string? SpeechEngine { get; init; }
    public TypingEchoMode TypingEchoMode { get; init; } = TypingEchoMode.Both;
    public bool AudioCuesEnabled { get; init; } = true;
    public bool AnnounceVisitedLinks { get; init; } = true;
    public ModifierKey ModifierKey { get; init; } = ModifierKey.Insert;
    /// <summary>Desktop (numeric keypad) or laptop key bindings.</summary>
    public KeyboardLayout KeyboardLayout { get; init; } = KeyboardLayout.Desktop;
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
    /// <summary>The earcon scheme: a folder under assets/sounds with a manifest.json ("default").</summary>
    public string EarconScheme { get; init; } = "default";
    /// <summary>Keep the audio device awake between sounds so their start isn't clipped.</summary>
    public bool KeepAudioDeviceAwake { get; init; } = true;
    /// <summary>Object navigation skips layout-only objects (unnamed groups and panes).</summary>
    public bool SimpleReviewMode { get; init; } = true;
    /// <summary>The navigator object moves to each newly focused object.</summary>
    public bool ReviewFollowsFocus { get; init; } = true;
    /// <summary>The review cursor moves to the caret when it moves (and to the browse cursor in browse mode).</summary>
    public bool ReviewFollowsCaret { get; init; } = true;
    /// <summary>Speak what is under the mouse pointer as it moves.</summary>
    public bool MouseTracking { get; init; } = false;
    /// <summary>What mouse tracking reports: the object, or the line or word of text under the pointer.</summary>
    public MouseTextUnit MouseTextUnit { get; init; } = MouseTextUnit.Line;
}
