using Vox.Core.Configuration;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class PunctuationRuleTests
{
    private static readonly SymbolDictionary Symbols = SymbolDictionary.LoadBuiltIn();

    private static string Speak(string text, PunctuationLevel level) =>
        new PunctuationRule(Symbols, () => level).Apply(text, new Utterance(text, SpeechPriority.Normal));

    [Theory]
    [InlineData(PunctuationLevel.None, "Tom Jerry cartoon, 50 off!")]
    [InlineData(PunctuationLevel.Some, "Tom and Jerry cartoon, 50 percent off!")]
    [InlineData(PunctuationLevel.Most, "Tom and Jerry left paren cartoon right paren, 50 percent off!")]
    [InlineData(PunctuationLevel.All, "Tom and Jerry left paren cartoon right paren comma 50 percent off bang")]
    public void EachLevel_SpeaksItsSymbols(PunctuationLevel level, string expected)
    {
        Assert.Equal(Collapse(expected), Speak("Tom & Jerry (cartoon), 50% off!", level));
    }

    [Theory]
    [InlineData(".", "dot")]
    [InlineData("&", "and")]
    [InlineData(" ( ", "left paren")]
    [InlineData("—", "em dash")]
    public void ASymbolOnItsOwn_IsAlwaysNamed(string text, string expected)
    {
        Assert.Equal(expected, Speak(text, PunctuationLevel.None));
    }

    [Fact]
    public void DotsAndCommasInNumbers_AreLeftAlone()
    {
        Assert.Equal("pi is 3.14 and a thousand is 1,000", Speak("pi is 3.14 and a thousand is 1,000", PunctuationLevel.All));
    }

    [Fact]
    public void PreservedSymbols_AreKeptForTheirPause_WhenNotSpoken()
    {
        Assert.Equal("Wait: what? Yes.", Speak("Wait: what? Yes.", PunctuationLevel.Some));
        Assert.Equal("Wait colon what? Yes.", Speak("Wait: what? Yes.", PunctuationLevel.Most));
    }

    [Fact]
    public void LongerSymbolsWin()
    {
        Assert.Equal("And then dot dot dot", Speak("And then…", PunctuationLevel.Most));
    }

    [Fact]
    public void TextWithoutSymbols_IsUnchanged()
    {
        const string text = "Nothing  to  see here";
        Assert.Same(text, Speak(text, PunctuationLevel.All));
    }

    [Fact]
    public void TheBuiltInDictionary_NamesEverySymbol()
    {
        Assert.True(Symbols.Entries.Count >= 50);
        Assert.All(Symbols.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
        Assert.True(Symbols.TryGet("&", out var ampersand));
        Assert.Equal(PunctuationLevel.Some, ampersand.Level);
    }

    private static string Collapse(string text) => System.Text.RegularExpressions.Regex.Replace(text, " {2,}", " ");
}
