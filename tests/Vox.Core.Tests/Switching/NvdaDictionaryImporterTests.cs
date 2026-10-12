using Vox.Core.Speech;
using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class NvdaDictionaryImporterTests
{
    private static IReadOnlyList<ImportedEntry> ParseSample(out ImportReport report)
    {
        report = new ImportReport(ImportSource.Nvda);
        var path = Path.Combine(AppContext.BaseDirectory, "Switching", "Samples", "nvda-folder", "speechDicts", "default.dic");
        return NvdaDictionaryImporter.Parse(File.ReadAllText(path), "default.dic", report);
    }

    [Fact]
    public void Sample_ReadsEntriesWithTypeAndCase()
    {
        var entries = ParseSample(out _).Select(e => e.Entry).ToList();

        Assert.Equal(new PronunciationEntry("Vox", "vocks", PronunciationMatch.Word, true), entries[0]);
        Assert.Equal(new PronunciationEntry("lol", "laughing out loud", PronunciationMatch.Anywhere, false), entries[1]);
        Assert.Equal(new PronunciationEntry("#tag", "hash tag", PronunciationMatch.Anywhere, false), entries[2]);
        Assert.Equal(new PronunciationEntry(@"(?<n>\d+)px", "${n} pixels", PronunciationMatch.Regex, false), entries[3]);
        Assert.Equal(4, entries.Count);
    }

    [Fact]
    public void Sample_ReportsMalformedLinesAndUnsupportedExpressions()
    {
        var entries = ParseSample(out var report);

        Assert.Equal("default.dic:2", entries[0].Source);
        Assert.Contains(report.Skipped, s => s.Source == "default.dic:6" && s.Reason == ImportSkipReason.Unsupported);
        Assert.Contains(report.Skipped, s => s.Source == "default.dic:7" && s.Reason == ImportSkipReason.Malformed);
        Assert.Contains(report.Skipped, s => s.Source == "default.dic:8" && s.Reason == ImportSkipReason.Malformed);
        Assert.Equal(3, report.Skipped.Count);
    }

    [Fact]
    public void ConvertedExpression_WorksInVoxsDictionary()
    {
        var entries = ParseSample(out _).Select(e => e.Entry);
        var dictionary = new PronunciationDictionary(entries);

        Assert.Equal("10 pixels wide", dictionary.Apply("10px wide"));
    }

    [Theory]
    [InlineData(@"(?P<word>\w+) (?P=word)", @"(?<word>\w+) \k<word>")]
    [InlineData(@"end\Z", @"end\z")]
    [InlineData(@"a\\Z", @"a\\Z")]
    [InlineData(@"(?i)abc", @"(?i)abc")]
    public void Pattern_PythonToDotNet(string python, string dotNet)
    {
        Assert.Equal(dotNet, NvdaDictionaryImporter.ConvertPattern(python));
    }

    [Theory]
    [InlineData(@"(?a)\w+")]
    [InlineData(@"(?L:x)")]
    [InlineData(@"(unclosed")]
    public void Pattern_VoxCantTake_IsNull(string python)
    {
        Assert.Null(NvdaDictionaryImporter.ConvertPattern(python));
    }

    [Theory]
    [InlineData(@"\1 and \2", "${1} and ${2}")]
    [InlineData(@"\g<1>0", "${1}0")]
    [InlineData(@"cost $5", "cost $$5")]
    [InlineData(@"a\\b", @"a\b")]
    public void Replacement_PythonToDotNet(string python, string dotNet)
    {
        Assert.Equal(dotNet, NvdaDictionaryImporter.ConvertReplacement(python));
    }

    [Fact]
    public void VoiceIdOf_TakesTheIdAfterTheSynthesizer()
    {
        Assert.Equal("Tokens_DavidM", NvdaDictionaryImporter.VoiceIdOf("oneCore-Tokens_DavidM.dic", "oneCore"));
        Assert.Null(NvdaDictionaryImporter.VoiceIdOf("sapi5-x.dic", "oneCore"));
    }
}
