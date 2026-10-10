using Vox.Core.Configuration;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public class SettingsPagesTests
{
    private static SpeechOptions Options(SpeechCapabilities capabilities = SpeechCapabilities.Pitch | SpeechCapabilities.Volume) => new(
        [new SpeechEngineDescriptor("OneCore", "OneCore", () => new RecordingSpeechEngine()),
         new SpeechEngineDescriptor("SAPI", "SAPI 5", () => new RecordingSpeechEngine())],
        ["Zira", "David"], 600, capabilities);

    private static T Field<T>(SettingsPage page, string label) where T : SettingField =>
        page.Fields.OfType<T>().Single(f => f.Label.Replace("&", "").StartsWith(label, StringComparison.Ordinal));

    private static readonly AudioOptions Audio = new(["Speakers", "Headphones"], ["default", "subtle"]);

    [Fact]
    public void ThePagesAreTheSpecsTabs_EachLabelUnique()
    {
        var pages = SettingsPages.All(Options(), Audio);

        Assert.Equal(["General", "Speech", "Verbosity", "Browse mode", "Document formatting", "Audio cues", "Keyboard", "Mouse", "Review"],
            pages.Select(p => p.Title));
        foreach (var page in pages)
            Assert.Equal(page.Fields.Count, page.Fields.Select(f => f.Label).Distinct().Count());
    }

    [Fact]
    public void EnumChoices_AreReadable_AndChangeTheSetting()
    {
        var verbosity = Field<ChoiceField>(SettingsPages.Verbosity(), "Verbosity");
        var settings = new VoxSettings { VerbosityLevel = VerbosityLevel.Beginner };

        Assert.Equal(["Beginner", "Intermediate", "Advanced"], verbosity.Choices.Select(c => c.Text));
        Assert.Equal(0, verbosity.IndexOf(settings));
        var changed = verbosity.Set(settings, verbosity.Choices[2].Value);
        Assert.Equal(VerbosityLevel.Advanced, changed.VerbosityLevel);
        Assert.Equal(2, verbosity.IndexOf(changed));
    }

    [Fact]
    public void ScreenReaderKey_UsesFriendlyNames()
    {
        var key = Field<ChoiceField>(SettingsPages.Keyboard(), "Screen reader key");

        Assert.Equal(["Insert", "Caps Lock"], key.Choices.Select(c => c.Text));
    }

    [Fact]
    public void ChangingTheSynthesizer_StartsWithItsDefaultVoice()
    {
        var engine = Field<ChoiceField>(SettingsPages.Speech(Options()), "Synthesizer");
        var settings = new VoxSettings { SpeechEngine = "OneCore", VoiceName = "Zira" };

        Assert.Equal(["Automatic (best available)", "OneCore", "SAPI 5"], engine.Choices.Select(c => c.Text));
        Assert.Equal(1, engine.IndexOf(settings));
        var changed = engine.Set(settings, "SAPI");
        Assert.Equal("SAPI", changed.SpeechEngine);
        Assert.Null(changed.VoiceName);
        // Choosing the same one again keeps the voice
        Assert.Equal("Zira", engine.Set(settings, "OneCore").VoiceName);
    }

    [Fact]
    public void Rate_IsLimitedToWhatTheSynthesizerCanDo()
    {
        var rate = Field<NumberField>(SettingsPages.Speech(Options()), "Rate");

        Assert.Equal(ISpeechEngine.MinSupportedWpm, rate.Min);
        Assert.Equal(600, rate.Max);
        Assert.Equal(320, rate.Set(new VoxSettings(), 320).SpeechRateWpm);
    }

    [Fact]
    public void PitchAndVolume_OnlyWhenTheSynthesizerHasThem()
    {
        var without = SettingsPages.Speech(Options(SpeechCapabilities.None));
        var with = SettingsPages.Speech(Options());

        Assert.DoesNotContain(without.Fields, f => f.Label.Contains("Pitch"));
        Assert.Contains(with.Fields, f => f.Label.Replace("&", "") == "Pitch");
        Assert.Contains(with.Fields, f => f.Label.Replace("&", "") == "Volume");
    }

    [Fact]
    public void Toggle_ChangesTheSetting()
    {
        var start = Field<ToggleField>(SettingsPages.General(), "Start Vox");

        Assert.True(start.Set(new VoxSettings(), true).StartAtLogon);
        Assert.False(start.Get(new VoxSettings()));
    }

    /// <summary>The properties a field changes: those that differ after setting it to another value.</summary>
    private static IEnumerable<string> PropertiesChangedBy(SettingField field)
    {
        var settings = new VoxSettings();
        var others = field switch
        {
            ToggleField toggle => [toggle.Set(settings, !toggle.Get(settings))],
            NumberField number => [number.Set(settings, number.Get(settings) == number.Max ? number.Min : number.Max)],
            ChoiceField choice => choice.Choices.Select(c => choice.Set(settings, c.Value)).ToList(),
            _ => new List<VoxSettings>(),
        };
        foreach (var property in typeof(VoxSettings).GetProperties())
        {
            if (others.Any(o => !Equals(property.GetValue(o), property.GetValue(settings))))
                yield return property.Name;
        }
    }

    [Fact]
    public void EverySettingIsOnAPage()
    {
        var shown = SettingsPages.All(Options(), Audio).SelectMany(p => p.Fields).SelectMany(PropertiesChangedBy).ToHashSet();
        var settable = typeof(VoxSettings).GetProperties().Where(p => p.SetMethod is not null).Select(p => p.Name);

        var missing = settable.Except(shown).Except(SettingsPages.NotOnAnyPage).ToList();
        Assert.True(missing.Count == 0, "Not on any settings page: " + string.Join(", ", missing));
    }

    [Fact]
    public void AudioChoices_ComeFromWhatIsInstalled()
    {
        var page = SettingsPages.AudioCues(Audio);

        Assert.Equal(["Default", "Speakers", "Headphones"], Field<ChoiceField>(page, "Output device").Choices.Select(c => c.Text));
        Assert.Equal("subtle", Field<ChoiceField>(page, "Sound scheme").Set(new VoxSettings(), "subtle").EarconScheme);
    }

    [Theory]
    [InlineData("WhileSpeaking", "While speaking")]
    [InlineData("Off", "Off")]
    [InlineData("PitchAndCap", "Pitch and cap")]
    public void Readable_SplitsWords(string name, string expected) =>
        Assert.Equal(expected, SettingsPages.Readable(name));
}
