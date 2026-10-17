using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AddOns;
using Vox.Core.Configuration;
using Vox.Sdk;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class AddOnSettingsTests
{
    private sealed class NoSpeech : ISpeechOutput
    {
        public void Speak(string text, SpeechPriority priority = SpeechPriority.Normal) { }
        public void StopSpeaking() { }
    }

    private static readonly AddOnToggleSetting Toggle = new("loud", "Loud") { Default = true };
    private static readonly AddOnNumberSetting Number = new("count", "Count") { Min = 1, Max = 5, Default = 3 };
    private static readonly AddOnChoiceSetting Choice = new("mode", "Mode")
    {
        Choices = [new AddOnChoice("a", "Alpha"), new AddOnChoice("b", "Beta")],
        Default = "b",
    };
    private static readonly AddOnSetting[] All = [Toggle, Number, Choice];

    private static AddOnHost HostWith(Func<VoxSettings> settings) =>
        new("x", () => Path.GetTempPath(), NullLogger.Instance, new NoSpeech(), () => null, work => work(),
            output: new AddOnIntegration { CurrentSettings = settings });

    [Fact]
    public void Defaults_AreUsedUntilChanged()
    {
        var settings = new VoxSettings();
        Assert.Equal("true", AddOnSettingsModel.Value(settings, "x", Toggle));
        Assert.Equal("3", AddOnSettingsModel.Value(settings, "x", Number));
        Assert.Equal("b", AddOnSettingsModel.Value(settings, "x", Choice));
    }

    [Fact]
    public void ChoiceWithoutDefault_IsTheFirstChoice()
    {
        var choice = Choice with { Default = null };
        Assert.Equal("a", AddOnSettingsModel.Value(new VoxSettings(), "x", choice));
    }

    [Fact]
    public void With_StoresPerAddOn_AndLeavesTheOriginal()
    {
        var before = new VoxSettings();
        var after = AddOnSettingsModel.With(before, "x", Number, "5");
        Assert.Equal("5", AddOnSettingsModel.Value(after, "x", Number));
        Assert.Equal("3", AddOnSettingsModel.Value(after, "other", Number));
        Assert.Empty(before.AddOnSettings);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("many")]
    public void With_IgnoresANumberOutOfRange(string value)
    {
        var after = AddOnSettingsModel.With(new VoxSettings(), "x", Number, value);
        Assert.Empty(after.AddOnSettings);
    }

    [Fact]
    public void StoredValueThatNoLongerFits_ReadsAsTheDefault()
    {
        var settings = new VoxSettings
        {
            AddOnSettings = { ["x"] = new() { ["mode"] = "removed", ["count"] = "99", ["loud"] = "maybe" } },
        };
        Assert.Equal("b", AddOnSettingsModel.Value(settings, "x", Choice));
        Assert.Equal("3", AddOnSettingsModel.Value(settings, "x", Number));
        Assert.Equal("true", AddOnSettingsModel.Value(settings, "x", Toggle));
    }

    [Fact]
    public void Page_FieldsReadAndChangeSettings()
    {
        var page = AddOnSettingsModel.Page("My add-on", "x", All);
        Assert.Equal("My add-on", page.Title);
        var toggle = Assert.IsType<ToggleField>(page.Fields[0]);
        var number = Assert.IsType<NumberField>(page.Fields[1]);
        var choice = Assert.IsType<ChoiceField>(page.Fields[2]);

        var settings = new VoxSettings();
        Assert.True(toggle.Get(settings));
        Assert.Equal(3, number.Get(settings));
        Assert.Equal(1, choice.IndexOf(settings));

        settings = toggle.Set(settings, false);
        settings = number.Set(settings, 4);
        settings = choice.Set(settings, "a");
        Assert.False(toggle.Get(settings));
        Assert.Equal(4, number.Get(settings));
        Assert.Equal(0, choice.IndexOf(settings));
    }

    [Fact]
    public void Profiles_LayerAddOnSettings()
    {
        var normal = new VoxSettings();
        var layer = ProfileSettings.Diff(normal, AddOnSettingsModel.With(normal, "x", Number, "5"));
        var merged = ProfileSettings.Merge(normal, [layer]);
        Assert.Equal("5", AddOnSettingsModel.Value(merged, "x", Number));
        Assert.Equal("3", AddOnSettingsModel.Value(normal, "x", Number));
    }

    [Fact]
    public void Policy_LocksAddOnPages()
    {
        var pages = AddOnSettingsModel.Page("My add-on", "x", All);
        var policy = Vox.Core.Enterprise.PolicySettings.Read(
            new LockAllRegistry(), includeUser: false);
        var locked = SettingsPages.WithPolicy([pages], policy, new VoxSettings());
        Assert.All(locked[0].Fields, f => Assert.True(f.Locked));
    }

    [Fact]
    public void Host_GetSetting_ReadsCurrentSettings()
    {
        var settings = new VoxSettings();
        var host = HostWith(() => settings);
        host.AddSettings(All);

        Assert.Equal("3", host.GetSetting("count"));
        settings = AddOnSettingsModel.With(settings, "x", Number, "2");
        Assert.Equal("2", host.GetSetting("count"));
        Assert.Null(host.GetSetting("unknown"));
    }

    [Fact]
    public void Host_AddSettings_RejectsBadDeclarations_AndLateCalls()
    {
        var host = HostWith(() => new VoxSettings());
        Assert.Throws<ArgumentException>(() => host.AddSettings([new AddOnToggleSetting("", "x")]));
        Assert.Throws<ArgumentException>(() => host.AddSettings([new AddOnChoiceSetting("c", "C")]));
        Assert.Throws<ArgumentException>(() => host.AddSettings([Toggle, Toggle]));
        host.AddSettings([Toggle]);
        Assert.Throws<ArgumentException>(() => host.AddSettings([Toggle]));
        host.Seal();
        Assert.Throws<InvalidOperationException>(() => host.AddSettings([Number]));
    }

    private sealed class LockAllRegistry : Vox.Core.Enterprise.IRegistryReader
    {
        public IReadOnlyDictionary<string, object> ReadValues(Vox.Core.Enterprise.RegistryRoot root, string key) =>
            root == Vox.Core.Enterprise.RegistryRoot.LocalMachine && key == Vox.Core.Enterprise.PolicySettings.PolicyKey
                ? new Dictionary<string, object> { [Vox.Core.Enterprise.PolicySettings.LockSettingsValue] = 1 }
                : new Dictionary<string, object>();
    }
}
