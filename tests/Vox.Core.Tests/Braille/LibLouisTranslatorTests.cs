using Vox.Core.Braille;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Braille;

/// <summary>
/// The liblouis translator. The integration tests (named "WhenInstalled") find liblouis in
/// VOX_LIBLOUIS_DIR, the component folder or the usual Linux library folders, and pass without
/// checking anything when it is not installed (xunit 2 has no runtime skip).
/// </summary>
public class LibLouisTranslatorTests
{
    private static readonly Lazy<string?> LibraryDirectory = new(FindLibraryDirectory);

    [Fact]
    public void TryLoad_MissingDirectory_ReturnsNull()
    {
        var logger = new CapturingLogger<LibLouisTranslatorTests>();

        var translator = LibLouisTranslator.TryLoad(Path.Combine(Path.GetTempPath(), "vox-no-liblouis-" + Guid.NewGuid()), BrailleTables.Contracted, logger);

        Assert.Null(translator);
        Assert.NotEmpty(logger.Entries);
    }

    [Fact]
    public void TryLoad_DirectoryWithoutLibrary_ReturnsNull()
    {
        var directory = Directory.CreateTempSubdirectory("vox-liblouis-").FullName;
        try
        {
            Assert.Null(LibLouisTranslator.TryLoad(directory, BrailleTables.Contracted));
            Assert.False(LibLouisTranslator.IsInstalled(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Factory_FallsBackToComputerBraille()
    {
        var translator = BrailleTranslatorFactory.Create(BrailleTables.Contracted, Path.Combine(Path.GetTempPath(), "vox-no-liblouis-" + Guid.NewGuid()), null);

        Assert.IsType<ComputerBrailleTranslator>(translator);
    }

    [Fact]
    public void BrailleTables_KnowContractedTables()
    {
        Assert.True(BrailleTables.IsContracted(BrailleTables.Contracted));
        Assert.False(BrailleTables.IsContracted(BrailleTables.Uncontracted));
        Assert.True(BrailleTables.IsContracted("xx-g2.ctb"));
        Assert.Equal(BrailleTables.Contracted, BrailleTables.All[0].Id);
        Assert.Equal(BrailleTables.All.Count, BrailleTables.All.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void TryLoad_UnknownTable_ReturnsNull_WhenInstalled()
    {
        if (LibraryDirectory.Value is not { } directory)
            return;

        Assert.Null(LibLouisTranslator.TryLoad(directory, "no-such-table.ctb"));
    }

    [Fact]
    public void Factory_UsesLibLouis_WhenInstalled()
    {
        if (LibraryDirectory.Value is not { } directory)
            return;

        Assert.IsType<LibLouisTranslator>(BrailleTranslatorFactory.Create(BrailleTables.Contracted, directory, null));
    }

    [Fact]
    public void ContractedTranslation_MapsCharactersAndCells_WhenInstalled()
    {
        if (Load(BrailleTables.Contracted) is not { } translator)
            return;

        var translation = translator.Translate("the cat", cursorOffset: 4);

        // "the" is one cell (dots 2346), then a space and c a t
        Assert.Equal("⠮⠀⠉⠁⠞", BrailleDots.ToUnicode(translation.Cells));
        Assert.Equal(new[] { 0, 0, 0, 1, 2, 3, 4 }, translation.CellForChar);
        Assert.Equal(new[] { 0, 3, 4, 5, 6 }, translation.CharForCell);
        Assert.Equal(2, translation.CursorCell);
    }

    [Fact]
    public void CapitalSign_AddsACell_WhenInstalled()
    {
        if (Load(BrailleTables.Contracted) is not { } translator)
            return;

        var translation = translator.Translate("Hi", cursorOffset: 2);

        // Capital sign (dot 6), h, i
        Assert.Equal("⠠⠓⠊", BrailleDots.ToUnicode(translation.Cells));
        Assert.Equal(new[] { 0, 2 }, translation.CellForChar);
        Assert.Equal(new[] { 0, 0, 1 }, translation.CharForCell);
        Assert.Equal(3, translation.CursorCell);
    }

    [Fact]
    public void LongText_GrowsTheOutputBuffer_WhenInstalled()
    {
        if (Load(BrailleTables.Contracted) is not { } translator)
            return;

        // Every character needs a capital or number sign: output about twice the input
        var text = string.Concat(Enumerable.Repeat("A1 ", 200));

        var translation = translator.Translate(text);

        Assert.True(translation.Cells.Length > text.Length);
        Assert.Equal(text.Length, translation.CellForChar.Length);
        Assert.Equal(translation.Cells.Length, translation.CharForCell.Length);
        Assert.All(translation.CharForCell, offset => Assert.InRange(offset, 0, text.Length - 1));
    }

    [Fact]
    public void SurrogatePairs_MapBothHalvesToTheSameCell_WhenInstalled()
    {
        if (Load(BrailleTables.Uncontracted) is not { } translator)
            return;

        var text = "a\U0001F600b";
        var translation = translator.Translate(text, cursorOffset: 3);

        Assert.Equal(text.Length, translation.CellForChar.Length);
        Assert.Equal(translation.CellForChar[1], translation.CellForChar[2]);
        Assert.Equal(translation.CellForChar[3], translation.CursorCell);
        Assert.Equal(3, translation.CharForCell[^1]);
    }

    [Fact]
    public void BackTranslate_ExpandsContractions_WhenInstalled()
    {
        if (Load(BrailleTables.Contracted) is not { } translator)
            return;

        Assert.Equal("the cat", translator.BackTranslate(BrailleDots.FromUnicode("⠮⠀⠉⠁⠞")));
    }

    [Fact]
    public void ExpandWordAtCursor_WritesTheWordUncontracted_WhenInstalled()
    {
        if (LibraryDirectory.Value is not { } directory
            || LibLouisTranslator.TryLoad(directory, BrailleTables.Contracted, expandWordAtCursor: true) is not { } translator)
            return;

        var translation = translator.Translate("the cat", cursorOffset: 1);

        Assert.Equal("⠞⠓⠑⠀⠉⠁⠞", BrailleDots.ToUnicode(translation.Cells));
        Assert.Equal(1, translation.CursorCell);
    }

    [Fact]
    public void ComputerBraille_MatchesLibLouisComp8_WhenInstalled()
    {
        if (Load(BrailleTables.Uncontracted) is not { } translator)
            return;

        var ascii = new string(Enumerable.Range(0x20, 0x5F).Select(c => (char)c).ToArray());

        var expected = new ComputerBrailleTranslator().Translate(ascii).Cells;
        var actual = translator.Translate(ascii).Cells;

        Assert.Equal(BrailleDots.ToUnicode(expected), BrailleDots.ToUnicode(actual));
        Assert.Equal(ascii, translator.BackTranslate(actual));
    }

    [Fact]
    public void Translate_IsThreadSafe_WhenInstalled()
    {
        if (Load(BrailleTables.Contracted) is not { } translator)
            return;

        var results = Enumerable.Range(0, 200).AsParallel()
            .Select(_ => BrailleDots.ToUnicode(translator.Translate("the cat").Cells))
            .Distinct()
            .ToList();

        Assert.Equal("⠮⠀⠉⠁⠞", Assert.Single(results));
    }

    private static LibLouisTranslator? Load(string table) =>
        LibraryDirectory.Value is { } directory ? LibLouisTranslator.TryLoad(directory, table) : null;

    private static string? FindLibraryDirectory()
    {
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("VOX_LIBLOUIS_DIR"),
            LibLouisTranslator.DefaultDirectory,
            "/usr/lib/x86_64-linux-gnu",
            "/usr/lib/aarch64-linux-gnu",
            "/usr/lib",
            "/usr/local/lib",
        ];
        return candidates.FirstOrDefault(directory =>
            !string.IsNullOrEmpty(directory) && LibLouisTranslator.TryLoad(directory, BrailleTables.Contracted) is not null);
    }
}
