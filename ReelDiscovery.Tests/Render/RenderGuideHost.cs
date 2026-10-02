using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Minimal IGuideHost for rendering tab panels in isolation: holds a draft and its findings,
/// and records every action call instead of running it.
/// </summary>
internal sealed class RenderGuideHost : IGuideHost
{
    public RenderGuideHost(CaseDraft draft)
    {
        Draft = draft;
        Findings = CaseGuideValidator.Validate(draft);
    }

    public CaseDraft Draft { get; }
    public IReadOnlyList<GuideFinding> Findings { get; set; }
    public bool IsBusy { get; set; }
    public string WritingEntityId { get; set; } = string.Empty;
    public List<string> Calls { get; } = new();

    public void NotifyEdited(GuidePart part)
    {
        Calls.Add($"NotifyEdited:{part}");
        Findings = CaseGuideValidator.Validate(Draft);
    }

    public void SetStatus(string text, Color color) => Calls.Add($"SetStatus:{text}");

    public Task RegenerateEntryAsync(GuidePart part, string entityId, string guidance)
    {
        Calls.Add($"RegenerateEntry:{part}:{entityId}:{guidance}");
        return Task.CompletedTask;
    }

    public Task RegenerateTabAsync(GuidePart part)
    {
        Calls.Add($"RegenerateTab:{part}");
        return Task.CompletedTask;
    }

    public Task<GuideEntriesResult?> RegenerateEntriesAsync(GuidePart part, IReadOnlyList<string> entityIds)
    {
        Calls.Add($"RegenerateEntries:{part}:{string.Join(",", entityIds)}");
        return Task.FromResult<GuideEntriesResult?>(null);
    }

    public Task RetryIssueDetailsAsync()
    {
        Calls.Add("RetryIssueDetails");
        return Task.CompletedTask;
    }

    public void ShowTab(GuidePart part, string? entityId = null) => Calls.Add($"ShowTab:{part}:{entityId}");
}
