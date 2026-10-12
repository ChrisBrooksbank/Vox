using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Enterprise;

namespace Vox.Core.Ai;

/// <summary>Which cloud provider describes pictures (the <c>AiCloudProvider</c> setting).</summary>
public enum AiCloudProvider
{
    /// <summary>No cloud describer: only one on this computer.</summary>
    None,
    /// <summary>Anthropic's Claude, through the Messages API with the user's own key.</summary>
    Claude,
}

/// <summary>
/// Describes pictures through the cloud provider the user chose, with their own API key (kept by
/// <see cref="IAiKeyStore"/>). The request goes through the HTTP client it is given, which comes
/// from <see cref="Enterprise.NetworkGate"/>, so nothing is sent while the network is off. Only
/// ever called through <see cref="AiAssistant"/>, which checks consent first.
/// </summary>
public sealed class CloudImageDescriber(HttpClient http, IOptionsMonitor<VoxSettings> settings, IAiKeyStore keys,
    ILogger<CloudImageDescriber> logger) : IImageDescriber
{
    public const string DefaultClaudeModel = "claude-opus-5-5";
    public const string DefaultClaudeEndpoint = "https://api.anthropic.com";

    // Claude reads pictures best at up to 1568 pixels on the long side; requests take 5 MB images
    private const int MaxImageDimension = 1568;
    private const int MaxImageBytes = 3_750_000;
    private const int MaxAnswerTokens = 4096;

    // Models that take the effort setting and server-side fallbacks (others are sent neither)
    private static readonly HashSet<string> EffortModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-opus-5", "claude-fable-5-1",
    };
    private static readonly HashSet<string> FallbackModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude-opus-5-5", "claude-sonnet-5-5", "claude-opus-5", "claude-fable-5-1",
    };
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    public string Name => settings.CurrentValue.AiCloudProvider switch
    {
        AiCloudProvider.Claude => "Claude",
        _ => string.Empty,
    };

    public bool IsLocal => false;

    public bool IsAvailable => settings.CurrentValue.AiCloudProvider != AiCloudProvider.None && keys.HasKey && Endpoint is not null;

    /// <summary>
    /// The address requests go to (<c>AiEndpoint</c>, else Claude's), or null when the custom one
    /// isn't HTTPS: the key and the screenshots never go over plain HTTP, except to this computer
    /// (a local proxy).
    /// </summary>
    public string? Endpoint
    {
        get
        {
            if (settings.CurrentValue.AiEndpoint is not { Length: > 0 } configured || configured.Trim() is not { Length: > 0 } custom)
                return DefaultClaudeEndpoint;
            if (!Uri.TryCreate(custom, UriKind.Absolute, out var uri)
                || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && NetworkGate.IsLoopback(uri.Host))))
                return null;
            return custom.TrimEnd('/');
        }
    }

    /// <summary>The model requests go to.</summary>
    public string Model => settings.CurrentValue.AiModel is { Length: > 0 } model ? model.Trim() : DefaultClaudeModel;

    public async Task<string> DescribeAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (settings.CurrentValue.AiCloudProvider == AiCloudProvider.None || !keys.HasKey || keys.Read() is not { Length: > 0 } key)
            throw new AiException(AiFailure.NotSetUp, "No cloud provider or key is set up");
        if (Endpoint is null)
            throw new AiException(AiFailure.NotSetUp, "The AI endpoint isn't HTTPS; nothing was sent");

        using var message = BuildClaudeRequest(request, key);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiException(AiFailure.Failed, "The request could not be sent", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException(AiFailure.Failed, "The request timed out", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // The body names the error; it never holds what was sent
                logger.LogWarning("Claude answered {Status}: {Error}", (int)response.StatusCode, ErrorType(body));
                throw new AiException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailure.KeyRejected,
                    HttpStatusCode.TooManyRequests or (HttpStatusCode)529 => AiFailure.Busy,
                    _ => AiFailure.Failed,
                }, $"The provider answered {(int)response.StatusCode}");
            }
            return ReadClaudeAnswer(body);
        }
    }

    /// <summary>The Messages API request for <paramref name="request"/> (public for tests).</summary>
    public HttpRequestMessage BuildClaudeRequest(AiRequest request, string key)
    {
        var model = Model;
        var content = new JsonArray();
        if (request.Image is { } image)
        {
            var png = PngEncoder.EncodeWithin(image, MaxImageDimension, MaxImageBytes);
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = "image/png",
                    ["data"] = Convert.ToBase64String(png),
                },
            });
        }
        content.Add(new JsonObject { ["type"] = "text", ["text"] = AiPrompts.UserText(request) });

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = MaxAnswerTokens,
            ["system"] = AiPrompts.Instructions(request),
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = content } },
        };
        // Descriptions and short answers don't need deep thinking: low effort answers faster
        if (EffortModels.Contains(model))
            body["output_config"] = new JsonObject { ["effort"] = "low" };
        // A declined request is retried on the model Anthropic recommends (Claude API only, not a
        // custom endpoint)
        var endpoint = Endpoint ?? throw new AiException(AiFailure.NotSetUp, "The AI endpoint isn't HTTPS; nothing was sent");
        bool fallbacks = endpoint == DefaultClaudeEndpoint && FallbackModels.Contains(model);
        if (fallbacks)
            body["fallbacks"] = "default";

        var message = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-api-key", key);
        message.Headers.Add("anthropic-version", "2023-06-01");
        if (fallbacks)
            message.Headers.Add("anthropic-beta", FallbackBeta);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    /// <summary>The text of a Messages API response; throws <see cref="AiException"/> for a refusal or an unreadable one.</summary>
    public static string ReadClaudeAnswer(string body)
    {
        try
        {
            var root = JsonNode.Parse(body);
            if (root?["stop_reason"]?.GetValue<string>() == "refusal")
                throw new AiException(AiFailure.Declined, "The model declined to answer");

            var text = new StringBuilder();
            if (root?["content"] is JsonArray blocks)
            {
                // Text blocks only: thinking blocks (empty by default) are not part of the answer
                foreach (var block in blocks)
                {
                    if (block?["type"]?.GetValue<string>() == "text" && block["text"]?.GetValue<string>() is { } part)
                    {
                        if (text.Length > 0)
                            text.Append('\n');
                        text.Append(part);
                    }
                }
            }
            if (text.Length == 0)
                throw new AiException(AiFailure.Failed, "The answer had no text");
            return text.ToString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new AiException(AiFailure.Failed, "The answer could not be read", ex);
        }
    }

    private static string ErrorType(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["type"]?.GetValue<string>() ?? "unknown"; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return "unreadable"; }
    }
}
