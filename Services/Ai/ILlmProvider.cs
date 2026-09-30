namespace ReelDiscovery.Services.Ai;

public interface ILlmProvider
{
    LlmProviderKind Kind { get; }

    /// <summary>
    /// Effective per-request max_tokens ceiling imposed by the provider transport
    /// (Anthropic non-streaming: 21_333); null = no provider limit beyond the model catalog ceiling.
    /// </summary>
    int? MaxTokensCeiling { get; }

    /// <summary>
    /// Sends one structured-output request. Never throws for API/HTTP failures: they are mapped to
    /// Outcome = Failed with a FailureKind. Rethrows OperationCanceledException.
    /// </summary>
    Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Free key check (model retrieve). Never throws except OperationCanceledException.
    /// </summary>
    Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default);
}
