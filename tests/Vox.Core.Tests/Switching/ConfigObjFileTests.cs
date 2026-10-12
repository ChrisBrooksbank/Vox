using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class ConfigObjFileTests
{
    [Fact]
    public void Parse_ReadsSectionsSubsectionsAndValues()
    {
        var file = ConfigObjFile.Parse("""
            schemaVersion = 11
            [speech]
                synth = oneCore
                [[oneCore]]
                    rate = 75
            [keyboard]
                keyboardLayout = laptop
            """);

        Assert.Empty(file.Malformed);
        Assert.Equal("11", file.Root.Value("schemaVersion"));
        Assert.Equal("oneCore", file.Root.Section("speech")!.Value("synth"));
        Assert.Equal("75", file.Root.Section("speech", "oneCore")!.Value("rate"));
        Assert.Equal("laptop", file.Root.Section("Keyboard")!.Value("KEYBOARDLAYOUT"));
    }

    [Fact]
    public void Parse_SkipsCommentsAndUnquotesValues()
    {
        var file = ConfigObjFile.Parse("""
            # a comment
            [general]
                language = "fr"
                name = 'a # b'
                plain = value # trailing comment
                empty =
            """);

        var general = file.Root.Section("general")!;
        Assert.Equal("fr", general.Value("language"));
        Assert.Equal("a # b", general.Value("name"));
        Assert.Equal("value", general.Value("plain"));
        Assert.Equal(string.Empty, general.Value("empty"));
    }

    [Fact]
    public void Parse_KeepsMalformedLinesWithTheirNumbers_AndReadsTheRest()
    {
        var file = ConfigObjFile.Parse("[speech\nno equals here\n[speech]\nsynth = sapi5\n[[[tooDeep]]]\nlost = 1\n= value\n");

        Assert.Equal([1, 2, 5, 7], file.Malformed.Select(m => m.Number));
        Assert.Equal("sapi5", file.Root.Section("speech")!.Value("synth"));
        // Keys under a bad header are not put in the section before it
        Assert.Null(file.Root.Section("speech")!.Value("lost"));
    }

    [Fact]
    public void Parse_ASectionAtALowerDepth_ClosesTheDeeperOnes()
    {
        var file = ConfigObjFile.Parse("[a]\n[[b]]\n[[[c]]]\nx = 1\n[[d]]\ny = 2\n[e]\nz = 3\n");

        Assert.Equal("1", file.Root.Section("a", "b", "c")!.Value("x"));
        Assert.Equal("2", file.Root.Section("a", "d")!.Value("y"));
        Assert.Equal("3", file.Root.Section("e")!.Value("z"));
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("off", false)]
    [InlineData("1", true)]
    [InlineData("maybe", null)]
    public void ParseBool_ReadsConfigObjBooleans(string text, bool? expected) =>
        Assert.Equal(expected, ConfigObjFile.ParseBool(text));
}
