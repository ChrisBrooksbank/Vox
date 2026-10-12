using Vox.Core.Ai;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Ai;

public class AiConsentTests
{
    private VoxSettings _settings = new();

    private AiConsent Consent(RunPolicy? policy = null) => new(policy ?? RunPolicy.Normal, AiTestSupport.Settings(() => _settings));

    [Fact]
    public void ByDefault_TheCloudAsksFirst_AndOnlyOncePerSession()
    {
        var consent = Consent();

        Assert.Equal(AiConsentResult.Ask, consent.ForCloud(AiTarget.None));
        consent.AgreeForSession();
        Assert.Equal(AiConsentResult.Allowed, consent.ForCloud(AiTarget.None));
        // A new session (Vox restarted) asks again
        Assert.Equal(AiConsentResult.Ask, Consent().ForCloud(AiTarget.None));
    }

    [Fact]
    public void AllowAndNever_AreTheSetting()
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };
        Assert.Equal(AiConsentResult.Allowed, Consent().ForCloud(AiTarget.None));

        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Never };
        var consent = Consent();
        consent.AgreeForSession();
        Assert.Equal(AiConsentResult.Refused(AiRefusal.CloudNever), consent.ForCloud(AiTarget.None));
    }

    [Fact]
    public void PasswordFields_AreNeverSent_WhateverTheSetting()
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };

        Assert.Equal(AiConsentResult.Refused(AiRefusal.PasswordField), Consent().ForCloud(new AiTarget(IsPassword: true)));
    }

    [Theory]
    [InlineData("KeePass")]
    [InlineData("keepass")]
    [InlineData("KeePass.exe")]
    public void PrivateApps_AreNeverSent(string listed)
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow, AiPrivateApps = [listed] };

        Assert.Equal(AiConsentResult.Refused(AiRefusal.PrivateApp), Consent().ForCloud(new AiTarget(ProcessName: "KeePass")));
        Assert.Equal(AiConsentResult.Allowed, Consent().ForCloud(new AiTarget(ProcessName: "msedge")));
    }

    [Fact]
    public void PasswordManagers_ArePrivateByDefault()
    {
        Assert.Contains("1Password", new VoxSettings().AiPrivateApps);
        Assert.Contains("Bitwarden", new VoxSettings().AiPrivateApps);
    }

    [Fact]
    public void SecureScreens_DescribeNothing_EvenOnThisComputer()
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };
        var consent = Consent(RunPolicy.Secure);

        Assert.Equal(AiConsentResult.Refused(AiRefusal.SecureScreen), consent.ForLocal());
        Assert.Equal(AiConsentResult.Refused(AiRefusal.SecureScreen), consent.ForCloud(AiTarget.None));
    }

    [Fact]
    public void ThePolicy_TurnsAllAiOff()
    {
        var consent = Consent(RunPolicy.Normal with { AiOffByPolicy = true });

        Assert.Equal(AiConsentResult.Refused(AiRefusal.TurnedOffByPolicy), consent.ForLocal());
        Assert.Equal(AiConsentResult.Refused(AiRefusal.TurnedOffByPolicy), consent.ForCloud(AiTarget.None));
    }

    [Fact]
    public void NetworkOff_LeavesOnlyThisComputer()
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };
        var consent = Consent(RunPolicy.Normal with { NetworkOffByPolicy = true });

        Assert.Equal(AiConsentResult.Allowed, consent.ForLocal());
        Assert.Equal(AiConsentResult.Refused(AiRefusal.NetworkOff), consent.ForCloud(AiTarget.None));
    }

    [Fact]
    public void EveryRefusal_CanBeExplained()
    {
        foreach (var reason in Enum.GetValues<AiRefusal>().Where(r => r != AiRefusal.None))
            Assert.False(string.IsNullOrWhiteSpace(AiConsent.Explain(reason)), reason.ToString());
        foreach (var failure in Enum.GetValues<AiFailure>())
            Assert.False(string.IsNullOrWhiteSpace(AiAssistant.Explain(new AiAnswer(null, "Claude", Failure: failure))), failure.ToString());
    }

    [Theory]
    [InlineData("https://bank.example.com/login", true)]
    [InlineData("https://www.bank.example.com/", true)]
    [InlineData("https://notbank.example.com/", false)]
    [InlineData("https://example.org/", false)]
    [InlineData(null, false)]
    public void PrivateSites_AreNeverSent(string? address, bool isPrivate)
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow, AiPrivateSites = ["bank.example.com", "https://intranet.example.net"] };

        var result = Consent().ForCloud(new AiTarget(Site: address));

        Assert.Equal(isPrivate ? AiConsentResult.Refused(AiRefusal.PrivateSite) : AiConsentResult.Allowed, result);
        Assert.True(AiConsent.IsPrivateSite("http://intranet.example.net/wiki", _settings.AiPrivateSites));
    }

    [Fact]
    public void Background_UsesTheCloud_OnlyWhenAllowed_NeverOnASessionYes()
    {
        var consent = Consent();
        consent.AgreeForSession();
        Assert.Equal(AiConsentResult.Refused(AiRefusal.NotInBackground), consent.ForBackground(AiTarget.None));

        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };
        Assert.Equal(AiConsentResult.Allowed, Consent().ForBackground(AiTarget.None));
        Assert.Equal(AiConsentResult.Refused(AiRefusal.PasswordField), Consent().ForBackground(new AiTarget(IsPassword: true)));

        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Never };
        Assert.Equal(AiConsentResult.Refused(AiRefusal.CloudNever), Consent().ForBackground(AiTarget.None));
    }
}
