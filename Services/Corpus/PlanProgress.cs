namespace ReelDiscovery.Services.Corpus;

public enum PlanStage
{
    Allocating,
    PlanningRelevant,
    PlanningOther,
    WritingProtocol,
    Validating
}

public class PlanProgress
{
    public PlanStage Stage { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Units done over the whole plan run (relevant chunks + non-relevant batches + 2) for the progress bar.
    /// </summary>
    public int Done { get; set; }

    public int Total { get; set; }

    /// <summary>
    /// Units done within the current stage, for "6 of 17" text.
    /// </summary>
    public int StageDone { get; set; }

    public int StageTotal { get; set; }
}

/// <summary>
/// Outcome of one batched LLM pass (relevant-thread filling, non-relevant filling).
/// </summary>
public class FillResult
{
    public int TotalChunks { get; set; }
    public int RefusedChunks { get; set; }
    public int FailedChunks { get; set; }
    public string RefusalMessage { get; set; } = string.Empty;

    /// <summary>
    /// First Auth/SpendLimit message; non-empty means the pass stopped.
    /// </summary>
    public string FatalError { get; set; } = string.Empty;

    public bool IsFatal => FatalError.Length > 0;
    public List<string> Notes { get; set; } = new();
}
