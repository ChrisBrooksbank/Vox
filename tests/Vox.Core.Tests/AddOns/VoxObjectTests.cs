using Xunit;
using Vox.Core.AddOns;
using Vox.Core.Navigation;
using Vox.Core.Ocr;
using Vox.Core.Pipeline;
using Vox.Sdk;

namespace Vox.Core.Tests.AddOns;

public class VoxObjectTests
{
    private sealed class Node(string name, string role, string? value = null, bool password = false, ScreenRect? bounds = null)
        : INavigatorObject
    {
        public Node? Parent;
        public List<Node> Kids { get; } = [];

        public Node Add(Node child)
        {
            child.Parent = this;
            Kids.Add(child);
            return this;
        }

        public INavigatorObject? GetParent() => Parent;
        public INavigatorObject? GetFirstChild() => Kids.FirstOrDefault();
        public INavigatorObject? GetLastChild() => Kids.LastOrDefault();
        public INavigatorObject? GetNextSibling() => Step(1);
        public INavigatorObject? GetPreviousSibling() => Step(-1);
        public ScreenRect? GetBounds() => bounds;

        public FocusChangedEvent Describe() =>
            new(DateTimeOffset.UtcNow, name, role, IsPassword: password, Value: value);

        private Node? Step(int d)
        {
            if (Parent is null) return null;
            var i = Parent.Kids.IndexOf(this) + d;
            return i >= 0 && i < Parent.Kids.Count ? Parent.Kids[i] : null;
        }
    }

    private sealed class Source(Node? focused, Node? navigator, bool fail = false) : IAddOnObjectSource
    {
        public Task<T> RunAsync<T>(Func<T> work) =>
            fail ? Task.FromException<T>(new TimeoutException()) : Task.FromResult(work());

        public INavigatorObject? GetFocused() => focused;
        public INavigatorObject? GetNavigator() => navigator;
    }

    private static Task<IVoxObject?> Read(IAddOnObjectSource source, bool focused) =>
        Task.FromResult(VoxObject.Wrap(focused ? source.GetFocused() : source.GetNavigator(), source));

    [Fact]
    public async Task Snapshot_HasNameRoleValueAndBounds()
    {
        var node = new Node("Name", "Edit", "hello", bounds: new ScreenRect(1, 2, 30, 40));
        var obj = await Read(new Source(node, null), focused: true);

        Assert.NotNull(obj);
        Assert.Equal("Name", obj.Name);
        Assert.Equal("Edit", obj.Role);
        Assert.Equal("hello", obj.Value);
        Assert.Equal(new VoxRect(1, 2, 30, 40), obj.Bounds);
    }

    [Fact]
    public async Task PasswordValue_IsNeverGiven()
    {
        var obj = await Read(new Source(new Node("Password", "Edit", "secret", password: true), null), focused: true);

        Assert.Null(obj!.Value);
    }

    [Fact]
    public async Task NoObject_IsNull_AndEmptyBoundsAreNull()
    {
        Assert.Null(await Read(new Source(null, null), focused: false));
        var obj = await Read(new Source(new Node("x", "Pane", bounds: new ScreenRect(0, 0, 0, 0)), null), focused: true);
        Assert.Null(obj!.Bounds);
    }

    [Fact]
    public async Task Relatives_AreReachedAndNullAtTheEdge()
    {
        var root = new Node("root", "Window");
        root.Add(new Node("a", "Button")).Add(new Node("b", "Button")).Add(new Node("c", "Button"));
        var source = new Source(root.Kids[1], null);
        var b = (await Read(source, true))!;

        Assert.Equal("a", (await b.GetPreviousAsync())!.Name);
        Assert.Equal("c", (await b.GetNextAsync())!.Name);
        Assert.Equal("root", (await b.GetParentAsync())!.Name);
        Assert.Null(await b.GetFirstChildAsync());
        Assert.Equal("a", (await (await b.GetParentAsync())!.GetFirstChildAsync())!.Name);
        Assert.Null(await (await b.GetParentAsync())!.GetParentAsync());
    }

    [Fact]
    public async Task Children_AreListedUpToTheLimit()
    {
        var root = new Node("root", "Window");
        for (var i = 0; i < 5; i++)
            root.Add(new Node($"k{i}", "Button"));
        var obj = (await Read(new Source(root, null), true))!;

        Assert.Equal(5, (await obj.GetChildrenAsync()).Count);
        Assert.Equal(["k0", "k1"], (await obj.GetChildrenAsync(2)).Select(c => c.Name));
        Assert.Single(await obj.GetChildrenAsync(-5));
    }

    [Fact]
    public async Task FailedReads_GiveNothingInsteadOfThrowing()
    {
        var root = new Node("root", "Window").Add(new Node("a", "Button"));
        var live = new Source(root, null);
        var obj = (await Read(live, true))!;
        var failing = new Source(root, null, fail: true);
        var broken = VoxObject.Wrap(root, failing)!;

        Assert.Null(await broken.GetFirstChildAsync());
        Assert.Empty(await broken.GetChildrenAsync());
        Assert.NotNull(await obj.GetFirstChildAsync());
    }

    [Fact]
    public async Task Host_GivesFocusedAndNavigatorObjects_AndNullWithoutASource()
    {
        var focused = new Node("focus", "Edit");
        var navigator = new Node("nav", "Button");
        var host = new AddOnHost("x", () => Path.GetTempPath(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            new NoSpeech(), () => null, work => work(), new Source(focused, navigator));

        Assert.Equal("focus", (await host.GetFocusedObjectAsync())!.Name);
        Assert.Equal("nav", (await host.GetNavigatorObjectAsync())!.Name);

        var bare = new AddOnHost("x", () => Path.GetTempPath(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            new NoSpeech(), () => null, work => work());
        Assert.Null(await bare.GetFocusedObjectAsync());

        host.Stop();
        Assert.Null(await host.GetFocusedObjectAsync());
    }

    private sealed class NoSpeech : ISpeechOutput
    {
        public void Speak(string text, SpeechPriority priority = SpeechPriority.Normal) { }
        public void StopSpeaking() { }
    }
}
