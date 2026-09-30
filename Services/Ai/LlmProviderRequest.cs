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
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}
