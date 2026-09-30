using System.ClientModel.Primitives;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// SDK transport retry policy that behaves like <see cref="ClientRetryPolicy"/> except that a
/// 429 insufficient_quota response is never retried.
/// </summary>
public class QuotaAwareRetryPolicy : ClientRetryPolicy
{
    public QuotaAwareRetryPolicy(int maxRetries = 3)
        : base(maxRetries)
    {
    }

    protected override bool ShouldRetry(PipelineMessage message, Exception? exception)
    {
        // Quota exhaustion is not transient; retrying only burns time (denial-of-wallet guard, PROV-04)
        if (IsInsufficientQuota(message))
            return false;

        return base.ShouldRetry(message, exception);
    }

    protected override ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception)
    {
        // Quota exhaustion is not transient; retrying only burns time (denial-of-wallet guard, PROV-04)
        if (IsInsufficientQuota(message))
            return ValueTask.FromResult(false);

        return base.ShouldRetryAsync(message, exception);
    }

    internal static bool IsInsufficientQuota(PipelineMessage message)
    {
        var response = message.Response;
        if (response == null || response.Status != 429)
            return false;

        // BufferContent returns the already-buffered content, or reads the stream once so the caller can still read it
        var body = response.BufferContent().ToString();
        return body.Contains("insufficient_quota", StringComparison.Ordinal);
    }
}
