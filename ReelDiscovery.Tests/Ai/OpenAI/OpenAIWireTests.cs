using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using OpenAI;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai.OpenAI;

public class OpenAIWireTests
{
    internal const string SchemaJson =
        "{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"integer\"}},\"required\":[\"a\"],\"additionalProperties\":false}";

    internal const string SuccessBody =
        "{\"id\":\"c1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"gpt-6-luna\",\"choices\":[{\"index\":0," +
        "\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"a\\\":1}\",\"refusal\":null},\"finish_reason\":\"stop\"}]," +
        "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}";

    internal static LlmProviderRequest CreateRequest(string? effort = null) => new()
    {
        ModelId = "gpt-6-luna",
        Effort = effort,
        SchemaName = "SampleDto",
        SchemaJson = SchemaJson,
        SystemPrompt = "You are a test system prompt.",
        UserPrompt = "Produce a.",
        MaxTokens = 4096
    };

    internal static OpenAITextProvider CreateProvider(FakeHttpHandler handler, string apiKey = "sk-test", ClientRetryPolicy? retryPolicy = null) =>
        new(apiKey, new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(handler.CreateClient()),
            RetryPolicy = retryPolicy ?? new ClientRetryPolicy(0)
        });

    private static async Task<JsonObject> SendAndCaptureAsync(LlmProviderRequest request)
    {
        var handler = new FakeHttpHandler { Body = SuccessBody };
        await CreateProvider(handler).SendAsync(request);
        return JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
    }

    [Fact]
    public async Task Request_HasStrictJsonSchema()
    {
        var body = await SendAndCaptureAsync(CreateRequest());

        var format = body["response_format"]!.AsObject();
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        var jsonSchema = format["json_schema"]!.AsObject();
        Assert.True(jsonSchema["strict"]!.GetValue<bool>());
        Assert.Equal("SampleDto", jsonSchema["name"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SchemaJson), jsonSchema["schema"]));
        Assert.Equal("gpt-6-luna", body["model"]!.GetValue<string>());
        Assert.Equal(4096, body["max_completion_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task Request_MessagesSystemThenUser()
    {
        var body = await SendAndCaptureAsync(CreateRequest());

        var messages = body["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("You are a test system prompt.", MessageText(messages[0]!));
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Equal("Produce a.", MessageText(messages[1]!));
        Assert.DoesNotContain("Respond ONLY with valid JSON", body.ToJsonString());
    }

    [Fact]
    public async Task Request_EffortOnlyWhenSet()
    {
        var withEffort = await SendAndCaptureAsync(CreateRequest(effort: "none"));
        var withoutEffort = await SendAndCaptureAsync(CreateRequest(effort: null));

        Assert.Equal("none", withEffort["reasoning_effort"]!.GetValue<string>());
        Assert.False(withoutEffort.ContainsKey("reasoning_effort"));
    }

    [Fact]
    public async Task Request_NoTemperature()
    {
        var body = await SendAndCaptureAsync(CreateRequest(effort: "low"));

        Assert.DoesNotContain("temperature", body.ToJsonString());
    }

    [Fact]
    public async Task Response_Stop_Succeeded()
    {
        var handler = new FakeHttpHandler { Body = SuccessBody };

        var response = await CreateProvider(handler).SendAsync(CreateRequest());

        Assert.Equal(LlmOutcome.Succeeded, response.Outcome);
        Assert.Equal(LlmFailureKind.None, response.FailureKind);
        Assert.Equal("{\"a\":1}", response.Text);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
    }

    private static string MessageText(JsonNode message)
    {
        // The SDK may serialize content as a plain string or as a list of text parts
        var content = message["content"]!;
        if (content is JsonValue)
            return content.GetValue<string>();

        return string.Concat(content.AsArray().Select(p => p!["text"]!.GetValue<string>()));
    }
}
