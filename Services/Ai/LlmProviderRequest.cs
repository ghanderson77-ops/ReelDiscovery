namespace ReelDiscovery.Services.Ai;

public class LlmProviderRequest
{
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Reasoning effort; sent only when non-null.
    /// </summary>
    public string? Effort { get; set; }

    public string SchemaName { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public int MaxTokens { get; set; }

    /// <summary>True only on a first attempt the caller allows to wait for a provider batch (D-07); retries are always live (D-10).</summary>
    public bool AllowBatch { get; set; }
}

public class LlmProviderResponse
{
    /// <summary>
    /// Succeeded here means "stop reason OK"; the gateway still parses and validates the text.
    /// </summary>
    public LlmOutcome Outcome { get; set; }

    public LlmFailureKind FailureKind { get; set; }
    public string? Text { get; set; }
    public string? Message { get; set; }

    /// <summary>Uncached input only (cache writes and reads are reported separately).</summary>
    public int InputTokens { get; set; }

    public int CacheWriteTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>True when the call was answered from a provider batch (billed at batch prices).</summary>
    public bool IsBatch { get; set; }

    public TokenUsage ToUsage() => new()
    {
        InputTokens = InputTokens,
        CacheWriteTokens = CacheWriteTokens,
        CacheReadTokens = CacheReadTokens,
        OutputTokens = OutputTokens,
        IsBatch = IsBatch
    };
}
