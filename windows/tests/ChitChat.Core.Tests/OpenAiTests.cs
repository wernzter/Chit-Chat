using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using ChitChat.Abstractions;
using ChitChat.Core;
using Xunit;

namespace ChitChat.Core.Tests;

public sealed class OpenAiTests
{
    private const string FakeKey = "test-credential";

    [Fact]
    public async Task Sends_framed_text_to_responses_with_storage_off_and_only_the_selected_effort()
    {
        using var http = Http(async (request, cancellationToken) =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(FakeKey, request.Headers.Authorization?.Parameter);

            using var body = await ReadBodyAsync(request, cancellationToken);
            var fields = body.RootElement;
            Assert.Equal("gpt-5.4-mini", fields.GetProperty("model").GetString());
            Assert.False(fields.GetProperty("store").GetBoolean());
            Assert.Equal(8192, fields.GetProperty("max_output_tokens").GetInt32());
            Assert.Equal("none", fields.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Equal("<transcript>\nCan you resend the slide?\n</transcript>", fields.GetProperty("input").GetString());
            Assert.StartsWith("Correct spelling.\n\nThe input is a dictated transcript", fields.GetProperty("instructions").GetString());
            return Completed("<transcript>Can you resend the slide?</transcript>");
        });

        var output = await new OpenAiClient(http).EnhanceAsync("Can you resend the slide?", FakeKey, "gpt-5.4-mini",
            "Correct spelling.", OpenAiReasoningEffort.Off);
        Assert.Equal("Can you resend the slide?", output);
    }

    [Fact]
    public async Task Uses_the_public_sample_for_a_connection_test_and_never_sends_saved_prompt()
    {
        using var http = Http(async (request, cancellationToken) =>
        {
            using var body = await ReadBodyAsync(request, cancellationToken);
            var fields = body.RootElement;
            Assert.Contains(OpenAiEnhancement.Sample, fields.GetProperty("input").GetString());
            Assert.StartsWith(OpenAiSettings.DefaultPrompt, fields.GetProperty("instructions").GetString());
            Assert.False(fields.TryGetProperty("reasoning", out _));
            return Completed("Please send the updated agenda.");
        });

        var service = new OpenAiEnhancement(new MemoryKey(FakeKey), new OpenAiClient(http));
        var settings = new OpenAiSettings { Enabled = false, MinimumWords = int.MaxValue, Prompt = "Never sent." };
        Assert.Contains("Connection verified", await service.TestConnectionAsync(settings));
    }

    [Fact]
    public async Task Prompt_preview_uses_custom_instructions_and_speed_uses_the_standard_prompt()
    {
        var expected = new Queue<string>(["Punctuate the sample.", OpenAiSettings.DefaultPrompt]);
        using var http = Http(async (request, cancellationToken) =>
        {
            using var body = await ReadBodyAsync(request, cancellationToken);
            Assert.Contains(OpenAiEnhancement.Sample, body.RootElement.GetProperty("input").GetString());
            Assert.StartsWith(expected.Dequeue(), body.RootElement.GetProperty("instructions").GetString());
            return Completed("Please send the updated agenda.");
        });
        var service = new OpenAiEnhancement(new MemoryKey(FakeKey), new OpenAiClient(http));
        var settings = new OpenAiSettings { Prompt = "Punctuate the sample." };

        var preview = await service.TestModelAsync(settings, speedOnly: false);
        var benchmark = await service.TestModelAsync(settings, speedOnly: true);
        Assert.Equal("Please send the updated agenda.", preview.Text);
        Assert.Equal(settings.Model, benchmark.Model);
        Assert.True(benchmark.Elapsed >= TimeSpan.Zero);
        Assert.Empty(expected);
    }

    [Fact]
    public async Task Refreshes_only_text_model_candidates_and_accepts_unknown_custom_model_as_default()
    {
        using var http = Http((request, _) =>
        {
            Assert.Equal("https://api.openai.com/v1/models", request.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json("""
                {"data":[{"id":"o3"},{"id":"gpt-4.1-mini"},{"id":"gpt-4o-realtime"},
                         {"id":"ft:example"},{"id":"text-embedding-3-small"},{"id":"gpt-4.1-mini"}]}
                """));
        });

        var result = await new OpenAiClient(http).ModelsAsync(FakeKey);
        Assert.Equal(["ft:example", "gpt-4.1-mini", "o3"], result);
        Assert.Equal([OpenAiReasoningEffort.ModelDefault], OpenAiReasoning.Options("gpt-5-new-custom"));
        Assert.Contains(OpenAiReasoningEffort.Off, OpenAiReasoning.Options("gpt-5.4-mini-2026-09-06"));
    }

    [Fact]
    public async Task Opt_in_gate_and_threshold_use_the_original_text_without_reading_the_key()
    {
        var key = new MemoryKey(FakeKey);
        using var http = Http((_, _) => throw new InvalidOperationException("The request must be skipped."));
        var service = new OpenAiEnhancement(key, new OpenAiClient(http));

        var disabled = new OpenAiSettings();
        Assert.Equal("Keep this stable.", await service.EnhanceAsync("Keep this stable.", "one two three four five", disabled, default));

        var enabled = disabled with { Enabled = true, MinimumWords = 3 };
        Assert.Equal("Keep this stable.", await service.EnhanceAsync("Keep this stable.", "Café　…　②", enabled, default));
        Assert.Contains("2 words", service.LastDictationStatus);
        Assert.Equal(0, key.Reads);
    }

    [Theory]
    [InlineData("""{"status":"incomplete","output":[{"type":"message","content":[{"type":"output_text","text":"PARTIAL"}]}]}""")]
    [InlineData("""{"status":"completed","output":[{"type":"message","content":[{"type":"refusal"}]}]}""")]
    [InlineData("""{"status":"completed","output":[]}""")]
    [InlineData("""{"status":"completed","output":3}""")]
    public async Task Keeps_local_text_on_partial_refused_empty_or_unreadable_content(string response)
    {
        using var http = Http((_, _) => Task.FromResult(Json(response)));
        var service = new OpenAiEnhancement(new MemoryKey(FakeKey), new OpenAiClient(http));
        var settings = new OpenAiSettings { Enabled = true, MinimumWords = 0 };

        Assert.Equal("Local text.", await service.EnhanceAsync("Local text.", "example", settings, default));
        Assert.StartsWith("Used local text.", service.LastDictationStatus);
    }

    [Fact]
    public async Task Maps_rate_limits_to_safe_feedback_without_echoing_the_error_response()
    {
        using var http = Http((_, _) => Task.FromResult(Json("""{"message":"test-sensitive-server-text"}""",
            HttpStatusCode.TooManyRequests)));
        var service = new OpenAiEnhancement(new MemoryKey(FakeKey), new OpenAiClient(http));
        var settings = new OpenAiSettings { Enabled = true, MinimumWords = 0 };
        Assert.Equal("Local.", await service.EnhanceAsync("Local.", "example", settings, default));
        Assert.Contains("quota", service.LastDictationStatus);
        Assert.DoesNotContain("test-sensitive-server-text", service.LastDictationStatus);
    }

    [Fact]
    public async Task Propagates_dictation_cancellation_without_returning_a_fallback_for_insertion()
    {
        TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Http(async (_, cancellationToken) =>
        {
            pending.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        });
        var service = new OpenAiEnhancement(new MemoryKey(FakeKey), new OpenAiClient(http));
        using var cancellation = new CancellationTokenSource();
        var settings = new OpenAiSettings { Enabled = true, MinimumWords = 0 };

        var attempt = service.EnhanceAsync("Local.", "example", settings, cancellation.Token);
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.Null(service.LastDictationStatus);
    }

    [Fact]
    public async Task Timed_out_transport_falls_back_but_keeps_the_caller_uncancelled()
    {
        using var http = Http(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        });
        http.Timeout = TimeSpan.FromMilliseconds(30);
        var service = new OpenAiEnhancement(new MemoryKey(FakeKey), new OpenAiClient(http));
        var settings = new OpenAiSettings { Enabled = true, MinimumWords = 0 };
        Assert.Equal("Local.", await service.EnhanceAsync("Local.", "example", settings, default));
        Assert.Contains("timed out", service.LastDictationStatus);
    }

    [Fact]
    public void Timing_ranks_measured_efforts_before_untested_models()
    {
        const string measured = "gpt-5.4-mini";
        var settings = new OpenAiSettings { Sort = OpenAiModelSort.Speed };
        var result = settings.WithEffort(OpenAiReasoningEffort.High)
            .WithTiming(measured, OpenAiReasoningEffort.High, TimeSpan.FromSeconds(0.75));

        Assert.Equal(measured, result.SortedModels()[0]);
        Assert.Equal(0.75, result.SecondsFor(measured));
        Assert.Null(result.WithEffort(OpenAiReasoningEffort.Off).SecondsFor(measured));
    }

    [Fact]
    public void Preferences_round_trip_and_api_key_only_reaches_the_credential_store()
    {
        var folder = Directory.CreateTempSubdirectory("chitchat-openai-test-");
        try
        {
            var path = Path.Combine(folder.FullName, "settings.json");
            var preferences = new AppSettings(path);
            var store = new MemoryKey(null);
            var service = new OpenAiEnhancement(store);
            Assert.True(service.SaveKey($"  {FakeKey}  ", out _));
            Assert.Equal(FakeKey, store.Read());
            Assert.False(service.SaveKey("line\nbreak", out _));

            var edited = preferences.Data.OpenAi with { Enabled = true, MinimumWords = 7 };
            edited = edited.WithEffort(OpenAiReasoningEffort.High)
                .WithTiming(edited.Model, OpenAiReasoningEffort.High, TimeSpan.FromSeconds(0.9));
            preferences.Update(preferences.Data with { OpenAi = edited });
            var persisted = new AppSettings(path).Data.OpenAi;
            Assert.True(persisted.Enabled);
            Assert.Equal(7, persisted.MinimumWords);
            Assert.Equal(OpenAiReasoningEffort.High, persisted.EffortFor(edited.Model));
            Assert.Equal(0.9, persisted.SecondsFor(edited.Model));
            Assert.DoesNotContain(FakeKey, File.ReadAllText(path));
            Assert.True(service.RemoveKey(out _));
            Assert.Null(store.Read());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Storage_failure_has_safe_feedback_and_local_fallback()
    {
        var service = new OpenAiEnhancement(new FailingKey());
        Assert.False(service.SaveKey(FakeKey, out var saveMessage));
        Assert.Contains("Windows Credential Manager", saveMessage);
        Assert.DoesNotContain(FakeKey, saveMessage);

        var settings = new OpenAiSettings { Enabled = true, MinimumWords = 0 };
        Assert.Equal("Local.", await service.EnhanceAsync("Local.", "example", settings, default));
        Assert.Contains("could not read", service.LastDictationStatus);
        Assert.DoesNotContain(FakeKey, service.LastDictationStatus);
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.NotNull(request.Content);
        return JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
    }

    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new StubHandler(send));

    private static HttpResponseMessage Completed(string text) => Json(
        "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":" +
        JsonSerializer.Serialize(text) + "}]}]}");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class MemoryKey(string? key) : IOpenAiKeyStore
    {
        internal int Reads { get; private set; }

        public string? Read()
        {
            Reads++;
            return key;
        }
        public void Save(string value) => key = value;
        public void Remove() => key = null;
    }

    private sealed class FailingKey : IOpenAiKeyStore
    {
        public string? Read() => throw new Win32Exception(FakeKey);
        public void Save(string key) => throw new Win32Exception(key);
        public void Remove() => throw new Win32Exception(FakeKey);
    }
}
