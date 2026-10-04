namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// A new detail one rendered thread introduced (D-07); stored in the journal and turned into LedgerEntry by LedgerMerger.
/// </summary>
public class LedgerDelta
{
    // Seq of the message in the thread that introduced the detail
    public int Seq { get; set; }
    public LedgerKind Kind { get; set; }
    public string About { get; set; } = string.Empty;
    public string Statement { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public List<string> FactIds { get; set; } = new();

    // D-18: details an attachment introduced; LedgerMerger gives them ids derived from this DocId so repairs never renumber entries (review concern 7)
    public bool FromAttachment { get; set; }
    public string AttachmentDocId { get; set; } = string.Empty;
}
