namespace Vox.Core.Switching;

/// <summary>
/// Finds the installed voice that is the one another screen reader names by its own id: NVDA keeps
/// a OneCore or SAPI voice as its registry token ("HKEY_LOCAL_MACHINE\...\Tokens\MSTTS_V110_enUS_DavidM",
/// "TTS_MS_EN-US_ZIRA_11.0") where Vox has the voice's name ("Microsoft David", "Microsoft Zira Desktop").
/// </summary>
public static class VoiceMatcher
{
    // Words in voice names and tokens that don't tell one voice from another
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "desktop", "mobile", "mstts", "tts", "ms", "voice", "english", "united", "states",
        "kingdom", "hkey", "local", "machine", "software", "speech", "onecore", "voices", "tokens",
    };

    private static readonly char[] Separators = [' ', '\\', '/', '_', '-', '.', '(', ')', ',', '+'];

    /// <summary>The installed voice <paramref name="voiceId"/> names, or null when none is.</summary>
    public static string? Match(string? voiceId, IReadOnlyList<string> voices)
    {
        if (string.IsNullOrWhiteSpace(voiceId) || voices.Count == 0)
            return null;
        string id = voiceId.Trim();
        string last = id[(id.LastIndexOfAny(['\\', '/']) + 1)..];

        // The same name (or the token's last part) first
        var exact = voices.FirstOrDefault(v =>
            string.Equals(v, id, StringComparison.OrdinalIgnoreCase) || string.Equals(v, last, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var names = NameWords(last);
        if (names.Count == 0)
            return null;
        return voices.FirstOrDefault(v => NameWords(v).Overlaps(names));
    }

    // The words that name a voice: "MSTTS_V110_enUS_DavidM" → David; "Microsoft Zira Desktop" → Zira
    private static HashSet<string> NameWords(string text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 3 || !part.All(char.IsLetter) || Generic.Contains(part) || IsLanguageCode(part))
                continue;
            words.Add(part);
            // OneCore tokens end a name with M: "DavidM"
            if (part.Length > 3 && part[^1] == 'M' && char.IsLower(part[^2]))
                words.Add(part[..^1]);
        }
        return words;
    }

    // "enUS": a language and region run together
    private static bool IsLanguageCode(string part) =>
        part.Length == 4 && char.IsLower(part[0]) && char.IsLower(part[1]) && char.IsUpper(part[2]) && char.IsUpper(part[3]);
}
