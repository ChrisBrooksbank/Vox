using System.Xml.Linq;
using Vox.Core.Enterprise;
using Xunit;

namespace Vox.Core.Tests.Enterprise;

/// <summary>The ADMX templates and the installer stay in step with what Vox reads.</summary>
public class PolicyTemplateTests
{
    private static string RepoFile(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. parts]);

    private static readonly XNamespace Policy = "http://schemas.microsoft.com/GroupPolicy/2006/07/PolicyDefinitions";

    [Fact]
    public void Admx_SetsExactlyThePolicyValuesVoxReads()
    {
        var admx = XDocument.Load(RepoFile("installer", "policy", "Vox.admx"));
        var valueNames = admx.Descendants(Policy + "policy")
            .SelectMany(p => new[] { (string?)p.Attribute("valueName") }
                .Concat(p.Descendants().Select(e => (string?)e.Attribute("valueName"))))
            .Where(n => n is not null)
            .ToHashSet();

        Assert.Equal(PolicySettings.ValueNames.Order(), valueNames.Order()!);
        Assert.All(admx.Descendants(Policy + "policy"), p => Assert.Equal(PolicySettings.PolicyKey, (string?)p.Attribute("key")));
    }

    [Fact]
    public void Adml_HasEveryStringAndPresentationTheAdmxUses()
    {
        var admx = File.ReadAllText(RepoFile("installer", "policy", "Vox.admx"));
        var adml = XDocument.Load(RepoFile("installer", "policy", "en-US", "Vox.adml"));
        var strings = adml.Descendants(Policy + "string").Select(s => (string?)s.Attribute("id")).ToHashSet();
        var presentations = adml.Descendants(Policy + "presentation").Select(s => (string?)s.Attribute("id")).ToHashSet();

        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(admx, @"\$\((string|presentation)\.(\w+)\)"))
            Assert.Contains(match.Groups[2].Value, match.Groups[1].Value == "string" ? strings : presentations);
    }

    [Fact]
    public void Installer_WritesMachineDefaultsNamedAfterSettings()
    {
        var wxs = XDocument.Load(RepoFile("installer", "Vox.Installer", "Package.wxs"));
        XNamespace wix = "http://wixtoolset.org/schemas/v4/wxs";
        var defaults = wxs.Descendants(wix + "RegistryKey").Single(k => (string?)k.Attribute("Key") == MachineSettings.DefaultsKey);
        var names = defaults.Elements(wix + "RegistryValue").Select(v => (string)v.Attribute("Name")!).ToList();

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.NotNull(typeof(Vox.Core.Configuration.VoxSettings).GetProperty(name)));
    }
}
