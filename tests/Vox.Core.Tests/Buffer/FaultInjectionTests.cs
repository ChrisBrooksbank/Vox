using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Vox.Core.Buffer;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Buffer;

/// <summary>
/// The buffer must survive providers that fail or hang: a broken element costs only its own
/// subtree, and a hung one costs a timeout, after which building works again.
/// </summary>
public class FaultInjectionTests
{
    private static MockElement Page(params IVBufferElement[] middle)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Before", AriaRole = "heading", AriaProperties = "level=1" });
        foreach (var element in middle)
            root.AddChild(element);
        root.AddChild(new MockElement { RuntimeId = [3], Name = "After" });
        return root;
    }

    [Theory]
    [InlineData(ElementFault.ThrowOnName)]
    [InlineData(ElementFault.ThrowOnChildren)]
    public void Build_ElementThatThrows_IsLeftOutAndTheRestIsBuilt(ElementFault fault)
    {
        var faulty = new FaultyElement(fault).AddChild(new MockElement { RuntimeId = [5], Name = "Inside faulty" });

        var document = new VBufferBuilder().Build(Page(faulty));

        Assert.Contains("Before", document.FlatText);
        Assert.Contains("After", document.FlatText);
        Assert.DoesNotContain("Faulty element", document.FlatText);
        Assert.Single(document.Headings);
        // Node ids stay equal to their index in document order
        Assert.All(document.AllNodes.Select((n, i) => (n, i)), p => Assert.Equal(p.i, p.n.Id));
    }

    [Fact]
    public void Build_FaultySibling_DoesNotCostItsNeighboursTheirChildren()
    {
        var group = new MockElement { RuntimeId = [6], ControlType = "Group" };
        group.AddChild(new MockElement { RuntimeId = [7], Name = "Kept" });

        var document = new VBufferBuilder().Build(Page(group, new FaultyElement(ElementFault.ThrowOnName)));

        Assert.Contains("Kept", document.FlatText);
    }

    [Fact]
    public void Build_RootThatThrows_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new VBufferBuilder().Build(new FaultyElement(ElementFault.ThrowOnChildren)));
    }

    [Fact]
    public async Task Build_HungElementOnUiaThread_TimesOutThenWatchdogRecoversForTheNextPage()
    {
        using var uiaThread = new UIAThread(NullLogger<UIAThread>.Instance);
        using var watchdog = new UIAWatchdog(uiaThread, NullLogger<UIAWatchdog>.Instance,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromHours(1));
        var hung = new FaultyElement(ElementFault.HangOnChildren);
        try
        {
            // The capture of a page with a hung element times out instead of hanging the caller
            await Assert.ThrowsAsync<UIATimeoutException>(() =>
                uiaThread.RunAsync(() => new VBufferBuilder().Build(Page(hung)), TimeSpan.FromMilliseconds(100)));
            Assert.True(hung.Entered.Wait(TimeSpan.FromSeconds(2)));

            // The watchdog replaces the stuck thread, and the next page builds normally
            await Task.Delay(50);
            Assert.True(watchdog.Check());
            var next = await uiaThread.RunAsync(() => new VBufferBuilder().Build(Page()));
            Assert.Contains("After", next.FlatText);
        }
        finally
        {
            hung.Release.Set();
        }
    }
}
