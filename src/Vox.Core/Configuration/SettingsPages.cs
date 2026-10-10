using System.Text;
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

/// <summary>
/// The pages of the settings dialog, as data: the dialog only lays them out, and every change
/// goes through a field's <c>Set</c>, which returns the new settings (applied at once).
/// </summary>
public static class SettingsPages
{
    public static IReadOnlyList<SettingsPage> All(SpeechOptions speech) => [General(), Speech(speech)];

    public static SettingsPage General() => new("General",
    [
        Choice("&Verbosity", s => s.VerbosityLevel, (s, v) => s with { VerbosityLevel = v }),
        Choice("Screen reader &key", s => s.ModifierKey, (s, v) => s with { ModifierKey = v },
            new() { [ModifierKey.Insert] = "Insert", [ModifierKey.CapsLock] = "Caps Lock" }),
        Choice("Keyboard &layout", s => s.KeyboardLayout, (s, v) => s with { KeyboardLayout = v }),
        new ToggleField("Start Vox when I &sign in", s => s.StartAtLogon, (s, v) => s with { StartAtLogon = v }),
        new ToggleField("&Read the page language's text with a voice for it", s => s.AutoLanguageSwitching, (s, v) => s with { AutoLanguageSwitching = v }),
    ]);

    public static SettingsPage Speech(SpeechOptions options)
    {
        var fields = new List<SettingField>
        {
            new ChoiceField("&Synthesizer",
                [new SettingChoice("Automatic (best available)", null), .. options.Engines.Select(e => new SettingChoice(e.DisplayName, (object?)e.Id))],
                s => s.SpeechEngine,
                // Another synthesizer has other voices: start with its default one
                (s, v) => Equals(s.SpeechEngine, v) ? s : s with { SpeechEngine = (string?)v, VoiceName = null }),
            new ChoiceField("V&oice",
                [new SettingChoice("Default", null), .. options.Voices.Select(v => new SettingChoice(v, (object?)v))],
                s => s.VoiceName,
                (s, v) => s with { VoiceName = (string?)v }),
            new NumberField("&Rate (words per minute)", ISpeechEngine.MinSupportedWpm,
                Math.Clamp(options.MaxRateWpm, ISpeechEngine.MinSupportedWpm, ISpeechEngine.MaxSupportedWpm), 10,
                s => s.SpeechRateWpm, (s, v) => s with { SpeechRateWpm = v }),
        };
        if (options.Capabilities.HasFlag(SpeechCapabilities.Pitch))
            fields.Add(new NumberField("&Pitch", 0, 100, 5, s => s.SpeechPitch, (s, v) => s with { SpeechPitch = v }));
        if (options.Capabilities.HasFlag(SpeechCapabilities.Volume))
            fields.Add(new NumberField("Vol&ume", 10, 100, 5, s => s.SpeechVolume, (s, v) => s with { SpeechVolume = v }));
        fields.Add(Choice("P&unctuation", s => s.PunctuationLevel, (s, v) => s with { PunctuationLevel = v }));
        fields.Add(Choice("&Capitals when reading by character", s => s.CapitalsForCharacters, (s, v) => s with { CapitalsForCharacters = v }));
        fields.Add(Choice("Capitals when reading by &word or line", s => s.CapitalsForWords, (s, v) => s with { CapitalsForWords = v }));
        fields.Add(Choice("&Numbers", s => s.Numbers, (s, v) => s with { Numbers = v }));
        return new SettingsPage("Speech", fields);
    }

    /// <summary>A choice of every value of an enum setting, named readably ("WhileSpeaking": "While speaking").</summary>
    public static ChoiceField Choice<T>(string label, Func<VoxSettings, T> get, Func<VoxSettings, T, VoxSettings> set,
        Dictionary<T, string>? names = null) where T : struct, Enum =>
        new(label,
            Enum.GetValues<T>().Select(v => new SettingChoice(names?.GetValueOrDefault(v) ?? Readable(v.ToString()), v)).ToList(),
            s => get(s),
            (s, v) => v is T value ? set(s, value) : s);

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
