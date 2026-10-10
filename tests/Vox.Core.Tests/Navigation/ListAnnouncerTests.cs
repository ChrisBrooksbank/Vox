using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class ListAnnouncerTests
{
    private static int _nextId = 100;

    private static MockElement Text(string name) => new() { RuntimeId = [_nextId++], Name = name };

    private static MockElement List(params MockElement[] items)
    {
        var list = new MockElement { RuntimeId = [_nextId++], ControlType = "List" };
        foreach (var item in items)
            list.AddChild(item);
        return list;
    }

    private static MockElement Item(params MockElement[] children)
    {
        var item = new MockElement { RuntimeId = [_nextId++], ControlType = "ListItem" };
        foreach (var child in children)
            item.AddChild(child);
        return item;
    }

    // Intro / list (Apples, Pears (nested: Green, Red), Plums) / Outro
    private static (VBufferDocument Doc, MockElement Intro, MockElement Apples, MockElement Green, MockElement Plums, MockElement Outro) Build()
    {
        var intro = Text("Intro");
        var apples = Text("Apples");
        var green = Text("Green");
        var plums = Text("Plums");
        var outro = Text("Outro");
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(intro);
        root.AddChild(List(
            Item(apples),
            Item(Text("Pears"), List(Item(green), Item(Text("Red")))),
            Item(plums)));
        root.AddChild(outro);
        return (new VBufferBuilder().Build(root), intro, apples, green, plums, outro);
    }

    private static VBufferNode Node(VBufferDocument doc, MockElement element) => doc.FindByRuntimeId(element.RuntimeId)!;

    [Fact]
    public void EnteringAList_SaysItsItemCount()
    {
        var (doc, intro, apples, _, _, _) = Build();
        Assert.Equal("list with 3 items", ListAnnouncer.Transition(Node(doc, intro), Node(doc, apples)));
    }

    [Fact]
    public void EnteringANestedList_SaysTheNestingLevel()
    {
        var (doc, _, apples, green, _, _) = Build();
        Assert.Equal("list with 2 items, nesting level 2", ListAnnouncer.Transition(Node(doc, apples), Node(doc, green)));
    }

    [Fact]
    public void LeavingANestedList_SaysOutOfList()
    {
        var (doc, _, _, green, plums, _) = Build();
        Assert.Equal("out of list", ListAnnouncer.Transition(Node(doc, green), Node(doc, plums)));
    }

    [Fact]
    public void LeavingBothLists_SaysOutOfListForEach()
    {
        var (doc, _, _, green, _, outro) = Build();
        Assert.Equal("out of list, out of list", ListAnnouncer.Transition(Node(doc, green), Node(doc, outro)));
    }

    [Fact]
    public void JumpingIntoANestedList_SaysBothLists()
    {
        var (doc, intro, _, green, _, _) = Build();
        Assert.Equal("list with 3 items, list with 2 items, nesting level 2",
            ListAnnouncer.Transition(Node(doc, intro), Node(doc, green)));
    }

    [Fact]
    public void MovingWithinAList_SaysNothing()
    {
        var (doc, intro, apples, _, plums, outro) = Build();
        Assert.Null(ListAnnouncer.Transition(Node(doc, apples), Node(doc, plums)));
        Assert.Null(ListAnnouncer.Transition(Node(doc, intro), Node(doc, outro)));
        Assert.Null(ListAnnouncer.Transition(null, Node(doc, apples)));
    }

    [Fact]
    public void ItemCount_SaysOneItem()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(Text("Intro"));
        var only = Text("Only");
        root.AddChild(List(Item(only)));
        var doc = new VBufferBuilder().Build(root);

        Assert.Equal("list with 1 item", ListAnnouncer.Transition(doc.FindByRuntimeId([1])!.Children[0], Node(doc, only)));
    }

    [Fact]
    public void ListBoxes_AreNotLists()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var intro = Text("Intro");
        root.AddChild(intro);
        var listBox = new MockElement { RuntimeId = [_nextId++], ControlType = "List", AriaRole = "listbox" };
        var option = new MockElement { RuntimeId = [_nextId++], ControlType = "ListItem", AriaRole = "option", Name = "Red" };
        listBox.AddChild(option);
        root.AddChild(listBox);
        var doc = new VBufferBuilder().Build(root);

        Assert.Null(ListAnnouncer.Transition(Node(doc, intro), Node(doc, option)));
    }
}
