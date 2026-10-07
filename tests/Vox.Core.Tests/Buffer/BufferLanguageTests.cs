using Vox.Core.Accessibility;
using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class BufferLanguageTests
{
    // Page in English with a French paragraph (one span inside it in German)
    private static VBufferDocument Build()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Language = "en-GB" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Hello" });
        var french = new MockElement { RuntimeId = [3], ControlType = "Group", Language = "fr-FR" };
        french.AddChild(new MockElement { RuntimeId = [4], Name = "Bonjour" });
        french.AddChild(new MockElement { RuntimeId = [5], Name = "Guten Tag", Language = "de-DE" });
        root.AddChild(french);
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public void Nodes_HaveTheirOwnLanguage_OrInheritTheirParents()
    {
        var doc = Build();

        Assert.Equal("en-GB", doc.FindByRuntimeId([2])!.Language);
        Assert.Equal("fr-FR", doc.FindByRuntimeId([4])!.Language);
        Assert.Equal("de-DE", doc.FindByRuntimeId([5])!.Language);
    }

    [Fact]
    public void NoLanguageAnywhere_IsEmpty()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Hello" });

        Assert.Equal(string.Empty, new VBufferBuilder().Build(root).FindByRuntimeId([2])!.Language);
    }

    [Fact]
    public void ARebuiltSubtree_KeepsTheLanguageItInherits()
    {
        var doc = Build();
        var replacement = new MockElement { RuntimeId = [4], Name = "Salut" };

        var updated = new IncrementalUpdater().ApplyUpdate(doc, [4], replacement);

        Assert.Equal("fr-FR", updated.FindByRuntimeId([4])!.Language);
    }

    [Theory]
    [InlineData(1036, "fr-FR")]
    [InlineData(2057, "en-GB")]
    [InlineData(0, "")]
    [InlineData(127, "")]
    [InlineData(null, "")]
    public void UiaCulture_BecomesALanguageTag(int? lcid, string expected)
    {
        Assert.Equal(expected, UIAElementSnapshot.LanguageName(lcid));
    }
}
