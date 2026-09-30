using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
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

    public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
    {
        var message = await _client.Messages.Create(BuildParams(request), ct);
        return Classify(message);
    }

    public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    internal static MessageCreateParams BuildParams(LlmProviderRequest r)
    {
        var format = new JsonOutputFormat { Schema = GetSchemaDictionary(r.SchemaJson) };

        // Effort only when the catalog sets it (never for Haiku 4.5); the property is init-only,
        // so the unset case uses an initializer without it to keep "effort" off the wire
        var effort = MapEffort(r.Effort);
        var outputConfig = effort != null
            ? new OutputConfig { Format = format, Effort = effort.Value }
            : new OutputConfig { Format = format };

        // Opus 4.7+/5.5 reject non-default sampling and thinking params (400): never set them here.
        return new MessageCreateParams
        {
            Model = r.ModelId,
            MaxTokens = Math.Min(r.MaxTokens, MaxNonStreamingTokens),
            System = new List<TextBlockParam> { new(r.SystemPrompt) },
            Messages = [new() { Role = Role.User, Content = r.UserPrompt }],
            OutputConfig = outputConfig
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
            InputTokens = (int)Math.Min(m.Usage.InputTokens, int.MaxValue),
            OutputTokens = (int)Math.Min(m.Usage.OutputTokens, int.MaxValue)
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
