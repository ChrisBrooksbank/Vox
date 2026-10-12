using System.Net;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Ai;
using Vox.Core.Configuration;
using Vox.Core.Ocr;

namespace Vox.Core.Tests.Ai;

/// <summary>Keeps a key in memory.</summary>
public sealed class FakeKeyStore(string? key = null) : IAiKeyStore
{
    public string? Key { get; private set; } = key;
    public bool HasKey => Key is not null;
    public string? Read() => Key;

    public bool Save(string? key)
    {
        Key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        return true;
    }
}

/// <summary>A describer that records what it is asked and answers with <see cref="Answer"/>.</summary>
public sealed class FakeDescriber(string name, bool isLocal) : IImageDescriber
{
    public string Name { get; } = name;
    public bool IsLocal { get; } = isLocal;
    public bool IsAvailable { get; set; } = true;
    public string Answer { get; set; } = "A red bicycle against a brick wall.";
    public AiException? Throws { get; set; }
    public List<AiRequest> Requests { get; } = new();

    /// <summary>When set, answering waits for it.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<string> DescribeAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        lock (Requests) Requests.Add(request);
        if (Gate is { } gate)
            await gate.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (Throws is { } ex)
            throw ex;
        return Answer;
    }
}

/// <summary>Answers yes or no, counting the questions.</summary>
public sealed class FakeConsentPrompt : IAiConsentPrompt
{
    public bool Answer { get; set; } = true;
    public List<string> Questions { get; } = new();

    public Task<bool> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        lock (Questions) Questions.Add(question);
        return Task.FromResult(Answer);
    }
}

/// <summary>Returns <see cref="Response"/> for every request, keeping the requests.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string Response { get; set; } = "{}";
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
    }
}

public static class AiTestSupport
{
    public static IOptionsMonitor<VoxSettings> Settings(Func<VoxSettings> current)
    {
        var settings = new Mock<IOptionsMonitor<VoxSettings>>();
        settings.Setup(m => m.CurrentValue).Returns(current);
        return settings.Object;
    }

    /// <summary>A small picture with something in it (different <paramref name="seed"/>s, different pictures).</summary>
    public static CapturedImage Image(int width = 4, int height = 3, byte seed = 1)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(i * 7 + seed);
        return new CapturedImage(width, height, pixels, new ScreenRect(10, 20, width, height));
    }
}
