using System.Text;
using Vox.Core.Configuration;

namespace Vox.Core.Speech;

/// <summary>
/// Indicates capital letters, with separate settings for reading a character and for a word or
/// line. A lone capital letter (reading by character) is said at a higher pitch, after "cap", or
/// after a beep. A single word with capitals gets the word setting the same way ("all caps" for
/// a word in capitals); within a line only "cap" can be indicated, before each such word.
/// </summary>
public sealed class CapitalsRule : ITextRule
{
    /// <summary>How much higher (on the 0–100 pitch scale) a capital is said.</summary>
    public const int PitchRaise = 30;

    private readonly Func<VoxSettings> _settings;

    public CapitalsRule(Func<VoxSettings> settings)
    {
        _settings = settings;
    }

    public string Apply(string text, Utterance utterance) => ApplyTo(utterance).Text;

    public Utterance ApplyTo(Utterance utterance)
    {
        var text = utterance.Text.Trim();
        if (text.Length == 0)
            return utterance;
        var settings = _settings();

        if (IsSingleCharacter(text))
            return char.IsUpper(text, 0) ? Indicate(utterance, text, settings.CapitalsForCharacters, allCaps: false) : utterance;

        var indication = settings.CapitalsForWords;
        if (indication == CapitalIndication.Off)
            return utterance;
        if (!text.Any(char.IsWhiteSpace))
            return HasCapital(text) ? Indicate(utterance, text, indication, IsAllCaps(text)) : utterance;
        return indication == CapitalIndication.SayCap ? utterance with { Text = SayCapBeforeWords(utterance.Text) } : utterance;
    }

    private static Utterance Indicate(Utterance utterance, string text, CapitalIndication indication, bool allCaps) => indication switch
    {
        CapitalIndication.Pitch => utterance with { PitchOffset = utterance.PitchOffset + PitchRaise },
        CapitalIndication.SayCap => utterance with { Text = (allCaps ? "all caps " : "cap ") + text },
        CapitalIndication.Beep => utterance with { SoundCue = Utterance.CapitalCue },
        _ => utterance,
    };

    /// <summary>One character (a surrogate pair counts as one).</summary>
    private static bool IsSingleCharacter(string text) =>
        text.Length == 1 || (text.Length == 2 && char.IsSurrogatePair(text[0], text[1]));

    private static bool HasCapital(string word) => word.Any(char.IsUpper);

    /// <summary>Two or more letters, all capitals.</summary>
    private static bool IsAllCaps(string word) => word.Count(char.IsLetter) > 1 && !word.Any(char.IsLower);

    private static string SayCapBeforeWords(string text)
    {
        var result = new StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                result.Append(text[i++]);
                continue;
            }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;
            var word = text[start..i];
            if (HasCapital(word))
                result.Append(IsAllCaps(word) ? "all caps " : "cap ");
            result.Append(word);
        }
        return result.ToString();
    }
}
