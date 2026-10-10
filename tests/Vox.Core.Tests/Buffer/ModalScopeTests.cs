using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class ModalScopeTests
{
    private static VBufferDocument Page(bool modal)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Outside", AriaRole = "heading", HeadingLevel = 1 });
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Page text" });
        root.AddChild(new MockElement
            {
                RuntimeId = [4], ControlType = "Group", AriaRole = "dialog",
                AriaProperties = modal ? "modal=true" : string.Empty,
            }
            .AddChild(new MockElement { RuntimeId = [5], Name = "Sign in", AriaRole = "heading", HeadingLevel = 2 })
            .AddChild(new MockElement { RuntimeId = [6], Name = "Email" }));
        root.AddChild(new MockElement { RuntimeId = [7], Name = "Footer", AriaRole = "heading", HeadingLevel = 2 });
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public void ModalDialog_IsTheScope()
    {
        var doc = Page(modal: true);

        var scope = Assert.NotNull(doc.ModalScope);
        Assert.Contains("Sign in", doc.FlatText[scope.Start..scope.End]);
        Assert.Contains("Email", doc.FlatText[scope.Start..scope.End]);
        Assert.DoesNotContain("Outside", doc.FlatText[scope.Start..scope.End]);
        Assert.False(doc.InScope(doc.FindByRuntimeId([2])!));
        Assert.True(doc.InScope(doc.FindByRuntimeId([5])!));
        Assert.False(doc.InScope(doc.FindByRuntimeId([7])!));
    }

    [Fact]
    public void NonModalDialog_LeavesThePageBrowsable()
    {
        var doc = Page(modal: false);

        Assert.Null(doc.ModalScope);
        Assert.True(doc.InScope(doc.FindByRuntimeId([2])!));
        Assert.True(doc.InScope(0));
    }

    [Fact]
    public void QuickNav_OnlyFindsHeadingsInTheModal()
    {
        var doc = Page(modal: true);
        var handler = new QuickNavHandler(Mock.Of<IAudioCuePlayer>(a => a.IsEnabled)) { WrapEnabled = true };
        handler.SetDocument(doc);

        var first = handler.Handle(NavigationCommand.NextHeading);
        var second = handler.Handle(NavigationCommand.NextHeading);

        Assert.Same(doc.FindByRuntimeId([5]), first);
        Assert.Same(doc.FindByRuntimeId([5]), second); // wraps within the dialog
    }

    [Fact]
    public async Task SayAll_StopsAtTheEndOfTheModal()
    {
        var doc = Page(modal: true);
        var cursor = new VBufferCursor(doc, Mock.Of<IAudioCuePlayer>());
        cursor.MoveTo(doc.ModalScope!.Value.Start);
        var source = new BufferSayAllSource(cursor, doc.ModalScope!.Value.End);

        var lines = new List<string?> { await source.CurrentLineAsync(default) };
        while (await source.NextLineAsync(default) is { } line)
            lines.Add(line);

        Assert.Contains(lines, l => l!.Contains("Email"));
        Assert.DoesNotContain(lines, l => l!.Contains("Footer"));
    }
}
