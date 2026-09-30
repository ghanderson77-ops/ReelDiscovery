using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Tests;

public class WizardStateProviderTests
{
    [Fact]
    public void FreshState_DefaultsToOpenAI()
    {
        var state = new WizardState();

        Assert.Equal(LlmProviderKind.OpenAI, state.TextProvider);
        Assert.Equal("gpt-6.1-sol", state.StoryModelId);
        Assert.NotNull(state.StoryModelConfig);
        Assert.Equal("low", state.StoryModelConfig!.Effort);
        Assert.Equal("gpt-6-luna", state.VolumeModelId);
    }

    [Fact]
    public void SetTextProvider_Anthropic_ResetsTierDefaults()
    {
        var state = new WizardState();

        state.SetTextProvider(LlmProviderKind.Anthropic);

        Assert.Equal(LlmProviderKind.Anthropic, state.TextProvider);
        Assert.Equal("claude-opus-5-5", state.StoryModelId);
        Assert.Equal("claude-haiku-4-5-20251001", state.VolumeModelId);
        Assert.Equal(LlmProviderKind.Anthropic, state.StoryModelConfig!.Provider);
    }

    [Fact]
    public void ModelConfig_FilteredToProvider()
    {
        var state = new WizardState();
        state.SetTextProvider(LlmProviderKind.Anthropic);

        state.StoryModelId = "gpt-6.1-sol";

        Assert.Null(state.StoryModelConfig);
    }

    [Theory]
    [InlineData(LlmProviderKind.OpenAI, true, false, true)]
    [InlineData(LlmProviderKind.OpenAI, false, true, false)]
    [InlineData(LlmProviderKind.Anthropic, false, true, true)]
    [InlineData(LlmProviderKind.Anthropic, true, false, false)]
    public void CanProceedFromKeys_Matrix(LlmProviderKind provider, bool openAITested, bool anthropicTested, bool expected)
    {
        Assert.Equal(expected, WizardState.CanProceedFromKeys(provider, openAITested, anthropicTested));

        var state = new WizardState { OpenAIKeyTested = openAITested, AnthropicKeyTested = anthropicTested };
        state.SetTextProvider(provider);
        Assert.Equal(expected, state.CanProceedFromKeys());
    }

    [Fact]
    public void CreateGateway_UsesSelectedProvider()
    {
        var state = new WizardState { AnthropicApiKey = "k" };
        state.SetTextProvider(LlmProviderKind.Anthropic);

        var gateway = state.CreateGateway();

        Assert.Equal(LlmProviderKind.Anthropic, gateway.Provider);
        Assert.Equal("claude-opus-5-5", gateway.ResolveModel(LlmTier.Story).ModelId);
    }

    [Fact]
    public void CreateGateway_NoModel_Throws()
    {
        var state = new WizardState { AnthropicApiKey = "k" };
        state.SetTextProvider(LlmProviderKind.Anthropic);
        state.StoryModelId = "";

        var ex = Assert.Throws<InvalidOperationException>(() => state.CreateGateway());
        Assert.Equal("No Story model selected for Anthropic", ex.Message);
    }

    [Fact]
    public void ProviderSummary_Format()
    {
        var state = new WizardState();

        Assert.Equal("OpenAI: GPT-6.1 Sol / GPT-6 Luna", state.ProviderSummary);
    }

    [Fact]
    public void KeyTestModel_MediaOnlyOpenAI_UsesVolumeDefault()
    {
        var state = new WizardState();
        state.SetTextProvider(LlmProviderKind.Anthropic);
        Assert.Equal("gpt-6-luna", state.GetKeyTestModelId(LlmProviderKind.OpenAI));

        state.SetTextProvider(LlmProviderKind.OpenAI);
        Assert.Equal("gpt-6.1-sol", state.GetKeyTestModelId(LlmProviderKind.OpenAI));
        Assert.Equal("claude-haiku-4-5-20251001", state.GetKeyTestModelId(LlmProviderKind.Anthropic));
    }
}
