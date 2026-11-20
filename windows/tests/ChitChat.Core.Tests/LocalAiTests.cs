using System.Net;
using System.Text;
using System.Text.Json;
using ChitChat.Core;
using Xunit;

namespace ChitChat.Core.Tests;

public sealed class LocalAiTests
{
    private const string Model = "qwen2.5:1.5b";
    private const string Transcript = "um please send the draft on friday and check the attachment";
    private const string Edited = "Please send the draft on Friday and check the attachment.";
    private const string PrivateMode = """{"cloud":{"disabled":true,"source":"env"}}""";
    private const string ModelList = """
        {"models":[{"model":"qwen2.5:1.5b","size":986000000,
        "details":{"format":"gguf"},"capabilities":["completion"]}]}
        """;
    private const string LocalDetail = """{"details":{"format":"gguf"},"capabilities":["completion"]}""";
    private static readonly LocalAiSettings Enabled = new() { Enabled = true, Model = Model };

    [Fact]
    public async Task Checks_privacy_and_local_model_before_sending_text_to_loopback()
    {
        var steps = new Queue<string>(["/api/status", "/api/tags", "/api/show", "/api/generate"]);
        using var http = Http(async (request, token) =>
        {
            var path = steps.Dequeue();
            Assert.Equal(new Uri("http://127.0.0.1:11434" + path), request.RequestUri);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));

            if (path is "/api/status" or "/api/tags")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Null(request.Content);
                return Metadata(path);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(Model + ":local", body.RootElement.GetProperty("model").GetString());
            if (path == "/api/show")
            {
                Assert.False(body.RootElement.TryGetProperty("prompt", out _));
                return Metadata(path);
            }

            Assert.Equal(Transcript, body.RootElement.GetProperty("prompt").GetString());
            Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal(768, body.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
            return Json("""{"response":"Please send the draft on Friday and check the attachment.","done":true,"done_reason":"stop"}""");
        });
        var enhancement = new LocalAiEnhancement(http);
        Assert.Equal(Edited, await enhancement.EnhanceAsync(Transcript, Transcript, Enabled, 0, default));
        Assert.Empty(steps);
        Assert.Contains("enhanced on this PC", enhancement.LastDictationStatus);
    }

    [Theory]
    [InlineData("""{"cloud":{"disabled":false}}""", HttpStatusCode.OK, "Disable Ollama cloud")]
    [InlineData("""{"cloud":{"disabled":"true"}}""", HttpStatusCode.OK, "Disable Ollama cloud")]
    [InlineData("{}", HttpStatusCode.OK, "Update Ollama")]
    [InlineData("{}", HttpStatusCode.NotFound, "Update Ollama")]
    [InlineData("{}", HttpStatusCode.TemporaryRedirect, "could not complete")]
    public async Task Does_not_send_text_when_local_only_mode_cannot_be_verified(string status, HttpStatusCode code,
                                                                              string message)
    {
        using var http = Http((request, _) =>
        {
            Assert.Equal("/api/status", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(status, code));
        });
        var enhancement = new LocalAiEnhancement(http);
        Assert.Equal(Transcript, await enhancement.EnhanceAsync(Transcript, Transcript, Enabled, 0, default));
        Assert.Contains(message, enhancement.LastDictationStatus);
    }

    [Fact]
    public async Task Can_discover_installed_local_text_models()
    {
        using var http = Http((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/status" => Json(PrivateMode),
            "/api/tags" => Json("""
                {"models":[
                  {"model":"qwen2.5:1.5b","size":986000000,"details":{"format":"gguf"}},
                  {"model":"llama3.2:1b","size":1000000,"details":{"format":"gguf"},"capabilities":["completion"]},
                  {"model":"alias:latest","remote_model":"other","remote_host":"https://example.invalid",
                   "size":20,"details":{"format":"gguf"}},
                  {"model":"other:cloud","size":20,"details":{"format":"gguf"}},
                  {"model":"other:1b-cloud","size":20,"details":{"format":"gguf"}},
                  {"model":"embeddings:latest","size":20,"details":{"format":"gguf"},"capabilities":["embedding"]},
                  {"model":"unavailable:latest","size":0,"details":{"format":"gguf"}},
                  {"model":"external://other:latest","size":20,"details":{"format":"gguf"}}
                ]}
                """),
            _ => throw new InvalidOperationException("Discovery may not send text."),
        }));
        var availability = await new LocalAiEnhancement(http).CheckAsync();
        Assert.Equal(["llama3.2:1b", Model], availability.Models);
        Assert.Contains("ready", availability.Status);
    }

    [Fact]
    public async Task Refuses_an_alias_that_changed_since_discovery()
    {
        using var http = Http((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/show" => Json("""{"details":{"format":"gguf"},"remote_host":"https://example.invalid"}"""),
            "/api/generate" => throw new InvalidOperationException("The selected model is a remote alias."),
            var path => Metadata(path),
        }));
        var enhancement = new LocalAiEnhancement(http);
        Assert.Equal(Transcript, await enhancement.EnhanceAsync(Transcript, Transcript, Enabled, 0, default));
        Assert.Contains("Install an Ollama text model", enhancement.LastDictationStatus);
    }

    [Theory]
    [InlineData("""{"response":"Please send the draft.","done":true,"done_reason":"length"}""")]
    [InlineData("""{"response":"Please send the draft two months early.","done":true,"done_reason":"stop"}""")]
    [InlineData("""{"response":"Please send the draft on Friday and check the attachment.","done":false}""")]
    [InlineData("""{"response":"","done":true}""")]
    [InlineData("malformed")]
    public async Task Uses_basic_cleanup_on_incomplete_or_unfaithful_generation(string generated)
    {
        using var http = Http((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/generate"
            ? Json(generated) : Metadata(request.RequestUri.AbsolutePath)));
        var enhancement = new LocalAiEnhancement(http);
        Assert.Equal(Transcript, await enhancement.EnhanceAsync(Transcript, Transcript, Enabled, 0, default));
        Assert.StartsWith("Used basic cleanup.", enhancement.LastDictationStatus);
    }

    [Fact]
    public async Task Disabled_or_ineligible_enhancement_makes_no_requests()
    {
        using var http = Http((_, _) => throw new InvalidOperationException("The request must be skipped."));
        var enhancement = new LocalAiEnhancement(http);
        Assert.Equal(Transcript, await enhancement.EnhanceAsync(Transcript, Transcript, new(), 0, default));
        Assert.Equal(Transcript, await enhancement.EnhanceAsync(Transcript, "Café　…　②", Enabled, 3, default));
        Assert.Contains("below the AI word minimum", enhancement.LastDictationStatus);
        var longText = new string('a', 3_001);
        Assert.Equal(longText, await enhancement.EnhanceAsync(longText, longText, Enabled, 0, default));
        Assert.Contains("too long", enhancement.LastDictationStatus);
    }

    [Fact]
    public async Task Cancellation_interrupts_generation_and_propagates()
    {
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Http(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/generate") return Metadata(request.RequestUri.AbsolutePath);
            ready.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Expected cancellation.");
        });
        using var cancellation = new CancellationTokenSource();
        var enhancement = new LocalAiEnhancement(http);
        var attempt = enhancement.EnhanceAsync(Transcript, Transcript, Enabled, 0, cancellation.Token);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.Empty(enhancement.LastDictationStatus);
    }

    [Fact]
    public async Task A_transport_timeout_preserves_basic_text()
    {
        using var http = Http(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Expected timeout.");
        });
        http.Timeout = TimeSpan.FromMilliseconds(25);
        var enhancement = new LocalAiEnhancement(http);
        Assert.Equal(Transcript, await enhancement.EnhanceAsync(Transcript, Transcript, Enabled, 0, default));
        Assert.Contains("took too long", enhancement.LastDictationStatus);
    }

    private static HttpResponseMessage Metadata(string path) => path switch
    {
        "/api/status" => Json(PrivateMode),
        "/api/tags" => Json(ModelList),
        "/api/show" => Json(LocalDetail),
        _ => throw new InvalidOperationException("Unexpected test request."),
    };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(new TestTransport(respond));

    private sealed class TestTransport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
