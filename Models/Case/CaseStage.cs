namespace ReelDiscovery.Models.Case;

/// <summary>
/// Furthest wizard step the case has reached; mapped to a step type (never an index) when a case is reopened. Phase 4 adds values.
/// </summary>
public enum CaseStage
{
    Described,
    CoreDocument,
    CaseGuide,
    Summary
}
