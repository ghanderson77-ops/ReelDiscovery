using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Contract between StepCaseGuide (the host) and its tab panels. Panels read the draft and findings,
/// report edits, and ask the host to run generation calls; the host owns status, autosave and checks.
/// </summary>
public interface IGuideHost
{
    CaseDraft Draft { get; }
    IReadOnlyList<GuideFinding> Findings { get; }
    bool IsBusy { get; }

    /// <summary>
    /// Id of the entry being written or regenerated; "" when none.
    /// </summary>
    string WritingEntityId { get; }

    /// <summary>
    /// Revalidates, autosaves and refreshes captions, banner, checks and dots after a panel changed the draft.
    /// </summary>
    void NotifyEdited(GuidePart part);

    void SetStatus(string text, Color color);

    /// <summary>
    /// Runs the gateway call for one entry and reports progress in the status strip.
    /// </summary>
    Task RegenerateEntryAsync(GuidePart part, string entityId, string guidance);

    /// <summary>
    /// Shows the Regenerate dialog (D1) for the part and runs it when confirmed.
    /// </summary>
    Task RegenerateTabAsync(GuidePart part);

    Task RetryIssueDetailsAsync();

    void ShowTab(GuidePart part, string? entityId = null);
}
