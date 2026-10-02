namespace ReelDiscovery.Models.Corpus;

/// <summary>
/// Attachment planned on a message; relevance follows the parent email (family).
/// </summary>
public class PlannedAttachment
{
    // "DM-000317.0001"
    public string DocId { get; set; } = string.Empty;

    public AttachmentType Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsInline { get; set; }
}
