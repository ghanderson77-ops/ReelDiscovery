using ReelDiscovery.Models;

namespace ReelDiscovery.Services.Ai;

public interface ILlmGateway
{
    LlmProviderKind Provider { get; }

    AIModelConfig ResolveModel(LlmTier tier);

    Task<LlmResult<T>> GenerateAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class;
}
