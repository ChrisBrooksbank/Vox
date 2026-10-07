using System.Text.RegularExpressions;
using Vox.Core.Configuration;

namespace Vox.Core.Speech;

/// <summary>With the digits setting, numbers are read digit by digit ("1200" → "1 2 0 0").</summary>
public sealed class NumbersRule : ITextRule
{
    // A digit followed by another digit (any script's decimal digits)
    private static readonly Regex DigitBeforeDigit = new(@"\d(?=\d)", RegexOptions.Compiled);

    private readonly Func<NumberReading> _reading;

    public NumbersRule(Func<NumberReading> reading)
    {
        _reading = reading;
    }

    public string Apply(string text, Utterance utterance) =>
        _reading() == NumberReading.Digits ? DigitBeforeDigit.Replace(text, "$0 ") : text;
}
