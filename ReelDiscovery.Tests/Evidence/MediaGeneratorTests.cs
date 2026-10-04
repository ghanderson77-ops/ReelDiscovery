using System.ClientModel.Primitives;
using System.Text.Json;
using OpenAI;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Ai.OpenAI;
using ReelDiscovery.Tests.Fakes;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class MediaGeneratorTests
{
    private const string ImageBody = "{\"created\":1,\"data\":[{\"b64_json\":\"iVBORw0KGgo=\"}]}";
    private static readonly byte[] Mp3 = { 0x49, 0x44, 0x33, 0x04, 0x00 };

    private static OpenAIMediaGenerator CreateGenerator(FakeHttpHandler handler, TokenUsageTracker? tracker = null, MediaPrices? prices = null)
    {
        var client = new OpenAIMediaClient("sk-test", "gpt-image-2.5-flare", "low", new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(handler.CreateClient()),
            RetryPolicy = new ClientRetryPolicy(0)
        });
        return new OpenAIMediaGenerator(client, prices ?? MediaPrices.Defaults(), tracker);
    }

    private static FakeHttpHandler ErrorHandler(int status, string body) => new() { Status = status, Body = body };

    [Fact]
    public async Task Image_Ok_PricedAndTracked()
    {
        var tracker = new TokenUsageTracker();
        var prices = MediaPrices.Defaults();
        var generator = CreateGenerator(new FakeHttpHandler { Body = ImageBody }, tracker, prices);

        var result = await generator.GenerateImageAsync("A site office", "site-photo");

        Assert.Equal(MediaResultKind.Ok, result.Kind);
        Assert.NotNull(result.Bytes);
        Assert.NotEmpty(result.Bytes!);
        Assert.Equal(prices.ImagePerUnit, result.CostUsd);
        Assert.Equal(prices.ImagePerUnit, tracker.TotalCost);
        var entry = Assert.Single(tracker.Entries);
        Assert.Equal("Image - site-photo", entry.Operation);
        Assert.Equal(0, entry.InputTokens);
        Assert.Equal(0, entry.OutputTokens);
        Assert.Equal("media", entry.ModelId);
        Assert.Equal("OpenAI", entry.Provider);
    }

    [Fact]
    public async Task Speech_Ok_PricedPerMillionCharacters()
    {
        var tracker = new TokenUsageTracker();
        var prices = new MediaPrices { TtsPerMillionChars = 15m };
        var generator = CreateGenerator(new FakeHttpHandler { BinaryBody = Mp3, ContentType = "audio/mpeg" }, tracker, prices);

        var result = await generator.GenerateSpeechAsync(new string('a', 400), "onyx", "vm-1");

        Assert.Equal(MediaResultKind.Ok, result.Kind);
        Assert.Equal(Mp3, result.Bytes);
        Assert.Equal(0.006m, result.CostUsd);
        Assert.Equal(0.006m, tracker.TotalCost);
        Assert.Equal("Voicemail - vm-1", Assert.Single(tracker.Entries).Operation);
    }

    [Fact]
    public async Task Image_400_FailedRefused()
    {
        var tracker = new TokenUsageTracker();
        var handler = ErrorHandler(400, OpenAIOutcomeClassificationTests.ErrorBody("image_generation_user_error", "moderation_blocked", "Rejected."));

        var result = await CreateGenerator(handler, tracker).GenerateImageAsync("x", "img");

        Assert.Equal(MediaResultKind.Failed, result.Kind);
        Assert.Equal("the image request was refused", result.Reason);
        Assert.Equal(0m, result.CostUsd);
        Assert.Equal(0m, tracker.TotalCost);
        Assert.Empty(tracker.Entries);
    }

    [Fact]
    public async Task Speech_400_FailedNoUsableContent()
    {
        var handler = ErrorHandler(400, OpenAIOutcomeClassificationTests.ErrorBody("invalid_request_error", "invalid_value", "Bad."));

        var result = await CreateGenerator(handler).GenerateSpeechAsync("hello", "alloy", "vm");

        Assert.Equal(MediaResultKind.Failed, result.Kind);
        Assert.Equal("the model returned no usable content", result.Reason);
        Assert.Equal(0m, result.CostUsd);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task AuthErrors_Stopped(int status)
    {
        var body = OpenAIOutcomeClassificationTests.ErrorBody("invalid_request_error", "invalid_api_key", "Incorrect API key provided.");

        var image = await CreateGenerator(ErrorHandler(status, body)).GenerateImageAsync("x", "img");
        var speech = await CreateGenerator(ErrorHandler(status, body)).GenerateSpeechAsync("x", "alloy", "vm");

        Assert.Equal(MediaResultKind.Stopped, image.Kind);
        Assert.Equal("the OpenAI key was rejected", image.Reason);
        Assert.Equal(MediaResultKind.Stopped, speech.Kind);
        Assert.Equal("the OpenAI key was rejected", speech.Reason);
    }

    [Theory]
    [InlineData("insufficient_quota")]
    [InlineData("billing_hard_limit_reached")]
    public async Task Quota429_Stopped(string code)
    {
        var body = OpenAIOutcomeClassificationTests.ErrorBody("requests", code, "Limit reached.");

        var result = await CreateGenerator(ErrorHandler(429, body)).GenerateImageAsync("x", "img");

        Assert.Equal(MediaResultKind.Stopped, result.Kind);
        Assert.Equal("the OpenAI quota is used up", result.Reason);
    }

    [Fact]
    public async Task Quota429_ByMessage_Stopped()
    {
        var body = OpenAIOutcomeClassificationTests.ErrorBody("requests", "other", "Please check your plan and billing details.");

        var result = await CreateGenerator(ErrorHandler(429, body)).GenerateSpeechAsync("x", "alloy", "vm");

        Assert.Equal(MediaResultKind.Stopped, result.Kind);
        Assert.Equal("the OpenAI quota is used up", result.Reason);
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"Rate limit reached for requests.\",\"type\":\"requests\",\"param\":null,\"code\":\"rate_limit_exceeded\"}}")]
    [InlineData("")]
    public async Task RateLimit429_FailedRetryable(string body)
    {
        var result = await CreateGenerator(ErrorHandler(429, body)).GenerateImageAsync("x", "img");

        Assert.Equal(MediaResultKind.Failed, result.Kind);
        Assert.Equal("the OpenAI media service is busy (rate limited)", result.Reason);
    }

    [Fact]
    public async Task Server500_FailedWithStatus()
    {
        var body = OpenAIOutcomeClassificationTests.ErrorBody("server_error", "server_error", "The server had an error.");

        var result = await CreateGenerator(ErrorHandler(500, body)).GenerateImageAsync("x", "img");

        Assert.Equal(MediaResultKind.Failed, result.Kind);
        Assert.StartsWith("the OpenAI media request failed", result.Reason);
        Assert.Contains("500", result.Reason);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"created\":1,\"data\":[]}")]
    [InlineData("{\"created\":1,\"data\":[{\"b64_json\":\"\"}]}")]
    public async Task Image_MalformedOrEmpty200_Failed(string body)
    {
        var tracker = new TokenUsageTracker();

        var result = await CreateGenerator(new FakeHttpHandler { Body = body }, tracker).GenerateImageAsync("x", "img");

        Assert.Equal(MediaResultKind.Failed, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(0m, tracker.TotalCost);
    }

    [Fact]
    public async Task Speech_Empty200_Failed()
    {
        var result = await CreateGenerator(new FakeHttpHandler { BinaryBody = Array.Empty<byte>(), ContentType = "audio/mpeg" })
            .GenerateSpeechAsync("x", "alloy", "vm");

        Assert.Equal(MediaResultKind.Failed, result.Kind);
        Assert.Equal("the model returned no usable content", result.Reason);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(HttpRequestException))]
    public async Task TransportOrSdkException_Failed(Type exceptionType)
    {
        var handler = new FakeHttpHandler { ThrowOnSend = (Exception)Activator.CreateInstance(exceptionType, "boom")! };

        var result = await CreateGenerator(handler).GenerateImageAsync("x", "img");

        Assert.Equal(MediaResultKind.Failed, result.Kind);
        Assert.StartsWith("the OpenAI media request failed", result.Reason);
    }

    [Fact]
    public async Task UserCancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var generator = CreateGenerator(new FakeHttpHandler { Body = ImageBody });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateImageAsync("x", "img", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateSpeechAsync("x", "alloy", "vm", cts.Token));
    }

    [Fact]
    public void CreateMediaGenerator_NullWhenUnavailable()
    {
        var state = new WizardState { OpenAIApiKey = "sk-test", OpenAIKeyTested = false };
        Assert.Null(state.CreateMediaGenerator());

        state = new WizardState { OpenAIApiKey = "", OpenAIKeyTested = true };
        Assert.Null(state.CreateMediaGenerator());

        state = new WizardState { OpenAIApiKey = "sk-test", OpenAIKeyTested = true };
        Assert.IsType<OpenAIMediaGenerator>(state.CreateMediaGenerator());
    }

    [Fact]
    public void MediaPrices_DefaultsAndNormalize()
    {
        var defaults = MediaPrices.Defaults();
        Assert.Equal(0.006m, defaults.ImagePerUnit);
        Assert.Equal(15.00m, defaults.TtsPerMillionChars);
        Assert.Equal(0.006m, defaults.ImageCost());
        Assert.Equal(0m, defaults.SpeechCost(-5));

        var high = new MediaPrices { ImagePerUnit = 50m, TtsPerMillionChars = 5000m };
        high.Normalize();
        Assert.Equal(10m, high.ImagePerUnit);
        Assert.Equal(1000m, high.TtsPerMillionChars);

        var low = new MediaPrices { ImagePerUnit = -1m, TtsPerMillionChars = -1m };
        low.Normalize();
        Assert.Equal(0m, low.ImagePerUnit);
        Assert.Equal(0m, low.TtsPerMillionChars);

        var clone = defaults.Clone();
        clone.ImagePerUnit = 1m;
        Assert.Equal(0.006m, defaults.ImagePerUnit);
    }
}
