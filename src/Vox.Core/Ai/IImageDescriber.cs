using Vox.Core.Ocr;

namespace Vox.Core.Ai;

/// <summary>What is asked of a describer.</summary>
public enum AiTask
{
    /// <summary>Say what a picture (an image, an object, a window) shows.</summary>
    DescribeImage,
    /// <summary>Answer a question about the screen from a picture of it and its controls' text.</summary>
    ExplainScreen,
    /// <summary>Summarise a page from its text.</summary>
    SummarizePage,
}

/// <summary>
/// One request to a describer: a picture (optional for <see cref="AiTask.SummarizePage"/>), the
/// user's question, text that goes with it (the window's controls, the page's text) and the
/// language to answer in (BCP 47; null: English).
/// </summary>
public sealed record AiRequest(AiTask Task, CapturedImage? Image = null, string? Question = null, string? Context = null,
    string? Language = null);

/// <summary>
/// Describes pictures of the screen and answers questions about it: on this computer
/// (<see cref="LocalImageDescriber"/>) or through a cloud provider the user set up
/// (<see cref="CloudImageDescriber"/>). Callers go through <see cref="AiAssistant"/>, which
/// checks <see cref="AiConsent"/> first; a describer itself sends whatever it is given.
/// Safe to call from any thread.
/// </summary>
public interface IImageDescriber
{
    /// <summary>The describer as people know it ("Claude", "llava on this computer").</summary>
    string Name { get; }

    /// <summary>Runs on this computer: nothing leaves it.</summary>
    bool IsLocal { get; }

    /// <summary>Set up and allowed to run (a model chosen, a key given).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The answer as plain text. Throws <see cref="AiException"/> when the describer can't
    /// answer (rejected key, refusal, a failed request), <see cref="OperationCanceledException"/>
    /// when cancelled.
    /// </summary>
    Task<string> DescribeAsync(AiRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A describer for where there is none.</summary>
public sealed class NoImageDescriber : IImageDescriber
{
    public string Name => string.Empty;
    public bool IsLocal => true;
    public bool IsAvailable => false;

    public Task<string> DescribeAsync(AiRequest request, CancellationToken cancellationToken = default) =>
        throw new AiException(AiFailure.NotSetUp, "No describer is set up");
}

/// <summary>Why a describer couldn't answer.</summary>
public enum AiFailure
{
    /// <summary>Nothing is set up (no model, no key).</summary>
    NotSetUp,
    /// <summary>The provider rejected the key.</summary>
    KeyRejected,
    /// <summary>The provider is busy or the account is over its limit.</summary>
    Busy,
    /// <summary>The model declined to answer.</summary>
    Declined,
    /// <summary>The request failed (network, server, an answer that couldn't be read).</summary>
    Failed,
}

/// <summary>A describer couldn't answer; <see cref="Failure"/> says why.</summary>
public sealed class AiException(AiFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public AiFailure Failure { get; } = failure;
}
