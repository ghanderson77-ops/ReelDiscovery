using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Tests.Fakes;

/// <summary>
/// Scripted IBatchTransport: records submissions, reports InProgress for <see cref="PollsUntilEnded"/> status calls per
/// batch and then Ended, and answers items through <see cref="Respond"/>. Thread-safe.
/// </summary>
public class FakeBatchTransport : IBatchTransport
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<BatchItemRequest>> _batches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _polls = new(StringComparer.Ordinal);
    private int _nextId;

    public LlmProviderKind Kind { get; set; } = LlmProviderKind.OpenAI;
    public int MaxRequestsPerBatch { get; set; } = 10_000;
    public long MaxBatchBytes { get; set; } = 150L * 1024 * 1024;
    public long? MaxEstimatedInputTokens { get; set; }

    /// <summary>Builds the response of a succeeded item; null = a minimal "{}" success.</summary>
    public Func<BatchItemRequest, LlmProviderResponse>? Respond { get; set; }

    public HashSet<string> ErroredIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ExpiredIds { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, SubmitAsync returns Succeeded = false with this kind.</summary>
    public LlmFailureKind? SubmitFailure { get; set; }

    /// <summary>When true, every status call reports the batch as Failed.</summary>
    public bool FailWholeBatch { get; set; }

    /// <summary>When set, CancelAsync throws it (after nothing is recorded).</summary>
    public Exception? CancelFailure { get; set; }

    /// <summary>Batches whose first item custom id is in this set stay InProgress regardless of PollsUntilEnded.</summary>
    public HashSet<string> NeverEndIds { get; } = new(StringComparer.Ordinal);

    /// <summary>When true, a status or results call for a batch this fake never issued reports NotFound (HTTP 404).</summary>
    public bool UnknownIsNotFound { get; set; }

    /// <summary>Batches whose results download reports NotFound (results past the provider's retention window).</summary>
    public HashSet<string> ResultsGoneIds { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, every status call fails with this kind (not NotFound).</summary>
    public LlmFailureKind? StatusFailure { get; set; }

    public int PollsUntilEnded { get; set; } = 1;
    public TimeSpan SubmitDelay { get; set; }

    /// <summary>Completed when a SubmitAsync call starts (before SubmitHold).</summary>
    public TaskCompletionSource? SubmitStarted { get; set; }

    /// <summary>When set, SubmitAsync waits on it (ignoring the token, like an upload already in flight).</summary>
    public Task? SubmitHold { get; set; }

    public List<List<BatchItemRequest>> Submissions { get; } = new();
    public List<string> CancelledIds { get; } = new();
    public int StatusCalls { get; private set; }
    public List<(string?, string?)> DeletedFiles { get; } = new();

    public async Task<BatchSubmitResult> SubmitAsync(IReadOnlyList<BatchItemRequest> items, CancellationToken ct = default)
    {
        SubmitStarted?.TrySetResult();
        if (SubmitHold != null)
            await SubmitHold;

        if (SubmitDelay > TimeSpan.Zero)
            await Task.Delay(SubmitDelay, ct);

        ct.ThrowIfCancellationRequested();

        var failure = SubmitFailure;
        if (failure != null)
            return new BatchSubmitResult { Succeeded = false, FailureKind = failure.Value, Message = "Scripted submit failure" };

        var copy = items.ToList();
        lock (_lock)
        {
            var id = "fb_" + ++_nextId;
            Submissions.Add(copy);
            _batches[id] = copy;
            _polls[id] = 0;
            return new BatchSubmitResult { Succeeded = true, BatchId = id, InputFileId = "file_in_" + _nextId };
        }
    }

    public Task<BatchStatusResult> GetStatusAsync(string batchId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            StatusCalls++;
            if (StatusFailure != null)
                return Task.FromResult(new BatchStatusResult
                {
                    Succeeded = false, FailureKind = StatusFailure.Value, Message = "Scripted status failure"
                });

            if (!_batches.TryGetValue(batchId, out var items))
                return Task.FromResult(new BatchStatusResult
                {
                    Succeeded = false, FailureKind = LlmFailureKind.BadRequest, Message = "Unknown batch", NotFound = UnknownIsNotFound
                });

            var total = items.Count;
            if (FailWholeBatch)
                return Task.FromResult(new BatchStatusResult { Succeeded = true, State = BatchState.Failed, Total = total });

            if (CancelledIds.Contains(batchId))
                return Task.FromResult(new BatchStatusResult { Succeeded = true, State = BatchState.Cancelled, Total = total });

            var polls = _polls[batchId]++;
            if (polls < PollsUntilEnded || (items.Count > 0 && NeverEndIds.Contains(items[0].CustomId)))
                return Task.FromResult(new BatchStatusResult { Succeeded = true, State = BatchState.InProgress, Total = total });

            var failed = items.Count(i => ErroredIds.Contains(i.CustomId) || ExpiredIds.Contains(i.CustomId));
            return Task.FromResult(new BatchStatusResult
            {
                Succeeded = true,
                State = BatchState.Ended,
                Done = total,
                Total = total,
                Failed = failed,
                OutputFileId = "file_out_" + batchId
            });
        }
    }

    public Task<BatchResultsResult> GetResultsAsync(string batchId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        List<BatchItemRequest> items;
        lock (_lock)
        {
            if (!_batches.TryGetValue(batchId, out var found))
                return Task.FromResult(new BatchResultsResult
                {
                    Succeeded = false, FailureKind = LlmFailureKind.BadRequest, Message = "Unknown batch", NotFound = UnknownIsNotFound
                });

            if (ResultsGoneIds.Contains(batchId))
                return Task.FromResult(new BatchResultsResult
                {
                    Succeeded = false, FailureKind = LlmFailureKind.BadRequest, Message = "Results expired", NotFound = true
                });

            items = found.ToList();
        }

        var results = new List<BatchItemResult>();
        foreach (var item in items)
        {
            bool errored, expired;
            lock (_lock)
            {
                errored = ErroredIds.Contains(item.CustomId);
                expired = ExpiredIds.Contains(item.CustomId);
            }

            if (errored)
            {
                results.Add(new BatchItemResult { CustomId = item.CustomId, Outcome = BatchItemOutcome.Errored, Message = "Scripted item error" });
                continue;
            }

            if (expired)
            {
                results.Add(new BatchItemResult { CustomId = item.CustomId, Outcome = BatchItemOutcome.Expired, Message = "Scripted item expiry" });
                continue;
            }

            // Invoked outside the lock so a test callback can block without serializing other callers
            var response = Respond?.Invoke(item) ?? FakeLlmProvider.Ok("{}");
            response.IsBatch = true;
            results.Add(new BatchItemResult { CustomId = item.CustomId, Outcome = BatchItemOutcome.Succeeded, Response = response });
        }

        return Task.FromResult(new BatchResultsResult { Succeeded = true, Items = results });
    }

    public Task CancelAsync(string batchId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var failure = CancelFailure;
        if (failure != null)
            throw failure;

        lock (_lock)
            CancelledIds.Add(batchId);

        return Task.CompletedTask;
    }

    public Task DeleteFilesAsync(string? inputFileId, string? outputFileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            DeletedFiles.Add((inputFileId, outputFileId));

        return Task.CompletedTask;
    }
}
