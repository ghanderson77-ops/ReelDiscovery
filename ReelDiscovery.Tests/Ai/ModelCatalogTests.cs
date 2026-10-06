using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Tests.Ai;

public class ModelCatalogTests
{
    private readonly List<AIModelConfig> _catalog = AIModelConfig.GetDefaultModels();

    [Theory]
    [InlineData(LlmProviderKind.Anthropic, LlmTier.Story, "claude-opus-5-5")]
    [InlineData(LlmProviderKind.Anthropic, LlmTier.Volume, "claude-haiku-4-5-20251001")]
    [InlineData(LlmProviderKind.OpenAI, LlmTier.Story, "gpt-6.1-sol")]
    [InlineData(LlmProviderKind.OpenAI, LlmTier.Volume, "gpt-6-luna")]
    public void TierDefaults_ResolveToExpectedModels(LlmProviderKind provider, LlmTier tier, string expectedModelId)
    {
        var model = AIModelConfig.GetTierDefault(_catalog, provider, tier);

        Assert.NotNull(model);
        Assert.Equal(expectedModelId, model!.ModelId);
        Assert.Equal(provider, model.Provider);
    }

    [Fact]
    public void OpenAIStoryDefault_UsesLowEffort()
    {
        var model = AIModelConfig.GetTierDefault(_catalog, LlmProviderKind.OpenAI, LlmTier.Story);

        Assert.Equal("low", model!.Effort); // D-15
    }

    [Fact]
    public void Gpt6Sol_IsPresentButNotTierDefault()
    {
        var model = _catalog.SingleOrDefault(m => m.ModelId == "gpt-6-sol");

        Assert.NotNull(model);
        Assert.Null(model!.DefaultForTier);
        Assert.Equal(LlmProviderKind.OpenAI, model.Provider);
    }

    [Fact]
    public void AllEntries_HaveCeilingAndPositivePrices()
    {
        Assert.All(_catalog, m =>
        {
            Assert.True(m.MaxOutputTokens > 0, $"{m.ModelId} MaxOutputTokens");
            Assert.True(m.InputTokenPricePerMillion > 0, $"{m.ModelId} input price");
            Assert.True(m.OutputTokenPricePerMillion > 0, $"{m.ModelId} output price");
        });
    }

    [Fact]
    public void Haiku_HasNoEffort()
    {
        var haiku = _catalog.Single(m => m.ModelId == "claude-haiku-4-5-20251001");

        Assert.Equal(LlmProviderKind.Anthropic, haiku.Provider);
        Assert.Null(haiku.Effort);
    }

    [Fact]
    public void LegacyModels_AreRemoved()
    {
        Assert.DoesNotContain(_catalog, m =>
            m.ModelId.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase) ||
            m.ModelId.StartsWith("gpt-3.5", StringComparison.OrdinalIgnoreCase) ||
            m.ModelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EachProviderTierPair_HasExactlyOneDefault()
    {
        foreach (var provider in Enum.GetValues<LlmProviderKind>())
        {
            foreach (var tier in Enum.GetValues<LlmTier>())
            {
                var count = _catalog.Count(m => m.Provider == provider && m.DefaultForTier == tier);
                Assert.True(count == 1, $"{provider}/{tier} has {count} defaults");
            }
        }
    }

    // Verified 2026-10-06 (07-RESEARCH Pattern 4)
    [Theory]
    [InlineData("claude-opus-5-5", "5.00", "0.20", "50", 512)]
    [InlineData("claude-sonnet-5-5", "2.50", "0.20", "50", 512)]
    [InlineData("claude-haiku-4-5-20251001", "1.25", "0.10", "50", 4096)]
    [InlineData("gpt-6.1-sol", "2.50", "0.10", "50", 1024)]
    [InlineData("gpt-6-sol", "2.50", "0.20", "50", 1024)]
    [InlineData("gpt-6-luna", "0.125", "0.01", "50", 1024)]
    public void CacheAndBatchPrices_MatchVerifiedTable(string modelId, string cacheWrite, string cacheRead, string batchPercent, int minPrefix)
    {
        var model = _catalog.Single(m => m.ModelId == modelId);

        Assert.Equal(decimal.Parse(cacheWrite, System.Globalization.CultureInfo.InvariantCulture), model.CacheWriteTokenPricePerMillion);
        Assert.Equal(decimal.Parse(cacheRead, System.Globalization.CultureInfo.InvariantCulture), model.CacheReadTokenPricePerMillion);
        Assert.Equal(decimal.Parse(batchPercent, System.Globalization.CultureInfo.InvariantCulture), model.BatchDiscountPercent);
        Assert.Equal(minPrefix, model.MinCacheablePrefixTokens);
    }

    [Fact]
    public void AllEntries_HaveCacheAndBatchDefaults()
    {
        Assert.All(_catalog, m =>
        {
            Assert.True(m.CacheWriteTokenPricePerMillion > 0, $"{m.ModelId} cache write price");
            Assert.True(m.CacheReadTokenPricePerMillion > 0, $"{m.ModelId} cache read price");
            Assert.InRange(m.BatchDiscountPercent, 0m, 100m);
            Assert.True(m.MinCacheablePrefixTokens > 0, $"{m.ModelId} min cacheable prefix");
        });
    }

    [Fact]
    public void RecordUsage_StoresProvider()
    {
        var tracker = new TokenUsageTracker();
        var opus = _catalog.Single(m => m.ModelId == "claude-opus-5-5");

        tracker.RecordUsage("Test", opus, 1000, 500);

        var entry = Assert.Single(tracker.Entries);
        Assert.Equal("Anthropic", entry.Provider);
        Assert.Equal("claude-opus-5-5", entry.ModelId);
    }
}
