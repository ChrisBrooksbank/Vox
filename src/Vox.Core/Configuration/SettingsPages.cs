using System.Text;
using Vox.Core.Localization;
using Vox.Core.Speech;

namespace Vox.Core.Configuration;

/// <summary>One setting on a page of the settings dialog: how it is labelled, read and changed.</summary>
public abstract record SettingField(string Label);

/// <summary>An on/off setting (a check box).</summary>
public sealed record ToggleField(string Label, Func<VoxSettings, bool> Get, Func<VoxSettings, bool, VoxSettings> Set)
    : SettingField(Label);

/// <summary>A number in a range (a spin box).</summary>
public sealed record NumberField(string Label, int Min, int Max, int Step,
    Func<VoxSettings, int> Get, Func<VoxSettings, int, VoxSettings> Set) : SettingField(Label);

/// <summary>One of a list of values (a combo box).</summary>
public sealed record ChoiceField(string Label, IReadOnlyList<SettingChoice> Choices,
    Func<VoxSettings, object?> Get, Func<VoxSettings, object?, VoxSettings> Set) : SettingField(Label)
{
    /// <summary>The index of the current value among the choices (the first when it isn't one of them).</summary>
    public int IndexOf(VoxSettings settings)
    {
        var value = Get(settings);
        for (int i = 0; i < Choices.Count; i++)
        {
            if (Equals(Choices[i].Value, value))
                return i;
        }
        return 0;
    }
}

public sealed record SettingChoice(string Text, object? Value);

/// <summary>A tab of the settings dialog.</summary>
public sealed record SettingsPage(string Title, IReadOnlyList<SettingField> Fields);

/// <summary>What the speech page offers: the engines, the current engine's voices and what it can do.</summary>
public sealed record SpeechOptions(
    IReadOnlyList<SpeechEngineDescriptor> Engines,
    IReadOnlyList<string> Voices,
    int MaxRateWpm,
    SpeechCapabilities Capabilities);

/// <summary>What the audio page offers: the output devices and the earcon schemes installed.</summary>
public sealed record AudioOptions(IReadOnlyList<string> OutputDevices, IReadOnlyList<string> EarconSchemes);

/// <summary>
/// The pages of the settings dialog, as data: the dialog only lays them out, and every change
/// goes through a field's <c>Set</c>, which returns the new settings (applied at once).
/// </summary>
public static class SettingsPages
{
    public static IReadOnlyList<SettingsPage> All(SpeechOptions speech, AudioOptions audio) =>
        [General(), Speech(speech), Braille(), Verbosity(), BrowseMode(), DocumentFormatting(), AudioCues(audio), Keyboard(), Mouse(), Review()];

    /// <summary>Settings no page shows: edited elsewhere (sleep mode's key, the wizard) or not by people.</summary>
    public static readonly IReadOnlySet<string> NotOnAnyPage = new HashSet<string>
    {
        nameof(VoxSettings.SleepApps), nameof(VoxSettings.LanguageVoices), nameof(VoxSettings.AppDefaultModes),
        nameof(VoxSettings.FirstRunCompleted), nameof(VoxSettings.SchemaVersion),
    };

    public static SettingsPage General() => new(L("SettingsPage.General"),
    [
        new ToggleField(L("Setting.StartAtLogon"), s => s.StartAtLogon, (s, v) => s with { StartAtLogon = v }),
        Choice(L("Setting.UpdateChannel"), s => s.UpdateChannel, (s, v) => s with { UpdateChannel = v }),
    ]);

    public static SettingsPage Braille() => new(L("SettingsPage.Braille"),
    [
        new ChoiceField(L("Setting.BrailleDisplay"),
            [
                new SettingChoice(L("Choice.BrailleDisplay.Automatic"), null),
                new SettingChoice(L("Choice.BrailleDisplay.HID"), (object?)"HID"),
                new SettingChoice(L("Choice.BrailleDisplay.BRLTTY"), (object?)"BRLTTY"),
                new SettingChoice(L("Choice.BrailleDisplay.None"), (object?)Vox.Core.Braille.BrailleDisplayManager.NoneId),
            ],
            s => s.BrailleDisplay, (s, v) => s with { BrailleDisplay = (string?)v }),
        new ChoiceField(L("Setting.BrailleTable"),
            Vox.Core.Braille.BrailleTables.All.Select(t => new SettingChoice(t.DisplayName, (object?)t.Id)).ToList(),
            s => s.BrailleTable, (s, v) => v is string table ? s with { BrailleTable = table } : s),
        new ToggleField(L("Setting.BrailleExpandWordAtCursor"), s => s.BrailleExpandWordAtCursor, (s, v) => s with { BrailleExpandWordAtCursor = v }),
        Choice(L("Setting.BrailleTether"), s => s.BrailleTether, (s, v) => s with { BrailleTether = v }),
        Choice(L("Setting.BrailleFocusContext"), s => s.BrailleFocusContext, (s, v) => s with { BrailleFocusContext = v }),
        Choice(L("Setting.BrailleCursorShape"), s => s.BrailleCursorShape, (s, v) => s with { BrailleCursorShape = v }),
        new ToggleField(L("Setting.BrailleCursorBlink"), s => s.BrailleCursorBlink, (s, v) => s with { BrailleCursorBlink = v }),
        new NumberField(L("Setting.BrailleCursorBlinkRateMs"), 100, 2000, 50, s => s.BrailleCursorBlinkRateMs, (s, v) => s with { BrailleCursorBlinkRateMs = v }),
        new NumberField(L("Setting.BrailleMessageTimeoutSeconds"), 0, 20, 1, s => s.BrailleMessageTimeoutSeconds, (s, v) => s with { BrailleMessageTimeoutSeconds = v }),
        new ToggleField(L("Setting.BrailleShowSelection"), s => s.BrailleShowSelection, (s, v) => s with { BrailleShowSelection = v }),
        new ToggleField(L("Setting.BrailleWordWrap"), s => s.BrailleWordWrap, (s, v) => s with { BrailleWordWrap = v }),
    ]);

    public static SettingsPage Verbosity() => new(L("SettingsPage.Verbosity"),
    [
        Choice(L("Setting.VerbosityLevel"), s => s.VerbosityLevel, (s, v) => s with { VerbosityLevel = v }),
        Choice(L("Setting.ProgressBars"), s => s.ProgressBars, (s, v) => s with { ProgressBars = v }),
        new ToggleField(L("Setting.ReportBackgroundProgress"), s => s.ReportBackgroundProgress, (s, v) => s with { ReportBackgroundProgress = v }),
    ]);

    public static SettingsPage BrowseMode() => new(L("SettingsPage.BrowseMode"),
    [
        new ToggleField(L("Setting.ScreenLayout"), s => s.ScreenLayout, (s, v) => s with { ScreenLayout = v }),
        new NumberField(L("Setting.MaxLineLength"), 0, 1000, 10, s => s.MaxLineLength, (s, v) => s with { MaxLineLength = v }),
        new ToggleField(L("Setting.AnnounceVisitedLinks"), s => s.AnnounceVisitedLinks, (s, v) => s with { AnnounceVisitedLinks = v }),
        new ToggleField(L("Setting.ExpandAbbreviations"), s => s.ExpandAbbreviations, (s, v) => s with { ExpandAbbreviations = v }),
    ]);

    public static SettingsPage DocumentFormatting() => new(L("SettingsPage.DocumentFormatting"),
    [
        Choice(L("Setting.SpellingErrors"), s => s.SpellingErrors, (s, v) => s with { SpellingErrors = v }),
        Choice(L("Setting.Indentation"), s => s.Indentation, (s, v) => s with { Indentation = v }),
    ]);

    public static SettingsPage AudioCues(AudioOptions options) => new(L("SettingsPage.AudioCues"),
    [
        new ToggleField(L("Setting.AudioCuesEnabled"), s => s.AudioCuesEnabled, (s, v) => s with { AudioCuesEnabled = v }),
        new ChoiceField(L("Setting.EarconScheme"),
            options.EarconSchemes.Select(scheme => new SettingChoice(SettingsPages.Readable(scheme), (object?)scheme)).ToList(),
            s => s.EarconScheme, (s, v) => v is string scheme ? s with { EarconScheme = scheme } : s),
        new ToggleField(L("Setting.StatesAsSounds"), s => s.StatesAsSounds, (s, v) => s with { StatesAsSounds = v }),
        new ChoiceField(L("Setting.AudioOutputDevice"),
            [new SettingChoice(L("Choice.AudioOutputDevice.Default"), null), .. options.OutputDevices.Select(d => new SettingChoice(d, (object?)d))],
            s => s.AudioOutputDevice, (s, v) => s with { AudioOutputDevice = (string?)v }),
        Choice(L("Setting.AudioDucking"), s => s.AudioDucking, (s, v) => s with { AudioDucking = v }),
        new ToggleField(L("Setting.KeepAudioDeviceAwake"), s => s.KeepAudioDeviceAwake, (s, v) => s with { KeepAudioDeviceAwake = v }),
    ]);

    public static SettingsPage Keyboard() => new(L("SettingsPage.Keyboard"),
    [
        Choice(L("Setting.ModifierKey"), s => s.ModifierKey, (s, v) => s with { ModifierKey = v }),
        Choice(L("Setting.KeyboardLayout"), s => s.KeyboardLayout, (s, v) => s with { KeyboardLayout = v }),
        Choice(L("Setting.TypingEchoMode"), s => s.TypingEchoMode, (s, v) => s with { TypingEchoMode = v }),
    ]);

    public static SettingsPage Mouse() => new(L("SettingsPage.Mouse"),
    [
        new ToggleField(L("Setting.MouseTracking"), s => s.MouseTracking, (s, v) => s with { MouseTracking = v }),
        Choice(L("Setting.MouseTextUnit"), s => s.MouseTextUnit, (s, v) => s with { MouseTextUnit = v }),
    ]);

    public static SettingsPage Review() => new(L("SettingsPage.Review"),
    [
        new ToggleField(L("Setting.SimpleReviewMode"), s => s.SimpleReviewMode, (s, v) => s with { SimpleReviewMode = v }),
        new ToggleField(L("Setting.ReviewFollowsFocus"), s => s.ReviewFollowsFocus, (s, v) => s with { ReviewFollowsFocus = v }),
        new ToggleField(L("Setting.ReviewFollowsCaret"), s => s.ReviewFollowsCaret, (s, v) => s with { ReviewFollowsCaret = v }),
    ]);

    public static SettingsPage Speech(SpeechOptions options)
    {
        var fields = new List<SettingField>
        {
            new ChoiceField(L("Setting.SpeechEngine"),
                [new SettingChoice(L("Choice.SpeechEngine.Automatic"), null), .. options.Engines.Select(e => new SettingChoice(e.DisplayName, (object?)e.Id))],
                s => s.SpeechEngine,
                // Another synthesizer has other voices: start with its default one
                (s, v) => Equals(s.SpeechEngine, v) ? s : s with { SpeechEngine = (string?)v, VoiceName = null }),
            new ChoiceField(L("Setting.VoiceName"),
                [new SettingChoice(L("Choice.Voice.Default"), null), .. options.Voices.Select(v => new SettingChoice(v, (object?)v))],
                s => s.VoiceName,
                (s, v) => s with { VoiceName = (string?)v }),
            new NumberField(L("Setting.SpeechRateWpm"), ISpeechEngine.MinSupportedWpm,
                Math.Clamp(options.MaxRateWpm, ISpeechEngine.MinSupportedWpm, ISpeechEngine.MaxSupportedWpm), 10,
                s => s.SpeechRateWpm, (s, v) => s with { SpeechRateWpm = v }),
        };
        if (options.Capabilities.HasFlag(SpeechCapabilities.Pitch))
            fields.Add(new NumberField(L("Setting.SpeechPitch"), 0, 100, 5, s => s.SpeechPitch, (s, v) => s with { SpeechPitch = v }));
        if (options.Capabilities.HasFlag(SpeechCapabilities.Volume))
            fields.Add(new NumberField(L("Setting.SpeechVolume"), 10, 100, 5, s => s.SpeechVolume, (s, v) => s with { SpeechVolume = v }));
        fields.Add(Choice(L("Setting.PunctuationLevel"), s => s.PunctuationLevel, (s, v) => s with { PunctuationLevel = v }));
        fields.Add(Choice(L("Setting.CapitalsForCharacters"), s => s.CapitalsForCharacters, (s, v) => s with { CapitalsForCharacters = v }));
        fields.Add(Choice(L("Setting.CapitalsForWords"), s => s.CapitalsForWords, (s, v) => s with { CapitalsForWords = v }));
        fields.Add(Choice(L("Setting.Numbers"), s => s.Numbers, (s, v) => s with { Numbers = v }));
        fields.Add(new ToggleField(L("Setting.AutoLanguageSwitching"), s => s.AutoLanguageSwitching, (s, v) => s with { AutoLanguageSwitching = v }));
        return new SettingsPage(L("SettingsPage.Speech"), fields);
    }

    /// <summary>
    /// A choice of every value of an enum setting, each named by its <c>Choice.&lt;type&gt;.&lt;value&gt;</c>
    /// string, else readably ("WhileSpeaking": "While speaking").
    /// </summary>
    public static ChoiceField Choice<T>(string label, Func<VoxSettings, T> get, Func<VoxSettings, T, VoxSettings> set)
        where T : struct, Enum =>
        new(label,
            Enum.GetValues<T>().Select(v => new SettingChoice(ChoiceName(v), v)).ToList(),
            s => get(s),
            (s, v) => v is T value ? set(s, value) : s);

    /// <summary>How the settings dialog names a value of an enum setting.</summary>
    public static string ChoiceName<T>(T value) where T : struct, Enum =>
        VoxStrings.GetOrDefault($"Choice.{typeof(T).Name}.{value}", Readable(value.ToString()));

    private static string L(string name) => VoxStrings.Get(name);

    /// <summary>"WhileSpeaking" as "While speaking".</summary>
    public static string Readable(string name)
    {
        var text = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                text.Append(' ').Append(char.ToLowerInvariant(c));
            else
                text.Append(c);
        }
        return text.ToString();
    }
}
