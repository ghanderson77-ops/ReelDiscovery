using System.ClientModel;
using System.Text.Json;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Classifies, prices and tracks OpenAI image and speech calls. Every failure becomes a <see cref="MediaResult"/>;
/// only user cancellation propagates (RUN-02: media never aborts the run).
/// </summary>
public class OpenAIMediaGenerator : IMediaGenerator
{
    private const string KeyRejected = "the OpenAI key was rejected";
    private const string QuotaUsedUp = "the OpenAI quota is used up";
    private const string RateLimited = "the OpenAI media service is busy (rate limited)";
    private const string RequestFailed = "the OpenAI media request failed";

    private readonly OpenAIMediaClient _client;
    private readonly MediaPrices _prices;
    private readonly TokenUsageTracker? _tracker;

    public OpenAIMediaGenerator(OpenAIMediaClient client, MediaPrices prices, TokenUsageTracker? tracker)
    {
        _client = client;
        _prices = prices;
        _tracker = tracker;
    }

    public Task<MediaResult> GenerateImageAsync(string prompt, string label, CancellationToken ct = default) =>
        RunAsync(
            () => _client.GenerateImageAsync(prompt, $"Image - {label}", ct),
            _prices.ImageCost(),
            $"Image - {label}",
            "the image request was refused",
            ct);

    public Task<MediaResult> GenerateSpeechAsync(string text, string voice, string label, CancellationToken ct = default) =>
        RunAsync(
            () => _client.GenerateSpeechAsync(text, voice, $"Voicemail - {label}", ct),
            _prices.SpeechCost(text?.Length ?? 0),
            $"Voicemail - {label}",
            "the model returned no usable content",
            ct);

    private async Task<MediaResult> RunAsync(
        Func<Task<byte[]?>> call,
        decimal cost,
        string operation,
        string emptyReason,
        CancellationToken ct)
    {
        try
        {
            var bytes = await call().ConfigureAwait(false);

            // The client returns null on a 400 (refused/invalid) or a transport timeout
            if (bytes == null || bytes.Length == 0)
                return Failed(emptyReason);

            _tracker?.RecordMediaUsage(operation, cost);
            return new MediaResult { Kind = MediaResultKind.Ok, Bytes = bytes, CostUsd = cost };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancellation is the only exception that leaves the seam
            throw;
        }
        catch (ClientResultException ex) when (ex.Status is 401 or 403)
        {
            // Not rethrown: a rejected key stops remaining media for the run, text generation continues (RUN-02)
            return Stopped(KeyRejected);
        }
        catch (ClientResultException ex) when (ex.Status == 429 && IsQuota(ex))
        {
            // Not rethrown: exhausted quota or billing stops remaining media, text generation continues (RUN-02)
            return Stopped(QuotaUsedUp);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            // Throttling is retryable; only exhausted quota or billing stops media (review MEDIUM)
            return Failed(RateLimited);
        }
        catch (ClientResultException ex)
        {
            // Not rethrown: other HTTP failures (5xx after SDK retries, 404) skip this item only (RUN-02)
            return Failed($"{RequestFailed} ({ex.Status})");
        }
        catch (HttpRequestException ex)
        {
            // Not rethrown: transport failures skip this item only (RUN-02)
            return Failed($"{RequestFailed}: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Malformed responses and SDK surprises never escape (review 06-04)
            return Failed($"{RequestFailed}: {ex.GetType().Name}");
        }
        catch (OperationCanceledException)
        {
            // Not rethrown: a cancellation that is not the user's (internal timeout) skips this item only (RUN-02)
            return Failed($"{RequestFailed}: timed out");
        }
    }

    private static MediaResult Failed(string reason) => new() { Kind = MediaResultKind.Failed, Reason = reason };

    private static MediaResult Stopped(string reason) => new() { Kind = MediaResultKind.Stopped, Reason = reason };

    private static bool IsQuota(ClientResultException ex)
    {
        try
        {
            var content = ex.GetRawResponse()?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(content))
                return false;

            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return false;

            if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
            {
                var value = code.GetString();
                if (value is "insufficient_quota" or "billing_hard_limit_reached")
                    return true;
            }

            if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString() ?? string.Empty;
                return text.Contains("quota", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("billing", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        catch (JsonException)
        {
            // Unparseable error body: treat as plain throttling so Continue can retry it
            return false;
        }
    }
}
