using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai.Anthropic;

public class AnthropicKeyTestTests
{
    private const string ModelId = "claude-haiku-4-5-20251001";

    private const string ModelBody =
        "{\"type\":\"model\",\"id\":\"claude-haiku-4-5-20251001\",\"display_name\":\"Claude Haiku 4.5\",\"created_at\":\"2025-10-01T00:00:00Z\"}";

    private static AnthropicTextProvider CreateProvider(FakeHttpHandler handler) =>
        new(new global::Anthropic.Core.ClientOptions
        {
            ApiKey = "test",
            HttpClient = AnthropicTextProvider.CreateHttpClient(handler),
            MaxRetries = 0
        });

    [Fact]
    public async Task ValidKey()
    {
        var handler = new FakeHttpHandler { Body = ModelBody };

        var result = await CreateProvider(handler).TestKeyAsync(ModelId);

        Assert.True(result.Succeeded);
        Assert.Equal(LlmFailureKind.None, result.FailureKind);
        Assert.Equal("Anthropic key OK (claude-haiku-4-5-20251001)", result.Message);
        Assert.EndsWith("/v1/models/claude-haiku-4-5-20251001", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task BadKey()
    {
        var handler = new FakeHttpHandler
        {
            Status = 401,
            Body = AnthropicOutcomeClassificationTests.ErrorBody("authentication_error", "invalid x-api-key")
        };

        var result = await CreateProvider(handler).TestKeyAsync(ModelId);

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.Auth, result.FailureKind);
        Assert.Equal("Key rejected (authentication failed)", result.Message);
    }

    [Fact]
    public async Task ModelUnavailable()
    {
        var handler = new FakeHttpHandler
        {
            Status = 404,
            Body = AnthropicOutcomeClassificationTests.ErrorBody("not_found_error", "model: X")
        };

        var result = await CreateProvider(handler).TestKeyAsync("X");

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.BadRequest, result.FailureKind);
        Assert.Equal("Model 'X' is not available to this key", result.Message);
    }

    [Fact]
    public async Task Network()
    {
        var handler = new FakeHttpHandler { ThrowOnSend = new HttpRequestException("down") };

        var result = await CreateProvider(handler).TestKeyAsync(ModelId);

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.Network, result.FailureKind);
        Assert.Equal("Could not reach api.anthropic.com", result.Message);
    }

    [Fact]
    public async Task NoTokensSpent()
    {
        var handler = new FakeHttpHandler { Body = ModelBody };

        await CreateProvider(handler).TestKeyAsync(ModelId);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Get, handler.LastRequestMethod);
        Assert.DoesNotContain("/v1/messages", handler.LastRequestUri!.AbsolutePath);
    }
}
