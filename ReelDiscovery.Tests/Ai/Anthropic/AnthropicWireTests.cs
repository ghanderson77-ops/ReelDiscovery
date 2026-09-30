using System.Text.Json;
using System.Text.Json.Nodes;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai.Anthropic;

public class AnthropicWireTests
{
    public class ChildDto
    {
        public string Label { get; set; } = string.Empty;
    }

    public class SimpleDto
    {
        public string Name { get; set; } = string.Empty;
        public ChildDto Child { get; set; } = new();
    }

    internal const string SuccessBody =
        "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-haiku-4-5-20251001\"," +
        "\"content\":[{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"sig\"},{\"type\":\"text\",\"text\":\"{\\\"a\\\":1}\"}]," +
        "\"stop_reason\":\"end_turn\",\"stop_sequence\":null,\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}";

    internal static string BodyWithStop(string stopReason, bool includeText = true)
    {
        var content = includeText
            ? "[{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"sig\"},{\"type\":\"text\",\"text\":\"partial\"}]"
            : "[{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"sig\"}]";
        return "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5-5\"," +
               $"\"content\":{content},\"stop_reason\":\"{stopReason}\",\"stop_sequence\":null," +
               "\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}";
    }

    internal static AnthropicTextProvider CreateProvider(FakeHttpHandler handler, int maxRetries = 0, string apiKey = "test") =>
        new(new global::Anthropic.Core.ClientOptions
        {
            ApiKey = apiKey,
            HttpClient = handler.CreateClient(),
            MaxRetries = maxRetries
        });

    internal static LlmProviderRequest CreateRequest(string? effort = null, int maxTokens = 1000) => new()
    {
        ModelId = "claude-opus-5-5",
        Effort = effort,
        SchemaName = JsonSchemaRegistry.GetSchemaName(typeof(SimpleDto)),
        SchemaJson = JsonSchemaRegistry.GetSchemaJson(typeof(SimpleDto)),
        SystemPrompt = "You are a test system prompt.",
        UserPrompt = "Produce the JSON.",
        MaxTokens = maxTokens
    };

    private static async Task<(JsonObject Body, LlmProviderResponse Response, FakeHttpHandler Handler)> SendAsync(LlmProviderRequest request)
    {
        var handler = new FakeHttpHandler { Body = SuccessBody };
        var provider = CreateProvider(handler);
        var response = await provider.SendAsync(request);
        var body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        return (body, response, handler);
    }

    [Fact]
    public async Task Request_HasStrictOutputConfig()
    {
        var request = CreateRequest();
        var (body, _, _) = await SendAsync(request);

        var format = body["output_config"]!["format"]!;
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(request.SchemaJson), format["schema"]));
        Assert.Equal("claude-opus-5-5", body["model"]!.GetValue<string>());
        Assert.Equal(1000, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task Request_SystemIsListForm()
    {
        var request = CreateRequest();
        var (body, _, _) = await SendAsync(request);

        var system = body["system"]!.AsArray();
        Assert.Equal("text", system[0]!["type"]!.GetValue<string>());
        Assert.Equal(request.SystemPrompt, system[0]!["text"]!.GetValue<string>());

        var message = body["messages"]!.AsArray()[0]!;
        Assert.Equal("user", message["role"]!.GetValue<string>());
        Assert.Equal(request.UserPrompt, message["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Request_NeverSendsSamplingOrThinking()
    {
        var handler = new FakeHttpHandler { Body = SuccessBody };
        await CreateProvider(handler).SendAsync(CreateRequest(effort: "high"));

        var raw = handler.LastRequestBody!;
        Assert.DoesNotContain("temperature", raw);
        Assert.DoesNotContain("top_p", raw);
        Assert.DoesNotContain("top_k", raw);
        Assert.DoesNotContain("thinking", raw);
    }

    [Fact]
    public async Task Request_EffortOnlyWhenSet()
    {
        var (noEffort, _, handler) = await SendAsync(CreateRequest(effort: null));
        Assert.DoesNotContain("effort", handler.LastRequestBody!);
        Assert.False(noEffort["output_config"]!.AsObject().ContainsKey("effort"));

        var (withEffort, _, _) = await SendAsync(CreateRequest(effort: "low"));
        Assert.Equal("low", withEffort["output_config"]!["effort"]!.GetValue<string>());
    }

    [Fact]
    public async Task Response_SkipsThinkingBlock()
    {
        var (_, response, _) = await SendAsync(CreateRequest());

        Assert.Equal(LlmOutcome.Succeeded, response.Outcome);
        Assert.Equal("{\"a\":1}", response.Text);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
    }

    [Fact]
    public async Task Response_Refusal()
    {
        var handler = new FakeHttpHandler { Body = BodyWithStop("refusal", includeText: false) };
        var response = await CreateProvider(handler).SendAsync(CreateRequest());

        Assert.Equal(LlmOutcome.Refused, response.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task Response_MaxTokens()
    {
        var handler = new FakeHttpHandler { Body = BodyWithStop("max_tokens", includeText: false) };
        var response = await CreateProvider(handler).SendAsync(CreateRequest());

        Assert.Equal(LlmOutcome.Truncated, response.Outcome);
    }

    [Fact]
    public async Task Response_UnknownStop()
    {
        var handler = new FakeHttpHandler { Body = BodyWithStop("pause_turn") };
        var response = await CreateProvider(handler).SendAsync(CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(LlmFailureKind.Unknown, response.FailureKind);
    }

    [Fact]
    public async Task Request_MaxTokensClampedForNonStreaming()
    {
        var (big, _, bigHandler) = await SendAsync(CreateRequest(maxTokens: 128_000));
        Assert.Equal(1, bigHandler.Calls);
        Assert.Equal(AnthropicTextProvider.MaxNonStreamingTokens, big["max_tokens"]!.GetValue<int>());
        Assert.Equal(21_333, AnthropicTextProvider.MaxNonStreamingTokens);

        var (normal, _, _) = await SendAsync(CreateRequest(maxTokens: 16_000));
        Assert.Equal(16_000, normal["max_tokens"]!.GetValue<int>());
    }
}
