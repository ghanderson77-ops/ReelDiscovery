namespace ReelDiscovery.Services.Ai;

public enum LlmOutcome
{
    Succeeded,
    Refused,
    Truncated,
    Invalid,
    Failed
}

public enum LlmFailureKind
{
    None,
    Auth,
    SpendLimit,
    RateLimited,
    Transient,
    BadRequest,
    Network,
    Unknown
}

public class LlmResult
{
    public LlmOutcome Outcome { get; set; }
    public LlmFailureKind FailureKind { get; set; }
    public string? RawText { get; set; }

    /// <summary>
    /// Refusal text or error message. Never contains an API key.
    /// </summary>
    public string? Message { get; set; }

    public string OperationName { get; set; } = string.Empty;
    public LlmProviderKind Provider { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public int Attempts { get; set; }

    /// <summary>All input tokens billed (uncached + cache write + cache read), summed over every billed attempt of this call.</summary>
    public int InputTokens { get; set; }

    /// <summary>Output tokens summed over every billed attempt of this call.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Cache-write input tokens summed over every billed attempt (included in InputTokens).</summary>
    public int CacheWriteTokens { get; set; }

    /// <summary>Cache-read input tokens summed over every billed attempt (included in InputTokens).</summary>
    public int CacheReadTokens { get; set; }

    /// <summary>Cost of every billed attempt, priced through AIModelConfig.CalculateCost.</summary>
    public CostBreakdown Cost { get; set; } = new();

    public decimal CostUsd => Cost.TotalUsd;

    /// <summary>
    /// True when the failure cannot be fixed by retrying (bad key or exhausted spend limit);
    /// the whole run should stop.
    /// </summary>
    public bool IsFatal => FailureKind is LlmFailureKind.Auth or LlmFailureKind.SpendLimit;
}

public class LlmResult<T> : LlmResult where T : class
{
    public T? Value { get; set; }

    public T GetValueOrThrow()
    {
        if (Outcome == LlmOutcome.Succeeded && Value != null)
            return Value;

        throw new LlmCallException(this);
    }
}

public class LlmCallException : Exception
{
    public LlmCallException(LlmResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    public LlmResult Result { get; }

    private static string BuildMessage(LlmResult result)
    {
        var kind = result.FailureKind != LlmFailureKind.None ? $" ({result.FailureKind})" : string.Empty;
        return $"{result.OperationName}: {result.Outcome}{kind} - {result.Message}";
    }
}

public class LlmKeyTestResult
{
    public bool Succeeded { get; set; }
    public LlmFailureKind FailureKind { get; set; }

    /// <summary>
    /// User-facing status text. Never contains the key.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
