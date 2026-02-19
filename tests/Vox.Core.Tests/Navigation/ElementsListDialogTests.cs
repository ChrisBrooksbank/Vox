using System.Windows.Forms;
using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

/// <summary>
/// Unit tests for ElementsListViewModel (pure logic, no WinForms required)
/// and basic smoke tests for ElementsListDialog (WinForms; run on STA thread).
/// </summary>
public class ElementsListDialogTests
{
    // -------------------------------------------------------------------------
    // STA helper (for WinForms smoke tests)
    // -------------------------------------------------------------------------

    private static void RunOnSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            throw new Xunit.Sdk.XunitException($"STA thread threw: {caught}", caught);
    }

    // -------------------------------------------------------------------------
    // Document helpers
    // -------------------------------------------------------------------------

    private static VBufferDocument BuildDocument(IEnumerable<VBufferNode> nodes)
    {
        var root = new VBufferNode { Id = 0, UIARuntimeId = [0], ControlType = "Document", Name = "doc" };
        var all  = new List<VBufferNode> { root };
        int id = 1, pos = 0;
        foreach (var n in nodes)
        {
            n.GetType().GetProperty(nameof(VBufferNode.Id))!.SetValue(n, id++);
            n.TextRange = (pos, pos + n.Name.Length);
            pos += n.Name.Length + 1;
            all.Add(n);
        }
        var flatText = string.Join("\n", all.Skip(1).Select(n => n.Name));
        return new VBufferDocument(flatText, root, all);
    }

    private static VBufferNode Heading(int level, string name) =>
        new VBufferNode { UIARuntimeId = [level, 0], Name = name, ControlType = "Heading", HeadingLevel = level };

    private static VBufferNode Link(string name, bool visited = false) =>
        new VBufferNode { UIARuntimeId = [10, 0], Name = name, ControlType = "Hyperlink", IsLink = true, IsVisited = visited };

    private static VBufferNode Landmark(string type, string name = "") =>
        new VBufferNode { UIARuntimeId = [20, 0], Name = name, ControlType = "Group", LandmarkType = type };

    private static VBufferNode FormField(string name) =>
        new VBufferNode { UIARuntimeId = [30, 0], Name = name, ControlType = "Edit" };

    private static VBufferDocument EmptyDocument()
    {
        var root = new VBufferNode { Id = 0, UIARuntimeId = [0], ControlType = "Document", Name = "doc" };
        return new VBufferDocument("", root, new[] { root });
    }

    // =========================================================================
    // ElementsListViewModel tests (no WinForms, runs on MTA)
    // =========================================================================

    [Fact]
    public void ViewModel_Constructor_NullDocument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementsListViewModel(null!));
    }

    [Fact]
    public void ViewModel_DefaultTabIsHeadings()
    {
        var vm = new ElementsListViewModel(EmptyDocument());
        Assert.Equal(0, vm.SelectedTabIndex);
    }

    [Fact]
    public void ViewModel_HeadingsTab_ReturnsHeadingNodes()
    {
        var doc = BuildDocument(new[]
        {
            Heading(1, "Introduction"),
            Heading(2, "Details"),
            Link("Click me"),
        });

        var vm = new ElementsListViewModel(doc);
        vm.SelectedTabIndex = 0; // Headings

        var items = vm.GetFilteredItems();
        Assert.Equal(2, items.Count);
        Assert.All(items, n => Assert.True(n.IsHeading));
    }

    [Fact]
    public void ViewModel_LinksTab_ReturnsLinkNodes()
    {
        var doc = BuildDocument(new[]
        {
            Heading(1, "Title"),
            Link("Home"),
            Link("About"),
        });

        var vm = new ElementsListViewModel(doc);
        vm.SelectedTabIndex = 1; // Links

        var items = vm.GetFilteredItems();
        Assert.Equal(2, items.Count);
        Assert.All(items, n => Assert.True(n.IsLink));
    }

    [Fact]
    public void ViewModel_LandmarksTab_ReturnsLandmarkNodes()
    {
        var doc = BuildDocument(new[]
        {
            Landmark("main", "Main content"),
            Landmark("nav", "Primary nav"),
            Heading(1, "Not a landmark"),
        });

        var vm = new ElementsListViewModel(doc);
        vm.SelectedTabIndex = 2; // Landmarks

        var items = vm.GetFilteredItems();
        Assert.Equal(2, items.Count);
        Assert.All(items, n => Assert.True(n.IsLandmark));
    }

    [Fact]
    public void ViewModel_FormFieldsTab_ReturnsFormFieldNodes()
    {
        var doc = BuildDocument(new[]
        {
            FormField("Search"),
            FormField("Password"),
            Heading(1, "Not a form field"),
        });

        var vm = new ElementsListViewModel(doc);
        vm.SelectedTabIndex = 3; // Form Fields

        var items = vm.GetFilteredItems();
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void ViewModel_TabSwitch_ResetsFilter()
    {
        var doc = BuildDocument(new[]
        {
            Heading(1, "Alpha"),
            Heading(2, "Beta"),
        });

        var vm = new ElementsListViewModel(doc);
        vm.SelectedTabIndex = 0;
        vm.FilterText = "Alpha";

        // Switch tab
        vm.SelectedTabIndex = 1;

        Assert.Equal(string.Empty, vm.FilterText);
    }

    [Fact]
    public void ViewModel_Filter_CaseInsensitive_NarrowsResults()
    {
        var doc = BuildDocument(new[]
        {
            Heading(1, "Introduction"),
            Heading(2, "Background"),
            Heading(3, "Conclusion"),
        });

        var vm = new ElementsListViewModel(doc);
        vm.FilterText = "intro";

        var items = vm.GetFilteredItems();
        Assert.Single(items);
        Assert.Equal("Introduction", items[0].Name);
    }

    [Fact]
    public void ViewModel_Filter_EmptyString_ShowsAll()
    {
        var doc = BuildDocument(new[]
        {
            Heading(1, "Alpha"),
            Heading(2, "Beta"),
            Heading(3, "Gamma"),
        });

        var vm = new ElementsListViewModel(doc);
        vm.FilterText = "Alpha";
        Assert.Single(vm.GetFilteredItems());

        vm.FilterText = string.Empty;
        Assert.Equal(3, vm.GetFilteredItems().Count);
    }

    [Fact]
    public void ViewModel_Filter_NoMatch_ReturnsEmpty()
    {
        var doc = BuildDocument(new[] { Heading(1, "Introduction") });
        var vm = new ElementsListViewModel(doc);
        vm.FilterText = "xyzzy";

        Assert.Empty(vm.GetFilteredItems());
    }

    [Fact]
    public void ViewModel_Filter_WhitespaceOnly_TreatedAsEmpty()
    {
        var doc = BuildDocument(new[]
        {
            Heading(1, "Alpha"),
            Heading(2, "Beta"),
        });

        var vm = new ElementsListViewModel(doc);
        vm.FilterText = "   ";

        Assert.Equal(2, vm.GetFilteredItems().Count);
    }

    // -------------------------------------------------------------------------
    // GetDisplayText tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "Introduction", "H1: Introduction")]
    [InlineData(2, "Details",      "H2: Details")]
    [InlineData(6, "Deep",         "H6: Deep")]
    public void GetDisplayText_Heading_IncludesLevelPrefix(int level, string name, string expected)
    {
        var node = Heading(level, name);
        Assert.Equal(expected, ElementsListViewModel.GetDisplayText(node));
    }

    [Fact]
    public void GetDisplayText_Landmark_WithName_IncludesBoth()
    {
        var node = Landmark("nav", "Primary navigation");
        var display = ElementsListViewModel.GetDisplayText(node);
        Assert.Contains("nav", display, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Primary navigation", display);
    }

    [Fact]
    public void GetDisplayText_Landmark_WithoutName_FallsBackToType()
    {
        var node = Landmark("main");
        Assert.Equal("main", ElementsListViewModel.GetDisplayText(node));
    }

    [Fact]
    public void GetDisplayText_Link_ReturnsName()
    {
        var node = Link("Click here");
        Assert.Equal("Click here", ElementsListViewModel.GetDisplayText(node));
    }

    [Fact]
    public void GetDisplayText_NodeWithoutName_FallsBackToControlType()
    {
        var node = new VBufferNode { UIARuntimeId = [99, 0], Name = "", ControlType = "Button" };
        Assert.Equal("[Button]", ElementsListViewModel.GetDisplayText(node));
    }

    // =========================================================================
    // ElementsListDialog smoke tests (WinForms; must run on STA thread)
    // =========================================================================

    [Fact]
    public void Dialog_Constructor_DoesNotThrow()
    {
        RunOnSta(() =>
        {
            using var dlg = new ElementsListDialog(EmptyDocument());
            Assert.Equal("Elements List", dlg.Text);
        });
    }

    [Fact]
    public void Dialog_Constructor_NullDocument_Throws()
    {
        RunOnSta(() =>
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                using var _ = new ElementsListDialog(null!);
            });
        });
    }

    [Fact]
    public void Dialog_SelectedNode_InitiallyNull()
    {
        RunOnSta(() =>
        {
            using var dlg = new ElementsListDialog(EmptyDocument());
            Assert.Null(dlg.SelectedNode);
        });
    }
}
