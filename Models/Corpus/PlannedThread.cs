namespace ReelDiscovery.Models.Corpus;

public enum ThreadRelevance
{
    Relevant,
    NotRelevant
}

public enum NonRelevantCategory
{
    None,
    DailyBusiness,
    Ancillary,
    NewsletterSpamPersonal,
    Decoy
}

/// <summary>
/// One planned email thread; relevance, links and participants are code-owned, the LLM writes the text fields.
/// </summary>
public class PlannedThread
{
    /// <summary>
    /// Stable id in the form T-0001; formatted locally by the planner (PlanNumbering), never by the model and never via IdAllocator.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public ThreadRelevance Relevance { get; set; }
    public NonRelevantCategory Category { get; set; }
    public bool IsPrivileged { get; set; }
    public List<string> IssueIds { get; set; } = new();
    public List<string> FactIds { get; set; } = new();
    public string Subject { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;

    // Decoys only: why the thread looks relevant but is not, and terms it must avoid
    public string DecoyAngle { get; set; } = string.Empty;
    public List<string> MustNotMention { get; set; } = new();

    public List<string> LinkedThreadIds { get; set; } = new();
    public string OwnerCustodianId { get; set; } = string.Empty;

    /// <summary>
    /// True when the model did not return usable content and code templates filled the thread (D-19).
    /// </summary>
    public bool UsesFallbackContent { get; set; }

    public List<PlannedMessage> Messages { get; set; } = new();
}
