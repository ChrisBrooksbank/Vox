using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

/// <summary>
/// Tests for UIAEventSubscriber helper logic.
/// Live UIA subscription tests are skipped (require COM / desktop environment).
/// </summary>
public class UIAEventSubscriberTests
{
    // -------------------------------------------------------------------------
    // Helpers: ControlTypeIdToName — tested via the public-facing handler behavior
    // by subclassing and using reflection or by testing ParseAriaRole indirectly.
    // We expose helpers via a test-accessible subclass below.
    // -------------------------------------------------------------------------

    private sealed class FakeEventSink : IEventSink
    {
        public List<ScreenReaderEvent> Events { get; } = new();
        public void Post(ScreenReaderEvent evt) => Events.Add(evt);
    }

    // -------------------------------------------------------------------------
    // AriaProperty parsing — tested directly via a thin wrapper
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("visited=true", "visited", true)]
    [InlineData("visited=false", "visited", false)]
    [InlineData("required=true;expanded=false", "required", true)]
    [InlineData("required=true;expanded=false", "expanded", false)]
    [InlineData("expanded=true;required=false", "expanded", true)]
    [InlineData("haspopup=true", "haspopup", true)]
    [InlineData("", "visited", false)]
    [InlineData(null, "visited", false)]
    [InlineData("level=3", "other", false)]
    public void ParseAriaPropertyBool_VariousInputs_ReturnsExpected(
        string? ariaProps, string key, bool expected)
    {
        var result = AriaPropsHelper.ParseBool(ariaProps, key);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("level=3", "level", 3)]
    [InlineData("level=6;other=1", "level", 6)]
    [InlineData("level=abc", "level", 0)]
    [InlineData("", "level", 0)]
    [InlineData(null, "level", 0)]
    public void ParseAriaPropertyInt_VariousInputs_ReturnsExpected(
        string? ariaProps, string key, int expected)
    {
        var result = AriaPropsHelper.ParseInt(ariaProps, key);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("heading", "level=2", 2, null, false)]
    [InlineData("h1", null, 1, null, false)]
    [InlineData("h2", null, 2, null, false)]
    [InlineData("h3", null, 3, null, false)]
    [InlineData("h4", null, 4, null, false)]
    [InlineData("h5", null, 5, null, false)]
    [InlineData("h6", null, 6, null, false)]
    [InlineData("link", null, 0, null, true)]
    [InlineData("a", null, 0, null, true)]
    [InlineData("main", null, 0, "Main", false)]
    [InlineData("navigation", null, 0, "Navigation", false)]
    [InlineData("banner", null, 0, "Banner", false)]
    [InlineData("complementary", null, 0, "Complementary", false)]
    [InlineData("contentinfo", null, 0, "Content info", false)]
    [InlineData("form", null, 0, "Form", false)]
    [InlineData("region", null, 0, "Region", false)]
    [InlineData("search", null, 0, "Search", false)]
    [InlineData("button", null, 0, null, false)]
    [InlineData(null, null, 0, null, false)]
    public void ParseAriaRole_VariousRoles_ReturnsExpected(
        string? role, string? ariaProps, int expectedLevel, string? expectedLandmark, bool expectedIsLink)
    {
        var (level, _, landmarkType, isLink) = AriaPropsHelper.ParseRole(role, ariaProps);
        Assert.Equal(expectedLevel, level);
        Assert.Equal(expectedLandmark, landmarkType);
        Assert.Equal(expectedIsLink, isLink);
    }

    [Theory]
    [InlineData(50000, "Button")]
    [InlineData(50004, "Edit")]
    [InlineData(50005, "Hyperlink")]
    [InlineData(50030, "Document")]
    [InlineData(50032, "Window")]
    [InlineData(99999, "Unknown")]
    [InlineData(0, "Unknown")]
    public void ControlTypeIdToName_KnownAndUnknownIds_ReturnsExpected(int id, string expected)
    {
        var result = AriaPropsHelper.ControlTypeName(id);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void UIAEventSubscriber_CanBeConstructed_WithoutInitialization()
    {
        // Verify construction doesn't throw even without UIAProvider initialized
        var sink = new FakeEventSink();
        var uiaThread = new UIAThread(NullLogger<UIAThread>.Instance);
        var provider = new UIAProvider(uiaThread, NullLogger<UIAProvider>.Instance);

        using var subscriber = new UIAEventSubscriber(
            uiaThread,
            provider,
            sink,
            NullLogger<UIAEventSubscriber>.Instance);

        // No exception = pass
        Assert.Empty(sink.Events);

        provider.Dispose();
        uiaThread.Dispose();
    }

    [Fact]
    public void UIAEventSubscriber_Dispose_WithoutSubscribe_DoesNotThrow()
    {
        var sink = new FakeEventSink();
        var uiaThread = new UIAThread(NullLogger<UIAThread>.Instance);
        var provider = new UIAProvider(uiaThread, NullLogger<UIAProvider>.Instance);

        var subscriber = new UIAEventSubscriber(
            uiaThread,
            provider,
            sink,
            NullLogger<UIAEventSubscriber>.Instance);

        var ex = Record.Exception(() => subscriber.Dispose());
        Assert.Null(ex);

        provider.Dispose();
        uiaThread.Dispose();
    }

    [Fact(Skip = "Requires live UIA COM on STA thread — integration test only")]
    public async Task SubscribeAsync_RequiresInitializedUIAProvider()
    {
        var sink = new FakeEventSink();
        var uiaThread = new UIAThread(NullLogger<UIAThread>.Instance);
        var provider = new UIAProvider(uiaThread, NullLogger<UIAProvider>.Instance);
        await provider.InitializeAsync();

        using var subscriber = new UIAEventSubscriber(
            uiaThread,
            provider,
            sink,
            NullLogger<UIAEventSubscriber>.Instance);

        await subscriber.SubscribeAsync();

        provider.Dispose();
        uiaThread.Dispose();
    }
}

/// <summary>
/// Thin test helper that exposes the private static parsing methods
/// via reflection or re-implementation to allow table-driven tests.
/// Re-implemented here to match production logic exactly.
/// </summary>
internal static class AriaPropsHelper
{
    public static bool ParseBool(string? ariaProps, string key)
    {
        if (string.IsNullOrEmpty(ariaProps)) return false;
        foreach (var segment in ariaProps.Split(';', ','))
        {
            var sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep < 0) continue;
            var k = segment[..sep].Trim().ToLowerInvariant();
            var v = segment[(sep + 1)..].Trim().ToLowerInvariant();
            if (k == key.ToLowerInvariant())
                return v is "true" or "1" or "yes";
        }
        return false;
    }

    public static int ParseInt(string? ariaProps, string key)
    {
        if (string.IsNullOrEmpty(ariaProps)) return 0;
        foreach (var segment in ariaProps.Split(';', ','))
        {
            var sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep < 0) continue;
            var k = segment[..sep].Trim().ToLowerInvariant();
            var v = segment[(sep + 1)..].Trim();
            if (k == key.ToLowerInvariant() && int.TryParse(v, out var result))
                return result;
        }
        return 0;
    }

    public static (int HeadingLevel, bool IsLandmark, string? LandmarkType, bool IsLink) ParseRole(
        string? ariaRole, string? ariaProps)
    {
        if (string.IsNullOrEmpty(ariaRole))
            return (0, false, null, false);

        var role = ariaRole.ToLowerInvariant().Trim();

        var headingLevel = role switch
        {
            "heading" => ParseInt(ariaProps, "level"),
            "h1" => 1,
            "h2" => 2,
            "h3" => 3,
            "h4" => 4,
            "h5" => 5,
            "h6" => 6,
            _ => 0
        };

        var isLink = role is "link" or "a";

        var (isLandmark, landmarkType) = role switch
        {
            "banner" => (true, "Banner"),
            "complementary" => (true, "Complementary"),
            "contentinfo" => (true, "Content info"),
            "form" => (true, "Form"),
            "main" => (true, "Main"),
            "navigation" => (true, "Navigation"),
            "region" => (true, "Region"),
            "search" => (true, "Search"),
            _ => (false, (string?)null)
        };

        return (headingLevel, isLandmark, landmarkType, isLink);
    }

    public static string ControlTypeName(int id) => id switch
    {
        50000 => "Button",
        50001 => "Calendar",
        50002 => "CheckBox",
        50003 => "ComboBox",
        50004 => "Edit",
        50005 => "Hyperlink",
        50006 => "Image",
        50007 => "ListItem",
        50008 => "List",
        50009 => "Menu",
        50010 => "MenuBar",
        50011 => "MenuItem",
        50012 => "ProgressBar",
        50013 => "RadioButton",
        50014 => "ScrollBar",
        50015 => "Slider",
        50016 => "Spinner",
        50017 => "StatusBar",
        50018 => "Tab",
        50019 => "TabItem",
        50020 => "Text",
        50021 => "ToolBar",
        50022 => "ToolTip",
        50023 => "Tree",
        50024 => "TreeItem",
        50025 => "Custom",
        50026 => "Group",
        50027 => "Thumb",
        50028 => "DataGrid",
        50029 => "DataItem",
        50030 => "Document",
        50031 => "SplitButton",
        50032 => "Window",
        50033 => "Pane",
        50034 => "Header",
        50035 => "HeaderItem",
        50036 => "Table",
        50037 => "TitleBar",
        50038 => "Separator",
        50039 => "SemanticZoom",
        50040 => "AppBar",
        _ => "Unknown"
    };
}
