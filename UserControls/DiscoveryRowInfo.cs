using ReelDiscovery.Models.Discovery;

namespace ReelDiscovery.UserControls;

/// <summary>
/// One row of the Discovery Documents grid (08-UI-SPEC S1 Card 3) and of the Case Summary Discovery row tooltip (S4).
/// Pure view model: the step model fills it, DiscoveryUiText turns it into copy.
/// </summary>
public class DiscoveryRowInfo
{
    // Stable sidecar key: DEP-{witnessId}, RFP, ROG, RFA
    public string Key = "";
    public DiscoveryKind Kind;
    public string WitnessId = "";
    public DiscoveryRowState State;

    // DiscoveryUiText.ShortTitle(...), e.g. "Deposition of {name}" or "Requests for Production (25)"
    public string ShortTitle = "";

    // Null until a version is saved
    public DateTime? GeneratedUtc;

    public int TestimonyPages;
    public int RequestCount;
    public List<DiscoveryViolation> Warnings = new();

    // Plain-words failure reason stored by the generator; empty unless State is Failed
    public string FailureReason = "";

    // A failed regenerate left the earlier version in place (UI-SPEC U54)
    public bool PreviousKept;

    public string FileName = "";
    public StaleSource Stale;

    // Writing only: the part being written (1-based) and the number of parts
    public int Part;
    public int PartCount;

    // Planner fallbacks shown in the row tooltip (full sentences)
    public List<string> PlannerNotes = new();

    public bool FileExists;
    public decimal CostUsd;
    public decimal EstimateUsd;
}
