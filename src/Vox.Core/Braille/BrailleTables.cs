namespace Vox.Core.Braille;

/// <summary>A liblouis table Vox offers in settings: its file name and the name shown to the user.</summary>
/// <param name="Id">The liblouis table file name, as in "en-ueb-g2.ctb".</param>
/// <param name="DisplayName">The name shown in settings.</param>
/// <param name="Contracted">True for contracted (grade 2) braille, where a cell can stand for several characters.</param>
public sealed record BrailleTable(string Id, string DisplayName, bool Contracted);

/// <summary>
/// The liblouis translation tables Vox offers. Without liblouis, Vox writes US computer braille
/// (<see cref="ComputerBrailleTranslator"/>) whatever the table.
/// </summary>
public static class BrailleTables
{
    /// <summary>US computer braille, 8-dot: one cell per character.</summary>
    public const string Uncontracted = "en-us-comp8.ctb";

    /// <summary>Unified English Braille, grade 2: the default.</summary>
    public const string Contracted = "en-ueb-g2.ctb";

    /// <summary>The tables offered in settings, the default first.</summary>
    public static IReadOnlyList<BrailleTable> All { get; } =
    [
        new(Contracted, "English, Unified English Braille, grade 2", true),
        new("en-ueb-g1.ctb", "English, Unified English Braille, grade 1", false),
        new(Uncontracted, "English, US computer braille, 8-dot", false),
        new("en-us-g2.ctb", "English, US English Braille (EBAE), grade 2", true),
        new("en-us-g1.ctb", "English, US English Braille (EBAE), grade 1", false),
        new("en-GB-g2.ctb", "English, UK, grade 2", true),
        new("de-g1.ctb", "German, grade 1", false),
        new("de-g2.ctb", "German, grade 2", true),
        new("fr-bfu-comp8.utb", "French, uncontracted, 8-dot", false),
        new("fr-bfu-g2.ctb", "French, grade 2", true),
        new("es-g1.ctb", "Spanish, grade 1", false),
        new("es-g2.ctb", "Spanish, grade 2", true),
        new("it-it-comp6.utb", "Italian, 6-dot", false),
        new("nl-NL-g0.utb", "Dutch, uncontracted", false),
        new("sv-g1.ctb", "Swedish, grade 1", false),
        new("da-dk-g16.ctb", "Danish, 6-dot, uncontracted", false),
        new("no-no-g1.ctb", "Norwegian, grade 1", false),
        new("pl-pl-comp8.ctb", "Polish, 8-dot computer braille", false),
    ];

    /// <summary>The offered table with this id (case-insensitive), or null.</summary>
    public static BrailleTable? Find(string table) =>
        All.FirstOrDefault(t => string.Equals(t.Id, table, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when <paramref name="table"/> is contracted braille: an offered contracted table, or
    /// another table whose name says grade 2 ("-g2").
    /// </summary>
    public static bool IsContracted(string table) =>
        Find(table)?.Contracted ?? table.Contains("-g2", StringComparison.OrdinalIgnoreCase);
}
