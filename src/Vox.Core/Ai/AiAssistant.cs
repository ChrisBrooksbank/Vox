using Microsoft.Extensions.Logging;
using Vox.Core.Localization;

namespace Vox.Core.Ai;

/// <summary>Asks the person whether something may be sent to a cloud describer; true for yes.</summary>
public interface IAiConsentPrompt
{
    Task<bool> AskAsync(string question, CancellationToken cancellationToken = default);
}

/// <summary>
/// An answer: its text (null when there is none), which describer gave it, whether it came from
/// the cache, and why there is none (<see cref="Refusal"/>, or <see cref="Failure"/> when the
/// describer failed).
/// </summary>
public sealed record AiAnswer(string? Text, string Describer = "", bool FromCache = false,
    AiRefusal Refusal = AiRefusal.None, AiFailure? Failure = null)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// The one way Vox asks a describer anything: the describer on this computer first, else the
/// cloud describer the user set up, with <see cref="AiConsent"/> checked (and the person asked
/// when it says so) before anything is sent; answers are cached by what was asked. Thread-safe.
/// </summary>
public sealed class AiAssistant(
    IImageDescriber local,
    IImageDescriber cloud,
    AiConsent consent,
    IAiConsentPrompt prompt,
    DescriptionCache cache,
    ILogger<AiAssistant> logger)
{
    /// <summary>Some describer is set up (whether or not a request would be allowed).</summary>
    public bool IsSetUp => local.IsAvailable || cloud.IsAvailable;

    /// <summary>The describer a request would go to now, or null when none is set up.</summary>
    public IImageDescriber? Describer => local.IsAvailable ? local : cloud.IsAvailable ? cloud : null;

    public AiConsent Consent => consent;

    /// <summary>
    /// Answers <paramref name="request"/>. <paramref name="target"/> is what it shows, for the rules
    /// that never send. <paramref name="asking"/> is told when the person is being asked (true)
    /// and when they have answered (false). A <paramref name="background"/> request (nobody asked
    /// for it: automatic labels) goes to the cloud only when consent is "allow"
    /// (<see cref="AiConsent.ForBackground"/>), and never asks. Never throws but for cancellation.
    /// </summary>
    public async Task<AiAnswer> AskAsync(AiRequest request, AiTarget target, CancellationToken cancellationToken = default,
        Action<bool>? asking = null, bool background = false)
    {
        var allowed = consent.ForLocal();
        if (allowed.Decision == AiConsentDecision.Refused)
            return new AiAnswer(null, Refusal: allowed.Reason);

        var describer = Describer;
        if (describer is null)
            return new AiAnswer(null, Refusal: AiRefusal.NotSetUp);

        bool ask = false;
        if (!describer.IsLocal)
        {
            var decision = background ? consent.ForBackground(target) : consent.ForCloud(target);
            if (decision.Decision == AiConsentDecision.Refused)
                return new AiAnswer(null, describer.Name, Refusal: decision.Reason);
            ask = decision.Decision == AiConsentDecision.Ask;
        }

        var key = DescriptionCache.KeyFor(request, describer.Name);
        if (cache.TryGet(key, out var cached))
            return new AiAnswer(cached, describer.Name, FromCache: true);

        if (ask)
        {
            bool yes;
            asking?.Invoke(true);
            try
            {
                yes = await prompt.AskAsync(Question(request, describer), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                asking?.Invoke(false);
            }
            if (!yes)
                return new AiAnswer(null, describer.Name, Refusal: AiRefusal.Declined);
            consent.AgreeForSession();
        }
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var text = (await describer.DescribeAsync(request, cancellationToken).ConfigureAwait(false)).Trim();
            if (text.Length > 0)
                cache.Add(key, text);
            // Never logged: it is about whatever was on the screen
            logger.LogDebug("{Describer} answered a {Task} request", describer.IsLocal ? "Local describer" : "Cloud describer", request.Task);
            return new AiAnswer(text, describer.Name);
        }
        catch (AiException ex)
        {
            logger.LogWarning("{Task} request failed: {Failure}", request.Task, ex.Failure);
            return new AiAnswer(null, describer.Name, Failure: ex.Failure);
        }
    }

    /// <summary>What to say when there is no answer.</summary>
    public static string Explain(AiAnswer answer) =>
        answer.Failure is { } failure
            ? VoxStrings.Format($"Ai.Failure.{failure}", answer.Describer)
            : AiConsent.Explain(answer.Refusal);

    private static string Question(AiRequest request, IImageDescriber describer) =>
        VoxStrings.Format(request.Task switch
        {
            AiTask.DescribeImage => "Ai.Consent.DescribeImage",
            AiTask.ExplainScreen => "Ai.Consent.ExplainScreen",
            AiTask.AskPage => "Ai.Consent.AskPage",
            AiTask.GuideMe => "Ai.Consent.GuideMe",
            _ => "Ai.Consent.SummarizePage",
        }, describer.Name);
}
