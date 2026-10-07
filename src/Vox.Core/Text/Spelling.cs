using System.Globalization;
using Vox.Core.Input;

namespace Vox.Core.Text;

/// <summary>How a read command's text is said: as text, spelled, or spelled phonetically.</summary>
public enum SpellMode
{
    None,
    Spell,
    Phonetic,
}

/// <summary>Spelling text out character by character, plainly or with the NATO alphabet.</summary>
public static class Spelling
{
    /// <summary>Longest text spelled (a long line would take minutes).</summary>
    public const int MaxLength = 500;

    private static readonly string[] Nato =
    [
        "Alfa", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliett",
        "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango",
        "Uniform", "Victor", "Whiskey", "X-ray", "Yankee", "Zulu",
    ];

    /// <summary>
    /// What the <paramref name="pressCount"/>th quick press of a read command does: once reads,
    /// twice spells, three times spells phonetically. A character is already "spelled" when read,
    /// so pressing twice gives it phonetically.
    /// </summary>
    public static SpellMode ForPress(int pressCount, TextUnit unit) => pressCount switch
    {
        <= 1 => SpellMode.None,
        2 when unit != TextUnit.Character => SpellMode.Spell,
        _ => SpellMode.Phonetic,
    };

    /// <summary>
    /// The text spelled out ("cap H, e, l, l, o"); phonetically, letters use the NATO alphabet
    /// ("cap Hotel, Echo, ..."). Line breaks at the end are dropped; nothing is "blank".
    /// </summary>
    public static string Spell(string text, bool phonetic)
    {
        text = text.TrimEnd('\r', '\n');
        if (text.Length > MaxLength)
            text = text[..MaxLength];
        if (text.Length == 0)
            return "blank";

        var parts = new List<string>();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
            parts.Add(SpellElement(elements.GetTextElement(), phonetic));
        return string.Join(", ", parts);
    }

    /// <summary>Speech for <paramref name="text"/> in <paramref name="mode"/> (as text for <see cref="SpellMode.None"/>).</summary>
    public static string Say(string text, TextUnit unit, SpellMode mode)
    {
        if (mode == SpellMode.None)
            return TextSpeech.ForUnit(text, unit);
        // A word unit includes the spaces after it, which aren't part of the word
        if (unit == TextUnit.Word && text.Trim().Length > 0)
            text = text.Trim();
        return Spell(text, phonetic: mode == SpellMode.Phonetic);
    }

    private static string SpellElement(string element, bool phonetic)
    {
        if (element.Length != 1)
            return element;
        char ch = element[0];
        bool isUpper = char.IsUpper(ch);
        string name;
        if (phonetic && char.ToLowerInvariant(ch) is >= 'a' and <= 'z' and var lower)
            name = Nato[lower - 'a'];
        else if (char.IsLetter(ch))
            name = char.ToLowerInvariant(ch).ToString();
        else
            name = TypingEchoHandler.GetCharacterName(ch);
        return isUpper ? "cap " + name : name;
    }
}
