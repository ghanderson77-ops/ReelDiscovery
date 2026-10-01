namespace ReelDiscovery.Models.Case;

public enum SectionKind
{
    NatureOfAction,
    Parties,
    JurisdictionVenue,
    DiscoveryControlPlan,
    ClaimForRelief,
    FactualAllegations,
    Count,
    ConditionsPrecedent,
    PrayerForRelief,
    ExecutiveSummary,
    Background,
    ScopeMethodology,
    KeyIndividuals,
    Finding,
    LegalAnalysis,
    Recommendations
}

public enum SectionStatus
{
    Pending,
    Writing,
    Ready,
    Failed
}

// Exactly one level: no subsections (strict schemas reject recursion; Pitfall 14)
public class CoreDocumentSection
{
    /// <summary>
    /// Stable id (e.g. facts-2, count-1); kept when the section is regenerated so Phase 3 can trace facts to sections.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public SectionKind Kind { get; set; }
    public string Heading { get; set; } = string.Empty;
    public string CountId { get; set; } = string.Empty;

    /// <summary>
    /// Paragraph text without numbers; numbering is computed by CoreDocumentAssembler.
    /// </summary>
    public List<string> Paragraphs { get; set; } = new();

    public SectionStatus Status { get; set; } = SectionStatus.Pending;
    public string FailureMessage { get; set; } = string.Empty;
    public bool IsUserEdited { get; set; }

    public override string ToString() => Heading;
}
