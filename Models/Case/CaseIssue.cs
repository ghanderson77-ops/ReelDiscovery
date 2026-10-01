using System.Text.Json.Serialization;

namespace ReelDiscovery.Models.Case;

/// <summary>
/// A case issue mirrored from one count/finding of the core document, plus the detail the corpus generator needs.
/// </summary>
public class CaseIssue
{
    /// <summary>
    /// Stable id in the form ISS-01; allocated by code (IdAllocator), never by the model.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The core document count (count-N) this issue mirrors.
    /// </summary>
    public string CountId { get; set; } = string.Empty;

    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string LegalBasis { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> AgainstPartyIds { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public List<string> Elements { get; set; } = new();

    /// <summary>
    /// What makes a document relevant to this issue; feeds the answer key.
    /// </summary>
    public string RelevanceCriteria { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasDetails => Description.Length > 0;
}
