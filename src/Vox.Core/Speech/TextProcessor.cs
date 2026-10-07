using Microsoft.Extensions.Logging;

namespace Vox.Core.Speech;

/// <summary>
/// One step of text processing before speech (symbols, dictionaries, numbers, ...). Rules run on
/// the speech queue's thread for every utterance, so they must be quick, and must never log the
/// text (it can be typed characters or field values).
/// </summary>
public interface ITextRule
{
    /// <summary>The text to speak instead of <paramref name="text"/> (part of <paramref name="utterance"/>).</summary>
    string Apply(string text, Utterance utterance);

    /// <summary>
    /// The utterance to speak instead of <paramref name="utterance"/>. By default only its text
    /// changes; a rule can also change how it is said (pitch, a cue before it).
    /// </summary>
    Utterance ApplyTo(Utterance utterance)
    {
        var text = Apply(utterance.Text, utterance) ?? utterance.Text;
        return text == utterance.Text ? utterance : utterance with { Text = text };
    }
}

/// <summary>
/// The chain of <see cref="ITextRule"/>s every utterance goes through on its way to the speech
/// engine, in order. A rule that throws is skipped for that utterance, so a broken rule can't
/// silence Vox. With no rules the text is spoken as it is.
/// </summary>
public sealed class TextProcessor
{
    /// <summary>A processor with no rules.</summary>
    public static readonly TextProcessor None = new([]);

    private readonly IReadOnlyList<ITextRule> _rules;
    private readonly ILogger? _logger;

    public TextProcessor(IEnumerable<ITextRule> rules, ILogger<TextProcessor>? logger = null)
    {
        _rules = rules.ToList();
        _logger = logger;
    }

    public IReadOnlyList<ITextRule> Rules => _rules;

    /// <summary>What to speak for <paramref name="utterance"/>.</summary>
    public Utterance Process(Utterance utterance)
    {
        foreach (var rule in _rules)
        {
            try
            {
                utterance = rule.ApplyTo(utterance);
            }
            catch (Exception ex)
            {
                // The rule's type, never the text
                _logger?.LogWarning(ex, "Text rule {Rule} failed; skipped", rule.GetType().Name);
            }
        }
        return utterance;
    }
}
