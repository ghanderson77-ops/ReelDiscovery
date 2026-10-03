using System.Text.Json;
using System.Text.Json.Serialization;
using ReelDiscovery.Models;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Provider-agnostic structured-output gateway: resolves the tier to a model of the single
/// selected provider, sends a strict JSON schema, applies the outcome-driven retry policy
/// and records token usage for every billed attempt.
/// </summary>
public class LlmGateway : ILlmGateway
{
    private const int MaxInvalidAttempts = 3;
    private const int MaxTruncationRetries = 1;

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILlmProvider _provider;
    private readonly AIModelConfig _storyModel;
    private readonly AIModelConfig _volumeModel;
    private readonly TokenUsageTracker? _usageTracker;

    public LlmGateway(ILlmProvider provider, AIModelConfig storyModel, AIModelConfig volumeModel, TokenUsageTracker? usageTracker = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(storyModel);
        ArgumentNullException.ThrowIfNull(volumeModel);

        // One provider per run (D-11)
        if (storyModel.Provider != provider.Kind || volumeModel.Provider != provider.Kind)
        {
            throw new ArgumentException(
                $"Story model ({storyModel.Provider}) and Volume model ({volumeModel.Provider}) must both use the selected provider ({provider.Kind}).");
        }

        _provider = provider;
        _storyModel = storyModel;
        _volumeModel = volumeModel;
        _usageTracker = usageTracker;
    }

    public LlmProviderKind Provider => _provider.Kind;

    public AIModelConfig ResolveModel(LlmTier tier) => tier == LlmTier.Story ? _storyModel : _volumeModel;

    public async Task<LlmResult<T>> GenerateAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(request);

        var model = ResolveModel(request.Tier);

        // Effective ceiling: model output limit and provider transport limit (e.g. Anthropic
        // non-streaming 21_333), so a truncation retry is never an identical billed request
        var cap = Math.Min(
            model.MaxOutputTokens > 0 ? model.MaxOutputTokens : int.MaxValue,
            _provider.MaxTokensCeiling ?? int.MaxValue);
        var maxTokens = Math.Min(request.MaxOutputTokens, cap);

        var providerRequest = new LlmProviderRequest
        {
            ModelId = model.ModelId,
            Effort = model.Effort,
            SchemaName = JsonSchemaRegistry.GetSchemaName(typeof(T)),
            SchemaJson = JsonSchemaRegistry.GetSchemaJson(typeof(T)),
            SystemPrompt = request.SystemPrompt,
            UserPrompt = request.UserPrompt,
            MaxTokens = maxTokens
        };

        var attempts = 0;
        var invalidAttempts = 0;
        var truncationRetries = 0;
        var inputTotal = 0;
        var outputTotal = 0;

        while (true)
        {
            attempts++;
            ct.ThrowIfCancellationRequested();

            var response = await _provider.SendAsync(providerRequest, ct);
            inputTotal += Math.Max(0, response.InputTokens); outputTotal += Math.Max(0, response.OutputTokens);

            // Every attempt is billed, including failed parses and truncations
            if (response.InputTokens > 0 || response.OutputTokens > 0)
                _usageTracker?.RecordUsage(request.OperationName, model, response.InputTokens, response.OutputTokens);

            switch (response.Outcome)
            {
                case LlmOutcome.Succeeded:
                case LlmOutcome.Invalid:
                {
                    string? invalidMessage;
                    if (response.Outcome == LlmOutcome.Invalid)
                    {
                        invalidMessage = response.Message ?? "Provider reported invalid output";
                    }
                    else if (string.IsNullOrWhiteSpace(response.Text))
                    {
                        invalidMessage = "Empty response text";
                    }
                    else
                    {
                        T? value = null;
                        try
                        {
                            value = JsonSerializer.Deserialize<T>(response.Text, DeserializeOptions);
                            invalidMessage = value == null ? "Response deserialized to null" : null;
                        }
                        catch (JsonException ex)
                        {
                            invalidMessage = $"Invalid JSON: {ex.Message}";
                        }

                        if (value != null)
                            return BuildResult(request, model, attempts, inputTotal, outputTotal, response, LlmOutcome.Succeeded, LlmFailureKind.None, response.Message, value);
                    }

                    invalidAttempts++;
                    if (invalidAttempts < MaxInvalidAttempts)
                        continue;

                    return BuildResult<T>(request, model, attempts, inputTotal, outputTotal, response, LlmOutcome.Invalid, LlmFailureKind.None, invalidMessage, null);
                }

                case LlmOutcome.Truncated:
                    if (truncationRetries < MaxTruncationRetries && providerRequest.MaxTokens < cap)
                    {
                        providerRequest.MaxTokens = (int)Math.Min((long)providerRequest.MaxTokens * 2, cap);
                        truncationRetries++;
                        continue;
                    }

                    return BuildResult<T>(request, model, attempts, inputTotal, outputTotal, response, LlmOutcome.Truncated, LlmFailureKind.None,
                        response.Message ?? $"Output truncated at {providerRequest.MaxTokens} max tokens", null);

                default:
                    // Refused / Failed: no app retry (the SDKs already retried 429/5xx)
                    return BuildResult<T>(request, model, attempts, inputTotal, outputTotal, response, response.Outcome, response.FailureKind, response.Message, null);
            }
        }
    }

    private LlmResult<T> BuildResult<T>(
        LlmRequest request,
        AIModelConfig model,
        int attempts,
        int inputTokens,
        int outputTokens,
        LlmProviderResponse response,
        LlmOutcome outcome,
        LlmFailureKind failureKind,
        string? message,
        T? value) where T : class
    {
        return new LlmResult<T>
        {
            Outcome = outcome,
            FailureKind = failureKind,
            RawText = response.Text,
            Message = message,
            OperationName = request.OperationName,
            Provider = _provider.Kind,
            ModelId = model.ModelId,
            Attempts = attempts,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Value = value
        };
    }
}
