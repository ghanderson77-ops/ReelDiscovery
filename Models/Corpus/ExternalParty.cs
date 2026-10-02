namespace ReelDiscovery.Models.Corpus;

public enum ExternalKind
{
    Newsletter,
    Vendor,
    Spam,
    Personal
}

/// <summary>
/// Outside sender or recipient of non-relevant mail who is not in the case cast.
/// </summary>
public class ExternalParty
{
    // "EXT-001", formatted locally by the planner
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public ExternalKind Kind { get; set; }
}
