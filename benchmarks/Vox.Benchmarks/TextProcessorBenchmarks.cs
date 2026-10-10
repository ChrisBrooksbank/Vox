using BenchmarkDotNet.Attributes;
using Vox.Core.Configuration;
using Vox.Core.Speech;

namespace Vox.Benchmarks;

/// <summary>The text rules every utterance goes through before the synthesizer.</summary>
public class TextProcessorBenchmarks
{
    private TextProcessor _processor = null!;

    private static readonly Utterance Sentence = new(
        "On 12/03/2024 Dr. Smith paid $1,234.56 (incl. VAT) for 3 items -- see https://example.com/orders?id=42 ... ★★★★ 😀",
        SpeechPriority.Normal);

    private static readonly Utterance Paragraph = new(string.Join(' ', Enumerable.Repeat(
        "The quick brown fox jumps over the lazy dog; 42 times, at 3:15pm, with 100% effort!", 20)), SpeechPriority.Normal);

    [Params(PunctuationLevel.Some, PunctuationLevel.All)]
    public PunctuationLevel Punctuation { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var settings = new VoxSettings { PunctuationLevel = Punctuation };
        _processor = new TextProcessor(
        [
            new CapitalsRule(() => settings),
            new PronunciationRule(PronunciationRule.LoadBuiltInDefault(), directory: null, () => null),
            new PunctuationRule(SymbolDictionary.LoadBuiltIn(), () => settings.PunctuationLevel),
            new NumbersRule(() => settings.Numbers),
        ]);
    }

    [Benchmark]
    public string ProcessSentence() => _processor.Process(Sentence).Text;

    [Benchmark]
    public string ProcessParagraph() => _processor.Process(Paragraph).Text;
}
