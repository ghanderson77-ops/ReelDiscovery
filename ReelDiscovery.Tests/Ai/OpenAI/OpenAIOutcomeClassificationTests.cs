using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai.OpenAI;

public class OpenAIOutcomeClassificationTests
{
    internal static string ErrorBody(string type, string code, string message) =>
        $"{{\"error\":{{\"message\":\"{message}\",\"type\":\"{type}\",\"param\":null,\"code\":\"{code}\"}}}}";

    internal static readonly string InsufficientQuotaBody = ErrorBody(
        "insufficient_quota",
        "insufficient_quota",
        "You exceeded your current quota, please check your plan and billing details.");

    private static string ChatBody(string? content, string? refusal, string finishReason)
    {
        static string Json(string? s) => s == null ? "null" : System.Text.Json.JsonSerializer.Serialize(s);
        return "{\"id\":\"c1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"gpt-6-luna\",\"choices\":[{\"index\":0," +
               $"\"message\":{{\"role\":\"assistant\",\"content\":{Json(content)},\"refusal\":{Json(refusal)}}},\"finish_reason\":\"{finishReason}\"}}]," +
               "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}";
    }

    [Fact]
    public async Task Refusal()
    {
        var handler = new FakeHttpHandler { Body = ChatBody(null, "I can't help", "stop") };

        var response = await OpenAIWireTests.CreateProvider(handler).SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Refused, response.Outcome);
        Assert.Contains("I can't help", response.Message);
    }

    [Fact]
    public async Task Length()
    {
        var handler = new FakeHttpHandler { Body = ChatBody("{\"a\":", null, "length") };

        var response = await OpenAIWireTests.CreateProvider(handler).SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Truncated, response.Outcome);
        Assert.Equal(5, response.OutputTokens);
    }

    [Fact]
    public async Task ContentFilter()
    {
        var handler = new FakeHttpHandler { Body = ChatBody(null, null, "content_filter") };

        var response = await OpenAIWireTests.CreateProvider(handler).SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Refused, response.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    public static IEnumerable<object[]> StatusCases() => new List<object[]>
    {
        new object[] { 401, ErrorBody("invalid_request_error", "invalid_api_key", "Incorrect API key provided."), LlmFailureKind.Auth },
        new object[] { 403, ErrorBody("invalid_request_error", "unsupported_country_region_territory", "Country not supported."), LlmFailureKind.Auth },
        new object[] { 429, InsufficientQuotaBody, LlmFailureKind.SpendLimit },
        new object[] { 429, ErrorBody("requests", "rate_limit_exceeded", "Rate limit reached for requests."), LlmFailureKind.RateLimited },
        new object[] { 400, ErrorBody("invalid_request_error", "invalid_value", "Invalid schema."), LlmFailureKind.BadRequest },
        new object[] { 404, ErrorBody("invalid_request_error", "model_not_found", "The model does not exist."), LlmFailureKind.BadRequest },
        new object[] { 500, ErrorBody("server_error", "server_error", "The server had an error."), LlmFailureKind.Transient },
        new object[] { 503, ErrorBody("server_error", "overloaded", "The engine is currently overloaded."), LlmFailureKind.Transient },
    };

    [Theory]
    [MemberData(nameof(StatusCases))]
    public async Task StatusMapping(int status, string body, LlmFailureKind expected)
    {
        var handler = new FakeHttpHandler { Status = status, Body = body };

        var response = await OpenAIWireTests.CreateProvider(handler).SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(expected, response.FailureKind);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task NetworkFailure()
    {
        var handler = new FakeHttpHandler { ThrowOnSend = new HttpRequestException("down") };

        var response = await OpenAIWireTests.CreateProvider(handler).SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(LlmFailureKind.Network, response.FailureKind);
    }

    [Fact]
    public async Task InsufficientQuota_NotRetriedByTransport()
    {
        var handler = new FakeHttpHandler();
        handler.Responses.Enqueue((429, InsufficientQuotaBody));
        handler.Responses.Enqueue((200, OpenAIWireTests.SuccessBody));
        var provider = OpenAIWireTests.CreateProvider(handler, retryPolicy: new QuotaAwareRetryPolicy(maxRetries: 3));

        var response = await provider.SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(LlmFailureKind.SpendLimit, response.FailureKind);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PlainRateLimit_StillRetriedByTransport()
    {
        var handler = new FakeHttpHandler();
        handler.ResponseHeaders["retry-after-ms"] = "0";
        handler.Responses.Enqueue((429, ErrorBody("requests", "rate_limit_exceeded", "Rate limit reached for requests.")));
        handler.Responses.Enqueue((200, OpenAIWireTests.SuccessBody));
        var provider = OpenAIWireTests.CreateProvider(handler, retryPolicy: new QuotaAwareRetryPolicy(maxRetries: 3));

        var response = await provider.SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Succeeded, response.Outcome);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Message_NeverContainsKey()
    {
        const string key = "sk-test-secret-456";
        var handler = new FakeHttpHandler
        {
            Status = 401,
            Body = ErrorBody("invalid_request_error", "invalid_api_key", "Incorrect API key provided: sk-test-***456.")
        };

        var response = await OpenAIWireTests.CreateProvider(handler, apiKey: key).SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmFailureKind.Auth, response.FailureKind);
        Assert.DoesNotContain(key, response.Message);
    }
}
