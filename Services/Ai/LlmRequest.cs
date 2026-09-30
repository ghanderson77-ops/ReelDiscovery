namespace ReelDiscovery.Services.Ai;

public enum LlmTier
{
    Story,
    Volume
}

public class LlmRequest
{
    public LlmTier Tier { get; set; }

    public string OperationName { get; set; } = string.Empty;

    /// <summary>
    /// Stable prefix, must be byte-identical across calls for Phase 7 prompt caching.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Variable suffix (per-call content).
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Output token budget; includes thinking/reasoning tokens.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 16_000;
}
