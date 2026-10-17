using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AddOns;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Sdk;
using Xunit;
using CoreAppModule = Vox.Core.AppModules.IAppModule;

namespace Vox.Core.Tests.AddOns;

/// <summary>The real loader with the sample add-on (samples/Vox.SampleAddOn, copied beside the tests).</summary>
public class AddOnLoaderTests : IDisposable
{
    private readonly AddOnTestFiles _files = new();

    public void Dispose() => _files.Dispose();

    private static string SampleFolder => Path.Combine(AppContext.BaseDirectory, "SampleAddOn");

    private string SamplePackage() => _files.Package(SampleFolder, name: "vox.sample");

    [Fact]
    public void TheSample_IsAValidPackage()
    {
        var info = AddOnPackage.Read(SamplePackage(), AddOnTrustStore.Empty);

        Assert.Equal("vox.sample", info.Manifest.Id);
        Assert.Equal("Vox.SampleAddOn.dll", info.Manifest.EntryAssembly);
        Assert.Equal(AddOnCompatibilityKind.Compatible, AddOnCompatibility.Check(info.Manifest, VoxApi.Current));
        // Vox.Sdk comes from Vox, never from the package
        Assert.DoesNotContain("Vox.Sdk.dll", info.Manifest.Files.Keys);
    }

    [Fact]
    public void TheSample_InstallsLoadsAndWorks_ThroughTheManager()
    {
        var said = new List<string>();
        AddOnKeyLayer? keys = null;
        IReadOnlyList<CoreAppModule> modules = [];
        var manager = new AddOnManager(_files.AddOnsDirectory, RunPolicy.Normal, new AssemblyAddOnLoader(), AddOnTrustStore.Empty,
            new AddOnIntegration
            {
                Speak = (text, _) => said.Add(text),
                SetKeyLayer = layer => keys = layer,
                SetAppModules = m => modules = m,
            });
        manager.InstallFiles(manager.CheckPackage(SamplePackage()));

        manager.LoadAll();

        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.Running, info.State);
        // Its global command is on Insert+Ctrl+F12
        Assert.True(keys!.TryResolveOutsideDocument(KeyModifiers.Insert | KeyModifiers.Ctrl, 0x7B, out var hello));
        manager.OnEvent(new FocusChangedEvent(DateTimeOffset.UtcNow, "Name", "Edit"));
        manager.RunCommand(hello);
        Assert.Equal("Hello from the sample add-on. Focus moved 1 times.", Assert.Single(said));
        // Its Notepad module
        Assert.Equal(["notepad"], Assert.Single(modules).ProcessNames);
        // Its speech filter
        Assert.Equal("for example this", new AddOnTextRule(() => manager.Filters).Apply("e.g. this", new Utterance("e.g. this", Core.Speech.SpeechPriority.Normal)));

        manager.Disable("vox.sample");
        Assert.Null(keys);
        Assert.Empty(modules);
    }

    [Fact]
    public void Unloading_LetsTheAddOnsCodeGo()
    {
        var folder = _files.PathOf("installed");
        var package = SamplePackage();
        AddOnPackage.Extract(package, AddOnPackage.Read(package, AddOnTrustStore.Empty).Manifest, folder);
        var manifest = AddOnManifest.Parse(File.ReadAllText(Path.Combine(folder, AddOnManifest.FileName)));

        var context = LoadAndUnload(folder, manifest);
        for (int i = 0; i < 20 && context.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "the add-on's load context is still alive after unloading");
    }

    [Fact]
    public void TheManager_ChecksADisabledAddOnLeavesMemory()
    {
        var time = new ManualTimeProvider();
        var verified = new List<(string Id, bool Unloaded)>();
        var manager = LoadAndDisableTheSample(time);
        manager.Unloads.Verified += (id, unloaded) => verified.Add((id, unloaded));

        time.Advance(AddOnUnloadVerifier.Delay);

        Assert.Equal([("vox.sample", true)], verified);
        Assert.Equal(0, time.ActiveTimers);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private AddOnManager LoadAndDisableTheSample(ManualTimeProvider time)
    {
        var manager = new AddOnManager(_files.AddOnsDirectory, RunPolicy.Normal, new AssemblyAddOnLoader(), AddOnTrustStore.Empty, time: time);
        manager.InstallFiles(manager.CheckPackage(SamplePackage()));
        manager.LoadAll();
        Assert.Equal(AddOnState.Running, Assert.Single(manager.List()).State);
        manager.Disable("vox.sample");
        return manager;
    }

    [Fact]
    public void AnAddOnStillReferredTo_IsLoggedAsNotUnloaded()
    {
        var logger = new CapturingLogger<AddOnUnloadVerifier>();
        var verifier = new AddOnUnloadVerifier(logger);
        var held = new object();

        Assert.False(verifier.Verify("com.example.leaky", new WeakReference(held)));

        Assert.Contains("did not unload", logger.AllText);
        Assert.Contains("com.example.leaky", logger.AllText);
        GC.KeepAlive(held);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(string folder, AddOnManifest manifest)
    {
        var loaded = new AssemblyAddOnLoader().Load(folder, manifest);
        var context = AssemblyLoadContext.GetLoadContext(loaded.Instance.GetType().Assembly)!;
        Assert.True(context.IsCollectible);
        Assert.NotSame(AssemblyLoadContext.Default, context);
        // The add-on sees Vox's own Vox.Sdk
        Assert.Same(typeof(IAddOn).Assembly, loaded.Instance.GetType().GetInterfaces().Single(i => i.Name == nameof(IAddOn)).Assembly);
        loaded.Unload();
        return new WeakReference(context);
    }

    [Fact]
    public void AnAssemblyChangedAfterInstalling_IsNotLoaded()
    {
        var folder = _files.PathOf("installed");
        var package = SamplePackage();
        var manifest = AddOnPackage.Read(package, AddOnTrustStore.Empty).Manifest;
        AddOnPackage.Extract(package, manifest, folder);
        File.AppendAllText(Path.Combine(folder, "Vox.SampleAddOn.dll"), "tampered");

        var error = Assert.Throws<AddOnLoadException>(() => new AssemblyAddOnLoader().Load(folder, manifest));
        Assert.Contains("changed", error.Message);
    }

    [Fact]
    public void AnAssemblyWithoutAnEntryClass_IsRefused()
    {
        // Vox.Sdk itself has no IAddOn class
        var source = _files.PathOf("noentry");
        Directory.CreateDirectory(source);
        File.Copy(typeof(IAddOn).Assembly.Location, Path.Combine(source, "NoEntry.dll"));
        File.WriteAllText(Path.Combine(source, AddOnManifest.FileName),
            """{ "id": "com.example.noentry", "name": "No entry", "version": "1.0", "author": "Example", "minApiVersion": "1.0", "entryAssembly": "NoEntry.dll" }""");
        var package = _files.Package(source);
        var folder = _files.PathOf("installed-noentry");
        var manifest = AddOnPackage.Read(package, AddOnTrustStore.Empty).Manifest;
        AddOnPackage.Extract(package, manifest, folder);

        var error = Assert.Throws<AddOnLoadException>(() => new AssemblyAddOnLoader().Load(folder, manifest));
        Assert.Contains("IAddOn", error.Message);
    }

    private static (AssemblyLoadContext Context, System.Reflection.MethodInfo Load) NativeContext(string folder, AddOnManifest manifest)
    {
        var type = typeof(AssemblyAddOnLoader).Assembly.GetType("Vox.Core.AddOns.AddOnLoadContext")!;
        var context = (AssemblyLoadContext)Activator.CreateInstance(type, "native.test", folder, manifest)!;
        return (context, type.GetMethod("LoadUnmanagedDll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!);
    }

    [Fact]
    public void ANativeLibrary_ThatDiffersFromTheManifest_IsRefused()
    {
        var folder = _files.PathOf("native");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "native.dll"), [1, 2, 3]);
        var manifest = new AddOnManifest { Id = "native.test", Files = { ["native.dll"] = new string('0', 64) } };
        var (context, load) = NativeContext(folder, manifest);

        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => load.Invoke(context, ["native"]));

        Assert.IsType<AddOnLoadException>(error.InnerException);
        Assert.Contains("changed", error.InnerException!.Message);
    }

    [Fact]
    public void ANativeLibrary_TheManifestDoesNotList_IsNotLoaded()
    {
        var (context, load) = NativeContext(_files.PathOf("none"), new AddOnManifest { Id = "native.test" });

        Assert.Equal(IntPtr.Zero, load.Invoke(context, ["other"]));
    }
}
