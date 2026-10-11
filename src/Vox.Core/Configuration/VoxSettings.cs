using Vox.Core.Pipeline;

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

/// <summary>How changes of a line's indentation are reported (for code).</summary>
public enum IndentationReporting
{
    Off,
    /// <summary>"4 spaces", "1 tab", "no indent".</summary>
    Speech,
    /// <summary>A tone, higher for deeper indentation.</summary>
    Tones,
    Both
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

/// <summary>How the braille cursor is drawn.</summary>
public enum BrailleCursorShape
{
    /// <summary>Dots 7 and 8 (an underline).</summary>
    Underline,
    /// <summary>Dot 8 only.</summary>
    Dot8,
    /// <summary>All eight dots.</summary>
    AllDots
}

/// <summary>How much of the context (the objects around the focus) braille shows with a newly focused object.</summary>
public enum BrailleFocusContext
{
    /// <summary>The object first, then its ancestors up to the window, filling the display.</summary>
    FillDisplay,
    /// <summary>The object alone; panning back shows the ancestors.</summary>
    ScrollBackOnly,
    /// <summary>The object with the ancestors that differ from the previous focus's.</summary>
    ChangedContext
}

/// <summary>What the braille display follows.</summary>
public enum BrailleTether
{
    /// <summary>The focus, the caret and the browse cursor; review commands show the review cursor until the focus moves.</summary>
    Automatic,
    Focus,
    Review
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
    /// <summary>Report a line's indentation when it changes, moving by line in a text control.</summary>
    public IndentationReporting Indentation { get; init; } = IndentationReporting.Off;
    /// <summary>Checked, expanded and selected states are played as sounds instead of spoken.</summary>
    public bool StatesAsSounds { get; init; } = false;
    /// <summary>Text in another language (a page's lang) is read with a voice for that language.</summary>
    public bool AutoLanguageSwitching { get; init; } = true;
    /// <summary>The voice to use for a language (BCP 47 tag or just the language, e.g. "fr"), overriding the automatic choice.</summary>
    public Dictionary<string, string> LanguageVoices { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Applications (process names) in sleep mode: Vox neither speaks nor takes keys while they have focus.</summary>
    public List<string> SleepApps { get; init; } = [];
    /// <summary>
    /// The mode web content in an application (process name, e.g. "Code", "ms-teams") starts in,
    /// overriding the automatic choice (Focus mode for web applications, else Browse mode).
    /// </summary>
    public Dictionary<string, InteractionMode> AppDefaultModes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>App modules (by id: "word", "excel", ...) turned off: their applications are read like any other.</summary>
    public List<string> DisabledAppModules { get; init; } = [];
    /// <summary>Excel: say "has formula" for a cell with a formula.</summary>
    public bool ExcelReportFormulas { get; init; } = true;
    /// <summary>Excel: say "has comment" and "has note" for a cell with one.</summary>
    public bool ExcelReportCommentsAndNotes { get; init; } = true;
    /// <summary>Word: say comments and tracked changes when the caret moves into them.</summary>
    public bool WordReportCommentsAndRevisions { get; init; } = true;
    /// <summary>Outlook: the message list columns read, in order ("From", "Subject", "Received", "Size", "Categories", "To").</summary>
    public List<string> OutlookColumns { get; init; } = ["From", "Subject", "Received"];
    /// <summary>Outlook: whether a message's statuses (unread, flagged, attachment) are said before or after its columns.</summary>
    public AppModules.Outlook.OutlookStatusPosition OutlookStatusPosition { get; init; } = AppModules.Outlook.OutlookStatusPosition.First;
    /// <summary>Outlook: read the message after its row when another message is selected.</summary>
    public bool OutlookAutoRead { get; init; }
    public string? VoiceName { get; init; }
    /// <summary>Speech engine by id ("OneCore", "eSpeak" when installed, "SAPI"); null: the preferred engine that starts (OneCore, then SAPI).</summary>
    public string? SpeechEngine { get; init; }
    public TypingEchoMode TypingEchoMode { get; init; } = TypingEchoMode.Both;
    public bool AudioCuesEnabled { get; init; } = true;
    public bool AnnounceVisitedLinks { get; init; } = true;

    /// <summary>The settings file format this version of Vox writes (<see cref="SettingsMigrations"/>).</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The format of the file the settings came from; older files are migrated when read.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    /// <summary>Say an abbreviation's expansion (its title) before it when the browse cursor reaches it.</summary>
    public bool ExpandAbbreviations { get; init; }
    /// <summary>
    /// Browse mode keeps links and other inline text on their paragraph's line, as on screen
    /// (true), or puts every element on a line of its own (false).
    /// </summary>
    public bool ScreenLayout { get; init; } = true;
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
    /// <summary>Which updates to check for (once a day, at startup); Off never asks the network.</summary>
    public Updates.UpdateChannel UpdateChannel { get; init; } = Updates.UpdateChannel.Stable;
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

    /// <summary>The braille display driver by id ("HID", "BRLTTY"); null: find a display automatically; "None": no braille.</summary>
    public string? BrailleDisplay { get; init; }
    /// <summary>The liblouis braille table (<see cref="Braille.BrailleTables"/>); computer braille is used when liblouis isn't installed.</summary>
    public string BrailleTable { get; init; } = "en-ueb-g2.ctb";
    /// <summary>How the braille cursor is drawn.</summary>
    public BrailleCursorShape BrailleCursorShape { get; init; } = BrailleCursorShape.Underline;
    /// <summary>The braille cursor blinks.</summary>
    public bool BrailleCursorBlink { get; init; } = true;
    /// <summary>How long the braille cursor is shown, then hidden, when it blinks (milliseconds).</summary>
    public int BrailleCursorBlinkRateMs { get; init; } = 500;
    /// <summary>How long messages (what Vox says) stay on the braille display, in seconds; 0: never shown.</summary>
    public int BrailleMessageTimeoutSeconds { get; init; } = 4;
    /// <summary>How much context braille shows with a newly focused object.</summary>
    public BrailleFocusContext BrailleFocusContext { get; init; } = BrailleFocusContext.FillDisplay;
    /// <summary>What the braille display follows: the focus, the review cursor, or both.</summary>
    public BrailleTether BrailleTether { get; init; } = BrailleTether.Automatic;
    /// <summary>Selected text is shown with dots 7 and 8.</summary>
    public bool BrailleShowSelection { get; init; } = true;
    /// <summary>In contracted braille, the word at the cursor is shown uncontracted.</summary>
    public bool BrailleExpandWordAtCursor { get; init; } = true;
    /// <summary>Panning never splits a word across two windows.</summary>
    public bool BrailleWordWrap { get; init; } = true;
}
