using Vox.Core.AddOns;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class AddOnManifestTests
{
    private static readonly string Hash = new('a', 64);

    internal static AddOnManifest Valid(string id = "com.example.test", string min = "1.0", string? max = null) => new()
    {
        Id = id,
        Name = "Test add-on",
        Version = "1.2.0",
        Author = "Example",
        MinApiVersion = min,
        MaxApiVersion = max,
        EntryAssembly = "Test.dll",
        Files = new Dictionary<string, string> { ["Test.dll"] = Hash },
    };

    [Fact]
    public void AValidManifest_RoundTrips()
    {
        var manifest = Valid();

        var parsed = AddOnManifest.Parse(manifest.ToJson());

        Assert.Equal(manifest.Id, parsed.Id);
        Assert.Equal(new Version(1, 2, 0), parsed.ParsedVersion);
        Assert.Equal(Hash, parsed.Files["Test.dll"]);
        Assert.Empty(parsed.Problems());
    }

    [Theory]
    [InlineData("Upper.Case")]
    [InlineData("x")]
    [InlineData("has space")]
    [InlineData("../escape")]
    [InlineData("a..b")]
    public void BadIds_AreRefused(string id) =>
        Assert.Contains((Valid() with { Id = id }).Problems(), p => p.Contains("id"));

    [Theory]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("v1.0")]
    [InlineData("")]
    public void BadVersions_AreRefused(string version) =>
        Assert.NotEmpty((Valid() with { Version = version }).Problems());

    [Fact]
    public void TheApiRange_MustBeMajorMinor_AndInOrder()
    {
        Assert.NotEmpty(Valid(min: "1").Problems());
        Assert.NotEmpty(Valid(min: "1.0.0").Problems());
        Assert.NotEmpty(Valid(min: "2.0", max: "1.5").Problems());
        Assert.Empty(Valid(min: "1.0", max: "2.0").Problems());
    }

    [Fact]
    public void AuthorNameAndFiles_AreNeeded()
    {
        Assert.NotEmpty((Valid() with { Author = " " }).Problems());
        Assert.NotEmpty((Valid() with { Name = "" }).Problems());
        Assert.NotEmpty((Valid() with { Files = [] }).Problems());
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("/abs.dll")]
    [InlineData("C:/x.dll")]
    [InlineData("dir\\x.dll")]
    [InlineData("a//b.dll")]
    [InlineData("addon.json")]
    [InlineData("addon.sig")]
    public void UnsafeOrReservedPaths_AreRefused(string path)
    {
        var manifest = Valid() with { Files = new Dictionary<string, string> { ["Test.dll"] = Hash, [path] = Hash } };

        Assert.Contains(manifest.Problems(), p => p.Contains(path));
    }

    [Fact]
    public void FilesInFolders_AreFine() =>
        Assert.Empty((Valid() with { Files = new Dictionary<string, string> { ["Test.dll"] = Hash, ["data/words.txt"] = Hash } }).Problems());

    [Fact]
    public void TheEntryAssembly_MustBeAListedDllAtTheRoot()
    {
        Assert.NotEmpty((Valid() with { EntryAssembly = "Other.dll" }).Problems());
        Assert.NotEmpty((Valid() with { EntryAssembly = "lib/Test.dll" }).Problems());
        Assert.NotEmpty((Valid() with { EntryAssembly = "Test.exe" }).Problems());
    }

    [Fact]
    public void AFileWithoutAHash_IsRefused() =>
        Assert.NotEmpty((Valid() with { Files = new Dictionary<string, string> { ["Test.dll"] = "abc" } }).Problems());

    [Fact]
    public void Parse_ThrowsFormatException_NamingTheProblem()
    {
        var error = Assert.Throws<FormatException>(() => AddOnManifest.Parse("""{ "id": "x" }"""));

        Assert.Contains("id", error.Message);
        Assert.Throws<FormatException>(() => AddOnManifest.Parse("not json"));
    }

    [Fact]
    public void AnHttpUrl_IsRefused() =>
        Assert.NotEmpty((Valid() with { Url = "http://example.com" }).Problems());
}

public class AddOnCompatibilityTests
{
    [Theory]
    [InlineData("1.0", null, "1.0", AddOnCompatibilityKind.Compatible)]
    [InlineData("1.0", null, "1.3", AddOnCompatibilityKind.Compatible)]
    [InlineData("1.2", null, "1.1", AddOnCompatibilityKind.NeedsNewerVox)]
    [InlineData("2.0", null, "1.9", AddOnCompatibilityKind.NeedsNewerVox)]
    [InlineData("1.0", null, "2.0", AddOnCompatibilityKind.MadeForOlderVox)]
    [InlineData("1.0", "1.4", "2.0", AddOnCompatibilityKind.MadeForOlderVox)]
    [InlineData("1.0", "2.0", "2.3", AddOnCompatibilityKind.Compatible)]
    public void Compatibility_FollowsTheApiVersion(string min, string? max, string api, AddOnCompatibilityKind expected) =>
        Assert.Equal(expected, AddOnCompatibility.Check(AddOnManifestTests.Valid(min: min, max: max), Version.Parse(api)));

    [Fact]
    public void AnIncompatibleAddOn_HasASpokenReason()
    {
        var tooNew = AddOnCompatibility.Reason(AddOnManifestTests.Valid(min: "1.5"), new Version(1, 0));
        var tooOld = AddOnCompatibility.Reason(AddOnManifestTests.Valid(min: "1.0"), new Version(2, 0));

        Assert.NotNull(tooNew);
        Assert.Contains("Test add-on", tooNew);
        Assert.Contains("1.5", tooNew);
        Assert.Contains("1.0", tooNew);
        Assert.NotNull(tooOld);
        Assert.Contains("2.0", tooOld);
        Assert.Null(AddOnCompatibility.Reason(AddOnManifestTests.Valid(), new Version(1, 0)));
    }
}
