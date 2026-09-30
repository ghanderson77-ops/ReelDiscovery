using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai.Anthropic;

public class AnthropicOutcomeClassificationTests
{
    internal static string ErrorBody(string type, string message) =>
        $"{{\"type\":\"error\",\"error\":{{\"type\":\"{type}\",\"message\":\"{message}\"}}}}";

    internal const string SpendCapBody =
        "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"You have reached your spend limit.\"," +
        "\"details\":{\"error_code\":\"enforced_spend_limit_reached\"}}}";

    private static AnthropicTextProvider CreateGuardedProvider(FakeHttpHandler handler, int maxRetries, string apiKey = "test") =>
        new(new global::Anthropic.Core.ClientOptions
        {
            ApiKey = apiKey,
            HttpClient = AnthropicTextProvider.CreateHttpClient(handler),
            MaxRetries = maxRetries
        });

    public static IEnumerable<object[]> ErrorCases() => new List<object[]>
    {
        new object[] { 401, ErrorBody("authentication_error", "invalid x-api-key"), LlmFailureKind.Auth },
        new object[] { 403, ErrorBody("permission_error", "no access"), LlmFailureKind.Auth },
        new object[] { 429, SpendCapBody, LlmFailureKind.SpendLimit },
        new object[] { 429, ErrorBody("rate_limit_error", "Number of requests has exceeded your rate limit"), LlmFailureKind.RateLimited },
        new object[] { 402, ErrorBody("billing_error", "Payment required"), LlmFailureKind.SpendLimit },
        new object[] { 400, ErrorBody("invalid_request_error", "You have reached your specified API usage limits. You will regain access on 2026-10-01"), LlmFailureKind.SpendLimit },
        new object[] { 400, ErrorBody("invalid_request_error", "Your credit balance is too low to access the Anthropic API."), LlmFailureKind.SpendLimit },
        new object[] { 400, ErrorBody("invalid_request_error", "max_tokens: field required"), LlmFailureKind.BadRequest },
        new object[] { 404, ErrorBody("not_found_error", "model: claude-nope"), LlmFailureKind.BadRequest },
        new object[] { 500, ErrorBody("api_error", "Internal server error"), LlmFailureKind.Transient },
        new object[] { 529, ErrorBody("overloaded_error", "Overloaded"), LlmFailureKind.Transient },
    };

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task ErrorStatus_MapsToFailureKind(int status, string body, LlmFailureKind expected)
    {
        var handler = new FakeHttpHandler { Status = status, Body = body };
        var provider = CreateGuardedProvider(handler, maxRetries: 0);

        var response = await provider.SendAsync(AnthropicWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(expected, response.FailureKind);
        Assert.Equal(1, handler.Calls);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task SpendLimit429_NotRetriedBySdk_WithProductionRetries()
    {
        var handler = new FakeHttpHandler();
        handler.Responses.Enqueue((429, SpendCapBody));
        handler.Responses.Enqueue((200, AnthropicWireTests.SuccessBody));
        var provider = CreateGuardedProvider(handler, maxRetries: 2);

        var response = await provider.SendAsync(AnthropicWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(LlmFailureKind.SpendLimit, response.FailureKind);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PlainRateLimit_StillRetriedBySdk()
    {
        var handler = new FakeHttpHandler();
        handler.ResponseHeaders["retry-after"] = "0";
        handler.Responses.Enqueue((429, ErrorBody("rate_limit_error", "Number of requests has exceeded your rate limit")));
        handler.Responses.Enqueue((200, AnthropicWireTests.SuccessBody));
        var provider = CreateGuardedProvider(handler, maxRetries: 2);

        var response = await provider.SendAsync(AnthropicWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Succeeded, response.Outcome);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task NetworkFailure()
    {
        var handler = new FakeHttpHandler { ThrowOnSend = new HttpRequestException("down") };
        var provider = CreateGuardedProvider(handler, maxRetries: 0);

        var response = await provider.SendAsync(AnthropicWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(LlmFailureKind.Network, response.FailureKind);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var handler = new FakeHttpHandler { Body = AnthropicWireTests.SuccessBody };
        var provider = CreateGuardedProvider(handler, maxRetries: 0);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SendAsync(AnthropicWireTests.CreateRequest(), cts.Token));
    }

    [Fact]
    public async Task Message_NeverContainsKey()
    {
        const string key = "sk-ant-secret-123";
        foreach (var errorCase in ErrorCases())
        {
            var handler = new FakeHttpHandler { Status = (int)errorCase[0], Body = (string)errorCase[1] };
            var provider = CreateGuardedProvider(handler, maxRetries: 0, apiKey: key);

            var response = await provider.SendAsync(AnthropicWireTests.CreateRequest());
            Assert.DoesNotContain(key, response.Message ?? string.Empty);

            var keyTest = await provider.TestKeyAsync("claude-haiku-4-5-20251001");
            Assert.DoesNotContain(key, keyTest.Message);
        }

        var networkHandler = new FakeHttpHandler { ThrowOnSend = new HttpRequestException("down") };
        var networkProvider = CreateGuardedProvider(networkHandler, maxRetries: 0, apiKey: key);
        var networkResponse = await networkProvider.SendAsync(AnthropicWireTests.CreateRequest());
        Assert.DoesNotContain(key, networkResponse.Message ?? string.Empty);
    }
}
