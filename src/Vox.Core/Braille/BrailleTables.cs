using Vox.Core.Localization;

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
    public static IReadOnlyList<BrailleTable> All =>
    [
        Table(Contracted, true),
        Table("en-ueb-g1.ctb", false),
        Table(Uncontracted, false),
        Table("en-us-g2.ctb", true),
        Table("en-us-g1.ctb", false),
        Table("en-GB-g2.ctb", true),
        Table("de-g1.ctb", false),
        Table("de-g2.ctb", true),
        Table("fr-bfu-comp8.utb", false),
        Table("fr-bfu-g2.ctb", true),
        Table("es-g1.ctb", false),
        Table("es-g2.ctb", true),
        Table("it-it-comp6.utb", false),
        Table("nl-NL-g0.utb", false),
        Table("sv-g1.ctb", false),
        Table("da-dk-g16.ctb", false),
        Table("no-no-g1.ctb", false),
        Table("pl-pl-comp8.ctb", false),
        Table("pt-pt-g1.utb", false),
        Table("pt-pt-g2.ctb", true),
        Table("ar-ar-g1.utb", false),
        Table("ar-ar-g2.ctb", true),
        Table("zh-chn.ctb", false),
        Table("ja-kantenji.utb", false),
        Table("hi-in-g1.utb", false),
    ];

    // A table named by its BrailleTable.<id> string, in the language in use
    private static BrailleTable Table(string id, bool contracted) =>
        new(id, VoxStrings.GetOrDefault("BrailleTable." + id, id), contracted);

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
