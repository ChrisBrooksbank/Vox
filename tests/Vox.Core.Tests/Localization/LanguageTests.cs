using Vox.Core.Configuration;
using Vox.Core.Localization;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Localization;

public class LanguageTests
{
    private static string Say(string text, string? utteranceLanguage, string voxLanguage, PunctuationLevel level = PunctuationLevel.All) =>
        new PunctuationRule(SymbolDictionary.ForLanguage, () => level, () => voxLanguage)
            .Apply(text, new Utterance(text, SpeechPriority.Normal) { Language = utteranceLanguage });

    [Fact]
    public void Symbols_AreNamedInVoxsLanguage()
    {
        Assert.Equal("a virgule b", Say("a,b", null, "fr"));
        Assert.Equal("a comma b", Say("a,b", null, "en"));
    }

    [Fact]
    public void Symbols_InPageText_AreNamedInThePagesLanguage() =>
        Assert.Equal("a Komma b", Say("a,b", "de-DE", "en"));

    [Fact]
    public void Symbols_KeepTheEnglishLevels() =>
        // A comma isn't spoken at Some in English, so not in French either
        Assert.Equal("a,b", Say("a,b", null, "fr", PunctuationLevel.Some));

    [Fact]
    public void Symbols_OnlyALanguageHas_AreAdded() =>
        Assert.Equal("你好 句号", Say("你好。", null, "zh"));

    [Fact]
    public void Repeats_AreCountedWithoutEnglishPlurals() =>
        Assert.Equal("a 4 tiret b", Say("a----b", null, "fr"));

    [Fact]
    public void UnknownLanguage_IsEnglish() =>
        Assert.Same(SymbolDictionary.LoadBuiltIn().GetType(), SymbolDictionary.ForLanguage("xx").GetType());

    [Fact]
    public void EveryTranslatedLanguage_HasSymbols()
    {
        foreach (var (code, _) in VoxLanguages.All.Where(l => l.Code != "en"))
            Assert.Contains(code, SymbolDictionary.Languages);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("fr", "fr")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("not a language!", null)]
    public void Culture_OfTheSetting(string? setting, string? culture) =>
        Assert.Equal(culture, VoxLanguages.Culture(setting)?.Name);

    [Theory]
    [InlineData("de-DE", "de-g2.ctb")]
    [InlineData("en-US", "en-us-g2.ctb")]
    [InlineData("en-GB", "en-ueb-g2.ctb")]
    [InlineData("ar-SA", "ar-ar-g1.utb")]
    public void BrailleTable_ForALanguage_IsOneVoxOffers(string culture, string table)
    {
        Assert.Equal(table, VoxLanguages.BrailleTableFor(System.Globalization.CultureInfo.GetCultureInfo(culture)));
        Assert.NotNull(Vox.Core.Braille.BrailleTables.Find(table));
    }

    [Fact]
    public void TheLanguageSetting_OffersWindowsAndEveryTranslation()
    {
        var field = SettingsPages.General().Fields.OfType<ChoiceField>().First(f => f.Choices.Any(c => c.Value is "fr"));

        Assert.Null(field.Choices[0].Value);
        Assert.Equal(VoxLanguages.All.Select(l => (object?)l.Code), field.Choices.Skip(1).Select(c => c.Value));
        Assert.Equal("de", field.Set(new VoxSettings(), "de").Language);
    }
}

public class TranslationTests
{
    private static string RepoPath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. parts]);

    private static Dictionary<string, string> Read(string file) =>
        System.Xml.Linq.XDocument.Load(RepoPath("src", "Vox.Core", "Localization", file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    public static TheoryData<string> Languages() => [.. VoxLanguages.All.Select(l => l.Code).Where(c => c != "en")];

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_HasOnlyEnglishNames_WithTheSamePlaceholders(string language)
    {
        var english = Read("Strings.resx");
        var translated = Read($"Strings.{language}.resx");

        Assert.NotEmpty(translated);
        foreach (var (name, value) in translated)
        {
            Assert.True(english.ContainsKey(name), $"{language}: {name} isn't an English string");
            var placeholders = System.Text.RegularExpressions.Regex.Matches(english[name], @"\{\d+\}").Select(m => m.Value).Order();
            Assert.Equal(placeholders, System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).Order());
        }
    }

    /// <summary>The resources in one culture, for the current async flow only (the global language is left alone).</summary>
    private sealed class CultureLocalizer(string culture) : Microsoft.Extensions.Localization.IStringLocalizer
    {
        private readonly System.Globalization.CultureInfo _culture = System.Globalization.CultureInfo.GetCultureInfo(culture);

        public Microsoft.Extensions.Localization.LocalizedString this[string name] =>
            new(name, VoxStrings.Resources.GetString(name, _culture) ?? name, VoxStrings.Resources.GetString(name, _culture) is null);

        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(_culture, this[name].Value, arguments));

        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    [Fact]
    public void Announcements_AreSpokenInTheLanguageSet()
    {
        using (VoxStrings.Use(new CultureLocalizer("fr")))
        {
            Assert.Equal("bouton", Vox.Core.Navigation.ControlTypeNames.ToSpoken("Button"));
            Assert.Equal("Fermer", VoxStrings.Get("Dialog.Close"));
            // Not translated yet: English
            Assert.Equal(VoxStrings.Resources.GetString("Wizard.RerunKey", System.Globalization.CultureInfo.InvariantCulture), VoxStrings.Get("Wizard.RerunKey"));
        }
        using (VoxStrings.Use(new CultureLocalizer("ja-JP")))
            Assert.Equal("ボタン", Vox.Core.Navigation.ControlTypeNames.ToSpoken("Button"));
    }
}
