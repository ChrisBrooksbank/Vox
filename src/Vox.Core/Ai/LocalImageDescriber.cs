using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Enterprise;

namespace Vox.Core.Ai;

/// <summary>
/// Describes pictures with a vision model running on this computer, served by Ollama (or anything
/// with its <c>/api/chat</c> API) at <c>AiLocalEndpoint</c>: the <c>AiLocalModel</c> setting names
/// the model ("llama3.2-vision", "gemma3"). Only an address on this computer is ever used, so
/// nothing leaves it, and it works with the network turned off. Windows' own AI APIs need an app
/// with package identity, which Vox's installer doesn't give it, so they aren't used yet.
/// </summary>
public sealed class LocalImageDescriber(HttpClient http, IOptionsMonitor<VoxSettings> settings,
    ILogger<LocalImageDescriber> logger) : IImageDescriber
{
    public const string DefaultEndpoint = "http://localhost:11434";

    // Local models are slower with large pictures and gain little from them
    private const int MaxImageDimension = 1024;
    private const int MaxImageBytes = 4_000_000;

    public string Name => Model is { } model ? model : string.Empty;

    public bool IsLocal => true;

    /// <summary>A model is chosen and its address is on this computer.</summary>
    public bool IsAvailable => Model is not null && Endpoint is not null;

    private string? Model => settings.CurrentValue.AiLocalModel is { Length: > 0 } model ? model.Trim() : null;

    /// <summary>The endpoint, or null when it isn't an address on this computer (then it's never used).</summary>
    public Uri? Endpoint
    {
        get
        {
            var configured = settings.CurrentValue.AiLocalEndpoint is { Length: > 0 } text ? text.Trim() : DefaultEndpoint;
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !NetworkGate.IsLoopback(uri.Host))
                return null;
            return uri;
        }
    }

    public async Task<string> DescribeAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (Model is not { } model || Endpoint is not { } endpoint)
            throw new AiException(AiFailure.NotSetUp, "No local model is set up");

        using var message = BuildRequest(request, model, endpoint);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Most often: the model server isn't running
            throw new AiException(AiFailure.Failed, "The local model could not be reached", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException(AiFailure.Failed, "The local model took too long", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The local model answered {Status}", (int)response.StatusCode);
                throw new AiException(AiFailure.Failed, $"The local model answered {(int)response.StatusCode}");
            }
            return ReadAnswer(body);
        }
    }

    /// <summary>The <c>/api/chat</c> request (public for tests).</summary>
    public static HttpRequestMessage BuildRequest(AiRequest request, string model, Uri endpoint)
    {
        var user = new JsonObject { ["role"] = "user", ["content"] = AiPrompts.UserText(request) };
        if (request.Image is { } image)
            user["images"] = new JsonArray { Convert.ToBase64String(PngEncoder.EncodeWithin(image, MaxImageDimension, MaxImageBytes)) };
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = AiPrompts.Instructions(request) },
                user,
            },
        };
        return new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/api/chat"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>The text of an <c>/api/chat</c> response.</summary>
    public static string ReadAnswer(string body)
    {
        try
        {
            var text = JsonNode.Parse(body)?["message"]?["content"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text))
                throw new AiException(AiFailure.Failed, "The answer had no text");
            return text;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new AiException(AiFailure.Failed, "The answer could not be read", ex);
        }
    }
}
