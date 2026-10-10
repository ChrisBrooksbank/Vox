using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class AriaStatesTests
{
    [Theory]
    [InlineData(false, "", "true")]
    [InlineData(null, "invalid=true", "true")]
    [InlineData(null, "invalid=spelling", "spelling")]
    [InlineData(false, "invalid=grammar", "grammar")]
    [InlineData(true, "invalid=false", "")]
    [InlineData(null, "required=true", "")]
    [InlineData(true, "", "")]
    public void Invalid_FromUiaAndAria(bool? isDataValidForForm, string ariaProps, string expected) =>
        Assert.Equal(expected, AriaStates.Invalid(isDataValidForForm, ariaProps));

    [Theory]
    [InlineData("current=page", "page")]
    [InlineData("level=2;current=Step", "step")]
    [InlineData("current=true", "true")]
    [InlineData("current=yes-please", "true")]
    [InlineData("current=false", "")]
    [InlineData("", "")]
    public void Current_ParsesAriaCurrent(string ariaProps, string expected) =>
        Assert.Equal(expected, AriaStates.Current(ariaProps));

    [Theory]
    [InlineData("sort=ascending", "ascending")]
    [InlineData("sort=descending", "descending")]
    [InlineData("sort=other", "other")]
    [InlineData("sort=none", "")]
    [InlineData("", "")]
    public void Sort_ParsesAriaSort(string ariaProps, string expected) =>
        Assert.Equal(expected, AriaStates.Sort(ariaProps));

    [Theory]
    [InlineData("pressed=true", ControlState.ToggleOn)]
    [InlineData("pressed=false", ControlState.ToggleOff)]
    [InlineData("pressed=mixed", ControlState.ToggleIndeterminate)]
    [InlineData("", null)]
    public void Pressed_ParsesAriaPressed(string ariaProps, int? expected) =>
        Assert.Equal(expected, AriaStates.Pressed(ariaProps));

    [Theory]
    [InlineData("Button", "", 1, true)]
    [InlineData("Custom", "button", 0, true)]
    [InlineData("Button", "", null, false)]
    [InlineData("CheckBox", "checkbox", 1, false)]
    public void IsToggleButton_ButtonsWithAToggleState(string controlType, string ariaRole, int? toggleState, bool expected) =>
        Assert.Equal(expected, AriaStates.IsToggleButton(controlType, ariaRole, toggleState));

    [Fact]
    public void SpokenTexts()
    {
        Assert.Equal("invalid entry", AriaStates.InvalidText("true"));
        Assert.Equal("spelling error", AriaStates.InvalidText("spelling"));
        Assert.Equal("grammar error", AriaStates.InvalidText("grammar"));
        Assert.Null(AriaStates.InvalidText(""));
        Assert.Equal("current page", AriaStates.CurrentText("page"));
        Assert.Equal("current", AriaStates.CurrentText("true"));
        Assert.Null(AriaStates.CurrentText(""));
        Assert.Equal("sorted ascending", AriaStates.SortText("ascending"));
        Assert.Equal("sorted", AriaStates.SortText("other"));
        Assert.Null(AriaStates.SortText(""));
        Assert.Equal("pressed", AriaStates.PressedText(ControlState.ToggleOn));
        Assert.Equal("not pressed", AriaStates.PressedText(ControlState.ToggleOff));
        Assert.Equal("half pressed", AriaStates.PressedText(ControlState.ToggleIndeterminate));
    }
}

public class AriaStatesBufferTests
{
    private static VBufferDocument Build(params AriaElement[] children)
    {
        var root = new AriaElement { RuntimeId = [1], ControlType = "Document" };
        root.Children.AddRange(children);
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public void InvalidField_ErrorMessageIsTheTextOfItsErrorElement()
    {
        var error = new AriaElement { RuntimeId = [3], ControlType = "Group" };
        error.Children.Add(new AriaElement { RuntimeId = [4], Name = "Enter a valid" });
        error.Children.Add(new AriaElement { RuntimeId = [5], Name = "email address" });
        var doc = Build(
            new AriaElement { RuntimeId = [2], Name = "Email", ControlType = "Edit", IsDataValidForForm = false, ErrorMessageIds = [[3]] },
            error);

        var field = doc.FindByRuntimeId([2])!;
        Assert.Equal("true", field.Invalid);
        Assert.Equal("Enter a valid email address", field.ErrorMessage);
    }

    [Fact]
    public void ValidField_HasNoErrorMessage()
    {
        var doc = Build(
            new AriaElement { RuntimeId = [2], Name = "Email", ControlType = "Edit", IsDataValidForForm = true, ErrorMessageIds = [[3]] },
            new AriaElement { RuntimeId = [3], Name = "Enter a valid email address" });

        var field = doc.FindByRuntimeId([2])!;
        Assert.Equal("", field.Invalid);
        Assert.Equal("", field.ErrorMessage);
    }

    [Fact]
    public void ErrorMessage_LeavesOutAControlledPopupAndUnknownIds()
    {
        var list = new AriaElement { RuntimeId = [3], ControlType = "List", AriaRole = "listbox" };
        list.Children.Add(new AriaElement { RuntimeId = [4], Name = "France", ControlType = "ListItem" });
        var doc = Build(
            new AriaElement { RuntimeId = [2], Name = "Country", ControlType = "ComboBox", AriaProperties = "invalid=true", ErrorMessageIds = [[3], [9], [5]] },
            list,
            new AriaElement { RuntimeId = [5], Name = "Choose a country" });

        Assert.Equal("Choose a country", doc.FindByRuntimeId([2])!.ErrorMessage);
    }

    [Fact]
    public void ErrorMessage_ResolvedAgainAfterAnIncrementalUpdate()
    {
        var doc = Build(
            new AriaElement { RuntimeId = [2], Name = "Email", ControlType = "Edit", IsDataValidForForm = false, ErrorMessageIds = [[3]] },
            new AriaElement { RuntimeId = [3], Name = "Required" });

        var updated = new IncrementalUpdater().ApplyUpdate(doc, [3], new AriaElement { RuntimeId = [3], Name = "Enter an address" });

        Assert.Equal("Enter an address", updated.FindByRuntimeId([2])!.ErrorMessage);
        Assert.Equal("Required", doc.FindByRuntimeId([2])!.ErrorMessage);
    }

    [Fact]
    public void AriaStates_AreCaptured()
    {
        var doc = Build(
            new AriaElement { RuntimeId = [2], Name = "Home", ControlType = "Hyperlink", AriaRole = "link", AriaProperties = "current=page" },
            new AriaElement { RuntimeId = [3], Name = "Name", ControlType = "HeaderItem", AriaRole = "columnheader", AriaProperties = "sort=descending" },
            new AriaElement { RuntimeId = [4], Name = "Bold", ControlType = "Button", AriaRole = "button", AriaProperties = "pressed=true" },
            new AriaElement { RuntimeId = [5], Name = "Slide 1", ControlType = "Group", AriaRole = "group", RoleDescription = " slide ", HasDetails = true },
            new AriaElement { RuntimeId = [6], Name = "Save", ControlType = "Button", AcceleratorKey = "Control+S" });

        Assert.Equal("page", doc.FindByRuntimeId([2])!.Current);
        Assert.Equal("descending", doc.FindByRuntimeId([3])!.Sort);
        Assert.Equal(ControlState.ToggleOn, doc.FindByRuntimeId([4])!.ToggleState);
        Assert.Equal("slide", doc.FindByRuntimeId([5])!.RoleDescription);
        Assert.True(doc.FindByRuntimeId([5])!.HasDetails);
        Assert.Equal("Control+S", doc.FindByRuntimeId([6])!.AcceleratorKey);
    }

    [Fact]
    public void AriaPressed_OnlyAppliesToButtons()
    {
        var doc = Build(new AriaElement { RuntimeId = [2], Name = "Item", ControlType = "ListItem", AriaProperties = "pressed=true" });

        Assert.Null(doc.FindByRuntimeId([2])!.ToggleState);
    }

    [Fact]
    public void AriaStates_SurviveCloneDetached()
    {
        var node = new VBufferNode
        {
            Invalid = "spelling", ErrorMessageIds = [[3]], Current = "step", Sort = "ascending",
            RoleDescription = "slide", HasDetails = true, AcceleratorKey = "Alt+S",
        };

        var copy = node.CloneDetached(4, (0, 0));

        Assert.Equal("spelling", copy.Invalid);
        Assert.Equal([3], copy.ErrorMessageIds[0]);
        Assert.Equal("step", copy.Current);
        Assert.Equal("ascending", copy.Sort);
        Assert.Equal("slide", copy.RoleDescription);
        Assert.True(copy.HasDetails);
        Assert.Equal("Alt+S", copy.AcceleratorKey);
    }

    private sealed class AriaElement : IVBufferElement
    {
        public int[] RuntimeId { get; set; } = [];
        public string Name { get; set; } = string.Empty;
        public string ControlType { get; set; } = "Text";
        public string AriaRole { get; set; } = string.Empty;
        public string AriaProperties { get; set; } = string.Empty;
        public bool IsFocusable { get; set; }
        public bool? IsDataValidForForm { get; set; }
        public IReadOnlyList<int[]> ErrorMessageIds { get; set; } = [];
        public string RoleDescription { get; set; } = string.Empty;
        public string AcceleratorKey { get; set; } = string.Empty;
        public bool HasDetails { get; set; }
        public List<IVBufferElement> Children { get; } = new();
        public IReadOnlyList<IVBufferElement> GetChildren() => Children;
    }
}
