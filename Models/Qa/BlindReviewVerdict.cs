namespace ReelDiscovery.Models.Qa;

/// <summary>
/// One blind-review decision on a sampled email, appended to qa-blind-review.jsonl.
/// </summary>
public class BlindReviewVerdict
{
    public string DocId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public bool KeyRelevant { get; set; }
    public bool ReviewerRelevant { get; set; }
    public List<string> IssueCodes { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public DateTime Utc { get; set; }
    public decimal CostUsd { get; set; }
}

/// <summary>
/// Starts a blind-review pass in qa-blind-review.jsonl; verdicts after the latest header belong to it.
/// </summary>
public class BlindReviewHeader
{
    public Guid PlanId { get; set; }
    public string ReviewerProvider { get; set; } = string.Empty;
    public string ReviewerModel { get; set; } = string.Empty;

    /// <summary>True when no other provider had a key, so the reviewer shares the generator's provider.</summary>
    public bool SameProviderFallback { get; set; }

    public int SampleSize { get; set; }
    public DateTime StartedUtc { get; set; }
}
