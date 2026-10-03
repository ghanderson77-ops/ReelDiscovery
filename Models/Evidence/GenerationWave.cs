namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// A date-ordered batch of relevant threads rendered together; later waves see the ledger of earlier ones (D-11).
/// </summary>
public class GenerationWave
{
    // 1-based
    public int Index { get; set; }

    // Sender-local first-message dates of the wave's threads
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }

    public List<string> ThreadIds { get; set; } = new();
    public int MessageCount { get; set; }
}
