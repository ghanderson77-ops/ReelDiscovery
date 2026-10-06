using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Anthropic.Models.Models;
using AnthropicMessage = Anthropic.Models.Messages.Message;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// ILlmProvider for Anthropic Claude: strict structured output (output_config.format json_schema),
/// stop-reason classification and exception-to-FailureKind mapping.
/// </summary>
public class AnthropicTextProvider : ILlmProvider
{
    /// <summary>
    /// Anthropic SDKs refuse (or the 10-minute HTTP timeout kills) non-streaming requests whose max_tokens
    /// implies > 10 minutes of generation (60*60*max_tokens/128_000 > 600 s => max_tokens > 21_333).
    /// Phase 1 stays non-streaming; the cap keeps every request inside that budget.
    /// </summary>
    internal const int MaxNonStreamingTokens = 21_333;

    private static readonly ConcurrentDictionary<string, Dictionary<string, JsonElement>> _schemaCache = new();

    private readonly AnthropicClient _client;

    public AnthropicTextProvider(string apiKey)
        : this(new Anthropic.Core.ClientOptions
        {
            ApiKey = apiKey,
            HttpClient = CreateHttpClient(new HttpClientHandler()),
            Timeout = TimeSpan.FromMinutes(10)
        })
    {
    }

    internal AnthropicTextProvider(Anthropic.Core.ClientOptions options)
    {
        _client = new AnthropicClient(options);
    }

    public LlmProviderKind Kind => LlmProviderKind.Anthropic;

    public int? MaxTokensCeiling => MaxNonStreamingTokens;

    /// <summary>
    /// Wraps the transport in <see cref="AnthropicSpendLimitHandler"/> so spend-cap 429s are never retried by the SDK.
    /// </summary>
    internal static HttpClient CreateHttpClient(HttpMessageHandler inner) =>
        new(new AnthropicSpendLimitHandler { InnerHandler = inner }) { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
    {
        // No app-level retry loop: the SDK already retries 429/5xx (spend-cap 429s are excluded by the handler)
        try
        {
            var message = await _client.Messages.Create(BuildParams(request), ct);
            return Classify(message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (OperationCanceledException)
        {
            // Caller token not cancelled: the SDK/HttpClient network timeout fired. Classify, never throw (CR-01)
            return Failed(LlmFailureKind.Network, "Request to Anthropic timed out");
        }
        catch (AnthropicApiException ex)
        {
            var kind = MapException(ex);
            return Failed(kind, SafeMessage(kind, ex));
        }
        catch (AnthropicIOException ex)
        {
            return Failed(LlmFailureKind.Network, "Network error contacting Anthropic: " + ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return Failed(LlmFailureKind.Network, "Network error contacting Anthropic: " + ex.Message);
        }
    }

    public async Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default)
    {
        // Model retrieve is free: validates the key and model access without billing tokens
        try
        {
            await _client.Models.Retrieve(modelId, new ModelRetrieveParams(), ct);
            return new LlmKeyTestResult { Succeeded = true, Message = $"Anthropic key OK ({modelId})" };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (OperationCanceledException)
        {
            // Caller token not cancelled: the SDK/HttpClient network timeout fired. Classify, never throw (CR-01)
            return new LlmKeyTestResult { FailureKind = LlmFailureKind.Network, Message = "Key test timed out contacting api.anthropic.com" };
        }
        catch (AnthropicApiException ex)
        {
            var kind = MapException(ex);
            var message = kind switch
            {
                LlmFailureKind.Auth => "Key rejected (authentication failed)",
                LlmFailureKind.BadRequest => $"Model '{modelId}' is not available to this key",
                LlmFailureKind.SpendLimit => "Key valid but spend limit or credit balance reached",
                _ => $"Key test failed: {kind}"
            };
            return new LlmKeyTestResult { FailureKind = kind, Message = message };
        }
        catch (AnthropicIOException)
        {
            return new LlmKeyTestResult { FailureKind = LlmFailureKind.Network, Message = "Could not reach api.anthropic.com" };
        }
        catch (HttpRequestException)
        {
            return new LlmKeyTestResult { FailureKind = LlmFailureKind.Network, Message = "Could not reach api.anthropic.com" };
        }
    }

    /// <summary>
    /// Maps an Anthropic API error to a FailureKind, most specific first. Spend-limit errors are classified
    /// from the body because they arrive as 429 (tier spend cap), 402 (billing) or 400 (user-set usage limit).
    /// </summary>
    internal static LlmFailureKind MapException(AnthropicApiException ex)
    {
        var body = ex.ResponseBody ?? string.Empty;
        var status = (int)ex.StatusCode;

        if (ex is AnthropicUnauthorizedException or AnthropicForbiddenException || status is 401 or 403)
            return LlmFailureKind.Auth;

        if (ex is AnthropicRateLimitException || status == 429)
        {
            // Tier spend cap: 429 rate_limit_error with error.details.error_code enforced_spend_limit_reached
            return body.Contains("enforced_spend_limit_reached", StringComparison.Ordinal)
                ? LlmFailureKind.SpendLimit
                : LlmFailureKind.RateLimited;
        }

        if (status == 402 || body.Contains("billing_error", StringComparison.Ordinal))
            return LlmFailureKind.SpendLimit;

        if (ex is AnthropicBadRequestException || status == 400)
        {
            if (body.Contains("usage limits", StringComparison.OrdinalIgnoreCase)
                || body.Contains("credit balance", StringComparison.OrdinalIgnoreCase))
                return LlmFailureKind.SpendLimit;

            return LlmFailureKind.BadRequest;
        }

        if (ex is AnthropicNotFoundException or AnthropicUnprocessableEntityException || status is 404 or 422)
            return LlmFailureKind.BadRequest;

        if (ex is Anthropic5xxException || status >= 500)
            return LlmFailureKind.Transient;

        return LlmFailureKind.Unknown;
    }

    /// <summary>
    /// Builds a user-facing message from the provider's error.message only; request headers (and thus the key)
    /// are never included.
    /// </summary>
    internal static string SafeMessage(LlmFailureKind kind, AnthropicApiException ex)
    {
        var prefix = kind switch
        {
            LlmFailureKind.Auth => "Authentication failed",
            LlmFailureKind.SpendLimit => "Spend limit reached",
            LlmFailureKind.RateLimited => "Rate limited",
            LlmFailureKind.Transient => "Anthropic service error",
            LlmFailureKind.BadRequest => "Request rejected",
            _ => "Anthropic error"
        };

        var detail = ExtractErrorMessage(ex.ResponseBody);
        return detail != null
            ? $"{prefix}: {detail}"
            : $"{prefix} (HTTP {(int)ex.StatusCode})";
    }

    private static string? ExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. HTML from a proxy): fall back to the status-only message
        }

        return null;
    }

    private static LlmProviderResponse Failed(LlmFailureKind kind, string message) => new()
    {
        Outcome = LlmOutcome.Failed,
        FailureKind = kind,
        Message = message
    };

    /// <summary>
    /// Builds the strict structured-output config (json_schema format plus optional effort). Shared with the
    /// batch transport so live and batch requests carry identical output settings (D-05).
    /// </summary>
    internal static OutputConfig BuildOutputConfig(LlmProviderRequest r)
    {
        var format = new JsonOutputFormat { Schema = GetSchemaDictionary(r.SchemaJson) };

        // Effort only when the catalog sets it (never for Haiku 4.5); the property is init-only,
        // so the unset case uses an initializer without it to keep "effort" off the wire
        var effort = MapEffort(r.Effort);
        return effort != null
            ? new OutputConfig { Format = format, Effort = effort.Value }
            : new OutputConfig { Format = format };
    }

    internal static MessageCreateParams BuildParams(LlmProviderRequest r)
    {
        // D-19: block-level breakpoint on the byte-stable prefix; never top-level CacheControl, which would cache
        // the unique per-thread user prompt (research anti-pattern). Below the model minimum the provider silently
        // skips caching.
        var systemBlock = string.IsNullOrEmpty(r.SystemPrompt)
            ? new TextBlockParam(r.SystemPrompt)
            : new TextBlockParam(r.SystemPrompt) { CacheControl = new CacheControlEphemeral() };

        // Opus 4.7+/5.5 reject non-default sampling and thinking params (400): never set them here.
        return new MessageCreateParams
        {
            Model = r.ModelId,
            MaxTokens = Math.Min(r.MaxTokens, MaxNonStreamingTokens),
            System = new List<TextBlockParam> { systemBlock },
            Messages = [new() { Role = Role.User, Content = r.UserPrompt }],
            OutputConfig = BuildOutputConfig(r)
        };
    }

    internal static LlmProviderResponse Classify(AnthropicMessage m)
    {
        // Select text blocks by type, not position: Opus 5.5 emits a thinking block first
        var text = new StringBuilder();
        foreach (var block in m.Content)
        {
            if (block.TryPickText(out var t))
                text.Append(t.Text);
        }

        var response = new LlmProviderResponse
        {
            Text = text.ToString(),
            // Anthropic input_tokens excludes cached tokens; total input = read + write + input (PROV-07)
            InputTokens = ClampTokens(m.Usage.InputTokens),
            CacheWriteTokens = ClampTokens(m.Usage.CacheCreationInputTokens ?? 0),
            CacheReadTokens = ClampTokens(m.Usage.CacheReadInputTokens ?? 0),
            OutputTokens = ClampTokens(m.Usage.OutputTokens)
        };

        // Raw string is robust to stop reasons added after this SDK version
        var stop = m.StopReason?.Raw();
        switch (stop)
        {
            case "end_turn":
            case "stop_sequence":
                response.Outcome = LlmOutcome.Succeeded;
                break;
            case "refusal":
                response.Outcome = LlmOutcome.Refused;
                response.Message = BuildRefusalMessage(m);
                break;
            case "max_tokens":
                response.Outcome = LlmOutcome.Truncated;
                response.Message = "Response truncated at max_tokens";
                break;
            default:
                response.Outcome = LlmOutcome.Failed;
                response.FailureKind = LlmFailureKind.Unknown;
                response.Message = $"Unexpected stop reason '{stop}'";
                break;
        }

        return response;
    }

    private static int ClampTokens(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

    private static string BuildRefusalMessage(AnthropicMessage m)
    {
        var details = m.StopDetails;
        if (details == null)
            return "Refused by model";

        var category = details.Category?.Raw();
        var explanation = details.Explanation;
        var detail = $"{category} {explanation}".Trim();
        return detail.Length > 0 ? $"Refused by model: {detail}" : "Refused by model";
    }

    private static Dictionary<string, JsonElement> GetSchemaDictionary(string schemaJson) =>
        _schemaCache.GetOrAdd(schemaJson, json => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!);

    private static Effort? MapEffort(string? effort) => effort?.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => null
    };
}
