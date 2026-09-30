using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Models;

public class AIModelConfig
{
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal InputTokenPricePerMillion { get; set; }
    public decimal OutputTokenPricePerMillion { get; set; }
    public bool IsDefault { get; set; }

    public LlmProviderKind Provider { get; set; }

    /// <summary>
    /// Tier this model is the catalog default for (per provider), or null when it is an alternative.
    /// </summary>
    public LlmTier? DefaultForTier { get; set; }

    /// <summary>
    /// Sent as reasoning_effort (OpenAI) / output_config.effort (Anthropic) only when non-null;
    /// Haiku 4.5 must stay null.
    /// </summary>
    public string? Effort { get; set; }

    /// <summary>
    /// Model output-token ceiling; caps truncation retries.
    /// </summary>
    public int MaxOutputTokens { get; set; }

    public decimal CalculateInputCost(int tokens) =>
        tokens * InputTokenPricePerMillion / 1_000_000m;

    public decimal CalculateOutputCost(int tokens) =>
        tokens * OutputTokenPricePerMillion / 1_000_000m;

    public decimal CalculateTotalCost(int inputTokens, int outputTokens) =>
        CalculateInputCost(inputTokens) + CalculateOutputCost(outputTokens);

    public override string ToString() => DisplayName;

    public static AIModelConfig? GetTierDefault(IEnumerable<AIModelConfig> catalog, LlmProviderKind provider, LlmTier tier) =>
        catalog.FirstOrDefault(m => m.Provider == provider && m.DefaultForTier == tier);

    // Model catalog with verified prices (USD per million tokens)
    public static List<AIModelConfig> GetDefaultModels() => new()
    {
        new AIModelConfig
        {
            ModelId = "claude-opus-5-5",
            DisplayName = "Claude Opus 5.5",
            Provider = LlmProviderKind.Anthropic,
            InputTokenPricePerMillion = 4.00m,
            OutputTokenPricePerMillion = 20.00m,
            DefaultForTier = LlmTier.Story,
            Effort = null,
            MaxOutputTokens = 128_000
        },
        new AIModelConfig
        {
            ModelId = "claude-sonnet-5-5",
            DisplayName = "Claude Sonnet 5.5",
            Provider = LlmProviderKind.Anthropic,
            InputTokenPricePerMillion = 2.00m,
            OutputTokenPricePerMillion = 10.00m,
            DefaultForTier = null,
            Effort = null,
            MaxOutputTokens = 64_000
        },
        new AIModelConfig
        {
            ModelId = "claude-haiku-4-5-20251001",
            DisplayName = "Claude Haiku 4.5",
            Provider = LlmProviderKind.Anthropic,
            InputTokenPricePerMillion = 1.00m,
            OutputTokenPricePerMillion = 5.00m,
            DefaultForTier = LlmTier.Volume,
            Effort = null,
            MaxOutputTokens = 64_000
        },
        new AIModelConfig
        {
            ModelId = "gpt-6-sol",
            DisplayName = "GPT-6 Sol",
            Provider = LlmProviderKind.OpenAI,
            InputTokenPricePerMillion = 2.00m,
            OutputTokenPricePerMillion = 10.00m,
            DefaultForTier = null,
            Effort = "none",
            MaxOutputTokens = 128_000
        },
        new AIModelConfig
        {
            ModelId = "gpt-6.1-sol",
            DisplayName = "GPT-6.1 Sol",
            Provider = LlmProviderKind.OpenAI,
            InputTokenPricePerMillion = 2.00m,
            OutputTokenPricePerMillion = 10.00m,
            DefaultForTier = LlmTier.Story,
            Effort = "low", // D-15
            MaxOutputTokens = 128_000
        },
        new AIModelConfig
        {
            ModelId = "gpt-6-luna",
            DisplayName = "GPT-6 Luna",
            Provider = LlmProviderKind.OpenAI,
            InputTokenPricePerMillion = 0.10m,
            OutputTokenPricePerMillion = 0.50m,
            DefaultForTier = LlmTier.Volume,
            Effort = "none",
            MaxOutputTokens = 128_000,
            IsDefault = true // Legacy keys screen preselection until plan 01-07
        }
    };
}
