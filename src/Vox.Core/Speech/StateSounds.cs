using System.Text;

namespace Vox.Core.Speech;

/// <summary>
/// "States as sound only": announcements mark checked, expanded and selected states with
/// <see cref="Mark"/> (invisible delimiters around the cue and its word), and
/// <see cref="StateSoundsRule"/> turns the first mark of an utterance into its sound cue and any
/// others back into words (an utterance plays one cue).
/// </summary>
public static class StateSounds
{
    // Private-use characters: never in page text, and not symbols the punctuation rule names
    internal const char Start = '';
    internal const char Separator = '';
    internal const char End = '';

    public const string Checked = "checked";
    public const string NotChecked = "not_checked";
    public const string HalfChecked = "half_checked";
    public const string Expanded = "expanded";
    public const string Collapsed = "collapsed";
    public const string Selected = "selected";

    /// <summary>The cues for states, all of which the default earcon scheme has.</summary>
    public static readonly IReadOnlyList<string> Cues = [Checked, NotChecked, HalfChecked, Expanded, Collapsed, Selected];

    /// <summary>The state <paramref name="word"/>, to be played as <paramref name="cue"/>.</summary>
    public static string Mark(string cue, string word) => $"{Start}{cue}{Separator}{word}{End}";
}

/// <summary>
/// Turns the first state mark (<see cref="StateSounds.Mark"/>) in an utterance into its
/// <see cref="Utterance.SoundCue"/>, dropping the word and the comma before it; later marks (or
/// all of them, when the utterance already has a cue) are spoken as their words. Runs first, so
/// no other rule sees the marks.
/// </summary>
public sealed class StateSoundsRule : ITextRule
{
    public string Apply(string text, Utterance utterance) => ApplyTo(utterance).Text;

    public Utterance ApplyTo(Utterance utterance)
    {
        var text = utterance.Text;
        if (text.IndexOf(StateSounds.Start) < 0)
            return utterance;

        string? cue = utterance.SoundCue;
        var result = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int start = text.IndexOf(StateSounds.Start, i);
            int separator = start < 0 ? -1 : text.IndexOf(StateSounds.Separator, start);
            int end = separator < 0 ? -1 : text.IndexOf(StateSounds.End, separator);
            if (end < 0)
            {
                result.Append(text, i, text.Length - i);
                break;
            }
            result.Append(text, i, start - i);
            var markCue = text[(start + 1)..separator];
            var word = text[(separator + 1)..end];
            if (cue is null)
            {
                cue = markCue;
                TrimSeparator(result);
            }
            else
            {
                result.Append(word);
            }
            i = end + 1;
        }
        // Stray delimiters (a mark cut short) must not reach the synthesizer
        var cleaned = result.ToString().Replace(StateSounds.Start.ToString(), "")
            .Replace(StateSounds.Separator.ToString(), "").Replace(StateSounds.End.ToString(), "");
        if (cleaned.StartsWith(", ", StringComparison.Ordinal))
            cleaned = cleaned[2..];
        return utterance with { Text = cleaned.Trim(), SoundCue = cue };
    }

    /// <summary>Removes the ", " left before a state that became a sound.</summary>
    private static void TrimSeparator(StringBuilder text)
    {
        if (text.Length >= 2 && text[^2] == ',' && text[^1] == ' ')
            text.Length -= 2;
    }
}
