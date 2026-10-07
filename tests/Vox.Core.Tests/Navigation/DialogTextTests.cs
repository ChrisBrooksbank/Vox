using Vox.Core.Navigation;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class DialogTextTests
{
    private static MockElement Text(string name) => new() { Name = name, ControlType = "Text" };
    private static MockElement Button(string name) => new() { Name = name, ControlType = "Button", IsFocusable = true };

    [Fact]
    public void Collect_SaveChangesDialog_ReadsTheMessageNotTheButtons()
    {
        var dialog = new MockElement { Name = "Notepad", ControlType = "Window" }
            .AddChild(new MockElement { ControlType = "Pane" }
                .AddChild(Text("Do you want to save changes to Untitled?")))
            .AddChild(Button("Save"))
            .AddChild(Button("Don't Save"))
            .AddChild(Button("Cancel"));

        Assert.Equal(["Do you want to save changes to Untitled?"], DialogText.Collect(dialog));
    }

    [Fact]
    public void Collect_SkipsFieldLabelsAndTheTitle()
    {
        var dialog = new MockElement { Name = "Run", ControlType = "Window" }
            .AddChild(Text("Run"))
            .AddChild(Text("Type the name of a program, folder, document, or Internet resource, and Windows will open it for you."))
            .AddChild(Text("Open:"))
            .AddChild(new MockElement { Name = "Open:", ControlType = "ComboBox", IsFocusable = true })
            .AddChild(Button("OK"));

        Assert.Equal(
            ["Type the name of a program, folder, document, or Internet resource, and Windows will open it for you."],
            DialogText.Collect(dialog));
    }

    [Fact]
    public void Collect_SkipsTextInsideListsAndControls()
    {
        var dialog = new MockElement { Name = "Pick", ControlType = "Window" }
            .AddChild(Text("Choose a file"))
            .AddChild(new MockElement { Name = "Details", ControlType = "Button", IsFocusable = true }.AddChild(Text("Details")))
            .AddChild(new MockElement { ControlType = "List" }.AddChild(Text("file1.txt")));

        Assert.Equal(["Choose a file"], DialogText.Collect(dialog));
    }

    [Fact]
    public void Collect_TextJustBeforeAList_IsItsLabel()
    {
        var dialog = new MockElement { Name = "Open", ControlType = "Window" }
            .AddChild(Text("Files:"))
            .AddChild(new MockElement { ControlType = "List" });

        Assert.Empty(DialogText.Collect(dialog));
    }

    [Fact]
    public void Collect_DuplicateText_ReadOnce()
    {
        var dialog = new MockElement { Name = "Error", ControlType = "Window" }
            .AddChild(Text("Access denied"))
            .AddChild(new MockElement { ControlType = "Group" }.AddChild(Text("Access denied")));

        Assert.Equal(["Access denied"], DialogText.Collect(dialog));
    }
}
