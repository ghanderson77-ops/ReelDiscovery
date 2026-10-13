namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Provider-neutral batch API (OpenAI Batch, Anthropic Message Batches). Implementations never throw for provider or
/// network failures: only an OperationCanceledException from <c>ct</c> escapes; every other failure is reported in the
/// returned result object (Succeeded = false, FailureKind, Message).
/// </summary>
public interface IBatchTransport
{
    LlmProviderKind Kind { get; }

    /// <summary>Packaging cap on requests per submitted batch (10,000 for both providers).</summary>
    int MaxRequestsPerBatch { get; }

    /// <summary>Packaging cap on the serialized batch size in bytes (150 MB).</summary>
    long MaxBatchBytes { get; }

    /// <summary>Estimated input tokens allowed in the provider's batch queue (OpenAI Tier-1: 2,000,000); null = no cap.</summary>
    long? MaxEstimatedInputTokens { get; }

    /// <summary>Submits one batch. Never throws except OperationCanceledException from <paramref name="ct"/>.</summary>
    Task<BatchSubmitResult> SubmitAsync(IReadOnlyList<BatchItemRequest> items, CancellationToken ct = default);

    /// <summary>Polls one batch. Never throws except OperationCanceledException from <paramref name="ct"/>.</summary>
    Task<BatchStatusResult> GetStatusAsync(string batchId, CancellationToken ct = default);

    /// <summary>
    /// Downloads and classifies the results of an ended batch. Never throws except OperationCanceledException from
    /// <paramref name="ct"/>.
    /// </summary>
    Task<BatchResultsResult> GetResultsAsync(string batchId, CancellationToken ct = default);

    /// <summary>
    /// Cancels one batch. A batch the provider no longer has or that has already ended (404/409, or a terminal status)
    /// counts as cancelled and returns normally. Any other failure (network, timeout, 5xx, auth) throws
    /// <see cref="BatchCancelFailedException"/> so Start Over can keep the ledger and warn (CR-B01); an
    /// OperationCanceledException from <paramref name="ct"/> also escapes.
    /// </summary>
    Task CancelAsync(string batchId, CancellationToken ct = default);

    /// <summary>
    /// Best-effort delete of the uploaded input and the output files (OpenAI); a no-op for Anthropic. Only
    /// OperationCanceledException from <paramref name="ct"/> escapes.
    /// </summary>
    Task DeleteFilesAsync(string? inputFileId, string? outputFileId, CancellationToken ct = default);
}

/// <summary>
/// A provider cancel call that did not take effect (CR-B01). The message is built from the safe failure text only (no
/// request or response payload).
/// </summary>
public class BatchCancelFailedException : Exception
{
    public BatchCancelFailedException(string batchId, LlmFailureKind kind, string message)
        : base(message)
    {
        BatchId = batchId;
        FailureKind = kind;
    }

    public string BatchId { get; }
    public LlmFailureKind FailureKind { get; }
}

public class BatchItemRequest
{
    public string CustomId { get; set; } = string.Empty;
    public LlmProviderRequest Request { get; set; } = new();
}

public class BatchSubmitResult
{
    public bool Succeeded { get; set; }
    public string BatchId { get; set; } = string.Empty;

    /// <summary>OpenAI uploaded input file id; empty for Anthropic.</summary>
    public string InputFileId { get; set; } = string.Empty;

    public LlmFailureKind FailureKind { get; set; }
    public string Message { get; set; } = string.Empty;
}

public enum BatchState
{
    InProgress,
    Ended,
    Failed,
    Expired,
    Cancelled
}

public class BatchStatusResult
{
    public bool Succeeded { get; set; }
    public BatchState State { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
    public int Failed { get; set; }
    public string OutputFileId { get; set; } = string.Empty;
    public string ErrorFileId { get; set; } = string.Empty;
    public LlmFailureKind FailureKind { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Failed because the provider does not know the batch (HTTP 404: deleted, or the key belongs to another org or
    /// project). Permanent: a batch the provider no longer has can neither finish nor bill.
    /// </summary>
    public bool NotFound { get; set; }
}

public enum BatchItemOutcome
{
    Succeeded,
    Errored,
    Expired,
    Canceled
}

public class BatchItemResult
{
    public string CustomId { get; set; } = string.Empty;
    public BatchItemOutcome Outcome { get; set; }

    /// <summary>Classified response (IsBatch = true) for Succeeded items; null otherwise.</summary>
    public LlmProviderResponse? Response { get; set; }

    public string Message { get; set; } = string.Empty;
}

public class BatchResultsResult
{
    public bool Succeeded { get; set; }
    public List<BatchItemResult> Items { get; set; } = new();
    public LlmFailureKind FailureKind { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Failed because the batch or its result files are gone (HTTP 404, for example results past the provider's
    /// retention window). Permanent: retrying never brings them back.
    /// </summary>
    public bool NotFound { get; set; }
}
