using System.Text.RegularExpressions;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Localization;

/// <summary>
/// With a pseudo locale in place every word Vox adds is pseudo-localized, so output with an
/// ASCII letter in it has English that didn't come from the resources. The pages' own text in
/// these nodes is in other scripts, so it can't hide any.
/// </summary>
public class PseudoLocaleTests
{
    private static readonly Regex English = new("[A-Za-z]");

    private static readonly Dictionary<string, VBufferNode> Nodes = BuildNodes();

    public static TheoryData<string> Cases => new(Nodes.Keys);

    /// <summary>Nodes that between them reach every part of an announcement.</summary>
    private static Dictionary<string, VBufferNode> BuildNodes()
    {
        var data = new Dictionary<string, VBufferNode>();
        void Add(string what, VBufferNode node) => data[what] = node;
        foreach (var level in Enumerable.Range(1, 6))
            Add($"heading {level}", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "見出し", ControlType = "Text", HeadingLevel = level });
        foreach (var landmark in new[] { "Banner", "Complementary", "Content info", "Form", "Main", "Navigation", "Region", "Search" })
            Add(landmark, new VBufferNode { Id = 1, UIARuntimeId = [1], ControlType = "Group", LandmarkType = landmark });
        foreach (var type in new[]
        {
            "AppBar", "Button", "Calendar", "CheckBox", "ComboBox", "DataGrid", "DataItem", "Document", "Edit",
            "Header", "HeaderItem", "Hyperlink", "Image", "List", "ListItem", "Menu", "MenuBar", "MenuItem",
            "ProgressBar", "RadioButton", "ScrollBar", "SemanticZoom", "Separator", "Slider", "Spinner",
            "SplitButton", "StatusBar", "Tab", "TabItem", "Table", "Thumb", "TitleBar", "ToolBar", "ToolTip",
            "Tree", "TreeItem", "Window", "Text", "Group", "Pane", "Custom",
        })
            Add(type, new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = type });
        Add("visited link", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "リンク", ControlType = "Hyperlink", IsLink = true, IsVisited = true });
        Add("required edit", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "Edit", IsRequired = true, Value = "値" });
        Add("expanded", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "Button", IsExpandable = true, IsExpanded = true });
        Add("collapsed", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "TreeItem", IsExpandable = true, Level = 2, PositionInSet = 1, SizeOfSet = 3 });
        foreach (var toggle in new[] { ControlState.ToggleOff, ControlState.ToggleOn, ControlState.ToggleIndeterminate })
        {
            Add($"check box {toggle}", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "CheckBox", ToggleState = toggle });
            Add($"toggle button {toggle}", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "Button", ToggleState = toggle });
        }
        Add("radio checked", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "RadioButton", IsSelected = true });
        Add("radio not checked", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "RadioButton", IsSelected = false });
        Add("selected", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "ListItem", IsSelected = true });
        foreach (var invalid in new[] { "true", "spelling", "grammar" })
            Add($"invalid {invalid}", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "Edit", Invalid = invalid, ErrorMessage = "エラー" });
        foreach (var current in new[] { "true", "page", "step", "location", "date", "time" })
            Add($"current {current}", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "Hyperlink", IsLink = true, Current = current });
        foreach (var sort in new[] { "ascending", "descending", "other" })
            Add($"sort {sort}", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "HeaderItem", Sort = sort });
        Add("details", new VBufferNode
        {
            Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "Button", HasDetails = true,
            Description = "説明", AcceleratorKey = "Ⅹ", AccessKey = "Ⅺ",
        });
        Add("role description", new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "Group", AriaRole = "region", RoleDescription = "スライド" });
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Announcement_HasNoEnglish_InThePseudoLocale(string what)
    {
        using var _ = VoxStrings.Use(new PseudoLocalizer());
        var builder = new AnnouncementBuilder();
        var node = Nodes[what];

        foreach (var level in Enum.GetValues<VerbosityLevel>())
        {
            var text = builder.Build(node, VerbosityProfile.For(level));
            Assert.False(English.IsMatch(text), $"{what} at {level}: \"{text}\" has English in it");
        }
    }

    [Fact]
    public void Announcement_IsPseudoLocalized_NotJustEmpty()
    {
        using var _ = VoxStrings.Use(new PseudoLocalizer());

        var text = new AnnouncementBuilder().Build(
            new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "名前", ControlType = "CheckBox", ToggleState = ControlState.ToggleOn },
            VerbosityLevel.Beginner);

        Assert.Equal("名前, ćĥéćķ ƀöẋ, ćĥéćķéď", text);
    }

    [Fact]
    public void InjectedLocalizer_IsUsed_ForTheBuildersOwnWords()
    {
        var builder = new AnnouncementBuilder(new TypedPseudoLocalizer());

        var text = builder.Build(
            new VBufferNode { Id = 1, UIARuntimeId = [1], Name = "見出し", ControlType = "Text", HeadingLevel = 2 },
            VerbosityLevel.Beginner);

        Assert.Equal("見出し".Insert(0, "ĥéàďîñĝ ļéṽéļ 2, "), text);
    }

    [Fact]
    public async Task PseudoLocale_DoesNotLeakIntoOtherFlows()
    {
        var outside = new Thread(() => { });
        string? seenOutside = null;
        using (VoxStrings.Use(new PseudoLocalizer()))
        {
            Assert.Equal("ćĥéćķéď", VoxStrings.Get("State.Checked"));
            // A task started in the scope flows with it
            Assert.Equal("ćĥéćķéď", await Task.Run(() => VoxStrings.Get("State.Checked")));
            // A thread started without the flow's context does not
            using (ExecutionContext.SuppressFlow())
            {
                outside = new Thread(() => seenOutside = VoxStrings.Get("State.Checked"));
                outside.Start();
            }
            outside.Join();
        }
        Assert.Equal("checked", seenOutside);
        Assert.Equal("checked", VoxStrings.Get("State.Checked"));
    }

    private sealed class TypedPseudoLocalizer : Microsoft.Extensions.Localization.IStringLocalizer<AnnouncementBuilder>
    {
        private readonly PseudoLocalizer _inner = new();
        public Microsoft.Extensions.Localization.LocalizedString this[string name] => _inner[name];
        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments] => _inner[name, arguments];
        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
