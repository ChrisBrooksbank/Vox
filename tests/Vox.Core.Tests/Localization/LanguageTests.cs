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
