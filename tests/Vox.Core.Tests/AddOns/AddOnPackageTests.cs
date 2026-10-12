using System.Security.Cryptography;
using Vox.Core.AddOns;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class AddOnPackageTests : IDisposable
{
    private readonly AddOnTestFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void AnUnsignedPackage_IsReadWithItsManifest()
    {
        var package = _files.Package(_files.Source(files: new Dictionary<string, string> { ["Test.dll"] = "code", ["data/words.txt"] = "w" }));

        var info = AddOnPackage.Read(package, AddOnTrustStore.Empty);

        Assert.Equal("com.example.test", info.Manifest.Id);
        Assert.Equal(AddOnTrust.Unsigned, info.Trust);
        Assert.Null(info.Publisher);
        Assert.Equal(2, info.Manifest.Files.Count);
        Assert.Equal(64, info.Sha256.Length);
    }

    [Fact]
    public void ASignedPackage_IsTrustedOnlyWhenItsKeyIs()
    {
        using var key = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var package = _files.Package(key: key);

        var unknown = AddOnPackage.Read(package, AddOnTestFiles.Trusting(other));
        var trusted = AddOnPackage.Read(package, AddOnTestFiles.Trusting(key));

        Assert.Equal(AddOnTrust.UnknownPublisher, unknown.Trust);
        Assert.Equal(AddOnTrust.TrustedPublisher, trusted.Trust);
        Assert.Equal(AddOnTrustStore.Fingerprint(key), trusted.Publisher);
    }

    [Fact]
    public void AChangedFile_IsRefused()
    {
        var package = _files.Package();
        AddOnTestFiles.Replace(package, "Test.dll", "changed code");

        var error = Assert.Throws<AddOnPackageException>(() => AddOnPackage.Read(package, AddOnTrustStore.Empty));
        Assert.Contains("Test.dll", error.Message);
    }

    [Fact]
    public void AFileTheManifestDoesntList_IsRefused()
    {
        var package = _files.Package();
        AddOnTestFiles.Add(package, "Extra.dll", "sneaked in");

        Assert.Throws<AddOnPackageException>(() => AddOnPackage.Read(package, AddOnTrustStore.Empty));
    }

    [Fact]
    public void AMissingFile_IsRefused()
    {
        var package = _files.Package(_files.Source(files: new Dictionary<string, string> { ["Test.dll"] = "code", ["More.dll"] = "more" }));
        using (var zip = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Update))
            zip.GetEntry("More.dll")!.Delete();

        Assert.Throws<AddOnPackageException>(() => AddOnPackage.Read(package, AddOnTrustStore.Empty));
    }

    [Fact]
    public void AManifestChangedAfterSigning_IsRefused()
    {
        using var key = RSA.Create(2048);
        var package = _files.Package(key: key);
        var manifest = AddOnPackage.Read(package, AddOnTestFiles.Trusting(key)).Manifest;
        AddOnTestFiles.Replace(package, AddOnManifest.FileName, (manifest with { Author = "Someone else" }).ToJson());

        var error = Assert.Throws<AddOnPackageException>(() => AddOnPackage.Read(package, AddOnTestFiles.Trusting(key)));
        Assert.Contains("signature", error.Message);
    }

    [Fact]
    public void ASignatureWithoutItsKey_IsRefused()
    {
        using var key = RSA.Create(2048);
        var package = _files.Package(key: key);
        using (var zip = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Update))
            zip.GetEntry(AddOnPackage.KeyFileName)!.Delete();

        Assert.Throws<AddOnPackageException>(() => AddOnPackage.Read(package, AddOnTrustStore.Empty));
    }

    [Fact]
    public void SomethingElse_IsNotAPackage()
    {
        var notZip = _files.PathOf("x" + AddOnPackage.Extension);
        File.WriteAllText(notZip, "hello");

        Assert.Throws<AddOnPackageException>(() => AddOnPackage.Read(notZip, AddOnTrustStore.Empty));
    }

    [Fact]
    public void Extract_WritesEveryFile_WithTheManifestAndSignature()
    {
        using var key = RSA.Create(2048);
        var package = _files.Package(_files.Source(files: new Dictionary<string, string> { ["Test.dll"] = "code", ["data/words.txt"] = "w" }), key);
        var info = AddOnPackage.Read(package, AddOnTrustStore.Empty);
        var target = _files.PathOf("installed");

        AddOnPackage.Extract(package, info.Manifest, target);

        Assert.Equal("code", File.ReadAllText(Path.Combine(target, "Test.dll")));
        Assert.Equal("w", File.ReadAllText(Path.Combine(target, "data", "words.txt")));
        Assert.True(File.Exists(Path.Combine(target, AddOnManifest.FileName)));
        Assert.Equal(AddOnTrust.TrustedPublisher, AddOnPackage.TrustOfFolder(target, AddOnTestFiles.Trusting(key)).Trust);
    }

    [Fact]
    public void Extract_LeavesNothing_WhenThePackageChangedAfterItWasChecked()
    {
        var package = _files.Package();
        var info = AddOnPackage.Read(package, AddOnTrustStore.Empty);
        AddOnTestFiles.Replace(package, "Test.dll", "swapped after the check");
        var target = _files.PathOf("installed");

        Assert.Throws<AddOnPackageException>(() => AddOnPackage.Extract(package, info.Manifest, target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Create_ListsEveryFile_AndRefusesABadManifest()
    {
        var source = _files.Source(files: new Dictionary<string, string> { ["Test.dll"] = "code", ["sub/a.txt"] = "a" });

        var manifest = AddOnPackage.Create(source, _files.PathOf("out", "p.voxaddon"));

        Assert.Equal(["Test.dll", "sub/a.txt"], manifest.Files.Keys.Order(StringComparer.Ordinal).ToList());
        Assert.Throws<FormatException>(() => AddOnPackage.Create(_files.Source(id: "BAD ID"), _files.PathOf("bad.voxaddon")));
    }

    [Fact]
    public void TheTrustStore_ChecksSignatures_WithTrustedKeysOnly()
    {
        using var key = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var data = "catalogue"u8.ToArray();
        var signature = Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        Assert.True(AddOnTestFiles.Trusting(key).IsSignedByTrustedKey(data, signature));
        Assert.False(AddOnTestFiles.Trusting(other).IsSignedByTrustedKey(data, signature));
        Assert.False(AddOnTrustStore.Empty.HasKeys);
        Assert.Equal(19, AddOnTrustStore.Short(AddOnTrustStore.Fingerprint(key)).Length);
    }

    [Fact]
    public void TheTrustStore_ReadsVoxsKeyAndTheUsersKeys()
    {
        using var vox = RSA.Create(2048);
        using var user = RSA.Create(2048);
        var voxPath = _files.PathOf("addon-key.pem");
        File.WriteAllText(voxPath, vox.ExportSubjectPublicKeyInfoPem());
        Directory.CreateDirectory(_files.PathOf("keys"));
        File.WriteAllText(_files.PathOf("keys", "friend.pem"), user.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(_files.PathOf("keys", "junk.pem"), "not a key");

        var store = AddOnTrustStore.Load(voxPath, _files.PathOf("keys"));

        Assert.True(store.IsTrusted(AddOnTrustStore.Fingerprint(vox)));
        Assert.True(store.IsTrusted(AddOnTrustStore.Fingerprint(user)));
    }
}
