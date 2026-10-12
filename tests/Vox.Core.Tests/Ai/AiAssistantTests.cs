using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Ai;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Ai;

public class AiAssistantTests
{
    private readonly FakeDescriber _local = new("llava", isLocal: true) { IsAvailable = false };
    private readonly FakeDescriber _cloud = new("Claude", isLocal: false);
    private readonly FakeConsentPrompt _prompt = new();
    private readonly DescriptionCache _cache = new();
    private VoxSettings _settings = new();
    private RunPolicy _policy = RunPolicy.Normal;

    private AiAssistant Assistant() => new(_local, _cloud, new AiConsent(_policy, AiTestSupport.Settings(() => _settings)),
        _prompt, _cache, NullLogger<AiAssistant>.Instance);

    private static AiRequest Describe(byte seed = 1) => new(AiTask.DescribeImage, AiTestSupport.Image(seed: seed));

    [Fact]
    public async Task TheCloud_AsksFirst_ThenAnswers()
    {
        var answer = await Assistant().AskAsync(Describe(), AiTarget.None);

        Assert.Equal("A red bicycle against a brick wall.", answer.Text);
        Assert.Equal("Claude", answer.Describer);
        Assert.Single(_prompt.Questions);
        Assert.Contains("Claude", _prompt.Questions[0]);
        Assert.Single(_cloud.Requests);
    }

    [Fact]
    public async Task AYes_HoldsForTheSession()
    {
        var assistant = Assistant();

        await assistant.AskAsync(Describe(1), AiTarget.None);
        await assistant.AskAsync(Describe(2), AiTarget.None);

        Assert.Single(_prompt.Questions);
        Assert.Equal(2, _cloud.Requests.Count);
    }

    [Fact]
    public async Task ANo_SendsNothing()
    {
        _prompt.Answer = false;

        var answer = await Assistant().AskAsync(Describe(), AiTarget.None);

        Assert.False(answer.HasText);
        Assert.Equal(AiRefusal.Declined, answer.Refusal);
        Assert.Empty(_cloud.Requests);
    }

    [Fact]
    public async Task APasswordField_IsNeverSent_NorAskedAbout()
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };

        var answer = await Assistant().AskAsync(Describe(), new AiTarget(IsPassword: true));

        Assert.Equal(AiRefusal.PasswordField, answer.Refusal);
        Assert.Empty(_prompt.Questions);
        Assert.Empty(_cloud.Requests);
    }

    [Fact]
    public async Task SecureScreens_SendNothing_AndDescribeNothingLocally()
    {
        _policy = RunPolicy.Secure;
        _local.IsAvailable = true;

        var answer = await Assistant().AskAsync(Describe(), AiTarget.None);

        Assert.Equal(AiRefusal.SecureScreen, answer.Refusal);
        Assert.Empty(_local.Requests);
        Assert.Empty(_cloud.Requests);
    }

    [Fact]
    public async Task ThisComputer_ComesFirst_AndNeedsNoConsent()
    {
        _local.IsAvailable = true;
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Never };

        // Even a password field: nothing leaves the computer
        var answer = await Assistant().AskAsync(Describe(), new AiTarget(IsPassword: true));

        Assert.Equal("llava", answer.Describer);
        Assert.Single(_local.Requests);
        Assert.Empty(_cloud.Requests);
        Assert.Empty(_prompt.Questions);
    }

    [Fact]
    public async Task TheSameImage_IsDescribedOnce()
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };
        var assistant = Assistant();

        var first = await assistant.AskAsync(Describe(5), AiTarget.None);
        var second = await assistant.AskAsync(Describe(5), AiTarget.None);
        var other = await assistant.AskAsync(Describe(6), AiTarget.None);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Text, second.Text);
        Assert.False(other.FromCache);
        Assert.Equal(2, _cloud.Requests.Count);
    }

    [Fact]
    public async Task AnAnswerAlreadyGiven_IsntAskedAboutAgain()
    {
        var assistant = Assistant();
        await assistant.AskAsync(Describe(5), AiTarget.None);
        assistant.Consent.Reset();

        var again = await assistant.AskAsync(Describe(5), AiTarget.None);

        Assert.True(again.FromCache);
        Assert.Single(_prompt.Questions);
    }

    [Fact]
    public async Task NothingSetUp_SaysSo()
    {
        _cloud.IsAvailable = false;

        var answer = await Assistant().AskAsync(Describe(), AiTarget.None);

        Assert.Equal(AiRefusal.NotSetUp, answer.Refusal);
        Assert.False(Assistant().IsSetUp);
    }

    [Fact]
    public async Task AFailure_IsReported_AndNotCached()
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };
        _cloud.Throws = new AiException(AiFailure.KeyRejected, "401");
        var assistant = Assistant();

        var answer = await assistant.AskAsync(Describe(), AiTarget.None);

        Assert.Equal(AiFailure.KeyRejected, answer.Failure);
        Assert.Contains("rejected the API key", AiAssistant.Explain(answer));
        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task Cancelling_StopsTheRequest()
    {
        _settings = new VoxSettings { AiCloudConsent = AiConsentMode.Allow };
        _cloud.Gate = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();

        var asking = Assistant().AskAsync(Describe(), AiTarget.None, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task ThePersonBeingAsked_IsReported()
    {
        var states = new List<bool>();

        await Assistant().AskAsync(Describe(), AiTarget.None, asking: states.Add);

        Assert.Equal([true, false], states);
    }
}
