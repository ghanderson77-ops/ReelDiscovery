using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai.OpenAI;

public class OpenAIKeyTestTests
{
    private const string ModelId = "gpt-6-luna";

    private const string ModelBody = "{\"id\":\"gpt-6-luna\",\"object\":\"model\",\"created\":1,\"owned_by\":\"openai\"}";

    [Fact]
    public async Task ValidKey()
    {
        var handler = new FakeHttpHandler { Body = ModelBody };

        var result = await OpenAIWireTests.CreateProvider(handler).TestKeyAsync(ModelId);

        Assert.True(result.Succeeded);
        Assert.Equal(LlmFailureKind.None, result.FailureKind);
        Assert.Equal("OpenAI key OK (gpt-6-luna)", result.Message);
        Assert.EndsWith("/models/gpt-6-luna", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.LastRequestMethod);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task BadKey()
    {
        var handler = new FakeHttpHandler
        {
            Status = 401,
            Body = OpenAIOutcomeClassificationTests.ErrorBody("invalid_request_error", "invalid_api_key", "Incorrect API key provided.")
        };

        var result = await OpenAIWireTests.CreateProvider(handler).TestKeyAsync(ModelId);

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
            Body = OpenAIOutcomeClassificationTests.ErrorBody("invalid_request_error", "model_not_found", "The model does not exist.")
        };

        var result = await OpenAIWireTests.CreateProvider(handler).TestKeyAsync(ModelId);

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.BadRequest, result.FailureKind);
        Assert.Equal("Model 'gpt-6-luna' is not available to this key", result.Message);
    }

    [Fact]
    public async Task Network()
    {
        var handler = new FakeHttpHandler { ThrowOnSend = new HttpRequestException("down") };

        var result = await OpenAIWireTests.CreateProvider(handler).TestKeyAsync(ModelId);

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.Network, result.FailureKind);
        Assert.Equal("Could not reach api.openai.com", result.Message);
    }
}
