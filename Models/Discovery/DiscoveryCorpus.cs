using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Models.Discovery;

/// <summary>
/// A generated email the discovery documents may cite as an exhibit.
/// </summary>
public class ExhibitCandidate
{
    public string DocId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    // yyyy-MM-dd
    public string Date { get; set; } = string.Empty;

    public List<string> ParticipantIds { get; set; } = new();
    public bool IsHot { get; set; }
    public List<string> FactIds { get; set; } = new();
}

/// <summary>
/// Snapshot of the generated corpus used to ground discovery documents; Usable is false when no corpus can be cited.
/// </summary>
public class DiscoveryCorpus
{
    public bool Usable { get; set; }
    public Guid PlanId { get; set; }

    // Why the corpus is not usable (shown to the user); empty when usable
    public string Reason { get; set; } = string.Empty;

    public List<ExhibitCandidate> Candidates { get; set; } = new();
    public List<LedgerEntry> Ledger { get; set; } = new();

    public static DiscoveryCorpus None(string reason = "") => new() { Reason = reason ?? string.Empty };
}
