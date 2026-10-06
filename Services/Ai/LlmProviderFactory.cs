namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Maps a provider kind and API key to the matching text provider or batch transport.
/// </summary>
public static class LlmProviderFactory
{
    public static ILlmProvider Create(LlmProviderKind kind, string apiKey) => kind switch
    {
        LlmProviderKind.OpenAI => new OpenAITextProvider(apiKey),
        LlmProviderKind.Anthropic => new AnthropicTextProvider(apiKey),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static IBatchTransport CreateBatchTransport(LlmProviderKind kind, string apiKey) => kind switch
    {
        LlmProviderKind.OpenAI => throw new NotSupportedException("OpenAI batch transport not yet available"),
        LlmProviderKind.Anthropic => new AnthropicBatchTransport(apiKey),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
