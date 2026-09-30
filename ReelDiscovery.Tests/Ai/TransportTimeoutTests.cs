using System.ClientModel.Primitives;
using OpenAI;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Ai.Anthropic;
using ReelDiscovery.Tests.Ai.OpenAI;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai;

/// <summary>
/// CR-01 regression: a transport timeout (TaskCanceledException while the caller token is NOT cancelled)
/// must be classified as a Network failure (or a skipped media item), never thrown as a cancel.
/// </summary>
public class TransportTimeoutTests
{
    private const string TimeoutMessage =
        "The request was canceled due to the configured HttpClient.Timeout of 600 seconds elapsing.";

    private static FakeHttpHandler TimeoutHandler() =>
        new() { ThrowOnSend = new TaskCanceledException(TimeoutMessage) };

    private static AnthropicTextProvider CreateGuardedProvider(FakeHttpHandler handler, int maxRetries, string apiKey = "test") =>
        new(new global::Anthropic.Core.ClientOptions
        {
            ApiKey = apiKey,
            HttpClient = AnthropicTextProvider.CreateHttpClient(handler),
            MaxRetries = maxRetries
        });

    private static OpenAIMediaClient CreateClient(FakeHttpHandler handler, string imageModel = "gpt-image-2.5-flare", string imageQuality = "low") =>
        new("sk-test", imageModel, imageQuality, new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(handler.CreateClient()),
            RetryPolicy = new ClientRetryPolicy(0)
        });

    [Fact]
    public async Task OpenAI_Send_Timeout_IsNetworkFailure()
    {
        var provider = OpenAIWireTests.CreateProvider(TimeoutHandler());

        var response = await provider.SendAsync(OpenAIWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(LlmFailureKind.Network, response.FailureKind);
        Assert.DoesNotContain("sk-test", response.Message ?? string.Empty);
    }

    [Fact]
    public async Task OpenAI_KeyTest_Timeout_IsNetworkFailure()
    {
        var provider = OpenAIWireTests.CreateProvider(TimeoutHandler());

        var result = await provider.TestKeyAsync("gpt-5-mini");

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.Network, result.FailureKind);
    }

    [Fact]
    public async Task OpenAI_Send_UserCancel_Throws()
    {
        var provider = OpenAIWireTests.CreateProvider(new FakeHttpHandler { Body = OpenAIWireTests.SuccessBody });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SendAsync(OpenAIWireTests.CreateRequest(), cts.Token));
    }

    [Fact]
    public async Task Anthropic_Send_Timeout_IsNetworkFailure()
    {
        var provider = CreateGuardedProvider(TimeoutHandler(), maxRetries: 0, apiKey: "test-key-secret");

        var response = await provider.SendAsync(AnthropicWireTests.CreateRequest());

        Assert.Equal(LlmOutcome.Failed, response.Outcome);
        Assert.Equal(LlmFailureKind.Network, response.FailureKind);
        Assert.DoesNotContain("test-key-secret", response.Message ?? string.Empty);
    }

    [Fact]
    public async Task Anthropic_KeyTest_Timeout_IsNetworkFailure()
    {
        var provider = CreateGuardedProvider(TimeoutHandler(), maxRetries: 0);

        var result = await provider.TestKeyAsync("claude-haiku-4-5-20251001");

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.Network, result.FailureKind);
    }

    [Fact]
    public async Task Anthropic_Send_UserCancel_Throws()
    {
        var provider = CreateGuardedProvider(new FakeHttpHandler { Body = AnthropicWireTests.SuccessBody }, maxRetries: 0);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SendAsync(AnthropicWireTests.CreateRequest(), cts.Token));
    }

    [Fact]
    public async Task Media_Image_Timeout_ReturnsNull()
    {
        var result = await CreateClient(TimeoutHandler()).GenerateImageAsync("A warehouse");

        Assert.Null(result);
    }

    [Fact]
    public async Task Media_Speech_Timeout_ReturnsNull()
    {
        var result = await CreateClient(TimeoutHandler()).GenerateSpeechAsync("Call me back.", "onyx");

        Assert.Null(result);
    }

    [Fact]
    public async Task Media_Image_UserCancel_Throws()
    {
        var client = CreateClient(new FakeHttpHandler());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GenerateImageAsync("A warehouse", ct: cts.Token));
    }
}
