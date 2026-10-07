using Vox.Core.Input;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class SpellingTests
{
    [Fact]
    public void Spell_SaysEachCharacter_WithCapsAndSymbolNames() =>
        Assert.Equal("cap h, i, Space, 2, comma", Spelling.Spell("Hi 2,", phonetic: false).Replace("Comma", "comma"));

    [Fact]
    public void Phonetic_UsesTheNatoAlphabetForLetters() =>
        Assert.Equal("cap Victor, Oscar, X-ray, 9", Spelling.Spell("Vox9", phonetic: true));

    [Fact]
    public void Spell_DropsTheLineBreakAndSaysBlankForNothing()
    {
        Assert.Equal("o, k", Spelling.Spell("ok\r\n", phonetic: false));
        Assert.Equal("blank", Spelling.Spell("\n", phonetic: true));
    }

    [Fact]
    public void Spell_KeepsSurrogatePairsTogether() =>
        Assert.Equal("a, 😀", Spelling.Spell("a😀", phonetic: false));

    [Fact]
    public void Spell_IsCappedForVeryLongText() =>
        Assert.Equal(Spelling.MaxLength, Spelling.Spell(new string('a', 2000), phonetic: false).Split(", ").Length);

    [Theory]
    [InlineData(1, TextUnit.Word, SpellMode.None)]
    [InlineData(2, TextUnit.Word, SpellMode.Spell)]
    [InlineData(3, TextUnit.Line, SpellMode.Phonetic)]
    [InlineData(2, TextUnit.Character, SpellMode.Phonetic)]
    public void ForPress(int presses, TextUnit unit, SpellMode expected) =>
        Assert.Equal(expected, Spelling.ForPress(presses, unit));
}

public class RepeatPressCounterTests
{
    private long _now;
    private RepeatPressCounter Counter() => new(TimeSpan.FromMilliseconds(500), () => _now);

    [Fact]
    public void QuickPressesOfTheSameKey_Count()
    {
        var counter = Counter();

        Assert.Equal(1, counter.Press(1));
        _now += 200;
        Assert.Equal(2, counter.Press(1));
        _now += 200;
        Assert.Equal(3, counter.Press(1));
        _now += 200;
        Assert.Equal(1, counter.Press(1)); // starts again after three
    }

    [Fact]
    public void APause_StartsAgain()
    {
        var counter = Counter();

        counter.Press(1);
        _now += 600;

        Assert.Equal(1, counter.Press(1));
    }

    [Fact]
    public void AnotherKey_StartsAgain()
    {
        var counter = Counter();

        counter.Press(1);
        Assert.Equal(1, counter.Press(2));
        Assert.Equal(2, counter.Press(2));
    }
}
