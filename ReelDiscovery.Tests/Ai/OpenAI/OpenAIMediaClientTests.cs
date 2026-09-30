using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using OpenAI;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai.OpenAI;

public class OpenAIMediaClientTests
{
    private const string ImageBody = "{\"created\":1,\"data\":[{\"b64_json\":\"iVBORw0KGgo=\"}]}";

    private static OpenAIMediaClient CreateClient(FakeHttpHandler handler, string imageModel = "gpt-image-2.5-flare", string imageQuality = "low") =>
        new("sk-test", imageModel, imageQuality, new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(handler.CreateClient()),
            RetryPolicy = new ClientRetryPolicy(0)
        });

    [Fact]
    public async Task Image_WireUsesGptImage()
    {
        var handler = new FakeHttpHandler { Body = ImageBody };

        await CreateClient(handler).GenerateImageAsync("A construction site at dusk");

        var body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        Assert.Equal("gpt-image-2.5-flare", body["model"]!.GetValue<string>());
        Assert.Equal("low", body["quality"]!.GetValue<string>());
        Assert.Equal("1024x1024", body["size"]!.GetValue<string>());
        Assert.Equal("png", body["output_format"]!.GetValue<string>());
        Assert.False(body.ContainsKey("response_format"));
        Assert.False(body.ContainsKey("style"));
        Assert.EndsWith("/images/generations", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Image_ReturnsBytes()
    {
        var handler = new FakeHttpHandler { Body = ImageBody };

        var bytes = await CreateClient(handler).GenerateImageAsync("A construction site at dusk");

        Assert.Equal(Convert.FromBase64String("iVBORw0KGgo="), bytes);
    }

    [Fact]
    public async Task Image_ContentPolicy400_ReturnsNull()
    {
        var handler = new FakeHttpHandler
        {
            Status = 400,
            Body = OpenAIOutcomeClassificationTests.ErrorBody("image_generation_user_error", "moderation_blocked", "Your request was rejected by the safety system.")
        };

        var bytes = await CreateClient(handler).GenerateImageAsync("Something disallowed");

        Assert.Null(bytes);
    }

    [Fact]
    public async Task Image_CustomModelAndQuality()
    {
        var handler = new FakeHttpHandler { Body = ImageBody };

        await CreateClient(handler, "gpt-image-2", "medium").GenerateImageAsync("A site office");

        var body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        Assert.Equal("gpt-image-2", body["model"]!.GetValue<string>());
        Assert.Equal("medium", body["quality"]!.GetValue<string>());
    }

    [Fact]
    public async Task Speech_UsesTts1AndVoice()
    {
        var mp3 = new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00 };
        var handler = new FakeHttpHandler { BinaryBody = mp3, ContentType = "audio/mpeg" };
        var client = CreateClient(handler);

        var bytes = await client.GenerateSpeechAsync("Call me back about the window order.", "onyx");

        var body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        Assert.Equal("tts-1", body["model"]!.GetValue<string>());
        Assert.Equal("onyx", body["voice"]!.GetValue<string>());
        Assert.Equal(mp3, bytes);

        await client.GenerateSpeechAsync("Hello.", "not-a-voice");

        var fallback = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        Assert.Equal("alloy", fallback["voice"]!.GetValue<string>());
    }
}
