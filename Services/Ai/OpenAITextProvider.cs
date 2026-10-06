using System.ClientModel;
using System.Collections.Concurrent;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// ILlmProvider for OpenAI Chat Completions: strict structured output (response_format json_schema),
/// finish-reason/refusal classification and HTTP status to FailureKind mapping.
/// </summary>
public class OpenAITextProvider : ILlmProvider
{
    private readonly OpenAIClient _client;
    private readonly OpenAIModelClient _modelClient;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new();

    public OpenAITextProvider(string apiKey)
        : this(apiKey, CreateDefaultOptions())
    {
    }

    internal OpenAITextProvider(string apiKey, OpenAIClientOptions options)
    {
        _client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        _modelClient = new OpenAIModelClient(new ApiKeyCredential(apiKey), options);
    }

    public LlmProviderKind Kind => LlmProviderKind.OpenAI;

    public int? MaxTokensCeiling => null;

    /// <summary>
    /// Production transport options: 10-minute timeout for long structured outputs and SDK retries
    /// that skip insufficient_quota 429s.
    /// </summary>
    internal static OpenAIClientOptions CreateDefaultOptions() => new()
    {
        NetworkTimeout = TimeSpan.FromMinutes(10),
        RetryPolicy = new QuotaAwareRetryPolicy()
    };

    public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
    {
        // No app-level retry loop: the SDK transport retries 429/5xx (insufficient_quota excluded)
        try
        {
            var chatClient = _chatClients.GetOrAdd(request.ModelId, id => _client.GetChatClient(id));
            var result = await chatClient.CompleteChatAsync(BuildMessages(request), BuildOptions(request), ct);
            return Classify(result.Value, result.GetRawResponse().Content);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (OperationCanceledException)
        {
            // Caller token not cancelled: the SDK/HttpClient network timeout fired. Classify, never throw (CR-01)
            return Failed(LlmFailureKind.Network, "Request to OpenAI timed out");
        }
        catch (ClientResultException ex)
        {
            var body = ReadBody(ex);
            var kind = MapStatus(ex.Status, body);
            return Failed(kind, SafeMessage(kind, ex.Status, body));
        }
        catch (HttpRequestException ex)
        {
            return Failed(LlmFailureKind.Network, "Network error contacting OpenAI: " + ex.Message);
        }
    }

    public async Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default)
    {
        // Model retrieve is free: validates the key and model access without billing tokens
        try
        {
            await _modelClient.GetModelAsync(modelId, ct);
            return new LlmKeyTestResult { Succeeded = true, Message = $"OpenAI key OK ({modelId})" };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (OperationCanceledException)
        {
            // Caller token not cancelled: the SDK/HttpClient network timeout fired. Classify, never throw (CR-01)
            return new LlmKeyTestResult { FailureKind = LlmFailureKind.Network, Message = "Key test timed out contacting api.openai.com" };
        }
        catch (ClientResultException ex)
        {
            var kind = MapStatus(ex.Status, ReadBody(ex));
            var message = kind switch
            {
                LlmFailureKind.Auth => "Key rejected (authentication failed)",
                LlmFailureKind.BadRequest => $"Model '{modelId}' is not available to this key",
                LlmFailureKind.SpendLimit => "Key valid but quota or billing limit reached",
                LlmFailureKind.Network => "Could not reach api.openai.com",
                _ => $"Key test failed: {kind}"
            };
            return new LlmKeyTestResult { FailureKind = kind, Message = message };
        }
        catch (HttpRequestException)
        {
            return new LlmKeyTestResult { FailureKind = LlmFailureKind.Network, Message = "Could not reach api.openai.com" };
        }
    }

    internal static ChatCompletionOptions BuildOptions(LlmProviderRequest r)
    {
        // No prompt cache options are sent: implicit caching stays the default until the 07-22 live spike (OQ1)
        // Sampling parameters (temperature/top_p) are never sent: reasoning models reject non-default values
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: r.SchemaName,
                jsonSchema: BinaryData.FromString(r.SchemaJson),
                jsonSchemaIsStrict: true),
            MaxOutputTokenCount = r.MaxTokens
        };

        // Effort only when the catalog sets it; unset keeps "reasoning_effort" off the wire
        if (r.Effort != null)
        {
            // ReasoningEffortLevel is marked experimental (OPENAI001) in OpenAI 2.14.0 but maps to the stable
            // Chat Completions "reasoning_effort" field
#pragma warning disable OPENAI001
            options.ReasoningEffortLevel = new ChatReasoningEffortLevel(r.Effort);
#pragma warning restore OPENAI001
        }

        return options;
    }

    internal static List<ChatMessage> BuildMessages(LlmProviderRequest r) => new()
    {
        new SystemChatMessage(r.SystemPrompt),
        new UserChatMessage(r.UserPrompt)
    };

    internal static LlmProviderResponse Classify(ChatCompletion c, BinaryData? rawBody = null)
    {
        // OpenAI prompt_tokens is inclusive; GPT-5.6+ implicit caching may bill writes at 1.25x and reports them
        // only in the raw JSON (research A1; verified live in 07-22)
        var prompt = Math.Max(0, c.Usage?.InputTokenCount ?? 0);
        var cached = Math.Max(0, c.Usage?.InputTokenDetails?.CachedTokenCount ?? 0);
        var write = ReadCacheWriteTokens(rawBody);
        var response = new LlmProviderResponse
        {
            InputTokens = Math.Max(0, prompt - cached - write),
            CacheReadTokens = cached,
            CacheWriteTokens = write,
            OutputTokens = c.Usage?.OutputTokenCount ?? 0
        };

        // Refusal is checked first: a refused completion can still report finish_reason "stop"
        if (!string.IsNullOrEmpty(c.Refusal))
        {
            response.Outcome = LlmOutcome.Refused;
            response.Message = $"Refused by model: {c.Refusal}";
            return response;
        }

        switch (c.FinishReason)
        {
            case ChatFinishReason.Stop:
                response.Outcome = LlmOutcome.Succeeded;
                response.Text = c.Content.Count > 0 ? c.Content[0].Text : null;
                break;
            case ChatFinishReason.Length:
                response.Outcome = LlmOutcome.Truncated;
                response.Message = "Response truncated at max_completion_tokens";
                break;
            case ChatFinishReason.ContentFilter:
                response.Outcome = LlmOutcome.Refused;
                response.Message = "Blocked by content filter";
                break;
            default:
                response.Outcome = LlmOutcome.Failed;
                response.FailureKind = LlmFailureKind.Unknown;
                response.Message = $"Unexpected finish reason '{c.FinishReason}'";
                break;
        }

        return response;
    }

    /// <summary>
    /// Reads usage.prompt_tokens_details.cache_write_tokens from the raw response JSON (the SDK has no typed
    /// property). Returns 0 when the body, any level or the value is missing, malformed or not a non-negative integer.
    /// </summary>
    internal static int ReadCacheWriteTokens(BinaryData? raw)
    {
        if (raw == null)
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(raw.ToMemory());
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("usage", out var usage)
                && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("prompt_tokens_details", out var details)
                && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("cache_write_tokens", out var write)
                && write.ValueKind == JsonValueKind.Number
                && write.TryGetInt32(out var tokens))
            {
                return Math.Max(0, tokens);
            }
        }
        catch (JsonException)
        {
            // Usage detail is optional; a missing or odd field must never fail a paid call
        }
        catch (ArgumentException)
        {
            // Usage detail is optional; a missing or odd field must never fail a paid call
        }

        return 0;
    }

    /// <summary>
    /// Maps an HTTP status (0 = no response, i.e. network failure) and error body to a FailureKind.
    /// </summary>
    internal static LlmFailureKind MapStatus(int status, string body)
    {
        if (status is 401 or 403)
            return LlmFailureKind.Auth;

        if (status == 429)
        {
            return body.Contains("insufficient_quota", StringComparison.Ordinal)
                ? LlmFailureKind.SpendLimit
                : LlmFailureKind.RateLimited;
        }

        if (status is 400 or 404 or 422)
            return LlmFailureKind.BadRequest;

        if (status >= 500)
            return LlmFailureKind.Transient;

        if (status == 0)
            return LlmFailureKind.Network;

        return LlmFailureKind.Unknown;
    }

    internal static string ReadBody(ClientResultException ex) =>
        ex.GetRawResponse()?.Content?.ToString() ?? string.Empty;

    /// <summary>
    /// Builds a user-facing message from the provider's error.message only; request headers (and thus the key)
    /// are never included.
    /// </summary>
    internal static string SafeMessage(LlmFailureKind kind, int status, string body)
    {
        var prefix = kind switch
        {
            LlmFailureKind.Auth => "Authentication failed",
            LlmFailureKind.SpendLimit => "Quota exhausted",
            LlmFailureKind.RateLimited => "Rate limited",
            LlmFailureKind.Transient => "OpenAI service error",
            LlmFailureKind.BadRequest => "Request rejected",
            LlmFailureKind.Network => "Network error contacting OpenAI",
            _ => "OpenAI error"
        };

        var detail = ExtractErrorMessage(body);
        return detail != null
            ? $"{prefix}: {detail}"
            : $"{prefix} (HTTP {status})";
    }

    private static string? ExtractErrorMessage(string body)
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
}
