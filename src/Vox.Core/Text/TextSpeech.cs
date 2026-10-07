using Vox.Core.Input;

namespace Vox.Core.Text;

/// <summary>How pieces of text are spoken when reading by unit.</summary>
public static class TextSpeech
{
    /// <summary>What to say for a text unit read at the caret: "blank" for nothing or only a line break.</summary>
    public static string ForUnit(string text, TextUnit unit)
    {
        if (unit == TextUnit.Character)
            return ForCharacter(text);

        var trimmed = text.TrimEnd('\r', '\n');
        if (unit == TextUnit.Word)
            trimmed = trimmed.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? (trimmed.Length > 0 && unit == TextUnit.Word ? "space" : "blank") : trimmed;
    }

    /// <summary>A single character's spoken name ("Space", "comma"); a line break or nothing is "blank".</summary>
    public static string ForCharacter(string text)
    {
        if (text.Length == 0 || text is "\r\n" or "\n" or "\r")
            return "blank";
        if (text.Length == 1)
            return TypingEchoHandler.GetCharacterName(text[0]);
        return text; // a surrogate pair
    }
}
