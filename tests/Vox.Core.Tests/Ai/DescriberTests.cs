using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Ai;
using Vox.Core.Configuration;
using Vox.Core.Enterprise;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Ai;

public class CloudImageDescriberTests
{
    private readonly FakeHttpHandler _http = new()
    {
        Response = """{"stop_reason":"end_turn","content":[{"type":"thinking","thinking":""},{"type":"text","text":"A cat on a sofa."}]}""",
    };
    private readonly FakeKeyStore _keys = new("sk-test");
    private VoxSettings _settings = new() { AiCloudProvider = AiCloudProvider.Claude };

    private CloudImageDescriber Describer() => new(NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), _http),
        AiTestSupport.Settings(() => _settings), _keys, NullLogger<CloudImageDescriber>.Instance);

    [Fact]
    public async Task ItSendsTheMessagesRequest_WithThePicture()
    {
        var answer = await Describer().DescribeAsync(new AiRequest(AiTask.DescribeImage, AiTestSupport.Image(), Language: "fr-FR"));

        Assert.Equal("A cat on a sofa.", answer);
        var (request, body) = Assert.Single(_http.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-test", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("server-side-fallback-2026-07-01", request.Headers.GetValues("anthropic-beta").Single());

        var json = JsonNode.Parse(body)!;
        Assert.Equal("claude-opus-5-5", json["model"]!.GetValue<string>());
        Assert.Equal("default", json["fallbacks"]!.GetValue<string>());
        Assert.Equal("low", json["output_config"]!["effort"]!.GetValue<string>());
        Assert.Contains("French", json["system"]!.GetValue<string>());
        var content = json["messages"]![0]!["content"]!.AsArray();
        Assert.Equal("image", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("image/png", content[0]!["source"]!["media_type"]!.GetValue<string>());
        var png = Convert.FromBase64String(content[0]!["source"]!["data"]!.GetValue<string>());
        Assert.Equal(0x89, png[0]);
        Assert.Equal("text", content[1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnotherModel_GetsNoOptionsItWouldReject()
    {
        _settings = _settings with { AiModel = "claude-haiku-4-5" };

        await Describer().DescribeAsync(new AiRequest(AiTask.SummarizePage, Context: "Page text"));

        var (request, body) = Assert.Single(_http.Requests);
        var json = JsonNode.Parse(body)!;
        Assert.Equal("claude-haiku-4-5", json["model"]!.GetValue<string>());
        Assert.Null(json["fallbacks"]);
        Assert.Null(json["output_config"]);
        Assert.False(request.Headers.Contains("anthropic-beta"));
        // No picture: text only
        Assert.Single(json["messages"]![0]!["content"]!.AsArray());
    }

    [Fact]
    public async Task ACustomEndpoint_IsUsed_WithoutFallbacks()
    {
        _settings = _settings with { AiEndpoint = "https://claude.example.com/" };

        await Describer().DescribeAsync(new AiRequest(AiTask.SummarizePage, Context: "Page text"));

        var (request, body) = Assert.Single(_http.Requests);
        Assert.Equal("https://claude.example.com/v1/messages", request.RequestUri!.ToString());
        Assert.Null(JsonNode.Parse(body)!["fallbacks"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiFailure.KeyRejected)]
    [InlineData(HttpStatusCode.TooManyRequests, AiFailure.Busy)]
    [InlineData((HttpStatusCode)529, AiFailure.Busy)]
    [InlineData(HttpStatusCode.BadRequest, AiFailure.Failed)]
    public async Task HttpErrors_SayWhatWentWrong(HttpStatusCode status, AiFailure failure)
    {
        _http.Status = status;
        _http.Response = """{"type":"error","error":{"type":"some_error","message":"no"}}""";

        var error = await Assert.ThrowsAsync<AiException>(() => Describer().DescribeAsync(new AiRequest(AiTask.DescribeImage, AiTestSupport.Image())));

        Assert.Equal(failure, error.Failure);
    }

    [Fact]
    public void ARefusal_IsDeclined()
    {
        var error = Assert.Throws<AiException>(() => CloudImageDescriber.ReadClaudeAnswer(
            """{"stop_reason":"refusal","stop_details":{"type":"refusal","category":null},"content":[]}"""));

        Assert.Equal(AiFailure.Declined, error.Failure);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"content":[]}""")]
    [InlineData("""{"content":[{"type":"text","text":42}]}""")]
    public void AnUnreadableAnswer_Fails(string body)
    {
        Assert.Equal(AiFailure.Failed, Assert.Throws<AiException>(() => CloudImageDescriber.ReadClaudeAnswer(body)).Failure);
    }

    [Fact]
    public async Task WithoutAKeyOrProvider_NothingIsSent()
    {
        Assert.True(Describer().IsAvailable);
        _keys.Save(null);
        Assert.False(Describer().IsAvailable);
        _keys.Save("sk-test");
        _settings = new VoxSettings();
        Assert.False(Describer().IsAvailable);

        await Assert.ThrowsAsync<AiException>(() => Describer().DescribeAsync(new AiRequest(AiTask.DescribeImage, AiTestSupport.Image())));
        Assert.Empty(_http.Requests);
    }
}

public class LocalImageDescriberTests
{
    private readonly FakeHttpHandler _http = new() { Response = """{"message":{"role":"assistant","content":"A dog."}}""" };
    private VoxSettings _settings = new() { AiLocalModel = "llama3.2-vision" };

    private LocalImageDescriber Describer() => new(NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), _http),
        AiTestSupport.Settings(() => _settings), NullLogger<LocalImageDescriber>.Instance);

    [Fact]
    public async Task ItAsksTheModelOnThisComputer()
    {
        Assert.Equal("A dog.", await Describer().DescribeAsync(new AiRequest(AiTask.DescribeImage, AiTestSupport.Image())));

        var (request, body) = Assert.Single(_http.Requests);
        Assert.Equal("http://localhost:11434/api/chat", request.RequestUri!.ToString());
        var json = JsonNode.Parse(body)!;
        Assert.Equal("llama3.2-vision", json["model"]!.GetValue<string>());
        Assert.False(json["stream"]!.GetValue<bool>());
        Assert.Equal("system", json["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Single(json["messages"]![1]!["images"]!.AsArray());
    }

    [Fact]
    public async Task ItWorks_WithTheNetworkOff()
    {
        var describer = new LocalImageDescriber(
            NetworkGate.CreateHttpClient(RunPolicy.Normal with { NetworkOffByPolicy = true }, TimeSpan.FromSeconds(5), _http),
            AiTestSupport.Settings(() => _settings), NullLogger<LocalImageDescriber>.Instance);

        Assert.Equal("A dog.", await describer.DescribeAsync(new AiRequest(AiTask.DescribeImage, AiTestSupport.Image())));
    }

    [Fact]
    public void WithoutAModel_ItIsOff()
    {
        _settings = new VoxSettings();

        Assert.False(Describer().IsAvailable);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://192.168.1.20:11434", false)]
    [InlineData("ftp://localhost", false)]
    [InlineData("not a url", false)]
    public void OnlyThisComputer_IsUsed(string endpoint, bool used)
    {
        _settings = _settings with { AiLocalEndpoint = endpoint };

        Assert.Equal(used, Describer().IsAvailable);
    }

    [Fact]
    public async Task AModelServerThatIsntRunning_Fails()
    {
        _http.Status = HttpStatusCode.NotFound;

        Assert.Equal(AiFailure.Failed, (await Assert.ThrowsAsync<AiException>(() =>
            Describer().DescribeAsync(new AiRequest(AiTask.DescribeImage, AiTestSupport.Image())))).Failure);
    }
}

public class AiPromptsTests
{
    [Fact]
    public void TheQuestion_AndTheWindowText_AreSent()
    {
        var text = AiPrompts.UserText(new AiRequest(AiTask.ExplainScreen, Question: " How do I save? ", Context: "Save button\nFile menu"));

        Assert.Contains("Save button\nFile menu", text);
        Assert.EndsWith("Question: How do I save?", text);
    }

    [Fact]
    public void AVeryLongPage_IsCut_AndTheModelIsTold()
    {
        var text = AiPrompts.UserText(new AiRequest(AiTask.SummarizePage, Context: new string('a', AiPrompts.MaxContextLength + 10)));

        Assert.Contains("left out", text);
        Assert.True(text.Length < AiPrompts.MaxContextLength + 200);
    }

    [Fact]
    public void Answers_AreAskedForAsPlainText_InTheLanguage()
    {
        var instructions = AiPrompts.Instructions(new AiRequest(AiTask.DescribeImage, Language: "de-DE"));

        Assert.Contains("plain text", instructions);
        Assert.Contains("German", instructions);
        Assert.Contains("English", AiPrompts.Instructions(new AiRequest(AiTask.DescribeImage)));
    }
}
