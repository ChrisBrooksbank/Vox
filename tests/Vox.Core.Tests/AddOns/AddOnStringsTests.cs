using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AddOns;
using Vox.Core.Localization;
using Vox.Sdk;
using Xunit;

namespace Vox.Core.Tests.AddOns;

[Collection("Culture")]
public sealed class AddOnStringsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vox-strings-" + Guid.NewGuid().ToString("N"));
    private readonly CultureInfo? _culture = VoxStrings.Culture;

    public void Dispose()
    {
        VoxStrings.Culture = _culture;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private AddOnManifest Write(params (string File, string Json)[] files)
    {
        Directory.CreateDirectory(Path.Combine(_folder, AddOnStrings.Folder));
        var hashes = new Dictionary<string, string>();
        foreach (var (file, json) in files)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            File.WriteAllBytes(Path.Combine(_folder, AddOnStrings.Folder, file), bytes);
            hashes[$"{AddOnStrings.Folder}/{file}"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        return new AddOnManifest { Id = "x", Name = "X", Version = "1.0", Author = "a", Files = hashes };
    }

    private AddOnStrings Standard() => AddOnStrings.Load(_folder, Write(
        ("en.json", """{"hello":"Hello","only.en":"English only"}"""),
        ("fr.json", """{"hello":"Bonjour"}"""),
        ("pt-BR.json", """{"hello":"Olá"}""")));

    [Fact]
    public void Resolve_UsesTheCurrentLanguage()
    {
        var strings = Standard();
        VoxStrings.Culture = new CultureInfo("fr-FR");
        Assert.Equal("Bonjour", strings.Resolve("@hello"));
        VoxStrings.Culture = new CultureInfo("pt-BR");
        Assert.Equal("Olá", strings.Resolve("@hello"));
    }

    [Fact]
    public void Resolve_FallsBackToEnglish_ThenToTheKey()
    {
        var strings = Standard();
        VoxStrings.Culture = new CultureInfo("fr");
        Assert.Equal("English only", strings.Resolve("@only.en"));
        Assert.Equal("missing", strings.Resolve("@missing"));
    }

    [Fact]
    public void Resolve_LeavesPlainTextAndEscapes()
    {
        var strings = Standard();
        Assert.Equal("Plain", strings.Resolve("Plain"));
        Assert.Equal("@hello", strings.Resolve("@@hello"));
    }

    [Fact]
    public void Load_SkipsAFileThatChangedSinceTheManifest()
    {
        var manifest = Write(("en.json", """{"hello":"Hello"}"""));
        File.WriteAllText(Path.Combine(_folder, AddOnStrings.Folder, "en.json"), """{"hello":"Tampered"}""");
        var problems = new List<string>();
        var strings = AddOnStrings.Load(_folder, manifest, (file, _) => problems.Add(file));
        Assert.True(strings.IsEmpty);
        Assert.Equal(["strings/en.json"], problems);
    }

    [Fact]
    public void Load_WithoutAFolder_IsEmpty() =>
        Assert.True(AddOnStrings.Load(_folder, new AddOnManifest { Id = "x", Name = "X", Version = "1", Author = "a" }).IsEmpty);

    [Fact]
    public void SettingsPage_UsesTranslatedLabelsAndChoices()
    {
        var strings = Standard();
        VoxStrings.Culture = new CultureInfo("fr");
        var setting = new AddOnChoiceSetting("mode", "@hello") { Choices = [new AddOnChoice("a", "@hello"), new AddOnChoice("b", "Plain")] };
        var page = AddOnSettingsModel.Page("X", "x", [setting], strings);
        var field = Assert.IsType<Vox.Core.Configuration.ChoiceField>(Assert.Single(page.Fields));
        Assert.Equal("Bonjour", field.Label);
        Assert.Equal(["Bonjour", "Plain"], field.Choices.Select(c => c.Text));
    }

    [Fact]
    public void Host_GetString_ReadsTheTable()
    {
        var host = new AddOnHost("x", () => _folder, NullLogger.Instance, new Silent(), () => null, w => w()) { Strings = Standard() };
        VoxStrings.Culture = new CultureInfo("fr");
        Assert.Equal("Bonjour", host.GetString("hello"));
        Assert.Equal("nope", host.GetString("nope"));
    }

    private sealed class Silent : ISpeechOutput
    {
        public void Speak(string text, SpeechPriority priority = SpeechPriority.Normal) { }
        public void StopSpeaking() { }
    }
}
