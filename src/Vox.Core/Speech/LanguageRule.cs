using Vox.Core.Configuration;

namespace Vox.Core.Speech;

/// <summary>
/// Automatic language switching: an utterance tagged with a language other than the current
/// voice's (text read from a page in another language) is said in a voice for that language —
/// the one the user mapped to it in settings, else an installed voice for exactly that language
/// tag, else one for the same language in another region. Dialects of the current voice's own
/// language keep the current voice. Without a voice for the language, the current voice is kept.
/// </summary>
public sealed class LanguageRule : ITextRule
{
    // The voice list is read at most this often (it rarely changes, and reading it isn't free)
    private static readonly TimeSpan VoiceListLifetime = TimeSpan.FromSeconds(10);

    private readonly ISpeechEngine _engine;
    private readonly Func<VoxSettings> _settings;
    private readonly Func<DateTimeOffset> _clock;
    private IReadOnlyList<SpeechVoice>? _voices;
    private DateTimeOffset _voicesRead;

    public LanguageRule(ISpeechEngine engine, Func<VoxSettings> settings, Func<DateTimeOffset>? clock = null)
    {
        _engine = engine;
        _settings = settings;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Apply(string text, Utterance utterance) => text;

    public Utterance ApplyTo(Utterance utterance)
    {
        if (string.IsNullOrEmpty(utterance.Language) || utterance.Voice is not null)
            return utterance;
        var settings = _settings();
        if (!settings.AutoLanguageSwitching)
            return utterance;

        var voices = Voices();
        var current = _engine.CurrentVoice;
        var currentLanguage = voices.FirstOrDefault(v => v.Name == current)?.Language;
        if (currentLanguage is not null && SameLanguage(currentLanguage, utterance.Language))
            return utterance;

        var voice = VoiceFor(utterance.Language, voices, settings.LanguageVoices);
        return voice is null || voice == current ? utterance : utterance with { Voice = voice };
    }

    /// <summary>The voice for <paramref name="language"/>, or null when none is installed.</summary>
    public static string? VoiceFor(string language, IReadOnlyList<SpeechVoice> voices, IReadOnlyDictionary<string, string>? mapping)
    {
        var primary = Primary(language);
        if (mapping is not null)
        {
            foreach (var key in new[] { language, primary })
            {
                if (mapping.TryGetValue(key, out var mapped) && voices.Any(v => v.Name == mapped))
                    return mapped;
            }
        }
        return voices.FirstOrDefault(v => string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase))?.Name
            ?? voices.FirstOrDefault(v => v.Language.Length > 0 && SameLanguage(v.Language, language))?.Name;
    }

    private static bool SameLanguage(string a, string b) => string.Equals(Primary(a), Primary(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The language without its region or script ("fr-CA" → "fr").</summary>
    private static string Primary(string tag)
    {
        int dash = tag.IndexOfAny(['-', '_']);
        return dash < 0 ? tag : tag[..dash];
    }

    private IReadOnlyList<SpeechVoice> Voices()
    {
        var now = _clock();
        if (_voices is null || now - _voicesRead > VoiceListLifetime)
        {
            _voices = _engine.GetVoiceDetails();
            _voicesRead = now;
        }
        return _voices;
    }
}
