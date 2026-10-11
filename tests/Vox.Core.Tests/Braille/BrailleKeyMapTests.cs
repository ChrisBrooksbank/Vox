using Vox.Core.Braille;
using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrailleKeyMapTests
{
    private static readonly OutputKey Up = new(0x26);

    private static BrailleKeyMap Map() => new(
    [
        new BrailleBinding(new HashSet<string> { "panLeft" }, Action: BrailleAction.PanLeft),
        new BrailleBinding(new HashSet<string> { "space", "dot1" }, Key: Up),
        new BrailleBinding(new HashSet<string> { "space", "dot1", "dot2", "dot5" }, Command: NavigationCommand.NextHeading),
        new BrailleBinding(new HashSet<string> { "routing" }, Action: BrailleAction.Route),
    ]);

    private static BrailleKeyResult Resolve(BrailleKeyMap map, params string[] keys) =>
        map.Resolve(new BrailleKeyEventArgs(keys));

    [Fact]
    public void Resolve_BoundChord_IsOrderIndependent()
    {
        var map = Map();

        var first = Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "space", "dot1"));
        var second = Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "dot1", "space"));

        Assert.Equal(Up, first.Binding.Key);
        Assert.Same(first.Binding, second.Binding);
    }

    [Fact]
    public void Resolve_Action_AndCommand()
    {
        var map = Map();

        Assert.Equal(BrailleAction.PanLeft, Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "panLeft")).Binding.Action);
        Assert.Equal(NavigationCommand.NextHeading,
            Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "dot5", "space", "dot2", "dot1")).Binding.Command);
    }

    [Fact]
    public void Resolve_Routing_CarriesTheCell()
    {
        var result = Assert.IsType<BrailleKeyResult.Bound>(Map().Resolve(new BrailleKeyEventArgs(["routing"], 7)));

        Assert.Equal(BrailleAction.Route, result.Binding.Action);
        Assert.Equal(7, result.RoutingCell);
    }

    [Fact]
    public void Resolve_UnboundDotsOnly_IsATypedCell()
    {
        var result = Assert.IsType<BrailleKeyResult.Typed>(Resolve(Map(), "dot1", "dot2"));

        Assert.Equal(BrailleDots.FromDots("12"), result.Cell);
        Assert.Equal(BrailleDots.FromDots("12345678"), Assert.IsType<BrailleKeyResult.Typed>(
            Resolve(Map(), "dot1", "dot2", "dot3", "dot4", "dot5", "dot6", "dot7", "dot8")).Cell);
    }

    [Fact]
    public void Resolve_BrlttyDots_AreNormalized()
    {
        var map = Map();

        Assert.Equal(BrailleDots.FromDots("125"), Assert.IsType<BrailleKeyResult.Typed>(Resolve(map, "dots", "dots125")).Cell);
        Assert.Equal(NavigationCommand.NextHeading,
            Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "space", "dots", "dots125")).Binding.Command);
    }

    [Fact]
    public void Resolve_BrlttyCharacter_IsTypedAsItIs()
    {
        Assert.Equal("a", Assert.IsType<BrailleKeyResult.Character>(Resolve(Map(), "char_a")).Text);
        Assert.Equal("é", Assert.IsType<BrailleKeyResult.Character>(Resolve(Map(), "char_é")).Text);
    }

    [Fact]
    public void Resolve_UnknownOrEmpty_IsUnbound()
    {
        var map = Map();

        Assert.IsType<BrailleKeyResult.Unbound>(Resolve(map, "frobnicate"));
        Assert.IsType<BrailleKeyResult.Unbound>(Resolve(map));
        Assert.IsType<BrailleKeyResult.Unbound>(Resolve(map, "dots"));
        // Dots with another key that isn't bound: not typing
        Assert.IsType<BrailleKeyResult.Unbound>(Resolve(map, "space", "dot3"));
        Assert.IsType<BrailleKeyResult.Unbound>(Resolve(map, "char_a", "space"));
    }

    [Fact]
    public void Normalize_SplitsDotsKeys()
    {
        Assert.Equal(new HashSet<string> { "dot1", "dot2", "dot5", "space" }, BrailleKeyMap.Normalize(["dots", "dots125", "space"]));
        // Not a dots key: kept as it is
        Assert.Contains("dots9", BrailleKeyMap.Normalize(["dots9"]));
    }

    [Fact]
    public void DotsOf_OnlyForDotKeys()
    {
        Assert.Equal(BrailleDots.FromDots("14"), BrailleKeyMap.DotsOf(["dot1", "dot4"]));
        Assert.Null(BrailleKeyMap.DotsOf(["dot1", "space"]));
        Assert.Null(BrailleKeyMap.DotsOf(["dot9"]));
        Assert.Null(BrailleKeyMap.DotsOf([]));
    }

    [Fact]
    public void Parse_ReadsActionsCommandsAndKeys()
    {
        var bindings = BrailleKeyMap.Parse("""
            {
              // comments and trailing commas are allowed
              "bindings": [
                { "keys": ["panLeft"], "action": "panleft" },
                { "keys": ["space", "dot1"], "command": "PrevLine" },
                { "keys": ["dot8"], "key": "Shift+Tab" },
              ]
            }
            """);

        Assert.Equal(3, bindings.Count);
        Assert.Equal(BrailleAction.PanLeft, bindings[0].Action);
        Assert.Equal(NavigationCommand.PrevLine, bindings[1].Command);
        Assert.Equal(new HashSet<string> { "space", "dot1" }, bindings[1].Keys);
        Assert.Equal(new OutputKey(0x09, KeyModifiers.Shift), bindings[2].Key);
    }

    [Fact]
    public void Parse_UnknownNames_AreSkippedWithWarnings()
    {
        var warnings = new List<string>();

        var bindings = BrailleKeyMap.Parse("""
            {
              "bindings": [
                { "keys": ["a"], "action": "Fly" },
                { "keys": ["b"], "command": "MakeCoffee" },
                { "keys": ["c"], "key": "Hyper+Q" },
                { "keys": [], "action": "PanLeft" },
                { "action": "PanLeft" },
                { "keys": ["d"], "action": "PanRight" }
              ]
            }
            """, warnings);

        Assert.Equal(BrailleAction.PanRight, Assert.Single(bindings).Action);
        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("Fly"));
        Assert.Contains(warnings, w => w.Contains("MakeCoffee"));
        Assert.Contains(warnings, w => w.Contains("Hyper+Q"));
    }

    [Fact]
    public void Parse_WithoutBindings_IsEmpty()
    {
        Assert.Empty(BrailleKeyMap.Parse("{}"));
    }

    [Fact]
    public void LaterBindings_ReplaceEarlierOnesForTheSameChord()
    {
        var map = new BrailleKeyMap(
        [
            new BrailleBinding(new HashSet<string> { "a", "b" }, Action: BrailleAction.PanLeft),
            new BrailleBinding(new HashSet<string> { "b", "a" }, Action: BrailleAction.PanRight),
        ]);

        Assert.Single(map.Bindings);
        Assert.Equal(BrailleAction.PanRight, Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "a", "b")).Binding.Action);
    }

    [Theory]
    [InlineData("HID")]
    [InlineData("hid")]
    [InlineData("BRLTTY")]
    [InlineData(null)]
    public void ForDriver_HasTheCommonBindings(string? driver)
    {
        var map = BrailleKeyMap.ForDriver(driver);

        Assert.Equal(BrailleAction.PanLeft, Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "panLeft")).Binding.Action);
        Assert.Equal(BrailleAction.PanRight, Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "panRight")).Binding.Action);
        Assert.Equal(BrailleAction.Route, Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "routing")).Binding.Action);
        Assert.Equal(BrailleAction.Space, Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "space")).Binding.Action);
        Assert.Equal(Up, Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "dot1", "space")).Binding.Key);
        Assert.Equal(NavigationCommand.NextHeading,
            Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "space", "dot1", "dot2", "dot5")).Binding.Command);
        Assert.Equal(BrailleAction.CycleTether,
            Assert.IsType<BrailleKeyResult.Bound>(Resolve(map, "space", "dot2", "dot4", "dot5")).Binding.Action);
        // Dots alone are typed
        Assert.IsType<BrailleKeyResult.Typed>(Resolve(map, "dot1", "dot3"));
    }

    [Fact]
    public void ForDriver_Brltty_AddsItsOwnBindings()
    {
        var brltty = BrailleKeyMap.ForDriver("BRLTTY");
        var hid = BrailleKeyMap.ForDriver("HID");

        Assert.Equal(BrailleAction.PreviousLine, Assert.IsType<BrailleKeyResult.Bound>(Resolve(brltty, "lineUp")).Binding.Action);
        Assert.Equal(new OutputKey(0x0D), Assert.IsType<BrailleKeyResult.Bound>(Resolve(brltty, "key_Return")).Binding.Key);
        Assert.Equal(NavigationCommand.TopOfDocument, Assert.IsType<BrailleKeyResult.Bound>(Resolve(brltty, "top")).Binding.Command);
        Assert.IsType<BrailleKeyResult.Unbound>(Resolve(hid, "lineUp"));
        Assert.True(brltty.Bindings.Count > hid.Bindings.Count);
    }

    [Fact]
    public void ForDriver_UnknownDriver_IsTheCommonMap()
    {
        Assert.Equal(BrailleKeyMap.ForDriver(null).Bindings.Count, BrailleKeyMap.ForDriver("NoSuchDriver").Bindings.Count);
    }

    [Theory]
    [InlineData("common")]
    [InlineData("hid")]
    [InlineData("brltty")]
    public void BuiltInMaps_HaveNoUnknownNames(string name)
    {
        using var stream = typeof(BrailleKeyMap).Assembly.GetManifestResourceStream($"Vox.Core.braille.keymaps.{name}.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var warnings = new List<string>();

        BrailleKeyMap.Parse(reader.ReadToEnd(), warnings);

        Assert.Empty(warnings);
    }
}
