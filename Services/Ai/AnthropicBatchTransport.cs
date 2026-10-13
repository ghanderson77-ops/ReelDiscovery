using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Anthropic.Models.Messages.Batches;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// IBatchTransport over Anthropic Message Batches. Every request's params are built by the live
/// <see cref="AnthropicTextProvider.BuildParams"/> (including the system cache_control block) and every succeeded
/// result is classified by the live <see cref="AnthropicTextProvider.Classify"/>, so batch and live stay identical (D-05).
/// </summary>
public class AnthropicBatchTransport : IBatchTransport
{
    private readonly AnthropicClient _client;

    public AnthropicBatchTransport(string apiKey)
        : this(new Anthropic.Core.ClientOptions
        {
            ApiKey = apiKey,
            HttpClient = AnthropicTextProvider.CreateHttpClient(new HttpClientHandler()),
            Timeout = TimeSpan.FromMinutes(10)
        })
    {
    }

    internal AnthropicBatchTransport(Anthropic.Core.ClientOptions options)
    {
        _client = new AnthropicClient(options);
    }

    public LlmProviderKind Kind => LlmProviderKind.Anthropic;

    public int MaxRequestsPerBatch => 10_000;

    public long MaxBatchBytes => 150L * 1024 * 1024;

    // Anthropic has no per-tier batch queue token cap
    public long? MaxEstimatedInputTokens => null;

    public async Task<BatchSubmitResult> SubmitAsync(IReadOnlyList<BatchItemRequest> items, CancellationToken ct = default)
    {
        try
        {
            var requests = items
                .Select(i => new Request
                {
                    CustomID = i.CustomId,
                    Params = ToBatchParams(AnthropicTextProvider.BuildParams(i.Request))
                })
                .ToList();

            var batch = await _client.Messages.Batches.Create(new BatchCreateParams { Requests = requests }, ct);
            return new BatchSubmitResult { Succeeded = true, BatchId = batch.ID };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (Exception ex)
        {
            var (kind, message) = MapFailure(ex);
            return new BatchSubmitResult { FailureKind = kind, Message = message };
        }
    }

    public async Task<BatchStatusResult> GetStatusAsync(string batchId, CancellationToken ct = default)
    {
        try
        {
            var batch = await _client.Messages.Batches.Retrieve(batchId, new BatchRetrieveParams(), ct);
            var counts = batch.RequestCounts;
            var processing = ClampCount(counts.Processing);
            var failed = ClampCount(counts.Errored) + ClampCount(counts.Expired) + ClampCount(counts.Canceled);
            var total = processing + ClampCount(counts.Succeeded) + failed;

            return new BatchStatusResult
            {
                Succeeded = true,
                State = MapStatus(batch.ProcessingStatus.Raw()),
                Done = total - processing,
                Total = total,
                Failed = failed
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (Exception ex)
        {
            var (kind, message) = MapFailure(ex);
            return new BatchStatusResult { FailureKind = kind, Message = message, NotFound = IsNotFound(ex) };
        }
    }

    public async Task<BatchResultsResult> GetResultsAsync(string batchId, CancellationToken ct = default)
    {
        var result = new BatchResultsResult();
        try
        {
            await foreach (var item in _client.Messages.Batches.ResultsStreaming(batchId, new BatchResultsParams(), ct))
            {
                var parsed = SafeItemResult(item);
                if (parsed != null)
                    result.Items.Add(parsed);
            }

            result.Succeeded = true;
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (Exception ex)
        {
            var (kind, message) = MapFailure(ex);
            return new BatchResultsResult { FailureKind = kind, Message = message, NotFound = IsNotFound(ex) };
        }
    }

    public async Task CancelAsync(string batchId, CancellationToken ct = default)
    {
        try
        {
            await _client.Messages.Batches.Cancel(batchId, new BatchCancelParams(), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (AnthropicApiException ex) when ((int)ex.StatusCode is 404 or 409)
        {
            // An unknown or already-ended batch needs no action: it can no longer bill
        }
        catch (Exception ex)
        {
            // CR-B01: a cancel that did not take effect must reach Start Over, which then keeps the ledger
            var (kind, message) = MapFailure(ex);
            throw new BatchCancelFailedException(batchId, kind, message);
        }
    }

    // Anthropic batches have no uploaded files to delete
    public Task DeleteFilesAsync(string? inputFileId, string? outputFileId, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Copies the live request params onto the batch Params type field by field (the SDK has separate types for the
    /// same wire shape), so nothing beyond the live fields can reach the batch body.
    /// </summary>
    internal static Params ToBatchParams(MessageCreateParams live) => new()
    {
        Model = live.Model,
        MaxTokens = live.MaxTokens,
        // BuildParams always uses the list form (with the cache_control block); the union types differ per SDK class
        System = live.System!.Match<ParamsSystem>(text => text, blocks => blocks.ToList()),
        Messages = live.Messages,
        OutputConfig = live.OutputConfig!
    };

    internal static BatchState MapStatus(string? status) => status switch
    {
        "ended" => BatchState.Ended,
        // in_progress, canceling and anything newer stay pollable
        _ => BatchState.InProgress
    };

    /// <summary>
    /// WR-A02: one result the SDK or classifier cannot read becomes an Errored item (or is skipped when even its custom id
    /// is unreadable), so a single bad line never fails the whole download; the decorator runs those ids live.
    /// </summary>
    private static BatchItemResult? SafeItemResult(MessageBatchIndividualResponse item)
    {
        try
        {
            return ToItemResult(item);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                var customId = item.CustomID;
                return string.IsNullOrEmpty(customId)
                    ? null
                    : new BatchItemResult { CustomId = customId, Outcome = BatchItemOutcome.Errored, Message = "Batch result could not be read" };
            }
            catch (Exception inner) when (inner is not OperationCanceledException)
            {
                // No readable custom id: skip it; the decorator treats the missing id as expired and runs it live
                return null;
            }
        }
    }

    private static BatchItemResult ToItemResult(MessageBatchIndividualResponse item)
    {
        var customId = item.CustomID;
        if (item.Result.TryPickSucceeded(out var ok))
        {
            var response = AnthropicTextProvider.Classify(ok.Message);
            response.IsBatch = true;
            return new BatchItemResult { CustomId = customId, Outcome = BatchItemOutcome.Succeeded, Response = response };
        }

        if (item.Result.TryPickExpired(out _))
            return new BatchItemResult { CustomId = customId, Outcome = BatchItemOutcome.Expired, Message = "Batch request expired" };

        if (item.Result.TryPickCanceled(out _))
            return new BatchItemResult { CustomId = customId, Outcome = BatchItemOutcome.Canceled, Message = "Batch request canceled" };

        // Errored (or a result type newer than this SDK): only a fixed message, never the provider body
        return new BatchItemResult { CustomId = customId, Outcome = BatchItemOutcome.Errored, Message = "Batch request errored" };
    }

    private static int ClampCount(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

    // CR-01: a deleted batch, a key of another workspace, or results past their retention window (about 29 days)
    private static bool IsNotFound(Exception ex) => ex is AnthropicApiException api && (int)api.StatusCode == 404;

    private static (LlmFailureKind Kind, string Message) MapFailure(Exception ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                // Caller token not cancelled: the SDK/HttpClient network timeout fired. Classify, never throw (CR-01)
                return (LlmFailureKind.Network, "Request to Anthropic timed out");
            case AnthropicApiException api:
                var kind = AnthropicTextProvider.MapException(api);
                return (kind, AnthropicTextProvider.SafeMessage(kind, api));
            case AnthropicIOException io:
                return (LlmFailureKind.Network, "Network error contacting Anthropic: " + io.Message);
            case HttpRequestException http:
                return (LlmFailureKind.Network, "Network error contacting Anthropic: " + http.Message);
            default:
                // Unexpected SDK/parse failure: report the type only so no payload text leaks
                return (LlmFailureKind.Unknown, $"Anthropic batch error ({ex.GetType().Name})");
        }
    }
}
