namespace ReelDiscovery.Models.Evidence;

public class RenderedMessage
{
    public int Seq { get; set; }
    public string Body { get; set; } = string.Empty;
    public string SignOff { get; set; } = string.Empty;

    // People the body names (CH-xxx / EXT-xxx), used by knowledge checks
    public List<string> PeopleNamed { get; set; } = new();
}

/// <summary>
/// Repaired model output for one thread; text only, structure comes from the plan (D-02).
/// </summary>
public class RenderedThread
{
    public string ThreadId { get; set; } = string.Empty;
    public List<RenderedMessage> Messages { get; set; } = new();
    public List<LedgerDelta> NewDetails { get; set; } = new();

    // What repair changed in the raw model output
    public List<string> RepairNotes { get; set; } = new();
}
