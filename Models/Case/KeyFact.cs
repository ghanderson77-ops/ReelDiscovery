namespace ReelDiscovery.Models.Case;

public enum FactSignificance
{
    Hot,
    Key,
    Supporting
}

public class KeyFact
{
    /// <summary>
    /// Stable id in the form F-001; allocated by code (IdAllocator), never by the model.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    // yyyy-MM-dd string (empty = unknown); parse only via CaseDraft.TryParseDate
    public string Date { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> IssueIds { get; set; } = new();
    public List<string> ActorIds { get; set; } = new();

    /// <summary>
    /// Everyone who knew at the time; always includes ActorIds (Phase 5 knowledge gating).
    /// </summary>
    public List<string> KnownByIds { get; set; } = new();

    public FactSignificance Significance { get; set; }

    /// <summary>
    /// Core document section id (facts-N) the fact was drawn from.
    /// </summary>
    public string SourceSectionId { get; set; } = string.Empty;

    /// <summary>
    /// Notes written by code when model-supplied names or ids could not be mapped.
    /// </summary>
    public List<string> MappingNotes { get; set; } = new();

    public bool IsUserEdited { get; set; }
    public bool NeedsReview { get; set; }
    public string ReviewReason { get; set; } = string.Empty;
}
