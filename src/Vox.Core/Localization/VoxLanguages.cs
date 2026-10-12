using System.Globalization;

namespace Vox.Core.Localization;

/// <summary>The languages Vox's own strings are translated into, and the Language setting's culture.</summary>
public static class VoxLanguages
{
    /// <summary>Each language's code and its own name (as people who speak it look for it).</summary>
    public static readonly IReadOnlyList<(string Code, string NativeName)> All =
    [
        ("en", "English"),
        ("es", "Español"),
        ("fr", "Français"),
        ("de", "Deutsch"),
        ("pt", "Português"),
        ("zh", "中文"),
        ("ja", "日本語"),
        ("ar", "العربية"),
        ("hi", "हिन्दी"),
    ];

    /// <summary>
    /// The culture for the Language setting: null (follow Windows) for null, empty or a name .NET
    /// doesn't know.
    /// </summary>
    public static CultureInfo? Culture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;
        try
        {
            return CultureInfo.GetCultureInfo(language.Trim());
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The braille table people reading <paramref name="culture"/> most likely want (contracted where liblouis has it).</summary>
    public static string BrailleTableFor(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "de" => "de-g2.ctb",
        "fr" => "fr-bfu-g2.ctb",
        "es" => "es-g2.ctb",
        "it" => "it-it-comp6.utb",
        "nl" => "nl-NL-g0.utb",
        "sv" => "sv-g1.ctb",
        "da" => "da-dk-g16.ctb",
        "nb" or "nn" or "no" => "no-no-g1.ctb",
        "pl" => "pl-pl-comp8.ctb",
        "pt" => "pt-pt-g2.ctb",
        "ar" => "ar-ar-g1.utb",
        "zh" => "zh-chn.ctb",
        "ja" => "ja-kantenji.utb",
        "hi" => "hi-in-g1.utb",
        _ => culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "en-us-g2.ctb" : Braille.BrailleTables.Contracted,
    };
}
