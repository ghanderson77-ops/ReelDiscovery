namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Marks Anthropic spend-cap 429s (error_code enforced_spend_limit_reached) as non-retryable so the SDK's
/// built-in retry loop does not repeat a request that cannot succeed until the cap resets.
/// Ordinary rate-limit 429s pass through untouched and are still retried by the SDK.
/// </summary>
internal sealed class AnthropicSpendLimitHandler : DelegatingHandler
{
    internal const string SpendLimitErrorCode = "enforced_spend_limit_reached";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);

        if ((int)response.StatusCode != 429)
            return response;

        await response.Content.LoadIntoBufferAsync(ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!body.Contains(SpendLimitErrorCode, StringComparison.Ordinal))
            return response;

        // Stainless SDKs honour x-should-retry before status-based retry decisions
        response.Headers.Remove("x-should-retry");
        response.Headers.TryAddWithoutValidation("x-should-retry", "false");
        response.Headers.Remove("retry-after");
        response.Headers.Remove("retry-after-ms");

        return response;
    }
}
