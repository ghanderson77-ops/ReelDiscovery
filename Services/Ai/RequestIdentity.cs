using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Deterministic provider batch custom_id for a request: "q" + 40 lowercase hex chars of the SHA-256 over the canonical
/// request. Always matches ^[a-zA-Z0-9_-]{1,64}$, which both providers accept.
/// </summary>
public static class RequestIdentity
{
    public static string CustomIdFor(LlmProviderKind kind, LlmProviderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Prompts are byte-deterministic given plan + journal (Phase 5 rule), so a Continue after a crash finds the same id and never pays twice (RUN-04, D-09)
        // AllowBatch is routing metadata, not request content, so it is not part of the identity; null and "" effort both encode as empty
        var canonical = string.Join('\u001F',
            kind.ToString(),
            request.ModelId ?? string.Empty,
            request.Effort ?? string.Empty,
            request.SchemaName ?? string.Empty,
            request.SchemaJson ?? string.Empty,
            request.SystemPrompt ?? string.Empty,
            request.UserPrompt ?? string.Empty,
            request.MaxTokens.ToString(CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "q" + Convert.ToHexString(hash)[..40].ToLowerInvariant();
    }
}
