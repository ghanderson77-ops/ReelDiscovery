namespace ReelDiscovery.Models.Corpus;

/// <summary>
/// LLM-written prose for the review protocol; the structure comes from the case guide (D-34).
/// </summary>
public class ReviewProtocolProse
{
    public string Purpose { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string HotGuidance { get; set; } = string.Empty;
    public string PrivilegeGuidance { get; set; } = string.Empty;
    public List<IssueProse> Issues { get; set; } = new();

    /// <summary>
    /// True when the prose call failed and code templates were used.
    /// </summary>
    public bool IsFallback { get; set; }
}

public class IssueProse
{
    public string IssueId { get; set; } = string.Empty;
    public List<string> RelevantExamples { get; set; } = new();
    public List<string> NotRelevantExamples { get; set; } = new();
}
