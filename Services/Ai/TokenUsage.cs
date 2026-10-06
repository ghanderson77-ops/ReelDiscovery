namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Normalized token usage of one billed call (or a sum of calls), split by pricing category.
/// </summary>
public class TokenUsage
{
    // Uncached input only (Anthropic input_tokens; OpenAI prompt_tokens - cached - cache_write)
    public int InputTokens { get; set; }
    public int CacheWriteTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int OutputTokens { get; set; }

    // Billed at batch prices (set by the batch path only)
    public bool IsBatch { get; set; }

    /// <summary>All input tokens billed: uncached + cache write + cache read.</summary>
    public int TotalInputTokens => InputTokens + CacheWriteTokens + CacheReadTokens;

    /// <summary>Sums the four token counts; IsBatch is left unchanged.</summary>
    public void Add(TokenUsage? other)
    {
        if (other == null)
            return;

        InputTokens += other.InputTokens;
        CacheWriteTokens += other.CacheWriteTokens;
        CacheReadTokens += other.CacheReadTokens;
        OutputTokens += other.OutputTokens;
    }

    public TokenUsage Clone() => new()
    {
        InputTokens = InputTokens,
        CacheWriteTokens = CacheWriteTokens,
        CacheReadTokens = CacheReadTokens,
        OutputTokens = OutputTokens,
        IsBatch = IsBatch
    };

    /// <summary>Clamps negative provider counts to 0 (T-07-01).</summary>
    public void Normalize()
    {
        InputTokens = Math.Max(0, InputTokens);
        CacheWriteTokens = Math.Max(0, CacheWriteTokens);
        CacheReadTokens = Math.Max(0, CacheReadTokens);
        OutputTokens = Math.Max(0, OutputTokens);
    }
}

/// <summary>
/// Dollar cost of a call split by category (D-16).
/// </summary>
public class CostBreakdown
{
    // Pre-discount dollars per category; BatchSavingsUsd is what the batch discount took off
    public decimal InputUsd { get; set; }
    public decimal CacheWriteUsd { get; set; }
    public decimal CacheReadUsd { get; set; }
    public decimal OutputUsd { get; set; }
    public decimal BatchSavingsUsd { get; set; }

    public decimal TotalUsd => InputUsd + CacheWriteUsd + CacheReadUsd + OutputUsd - BatchSavingsUsd;

    public void Add(CostBreakdown? other)
    {
        if (other == null)
            return;

        InputUsd += other.InputUsd;
        CacheWriteUsd += other.CacheWriteUsd;
        CacheReadUsd += other.CacheReadUsd;
        OutputUsd += other.OutputUsd;
        BatchSavingsUsd += other.BatchSavingsUsd;
    }

    public CostBreakdown Clone() => new()
    {
        InputUsd = InputUsd,
        CacheWriteUsd = CacheWriteUsd,
        CacheReadUsd = CacheReadUsd,
        OutputUsd = OutputUsd,
        BatchSavingsUsd = BatchSavingsUsd
    };
}
