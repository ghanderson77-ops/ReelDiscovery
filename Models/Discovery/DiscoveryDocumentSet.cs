using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Models.Discovery;

/// <summary>
/// Exhibit number reserved for a source document across all depositions in the case.
/// </summary>
public class ExhibitAssignment
{
    public int Number { get; set; }
    public string WitnessId { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DocId { get; set; } = string.Empty;

    // yyyy-MM-dd
    public string Date { get; set; } = string.Empty;

    public string IssueId { get; set; } = string.Empty;
}

/// <summary>
/// Root of discovery-documents.json in the case folder (D-29).
/// </summary>
public class DiscoveryDocumentSet
{
    public const int CurrentFormatVersion = 1;
    public const string FileName = "discovery-documents.json";

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid CaseId { get; set; }

    // Persisted once and never recomputed for an existing set (D-24)
    public string CaseNumber { get; set; } = string.Empty;

    public List<ExhibitAssignment> ExhibitAssignments { get; set; } = new();
    public List<DiscoveryDocument> Documents { get; set; } = new();

    // Spend on documents that were cancelled or stopped before any record existed, so Case Summary totals stay complete
    public decimal UnattributedCostUsd { get; set; }

    [JsonIgnore]
    public decimal TotalCostUsd => Documents.Sum(d => d.CostUsd) + UnattributedCostUsd;

    public DiscoveryDocument? Find(string key) =>
        Documents.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Replaces the document with the same Key, or adds it.
    /// </summary>
    public void Upsert(DiscoveryDocument doc)
    {
        var index = Documents.FindIndex(d => string.Equals(d.Key, doc.Key, StringComparison.Ordinal));
        if (index >= 0)
            Documents[index] = doc;
        else
            Documents.Add(doc);
    }

    /// <summary>
    /// Repairs a set loaded from the untrusted sidecar; never throws.
    /// </summary>
    public void Normalize()
    {
        CaseNumber ??= string.Empty;
        UnattributedCostUsd = Math.Max(0m, UnattributedCostUsd);

        ExhibitAssignments = CaseGuide.CleanList(ExhibitAssignments);
        foreach (var assignment in ExhibitAssignments)
        {
            assignment.Number = Math.Max(0, assignment.Number);
            assignment.WitnessId ??= string.Empty;
            assignment.SourceKey ??= string.Empty;
            assignment.Description ??= string.Empty;
            assignment.DocId ??= string.Empty;
            assignment.Date ??= string.Empty;
            assignment.IssueId ??= string.Empty;
        }

        Documents = CaseGuide.CleanList(Documents);
        foreach (var document in Documents)
            document.Normalize();
    }
}
