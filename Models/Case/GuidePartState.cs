namespace ReelDiscovery.Models.Case;

public enum GuidePart
{
    Issues,
    Cast,
    Facts,
    Values
}

public enum GuidePartStatus
{
    Pending,
    Generating,
    Ready,
    Failed
}

/// <summary>
/// Generation status of one case guide part (issues, issue details, cast, facts, values).
/// </summary>
public class GuidePartState
{
    public GuidePartStatus Status { get; set; } = GuidePartStatus.Pending;
    public string FailureMessage { get; set; } = string.Empty;

    /// <summary>
    /// GuideStaleness basis hash stamped when the part was generated; empty = never stamped, never reported stale.
    /// </summary>
    public string BasisHash { get; set; } = string.Empty;

    /// <summary>
    /// Per-upstream hashes at stamp time (core, issues, cast, facts) so messages can name what changed.
    /// </summary>
    public Dictionary<string, string> BasisComponents { get; set; } = new();

    public DateTime? GeneratedUtc { get; set; }

    /// <summary>
    /// Units already written in an unfinished run (fact section ids, organization ids for background staff) so Retry resumes instead of restarting.
    /// </summary>
    public List<string> CompletedUnits { get; set; } = new();
}
