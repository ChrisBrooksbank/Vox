using System.Security.Cryptography;
using Vox.Core.AddOns;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.AddOns;

/// <summary>With UI access (RunPolicy.RequireSignedAddOns) only add-ons signed by a trusted publisher load, checked at load time.</summary>
public class AddOnUiAccessTests : IDisposable
{
    private readonly AddOnTestFiles _files = new();

    public void Dispose() => _files.Dispose();

    private static string SampleFolder => Path.Combine(AppContext.BaseDirectory, "SampleAddOn");

    private AddOnManager Manager(RunPolicy policy, AddOnTrustStore trust) =>
        new(_files.AddOnsDirectory, policy, new AssemblyAddOnLoader(), trust);

    private static RunPolicy WithUiAccess => RunPolicy.Normal with { RequireSignedAddOns = true };

    [Fact]
    public void AnUnsignedAddOn_IsNotLoaded()
    {
        var manager = Manager(WithUiAccess, AddOnTrustStore.Empty);
        manager.InstallFiles(manager.CheckPackage(_files.Package(SampleFolder, name: "vox.sample")));

        manager.LoadAll();

        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.Failed, info.State);
        Assert.NotNull(info.Problem);
    }

    [Fact]
    public void AnAddOnSignedByAnUnknownPublisher_IsNotLoaded()
    {
        using var key = RSA.Create(2048);
        var manager = Manager(WithUiAccess, AddOnTrustStore.Empty);
        manager.InstallFiles(manager.CheckPackage(_files.Package(SampleFolder, key, "vox.sample")));

        manager.LoadAll();

        Assert.Equal(AddOnState.Failed, Assert.Single(manager.List()).State);
    }

    [Fact]
    public void AnAddOnSignedByATrustedPublisher_Loads()
    {
        using var key = RSA.Create(2048);
        var manager = Manager(WithUiAccess, AddOnTestFiles.Trusting(key));
        manager.InstallFiles(manager.CheckPackage(_files.Package(SampleFolder, key, "vox.sample")));

        manager.LoadAll();

        Assert.Equal(AddOnState.Running, Assert.Single(manager.List()).State);
    }

    [Fact]
    public void ASignatureThatIsRemovedAfterInstalling_StopsTheAddOnLoading()
    {
        using var key = RSA.Create(2048);
        var manager = Manager(WithUiAccess, AddOnTestFiles.Trusting(key));
        manager.InstallFiles(manager.CheckPackage(_files.Package(SampleFolder, key, "vox.sample")));
        File.Delete(Path.Combine(_files.AddOnsDirectory, "vox.sample", AddOnPackage.SignatureFileName));

        manager.LoadAll();

        Assert.Equal(AddOnState.Failed, Assert.Single(manager.List()).State);
    }

    [Fact]
    public void WithoutUiAccess_AnUnsignedAddOnStillLoads()
    {
        var manager = Manager(RunPolicy.Normal, AddOnTrustStore.Empty);
        manager.InstallFiles(manager.CheckPackage(_files.Package(SampleFolder, name: "vox.sample")));

        manager.LoadAll();

        Assert.Equal(AddOnState.Running, Assert.Single(manager.List()).State);
    }

    [Fact]
    public void WithUiAccess_ComponentsComeFromTheMachineFolder()
    {
        try
        {
            VoxPaths.RequireMachineComponents(true);
            if (!VoxPaths.IsPortable)
                Assert.Equal(VoxPaths.MachineComponents, VoxPaths.Components);
        }
        finally
        {
            VoxPaths.RequireMachineComponents(false);
        }
        Assert.NotEqual(VoxPaths.MachineComponents, VoxPaths.Components);
    }
}
