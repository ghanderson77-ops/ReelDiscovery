using System.Text.Json.Serialization;

namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// One attachment part of an EML; bytes come from the builders or are harvested from an earlier EML copy (D-26).
/// </summary>
public class ComposedAttachment
{
    public string DocId { get; set; } = string.Empty;
    public AttachmentType Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;

    // Kept in memory only; never written to the journal or case.json
    [JsonIgnore]
    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    // Inline images are referenced from the HTML body by ContentId (cid:)
    public bool IsInline { get; set; }

    public string ContentId { get; set; } = string.Empty;
}
