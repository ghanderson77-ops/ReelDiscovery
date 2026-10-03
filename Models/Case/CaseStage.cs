namespace ReelDiscovery.Models.Case;

/// <summary>
/// Furthest wizard step the case has reached; mapped to a step type (never an index) when a case is reopened.
/// Stored by name in case.json, so inserting values shifts ordinals safely (D-04).
/// </summary>
public enum CaseStage
{
    Described,
    CoreDocument,
    CaseGuide,
    CorpusSettings,
    ReviewPlan,
    Generation,
    Summary
}
