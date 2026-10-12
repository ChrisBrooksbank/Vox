using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public sealed class SiteSettingsTests : IDisposable
{
    private const string Site = "https://mail.example.com";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxSites_" + Guid.NewGuid().ToString("N"));
    private static readonly VoxSettings Normal = new() { SpeechRateWpm = 200, VerbosityLevel = VerbosityLevel.Intermediate };

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private SiteSettingsStore Sites() => new(Path.Combine(_directory, SiteSettingsStore.FileName));

    private ProfileManager Profiles(SiteSettingsStore? sites = null)
    {
        var store = new ProfileStore(Path.Combine(_directory, "profiles"));
        store.Save(new ConfigurationProfile("Browser", new JsonObject { ["SpeechRateWpm"] = 300, ["VerbosityLevel"] = "Advanced" }, ["msedge"]));
        store.Save(new ConfigurationProfile("Reading", new JsonObject { ["VerbosityLevel"] = "Intermediate" }, [], SayAll: true));
        store.Save(new ConfigurationProfile("Quiet", new JsonObject { ["ScreenLayout"] = true }, []));
        return new ProfileManager(store, sites ?? Sites());
    }

    [Fact]
    public void Store_KeepsOnlySiteSettings_ByTheSite_AndNoneRemovesIt()
    {
        var sites = Sites();

        Assert.True(sites.Set(Site, new JsonObject { ["VerbosityLevel"] = "Beginner", ["SpeechRateWpm"] = 500 }));

        var reloaded = Sites();
        Assert.Equal([Site], reloaded.Sites);
        var settings = reloaded.For(Site);
        Assert.Equal(["VerbosityLevel"], settings.Select(p => p.Key));
        Assert.Empty(reloaded.For("https://other.org"));

        Assert.True(reloaded.Set(Site, new JsonObject()));
        Assert.Empty(Sites().Sites);
        Assert.False(new SiteSettingsStore(null).Set(Site, new JsonObject { ["ScreenLayout"] = false }));
    }

    [Fact]
    public void TheSitesSettings_AreAboveTheApplicationsProfile_BelowSayAllAndTheHandChosenOne()
    {
        var profiles = Profiles();
        profiles.SetSiteSettings(Site, new JsonObject { ["VerbosityLevel"] = "Beginner", ["ScreenLayout"] = false });

        profiles.SetForegroundApp("msedge");
        Assert.Equal(VerbosityLevel.Advanced, profiles.Apply(Normal).VerbosityLevel);

        profiles.SetSite(Site);
        var browsing = profiles.Apply(Normal);
        Assert.Equal(VerbosityLevel.Beginner, browsing.VerbosityLevel);
        Assert.False(browsing.ScreenLayout);
        // What the site doesn't set still comes from the application's profile
        Assert.Equal(300, browsing.SpeechRateWpm);
        Assert.True(profiles.HasSiteSettings);
        Assert.Equal(VerbosityLevel.Advanced, profiles.Apply(Normal, includeSite: false).VerbosityLevel);

        profiles.SetSayAll(true);
        Assert.Equal(VerbosityLevel.Intermediate, profiles.Apply(Normal).VerbosityLevel);
        profiles.SetSayAll(false);

        profiles.ActivateManually("Quiet");
        Assert.True(profiles.Apply(Normal).ScreenLayout);
        Assert.Equal(VerbosityLevel.Beginner, profiles.Apply(Normal).VerbosityLevel);
        profiles.ActivateManually(null);

        // Without an application profile the site is laid over the normal configuration
        profiles.SetForegroundApp("chrome");
        Assert.Equal(VerbosityLevel.Beginner, profiles.Apply(Normal).VerbosityLevel);
        Assert.Equal(200, profiles.Apply(Normal).SpeechRateWpm);

        profiles.SetSite(null);
        Assert.Same(Normal, profiles.Apply(Normal));
    }

    [Fact]
    public void ChangingSite_ChangesTheSettings_OnlyWhenTheSitesSettingsDiffer()
    {
        var sites = Sites();
        sites.Set(Site, new JsonObject { ["WebStartMode"] = "Focus" });
        sites.Set("https://a.org", new JsonObject { ["WebStartMode"] = "Focus" });
        var profiles = Profiles(sites);
        int changes = 0;
        profiles.Changed += (_, _) => changes++;

        profiles.SetSite("https://plain.org");
        Assert.Equal(0, changes);
        profiles.SetSite(Site);
        Assert.Equal(1, changes);
        profiles.SetSite("https://a.org");
        Assert.Equal(1, changes);
        profiles.SetSite(null);
        Assert.Equal(2, changes);

        // Settings kept for the site being browsed apply at once
        profiles.SetSite(Site);
        profiles.SetSiteSettings(Site, new JsonObject { ["WebStartMode"] = "Browse" });
        Assert.Equal(4, changes);
        Assert.Equal(WebStartMode.Browse, profiles.Apply(Normal).WebStartMode);
    }

    [Fact]
    public void EditsWhileOnASiteWithSettings_GoToTheNormalConfiguration_WithoutTheSites()
    {
        var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "defaults.json"), Path.Combine(_directory, "settings.json"));
        using var monitor = new SettingsMonitor(manager, NullLogger<SettingsMonitor>.Instance);
        var sites = Sites();
        sites.Set(Site, new JsonObject { ["VerbosityLevel"] = "Beginner" });
        var profiles = new ProfileManager(new ProfileStore(null), sites);
        monitor.AttachProfiles(profiles);

        profiles.SetSite(Site);
        Assert.Equal(VerbosityLevel.Beginner, monitor.CurrentValue.VerbosityLevel);

        monitor.UpdateSettings(monitor.CurrentValue with { SpeechRateWpm = 333 });

        Assert.Equal(333, manager.Load().SpeechRateWpm);
        Assert.Equal(new VoxSettings().VerbosityLevel, manager.Load().VerbosityLevel);
        profiles.SetSite(null);
        Assert.Equal(333, monitor.CurrentValue.SpeechRateWpm);
        Assert.Equal(new VoxSettings().VerbosityLevel, monitor.CurrentValue.VerbosityLevel);
    }

    [Fact]
    public void Editor_OffersAsUsualThenEachValue_AndGivesOnlyWhatIsSet()
    {
        var editor = new SiteSettingsEditor(Site, new JsonObject { ["ScreenLayout"] = false, ["VerbosityLevel"] = "Beginner" },
            new VoxSettings { VerbosityLevel = VerbosityLevel.Advanced });

        Assert.Equal("mail.example.com", editor.SiteName);
        Assert.Equal(["WebStartMode", "VerbosityLevel", "ScreenLayout", "AutoDismissOverlays"], editor.Fields.Select(f => f.Key));
        var verbosity = editor.Fields[1];
        Assert.StartsWith("As usual (", verbosity.Choices[0].Text);
        Assert.Contains(SettingsPages.ChoiceName(VerbosityLevel.Advanced), verbosity.Choices[0].Text);
        Assert.Equal(1 + (int)VerbosityLevel.Beginner, verbosity.Selected);
        Assert.Equal(2, editor.Fields[2].Selected); // Off
        Assert.Equal(0, editor.Fields[0].Selected);

        editor.Fields[1].Selected = 0;
        editor.Fields[3].Selected = 1; // On
        editor.Fields[0].Selected = 3; // Focus

        var json = editor.ToJson();
        Assert.Equal(["AutoDismissOverlays", "ScreenLayout", "WebStartMode"], json.Select(p => p.Key).Order());
        var applied = ProfileSettings.Merge(new VoxSettings(), [json]);
        Assert.True(applied.AutoDismissOverlays);
        Assert.False(applied.ScreenLayout);
        Assert.Equal(WebStartMode.Focus, applied.WebStartMode);
    }
}

/// <summary>What the browse controller does with the site and its settings.</summary>
public sealed class SiteSettingsBrowsingTests : IDisposable
{
    private sealed class CaptureSink : IEventSink
    {
        private readonly List<ScreenReaderEvent> _events = new();
        public void Post(ScreenReaderEvent evt) { lock (_events) _events.Add(evt); }
        public List<T> OfType<T>() { lock (_events) return _events.OfType<T>().ToList(); }
    }

    private sealed class FakePresenter : ISiteSettingsPresenter
    {
        public SiteSettingsEditor? Shown { get; private set; }
        public Func<SiteSettingsEditor, JsonObject?> Choose { get; set; } = _ => null;
        public Task<JsonObject?> EditAsync(SiteSettingsEditor editor)
        {
            Shown = editor;
            return Task.FromResult(Choose(editor));
        }
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxSiteBrowsing_" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _spoken = new();
    private readonly SpeechQueue _speech;
    private readonly CaptureSink _sink = new();
    private readonly NavigationManager _navigation;
    private readonly Mock<IBrowseDocumentActions> _actions = new();
    private readonly Mock<IOptionsMonitor<VoxSettings>> _settings = new();
    private VoxSettings _current = new();
    private readonly BrowseModeController _browse;

    public SiteSettingsBrowsingTests()
    {
        var engine = new Mock<ISpeechEngine>();
        engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (_spoken) _spoken.Add(u.Text); })
            .Returns(Task.CompletedTask);
        _speech = new SpeechQueue(engine.Object, NullLogger<SpeechQueue>.Instance);
        _settings.SetupGet(m => m.CurrentValue).Returns(() => _current);
        _actions.Setup(a => a.ActivateAsync(It.IsAny<VBufferNode>())).ReturnsAsync(true);
        var audio = new Mock<IAudioCuePlayer>();
        _navigation = new NavigationManager(_sink, NullLogger<NavigationManager>.Instance);
        _browse = new BrowseModeController(
            _speech, audio.Object, _navigation, new QuickNavHandler(audio.Object),
            new SayAllController(_speech, NullLogger<SayAllController>.Instance),
            new AnnouncementBuilder(),
            new TypingEchoHandler(_sink, () => TypingEchoMode.Characters, NullLogger<TypingEchoHandler>.Instance),
            _settings.Object, _sink, _actions.Object, new Mock<IElementsListPresenter>().Object,
            NullLogger<BrowseModeController>.Instance);
    }

    public void Dispose()
    {
        _speech.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    // A page with a cookie banner: its text mentions cookies, with Accept and Reject buttons
    private static VBufferDocument Page(string address = "https://news.example.com/today")
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Value = address };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Headline", AriaRole = "heading", AriaProperties = "level=1" });
        var banner = new MockElement { RuntimeId = [3], ControlType = "Group", AriaRole = "region" };
        banner.AddChild(new MockElement { RuntimeId = [4], Name = "We use cookies to improve your visit." });
        banner.AddChild(new MockElement { RuntimeId = [5], Name = "Accept all", ControlType = "Button", IsFocusable = true });
        banner.AddChild(new MockElement { RuntimeId = [6], Name = "Reject all", ControlType = "Button", IsFocusable = true });
        root.AddChild(banner);
        return new VBufferBuilder().Build(root);
    }

    private void Load(VBufferDocument? document) =>
        _browse.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, document, null));

    private async Task WaitFor(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        lock (_spoken)
            Assert.True(condition(), $"{what}. Spoken: {string.Join(" | ", _spoken)}");
    }

    [Fact]
    public void TheSite_IsReportedWhenAPageLoads_AndWhenNoneIs()
    {
        var sites = new List<string?>();
        _browse.SiteChanged += (_, site) => sites.Add(site);

        Load(Page());
        Load(Page("https://news.example.com/tomorrow"));
        Load(Page("file:///C:/notes.html"));
        Load(Page("https://other.org"));
        Load(null);

        Assert.Equal(["https://news.example.com", null, "https://other.org", null], sites);
        Assert.Null(_browse.CurrentSite);
    }

    [Fact]
    public void TheSitesSettings_AreInPlace_BeforeThePageStarts()
    {
        // As ProfileManager would: the site's start mode, applied when the site is reported
        _browse.SiteChanged += (_, site) => _current = new VoxSettings { WebStartMode = site is null ? WebStartMode.Automatic : WebStartMode.Focus };

        Load(Page());

        Assert.Equal(InteractionMode.Focus, _navigation.CurrentMode);
    }

    [Fact]
    public async Task ACookieBanner_IsRejectedAutomatically_WhenAskedFor()
    {
        _current = new VoxSettings { AutoDismissOverlays = true };

        Load(Page());

        _actions.Verify(a => a.ActivateAsync(It.Is<VBufferNode>(n => n.Name == "Reject all")), Times.Once);
        await WaitFor(() => { lock (_spoken) return _spoken.Any(s => s.Contains("rejected: pressed Reject all")); }, "said");
    }

    [Fact]
    public async Task ACookieBanner_IsOnlyAnnounced_Otherwise()
    {
        Load(Page());

        await WaitFor(() => { lock (_spoken) return _spoken.Any(s => s.Contains("Cookie banner", StringComparison.OrdinalIgnoreCase)); }, "announced");
        _actions.Verify(a => a.ActivateAsync(It.IsAny<VBufferNode>()), Times.Never);
    }

    [Fact]
    public async Task TheDialog_EditsTheSiteBeingBrowsed_AndKeepsWhatIsChosen()
    {
        var profiles = new ProfileManager(new ProfileStore(null), new SiteSettingsStore(Path.Combine(_directory, SiteSettingsStore.FileName)));
        var presenter = new FakePresenter
        {
            Choose = editor =>
            {
                editor.Fields[1].Selected = 1;
                return editor.ToJson();
            },
        };
        var commands = new SiteSettingsCommands(profiles, presenter, _settings.Object, _browse, _sink, NullLogger<SiteSettingsCommands>.Instance, _speech);

        Assert.True(commands.TryHandle(NavigationCommand.OpenSiteSettings));
        await WaitFor(() => { lock (_spoken) return _spoken.Any(s => s.Contains("Open a web page first")); }, "no page");

        Load(Page());
        commands.TryHandle(NavigationCommand.OpenSiteSettings);
        await WaitFor(() => profiles.SiteSettingsFor("https://news.example.com").Count == 1, "kept");
        Assert.Equal("https://news.example.com", presenter.Shown!.Site);
        Assert.Equal("Beginner", profiles.SiteSettingsFor("https://news.example.com")["VerbosityLevel"]!.GetValue<string>());
        await WaitFor(() => { lock (_spoken) return _spoken.Any(s => s.Contains("Settings for news.example.com saved")); }, "saved said");
        Assert.Single(_sink.OfType<VoxDialogClosedEvent>());

        var secure = new SiteSettingsCommands(new ProfileManager(new ProfileStore(null)), presenter, _settings.Object, _browse, _sink,
            NullLogger<SiteSettingsCommands>.Instance, _speech);
        secure.TryHandle(NavigationCommand.OpenSiteSettings);
        await WaitFor(() => { lock (_spoken) return _spoken.Any(s => s.Contains("not available here")); }, "refused");
    }
}
