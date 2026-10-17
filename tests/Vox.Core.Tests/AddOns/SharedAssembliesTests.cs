using Vox.Core.AddOns;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class SharedAssembliesTests : IDisposable
{
    private readonly AddOnTestFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void TheCurrentProcess_ProvidesTheFrameworkAndVoxSdk()
    {
        var shared = SharedAssemblies.Current;

        Assert.True(shared.Contains("System.Private.CoreLib"));
        Assert.True(shared.Contains("Vox.Sdk"));
        Assert.True(shared.Contains("Vox.Core"));
        Assert.False(shared.Contains("Not.A.Real.Assembly"));
    }

    [Fact]
    public void TheTrustedList_AndTheApplicationFolder_AreBothUsed()
    {
        var app = _files.PathOf("app");
        Directory.CreateDirectory(app);
        File.WriteAllBytes(Path.Combine(app, "Vox.App.Thing.dll"), [1]);

        var shared = new SharedAssemblies([Path.Combine("x", "Framework.Lib.dll"), ""], app);

        Assert.True(shared.Contains("Framework.Lib"));
        Assert.True(shared.Contains("vox.app.thing"));
        Assert.False(shared.Contains("Other"));
    }

    [Fact]
    public void ThereIsNoApplicationFolder_Fine()
    {
        var shared = new SharedAssemblies([], _files.PathOf("missing"));

        Assert.False(shared.Contains("anything"));
    }

    [Fact]
    public void VersionOf_ReadsTheFile_AndIsNullWhenUnreadable()
    {
        var sdk = typeof(Vox.Sdk.IAddOn).Assembly;
        var bad = _files.PathOf("bad.dll");
        File.WriteAllBytes(bad, [1, 2, 3]);
        var shared = new SharedAssemblies([sdk.Location, bad], null);

        Assert.Equal(sdk.GetName().Version, shared.VersionOf("Vox.Sdk"));
        Assert.Null(shared.VersionOf("bad"));
        Assert.Null(shared.VersionOf("unknown"));
    }

    [Fact]
    public void TheLoadContext_LeavesASharedAssemblyToVox_AndWarnsWhenTheAddOnWantsANewerOne()
    {
        var folder = _files.PathOf("addon");
        Directory.CreateDirectory(folder);
        File.Copy(typeof(Vox.Sdk.IAddOn).Assembly.Location, Path.Combine(folder, "Shared.Lib.dll"));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "Shared.Lib.dll")))).ToLowerInvariant();
        var manifest = new AddOnManifest { Id = "shared.test", Files = { ["Shared.Lib.dll"] = hash } };
        var provided = _files.PathOf("provided");
        Directory.CreateDirectory(provided);
        File.Copy(typeof(Vox.Sdk.IAddOn).Assembly.Location, Path.Combine(provided, "Shared.Lib.dll"));
        var logger = new CapturingLogger<object>();

        var type = typeof(AssemblyAddOnLoader).Assembly.GetType("Vox.Core.AddOns.AddOnLoadContext")!;
        var context = (System.Runtime.Loader.AssemblyLoadContext)Activator.CreateInstance(type, "shared.test", folder, manifest)!;
        type.GetProperty("SharedAssemblies", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .SetValue(context, new SharedAssemblies([Path.Combine(provided, "Shared.Lib.dll")], null));
        type.GetProperty("Logger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .SetValue(context, logger);
        var load = type.GetMethod("Load", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, [typeof(System.Reflection.AssemblyName)])!;

        var older = load.Invoke(context, [new System.Reflection.AssemblyName("Shared.Lib") { Version = new Version(0, 0, 1) }]);
        var newer = load.Invoke(context, [new System.Reflection.AssemblyName("Shared.Lib") { Version = new Version(99, 0, 0) }]);

        Assert.Null(older);
        Assert.Null(newer);
        Assert.Single(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }
}
