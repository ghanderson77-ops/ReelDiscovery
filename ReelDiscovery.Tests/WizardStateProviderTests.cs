using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

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
    public class EchoDto
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void CreateRunGateway_Live_NoBatch_SameAsCreateGateway()
    {
        var state = new WizardState { AnthropicApiKey = "k" };
        state.SetTextProvider(LlmProviderKind.Anthropic);

        var (gateway, batch) = state.CreateRunGateway(useBatch: false, Path.GetTempPath(), Guid.NewGuid());

        Assert.Null(batch);
        Assert.Equal(LlmProviderKind.Anthropic, gateway.Provider);
        Assert.Equal(state.CreateGateway().ResolveModel(LlmTier.Story).ModelId, gateway.ResolveModel(LlmTier.Story).ModelId);
        Assert.Equal(state.CreateGateway().ResolveModel(LlmTier.Volume).ModelId, gateway.ResolveModel(LlmTier.Volume).ModelId);
    }

    [Fact]
    public void CreateRunGateway_NoModel_Throws()
    {
        var state = new WizardState { OpenAIApiKey = "k" };
        state.VolumeModelId = "";

        var ex = Assert.Throws<InvalidOperationException>(() => state.CreateRunGateway(true, Path.GetTempPath(), Guid.NewGuid(),
            new FakeBatchTransport()));
        Assert.Equal("No Volume model selected for OpenAI", ex.Message);
    }

    [Fact]
    public async Task CreateRunGateway_Batch_RoutesBatchableRequestsThroughTransport()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-wsp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var state = new WizardState { OpenAIApiKey = "k" };
            var transport = new FakeBatchTransport { Respond = _ => FakeLlmProvider.Ok("{\"name\":\"batched\"}", 100, 20) };
            var timings = new BatchTimings
            {
                QuietPeriod = TimeSpan.FromMilliseconds(20),
                PollInterval = TimeSpan.FromMilliseconds(10),
                SlowPollInterval = TimeSpan.FromMilliseconds(10)
            };

            var (gateway, batch) = state.CreateRunGateway(useBatch: true, folder, Guid.NewGuid(), transport, timings);

            Assert.NotNull(batch);
            Assert.Equal(LlmProviderKind.OpenAI, gateway.Provider);
            var result = await gateway.GenerateAsync<EchoDto>(new LlmRequest
            {
                Tier = LlmTier.Volume,
                OperationName = "Test",
                SystemPrompt = "system",
                UserPrompt = "user",
                MaxOutputTokens = 1000,
                AllowBatch = true
            }).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal("batched", result.Value?.Name);
            Assert.Single(transport.Submissions);
            Assert.True(File.Exists(BatchLedger.PathFor(folder)));
            batch!.Detach();
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temp folder cleanup only
            }
        }
    }
}
