using Vox.Core.Braille;
using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrailleLabelsTests
{
    private static string Describe(BrailleObject obj) => BrailleLabels.Default.Describe(obj);

    [Theory]
    [InlineData("Button", "OK btn")]
    [InlineData("Edit", "OK edt")]
    [InlineData("ComboBox", "OK cbo")]
    [InlineData("ListItem", "OK lstitm")]
    [InlineData("Image", "OK gra")]
    [InlineData("Window", "OK wnd")]
    public void ControlTypes_HaveTheirRoleLabels(string controlType, string expected)
    {
        Assert.Equal(expected, Describe(new BrailleObject { Name = "OK", ControlType = controlType }));
    }

    [Theory]
    [InlineData(1, "Intro h1")]
    [InlineData(2, "Intro h2")]
    [InlineData(6, "Intro h6")]
    public void Heading_IsLabelledWithItsLevel(int level, string expected)
    {
        Assert.Equal(expected, Describe(new BrailleObject { Name = "Intro", ControlType = "Text", HeadingLevel = level }));
    }

    [Fact]
    public void Heading_DoesNotAlsoShowItsLevelAsATreeLevel()
    {
        Assert.Equal("Intro h2", Describe(new BrailleObject { Name = "Intro", ControlType = "Text", HeadingLevel = 2, Level = 2 }));
    }

    [Theory]
    [InlineData("navigation", "nav")]
    [InlineData("main", "main")]
    [InlineData("banner", "bnnr")]
    [InlineData("contentinfo", "cinf")]
    [InlineData("search", "srch")]
    public void Landmarks_HaveTheirLabels(string landmark, string expected)
    {
        Assert.Equal(expected, Describe(new BrailleObject { ControlType = "Group", LandmarkType = landmark }));
    }

    [Fact]
    public void NamedLandmark_ShowsNameThenLabel()
    {
        Assert.Equal("Site menu nav", Describe(new BrailleObject { Name = "Site menu", ControlType = "Group", LandmarkType = "navigation" }));
    }

    [Fact]
    public void UnknownLandmark_ShowsItsType()
    {
        Assert.Equal("toolbox", Describe(new BrailleObject { ControlType = "Group", LandmarkType = "toolbox" }));
    }

    [Fact]
    public void Link_IsLnk_AndVisitedAddsV()
    {
        Assert.Equal("Home lnk", Describe(new BrailleObject { Name = "Home", ControlType = "Hyperlink", IsLink = true }));
        Assert.Equal("Home lnk v", Describe(new BrailleObject { Name = "Home", ControlType = "Hyperlink", IsLink = true, IsVisited = true }));
    }

    [Fact]
    public void LinkByFlag_IsLnkWhateverItsControlType()
    {
        Assert.Equal("Home lnk", Describe(new BrailleObject { Name = "Home", ControlType = "Text", IsLink = true }));
    }

    [Fact]
    public void Visited_IsOnlyShownForLinks()
    {
        Assert.Equal("OK btn", Describe(new BrailleObject { Name = "OK", ControlType = "Button", IsVisited = true }));
    }

    [Theory]
    [InlineData(ControlState.ToggleOff, "Remember me chk ( )")]
    [InlineData(ControlState.ToggleOn, "Remember me chk (x)")]
    [InlineData(ControlState.ToggleIndeterminate, "Remember me chk (-)")]
    public void CheckBox_ShowsItsState(int toggle, string expected)
    {
        Assert.Equal(expected, Describe(new BrailleObject { Name = "Remember me", ControlType = "CheckBox", ToggleState = toggle }));
    }

    [Theory]
    [InlineData(ControlState.ToggleOn, "Bold btn (x)")]
    [InlineData(ControlState.ToggleOff, "Bold btn ( )")]
    public void ToggleButton_ShowsPressedState(int toggle, string expected)
    {
        Assert.Equal(expected, Describe(new BrailleObject { Name = "Bold", ControlType = "Button", ToggleState = toggle }));
    }

    [Fact]
    public void ToggleButton_ByAriaRole_ShowsPressedState()
    {
        var labels = BrailleLabels.FromJson("""{"states": {"pressed": "prs", "notPressed": "nprs", "checked": "chkd"}}""");

        Assert.Equal("Bold prs", labels.Describe(new BrailleObject { Name = "Bold", ControlType = "Custom", AriaRole = "button", ToggleState = 1 }));
        Assert.Equal("Bold nprs", labels.Describe(new BrailleObject { Name = "Bold", ControlType = "Custom", AriaRole = "button", ToggleState = 0 }));
    }

    [Theory]
    [InlineData(true, "Small rbtn (x)")]
    [InlineData(false, "Small rbtn ( )")]
    public void RadioButton_ShowsCheckedState(bool selected, string expected)
    {
        Assert.Equal(expected, Describe(new BrailleObject { Name = "Small", ControlType = "RadioButton", IsSelected = selected }));
    }

    [Fact]
    public void SelectedItem_ShowsSel_UnselectedShowsNothing()
    {
        Assert.Equal("Apples lstitm sel", Describe(new BrailleObject { Name = "Apples", ControlType = "ListItem", IsSelected = true }));
        Assert.Equal("Apples lstitm", Describe(new BrailleObject { Name = "Apples", ControlType = "ListItem", IsSelected = false }));
    }

    [Theory]
    [InlineData(true, "Color cbo Red -")]
    [InlineData(false, "Color cbo Red +")]
    public void Expandable_ShowsExpandedOrCollapsed(bool expanded, string expected)
    {
        Assert.Equal(expected, Describe(new BrailleObject
        {
            Name = "Color", ControlType = "ComboBox", Value = "Red", IsExpandable = true, IsExpanded = expanded,
        }));
    }

    [Fact]
    public void Value_IsShownForValueControls_NotWhenItRepeatsTheName()
    {
        Assert.Equal("Search edt cats", Describe(new BrailleObject { Name = "Search", ControlType = "Edit", Value = "cats" }));
        Assert.Equal("cats edt", Describe(new BrailleObject { Name = "cats", ControlType = "Edit", Value = "cats" }));
        Assert.Equal("OK btn", Describe(new BrailleObject { Name = "OK", ControlType = "Button", Value = "something" }));
    }

    [Fact]
    public void Value_IsFlattenedToOneLine()
    {
        Assert.Equal("Notes edt one two three", Describe(new BrailleObject { Name = "Notes", ControlType = "Edit", Value = "one\r\ntwo\t  three" }));
    }

    [Fact]
    public void Password_ValueIsHidden()
    {
        var text = Describe(new BrailleObject { Name = "Password", ControlType = "Edit", Value = "hunter2", IsPassword = true });

        Assert.Equal("Password edt ***", text);
        Assert.DoesNotContain("hunter2", text);
    }

    [Fact]
    public void EmptyPassword_ShowsNoValueMarker()
    {
        Assert.Equal("Password edt", Describe(new BrailleObject { Name = "Password", ControlType = "Edit", IsPassword = true }));
    }

    [Fact]
    public void Position_IsShownAsNOfM()
    {
        Assert.Equal("Apples lstitm 2/5", Describe(new BrailleObject { Name = "Apples", ControlType = "ListItem", PositionInSet = 2, SizeOfSet = 5 }));
    }

    [Fact]
    public void Level_IsShownForTreeItems()
    {
        Assert.Equal("Docs tvitm + lv 2 1/3", Describe(new BrailleObject
        {
            Name = "Docs", ControlType = "TreeItem", IsExpandable = true, Level = 2, PositionInSet = 1, SizeOfSet = 3,
        }));
    }

    [Fact]
    public void RequiredInvalidCurrentReadOnly_AreShown()
    {
        Assert.Equal("Email edt req invld", Describe(new BrailleObject { Name = "Email", ControlType = "Edit", IsRequired = true, Invalid = "true" }));
        Assert.Equal("Email edt", Describe(new BrailleObject { Name = "Email", ControlType = "Edit", Invalid = "false" }));
        Assert.Equal("Home lnk cur", Describe(new BrailleObject { Name = "Home", IsLink = true, Current = "page" }));
        Assert.Equal("Log edt text ro", Describe(new BrailleObject { Name = "Log", ControlType = "Edit", Value = "text", IsReadOnly = true }));
    }

    [Fact]
    public void RoleDescription_ReplacesTheRole()
    {
        Assert.Equal("Inbox slide", Describe(new BrailleObject { Name = "Inbox", ControlType = "Group", RoleDescription = " slide " }));
    }

    [Fact]
    public void AriaRole_HasItsLabel()
    {
        Assert.Equal("Settings dlg", Describe(new BrailleObject { Name = "Settings", ControlType = "Window", AriaRole = "dialog" }));
    }

    [Fact]
    public void StructuralTypes_HaveNoRoleLabel()
    {
        Assert.Equal("Hello", Describe(new BrailleObject { Name = "Hello", ControlType = "Text" }));
        Assert.Equal(string.Empty, Describe(new BrailleObject { ControlType = "Group" }));
        Assert.Null(BrailleLabels.Default.Role(new BrailleObject { ControlType = "Pane" }));
    }

    [Fact]
    public void Name_IsFlattenedToOneLine()
    {
        Assert.Equal("Sign in now btn", Describe(new BrailleObject { Name = "  Sign in\n now ", ControlType = "Button" }));
    }

    [Fact]
    public void State_WithoutLabel_IsItsName()
    {
        Assert.Equal("(x)", BrailleLabels.Default.State("checked"));
        Assert.Equal("somethingElse", BrailleLabels.Default.State("somethingElse"));
    }

    [Fact]
    public void FromJson_OverridesLabels_AndKeepsFormatDefaults()
    {
        var labels = BrailleLabels.FromJson("""
            {
              "roles": { "Button": "knop", "CheckBox": "kvk" },
              "states": { "checked": "[x]" },
              "heading": "k{0}"
            }
            """);

        Assert.Equal("OK knop", labels.Describe(new BrailleObject { Name = "OK", ControlType = "Button" }));
        Assert.Equal("Agree kvk [x]", labels.Describe(new BrailleObject { Name = "Agree", ControlType = "CheckBox", ToggleState = 1 }));
        Assert.Equal("Intro k3", labels.Describe(new BrailleObject { Name = "Intro", HeadingLevel = 3 }));
        // Not in the table: the default position format, the state's name, "lnk" for links
        Assert.Equal("Item 1/2", labels.Describe(new BrailleObject { Name = "Item", PositionInSet = 1, SizeOfSet = 2 }));
        Assert.Equal("Agree kvk notChecked", labels.Describe(new BrailleObject { Name = "Agree", ControlType = "CheckBox", ToggleState = 0 }));
        Assert.Equal("Home lnk", labels.Describe(new BrailleObject { Name = "Home", IsLink = true }));
    }

    [Fact]
    public void FromJson_PropertyNamesAreCaseInsensitive()
    {
        var labels = BrailleLabels.FromJson("""{"Roles": {"button": "B"}}""");

        Assert.Equal("OK B", labels.Describe(new BrailleObject { Name = "OK", ControlType = "Button" }));
    }
}
