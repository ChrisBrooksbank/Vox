using Vox.Core.Buffer;
using Vox.Core.Labels;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Labels;

public class ElementSignatureTests
{
    private const string Address = "https://example.com/inbox";

    private sealed class Labels(CustomLabelStore store) : ICustomLabels
    {
        public int Asked { get; private set; }
        public SiteLabels? LabelsFor(string? address)
        {
            Asked++;
            return store.LabelsFor(address);
        }
    }

    // Document (address) / toolbar: "Compose" text, unnamed button with an icon "ic_send.svg", button "Archive"
    //                    / main: heading "Inbox", link "Item 1"
    private static (MockElement Root, MockElement Toolbar, MockElement Send, MockElement Main) Page(string sendId = "")
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Value = Address };
        var toolbar = new MockElement { RuntimeId = [2], ControlType = "ToolBar", AriaRole = "toolbar" };
        toolbar.AddChild(new MockElement { RuntimeId = [3], Name = "Compose" });
        var send = new MockElement { RuntimeId = [4], ControlType = "Button", IsFocusable = true, AutomationId = sendId };
        send.AddChild(new MockElement { RuntimeId = [5], Name = "ic_send.svg", ControlType = "Image" });
        toolbar.AddChild(send);
        toolbar.AddChild(new MockElement { RuntimeId = [6], Name = "Archive", ControlType = "Button", IsFocusable = true });
        var wrapper = new MockElement { RuntimeId = [7], ControlType = "Group" };
        var main = new MockElement { RuntimeId = [8], ControlType = "Group", AriaRole = "main" };
        main.AddChild(new MockElement { RuntimeId = [9], Name = "Inbox", AriaRole = "heading", AriaProperties = "level=1" });
        main.AddChild(new MockElement { RuntimeId = [10], Name = "Item 1", ControlType = "Hyperlink", IsFocusable = true });
        wrapper.AddChild(main);
        root.AddChild(toolbar);
        root.AddChild(wrapper);
        return (root, toolbar, send, main);
    }

    private static CustomLabelStore StoreWith(VBufferDocument document, int[] runtimeId, string label)
    {
        var node = document.FindByRuntimeId(runtimeId)!;
        var store = new CustomLabelStore(Path.Combine(Path.GetTempPath(), "VoxSig_" + Guid.NewGuid().ToString("N"), "labels.json"));
        store.Set(new CustomLabel("https://example.com", ElementSignature.Of(node, document.FlatText), label));
        return store;
    }

    [Fact]
    public void Signature_IsTheAutomationId_ElseRolePathAndNearbyWords()
    {
        var withId = new VBufferBuilder().Build(Page("send-button").Root);
        Assert.Equal("id:send-button", ElementSignature.Of(withId.FindByRuntimeId([4])!, withId.FlatText));

        var document = new VBufferBuilder().Build(Page().Root);
        Assert.Equal("Button|toolbar>Document|compose", ElementSignature.Of(document.FindByRuntimeId([4])!, document.FlatText));
        // The plain wrapper is left out of the path
        Assert.Equal("Hyperlink|main>Document|ic_send.svg archive inbox", ElementSignature.Of(document.FindByRuntimeId([10])!, document.FlatText));
    }

    [Fact]
    public void Nearby_IsTheLastWords_WhateverTheLineBreaks()
    {
        Assert.Equal("read the docs", ElementSignature.Nearby("Please\nRead the\ndocs\n"));
        Assert.Equal("read the docs", ElementSignature.Nearby("Please Read the docs "));
        Assert.Equal("one", ElementSignature.Nearby(" one"));
        Assert.Equal(string.Empty, ElementSignature.Nearby(""));
    }

    [Fact]
    public void ALabelledElement_IsNamedByItsLabel_WithoutItsOwnContent()
    {
        var plain = new VBufferBuilder().Build(Page().Root);
        var labels = new Labels(StoreWith(plain, [4], "Send"));

        var document = new VBufferBuilder { Labels = labels }.Build(Page().Root);

        var send = document.FindByRuntimeId([4])!;
        Assert.Equal("Send", send.Name);
        Assert.Equal("Send", send.CustomLabel);
        Assert.Empty(send.Children);
        Assert.Null(document.FindByRuntimeId([5]));
        Assert.Contains("Compose Send", document.FlatText.Replace('\n', ' '));
        Assert.DoesNotContain("ic_send.svg", document.FlatText);
        Assert.Equal("Send", document.Labels!.Get(ElementSignature.Of(send, document.FlatText)));
        // Other elements keep their names
        Assert.Equal(string.Empty, document.FindByRuntimeId([6])!.CustomLabel);
    }

    [Fact]
    public void LabelsApply_OnlyOnTheirSite()
    {
        var plain = new VBufferBuilder().Build(Page().Root);
        var labels = new Labels(StoreWith(plain, [4], "Send"));
        var (elsewhere, _, _, _) = Page();
        elsewhere.Value = "https://another.example/inbox";

        var document = new VBufferBuilder { Labels = labels }.Build(elsewhere);

        Assert.Equal(string.Empty, document.FindByRuntimeId([4])!.CustomLabel);
        Assert.Null(document.Labels);
    }

    [Fact]
    public void ALabelByAutomationId_SurvivesTheElementMoving()
    {
        var plain = new VBufferBuilder().Build(Page("send").Root);
        var labels = new Labels(StoreWith(plain, [4], "Send"));
        var (root, toolbar, send, main) = Page("send");
        // The button moves from the toolbar to the main region
        var moved = new MockElement { RuntimeId = [1], ControlType = "Document", Value = Address };
        moved.AddChild(new MockElement { RuntimeId = [11], Name = "New text before" });
        main.AddChild(send);
        moved.AddChild(main);

        var document = new VBufferBuilder { Labels = labels }.Build(moved);

        Assert.Equal("Send", document.FindByRuntimeId([4])!.CustomLabel);
    }

    [Fact]
    public void TheLabel_SurvivesUpdates_ElsewhereOfTheElementAndAroundIt()
    {
        var plain = new VBufferBuilder().Build(Page().Root);
        var labels = new Labels(StoreWith(plain, [4], "Send"));
        var document = new VBufferBuilder { Labels = labels }.Build(Page().Root);
        var updater = new IncrementalUpdater();

        // Elsewhere: the main region gets another link
        var (_, _, _, main) = Page();
        main.AddChild(new MockElement { RuntimeId = [12], Name = "Item 2", ControlType = "Hyperlink", IsFocusable = true });
        document = updater.ApplyUpdate(document, [8], main);
        Assert.Equal("Send", document.FindByRuntimeId([4])!.CustomLabel);
        Assert.NotNull(document.FindByRuntimeId([12]));

        // The element itself, captured again (its icon changed)
        var (_, _, send, _) = Page();
        document = updater.ApplyUpdate(document, [4], send);
        Assert.Equal("Send", document.FindByRuntimeId([4])!.CustomLabel);
        Assert.Contains("Compose Send", document.FlatText.Replace('\n', ' '));

        // Its toolbar, captured again
        var (_, toolbar, _, _) = Page();
        document = updater.ApplyUpdate(document, [2], toolbar);
        Assert.Equal("Send", document.FindByRuntimeId([4])!.CustomLabel);
        // Updates keep the labels the page was built with, without asking again
        Assert.Equal(1, labels.Asked);

        // And the whole page loaded again
        var reloaded = new VBufferBuilder { Labels = labels }.Build(Page().Root);
        Assert.Equal("Send", reloaded.FindByRuntimeId([4])!.CustomLabel);
    }

    [Fact]
    public void LabelTargets_AreLinksControlsAndGraphics()
    {
        var document = new VBufferBuilder().Build(Page().Root);

        // From the icon inside the button: the button
        Assert.Equal([4], CustomLabelTargets.Around(document.FindByRuntimeId([5]))!.UIARuntimeId);
        Assert.Equal([10], CustomLabelTargets.Around(document.FindByRuntimeId([10]))!.UIARuntimeId);
        Assert.Null(CustomLabelTargets.Around(document.FindByRuntimeId([9])));
        // A graphic on its own
        var image = new VBufferBuilder().Build(new MockElement { RuntimeId = [1], ControlType = "Document" }
            .AddChild(new MockElement { RuntimeId = [2], ControlType = "Group" }.AddChild(new MockElement { RuntimeId = [3], Name = "logo.png", ControlType = "Image" })));
        Assert.Equal([3], CustomLabelTargets.Around(image.FindByRuntimeId([3]))!.UIARuntimeId);
        Assert.Null(CustomLabelTargets.Around(document.Root));
    }
}
