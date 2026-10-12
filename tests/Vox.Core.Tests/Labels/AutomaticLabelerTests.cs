using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Ai;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Labels;
using Vox.Core.Lifecycle;
using Vox.Core.Ocr;
using Vox.Core.Tests.Ai;
using Vox.Core.Tests.Buffer;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Labels;

public sealed class AutomaticLabelerTests : IDisposable
{
    private const string Address = "https://shop.example.com/cart";
    private const string Site = "https://shop.example.com";

    private sealed class FakeCapture : IScreenCapture
    {
        public bool IsAvailable { get; set; } = true;
        public List<ScreenRect> Captured { get; } = new();
        // The same picture for every place unless told otherwise
        public Func<ScreenRect, CapturedImage?> Picture { get; set; } = r => AiTestSupport.Image(seed: (byte)r.Left);

        public CapturedImage? Capture(ScreenRect bounds)
        {
            lock (Captured) Captured.Add(bounds);
            return Picture(bounds);
        }
    }

    private sealed class FakeBounds : IVisibleBoundsSource
    {
        public Func<VBufferNode, ScreenRect?> Bounds { get; set; } = n => new ScreenRect(n.UIARuntimeId[0] * 10, 100, 24, 24);
        public Task<IReadOnlyList<ScreenRect?>> VisibleBoundsAsync(IReadOnlyList<VBufferNode> nodes) =>
            Task.FromResult<IReadOnlyList<ScreenRect?>>(nodes.Select(Bounds).ToList());
    }

    private sealed class FakeContext : IAiContextSource
    {
        public Task<AiTarget> NavigatorTargetAsync() => Task.FromResult(new AiTarget(ProcessName: "msedge"));
        public Task<AiTarget> WindowTargetAsync() => Task.FromResult(new AiTarget(ProcessName: "msedge"));
        public Task<string?> WindowTextAsync() => Task.FromResult<string?>(null);
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxAutoLabels_" + Guid.NewGuid().ToString("N"));
    private readonly FakeDescriber _local = new("llava", isLocal: true) { Answer = "Shopping basket. A small icon of a basket." };
    private readonly FakeDescriber _cloud = new("Claude", isLocal: false) { Answer = "Search" };
    private readonly FakeConsentPrompt _prompt = new();
    private readonly FakeCapture _capture = new();
    private readonly FakeBounds _bounds = new();
    private readonly ManualTimeProvider _time = new();
    private VoxSettings _settings = new();
    private RunPolicy _policy = RunPolicy.Normal;
    private CustomLabelStore? _store;
    private int _recaptures;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private CustomLabelStore Store => _store ??= new CustomLabelStore(Path.Combine(_directory, "labels.json"));

    private AutomaticLabeler Labeler()
    {
        var settings = AiTestSupport.Settings(() => _settings);
        var ai = new AiAssistant(_local, _cloud, new AiConsent(_policy, settings), _prompt, new DescriptionCache(),
            NullLogger<AiAssistant>.Instance);
        return new AutomaticLabeler(ai, _capture, _bounds, new FakeContext(), Store, settings, () => _recaptures++,
            NullLogger<AutomaticLabeler>.Instance, _time, TimeSpan.Zero);
    }

    // Document / toolbar: unnamed button with icon "ic_basket.svg" (3), button "Checkout" (5),
    //   link with only an unnamed image (6, image 7); image "IMG_2041.jpg" (8); image "A red bicycle" (9);
    //   button "Search" with an unnamed icon inside (10, 11)
    private static VBufferDocument Page(string address = Address, int extraIcons = 0) =>
        new VBufferBuilder().Build(PageRoot(address, extraIcons));

    private async Task<int> RunAsync(AutomaticLabeler labeler, VBufferDocument document)
    {
        int added = -1;
        labeler.RunFinished += (_, count) => added = count;
        await labeler.OnDocumentLoaded(document);
        return added;
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ic_send.svg", true)]
    [InlineData("IMG_2041.JPG", true)]
    [InlineData("https://cdn.example.com/a/b.png", true)]
    [InlineData("image", true)]
    [InlineData("icon-search", true)]
    [InlineData("12345", true)]
    [InlineData("Search", false)]
    [InlineData("A red bicycle", false)]
    [InlineData("Annual report 2026", false)]
    public void UnhelpfulNames_AreEmptyFileNamesAndPlaceholders(string name, bool unhelpful) =>
        Assert.Equal(unhelpful, AutomaticLabeler.IsUnhelpful(name));

    [Fact]
    public void Candidates_AreUnnamedGraphicsAndIconOnlyControls()
    {
        var document = Page();

        var ids = AutomaticLabeler.Candidates(document).Select(n => n.UIARuntimeId[0]);

        // The basket button (not its icon), the image link (not its image), the file-named photo;
        // never a named control or picture, nor the icon inside a named button
        Assert.Equal([3, 6, 8], ids);
    }

    [Fact]
    public async Task Descriptions_BecomeDescribedLabels_AndThePageIsCapturedAgain()
    {
        _local.IsAvailable = true;

        var added = await RunAsync(Labeler(), Page());

        Assert.Equal(3, added);
        Assert.Equal(3, Store.All.Count);
        Assert.All(Store.All, l => Assert.True(l.Described));
        Assert.All(Store.All, l => Assert.Equal(Site, l.Site));
        // The first sentence, without its full stop
        Assert.Equal("Shopping basket", Store.Find(Site, "id:basket")!.Label);
        Assert.Equal(1, _recaptures);
        Assert.All(_local.Requests, r => Assert.Equal(AiPrompts.LabelQuestion, r.Question));
    }

    [Fact]
    public async Task ANewBuild_UsesTheDescribedLabels_AndOffersNothingMore()
    {
        _local.IsAvailable = true;
        await RunAsync(Labeler(), Page());

        var rebuilt = new VBufferBuilder { Labels = Store }.Build(PageRoot());

        Assert.Equal("Shopping basket", rebuilt.FindByRuntimeId([3])!.CustomLabel);
        Assert.Empty(AutomaticLabeler.Candidates(rebuilt));
    }

    private static MockElement PageRoot(string address = Address, int extraIcons = 0)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Value = address };
        var toolbar = new MockElement { RuntimeId = [2], ControlType = "ToolBar", AriaRole = "toolbar" };
        var basket = new MockElement { RuntimeId = [3], ControlType = "Button", IsFocusable = true, AutomationId = "basket" };
        basket.AddChild(new MockElement { RuntimeId = [4], Name = "ic_basket.svg", ControlType = "Image" });
        toolbar.AddChild(basket);
        toolbar.AddChild(new MockElement { RuntimeId = [5], Name = "Checkout", ControlType = "Button", IsFocusable = true });
        root.AddChild(toolbar);
        var link = new MockElement { RuntimeId = [6], ControlType = "Hyperlink", IsFocusable = true, AutomationId = "home" };
        link.AddChild(new MockElement { RuntimeId = [7], ControlType = "Image" });
        root.AddChild(link);
        root.AddChild(new MockElement { RuntimeId = [8], Name = "IMG_2041.jpg", ControlType = "Image", AutomationId = "photo" });
        root.AddChild(new MockElement { RuntimeId = [9], Name = "A red bicycle", ControlType = "Image" });
        var search = new MockElement { RuntimeId = [10], Name = "Search", ControlType = "Button", IsFocusable = true };
        search.AddChild(new MockElement { RuntimeId = [11], ControlType = "Image" });
        root.AddChild(search);
        for (int i = 0; i < extraIcons; i++)
            root.AddChild(new MockElement { RuntimeId = [20 + i], ControlType = "Button", IsFocusable = true, AutomationId = $"icon{i}" });
        return root;
    }

    [Fact]
    public async Task TheCloud_IsNeverAsked_WhileConsentIsAsk()
    {
        _local.IsAvailable = false;
        _settings = new VoxSettings { AiCloudProvider = AiCloudProvider.Claude, AiCloudConsent = AiConsentMode.Ask };

        var labeler = Labeler();
        var added = await RunAsync(labeler, Page());

        Assert.Equal(0, added);
        Assert.Empty(_cloud.Requests);
        Assert.Empty(_prompt.Questions);
        Assert.Empty(Store.All);
        Assert.Equal(0, _recaptures);
    }

    [Fact]
    public async Task TheCloud_IsUsed_WhenConsentIsAllow()
    {
        _local.IsAvailable = false;
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };

        var added = await RunAsync(Labeler(), Page());

        Assert.Equal(3, added);
        Assert.Equal(3, _cloud.Requests.Count);
        Assert.Empty(_prompt.Questions);
    }

    [Fact]
    public async Task APrivateSite_IsNeverSentToTheCloud()
    {
        _local.IsAvailable = false;
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow, AiPrivateSites = ["example.com"] };

        var added = await RunAsync(Labeler(), Page());

        Assert.Equal(0, added);
        Assert.Empty(_cloud.Requests);
    }

    [Fact]
    public async Task ThePageLimit_HoldsAcrossReloads()
    {
        _local.IsAvailable = true;
        var labeler = Labeler();

        await RunAsync(labeler, Page(extraIcons: 12));
        Assert.Equal(AutomaticLabeler.MaxPerPage, _local.Requests.Count);

        // The same page again: nothing new is described
        await RunAsync(labeler, Page(extraIcons: 12));
        Assert.Equal(AutomaticLabeler.MaxPerPage, _local.Requests.Count);
    }

    [Fact]
    public async Task TheMinuteLimit_HoldsAcrossPages_UntilAMinutePasses()
    {
        _local.IsAvailable = true;
        var labeler = Labeler();

        for (int page = 0; page < 4; page++)
            await RunAsync(labeler, Page($"https://site{page}.example.com/", extraIcons: 12));
        Assert.Equal(AutomaticLabeler.MaxPerMinute, Store.All.Count);

        _time.Advance(TimeSpan.FromMinutes(1));
        await RunAsync(labeler, Page("https://later.example.com/", extraIcons: 12));
        Assert.Equal(AutomaticLabeler.MaxPerMinute + AutomaticLabeler.MaxPerPage, Store.All.Count);
    }

    [Fact]
    public async Task TheSamePicture_OnTheSameSite_IsDescribedOnce()
    {
        _local.IsAvailable = true;
        _capture.Picture = _ => AiTestSupport.Image(seed: 9);

        var added = await RunAsync(Labeler(), Page());

        Assert.Equal(3, added);
        Assert.Single(_local.Requests);
        Assert.All(Store.All, l => Assert.Equal("Shopping basket", l.Label));
    }

    [Fact]
    public async Task ElementsOffTheScreen_OrTiny_AreLeftForLater()
    {
        _local.IsAvailable = true;
        _bounds.Bounds = n => n.UIARuntimeId[0] switch
        {
            3 => null,                          // scrolled out of view
            6 => new ScreenRect(60, 100, 4, 4), // a spacer
            _ => new ScreenRect(80, 100, 32, 32),
        };

        var added = await RunAsync(Labeler(), Page());

        Assert.Equal(1, added);
        Assert.NotNull(Store.Find(Site, "id:photo"));
    }

    [Fact]
    public async Task TheUsersOwnLabel_IsNeverReplaced()
    {
        _local.IsAvailable = true;
        Store.Set(new CustomLabel(Site, "id:basket", "My basket"));

        var document = new VBufferBuilder { Labels = Store }.Build(PageRoot());
        await RunAsync(Labeler(), document);

        Assert.Equal("My basket", Store.Find(Site, "id:basket")!.Label);
        Assert.False(Store.Find(Site, "id:basket")!.Described);
        Assert.Equal(2, _local.Requests.Count);
    }

    [Fact]
    public async Task TheSetting_TurnsItOff()
    {
        _local.IsAvailable = true;
        _settings = new VoxSettings { AutomaticLabels = false };

        var labeler = Labeler();
        Assert.False(labeler.IsActive);
        await labeler.OnDocumentLoaded(Page());

        Assert.Empty(_local.Requests);
        Assert.Empty(_capture.Captured);
    }

    [Fact]
    public async Task SecureScreens_AndTheAiPolicy_DescribeNothing()
    {
        _local.IsAvailable = true;
        _policy = RunPolicy.Secure;
        await Labeler().OnDocumentLoaded(Page());
        Assert.Empty(_capture.Captured);

        _policy = RunPolicy.Normal with { AiOffByPolicy = true };
        await Labeler().OnDocumentLoaded(Page());
        Assert.Empty(_capture.Captured);
        Assert.Empty(_local.Requests);
    }

    [Fact]
    public async Task WithoutALabelsFile_NothingIsDescribed()
    {
        _local.IsAvailable = true;
        _store = new CustomLabelStore(null);

        await Labeler().OnDocumentLoaded(Page());

        Assert.Empty(_capture.Captured);
    }

    [Fact]
    public async Task PagesThatAreNoWebSite_AreLeftAlone()
    {
        _local.IsAvailable = true;

        await Labeler().OnDocumentLoaded(Page("file:///C:/report.pdf"));

        Assert.Empty(_capture.Captured);
    }

    [Theory]
    [InlineData("Shopping basket. A small icon.", "Shopping basket")]
    [InlineData("Version 2.5 release notes", "Version 2.5 release notes")]
    [InlineData("  Close  \n", "Close")]
    public void Shorten_KeepsTheFirstSentence(string description, string label) =>
        Assert.Equal(label, AutomaticLabeler.Shorten(description));

    [Fact]
    public void Shorten_CutsLongDescriptionsAtAWord()
    {
        var label = AutomaticLabeler.Shorten(string.Join(" ", Enumerable.Repeat("word", 40)));
        Assert.True(label.Length <= AutomaticLabeler.MaxLabelLength);
        Assert.EndsWith("word", label);
    }
}
