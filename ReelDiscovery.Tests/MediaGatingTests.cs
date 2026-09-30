using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Tests;

public class MediaGatingTests
{
    [Theory]
    [InlineData(LlmProviderKind.Anthropic, "", false, false)]
    [InlineData(LlmProviderKind.Anthropic, "sk-test", false, false)]
    [InlineData(LlmProviderKind.Anthropic, "sk-test", true, true)]
    [InlineData(LlmProviderKind.OpenAI, "sk-test", true, true)]
    public void IsMediaAvailable_Matrix(LlmProviderKind provider, string openAIKey, bool openAITested, bool expected)
    {
        var state = new WizardState { OpenAIApiKey = openAIKey, OpenAIKeyTested = openAITested };
        state.SetTextProvider(provider);

        Assert.Equal(expected, state.IsMediaAvailable);
    }

    [Fact]
    public void CreateMediaClient_NullWhenUnavailable()
    {
        var state = new WizardState { AnthropicApiKey = "k", AnthropicKeyTested = true };
        state.SetTextProvider(LlmProviderKind.Anthropic);

        Assert.Null(state.CreateMediaClient());
        Assert.Equal(
            "Images and voicemail need a tested OpenAI key. Add one on the API Keys step to enable them.",
            state.MediaUnavailableNotice);
    }

    [Fact]
    public void CreateMediaClient_NonNullWhenAvailable()
    {
        var state = new WizardState { OpenAIApiKey = "sk-test", OpenAIKeyTested = true };
        state.SetTextProvider(LlmProviderKind.Anthropic);

        Assert.NotNull(state.CreateMediaClient());
    }
}
