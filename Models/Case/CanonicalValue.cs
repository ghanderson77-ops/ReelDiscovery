namespace ReelDiscovery.Models.Case;

public enum ValueKind
{
    Amount,
    InvoiceNumber,
    ChangeOrderNumber,
    ContractNumber,
    PurchaseOrder,
    DocumentTitle,
    ProjectName,
    AccountNumber,
    Address,
    Other
}

/// <summary>
/// An amount, number or name that must read identically in every document that mentions it.
/// </summary>
public class CanonicalValue
{
    /// <summary>
    /// Stable id in the form V-001; allocated by code (IdAllocator), never by the model.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public ValueKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> FactIds { get; set; } = new();

    /// <summary>
    /// Notes written by code when model-supplied fact ids could not be mapped.
    /// </summary>
    public List<string> MappingNotes { get; set; } = new();

    public bool IsUserEdited { get; set; }
    public bool NeedsReview { get; set; }
    public string ReviewReason { get; set; } = string.Empty;
}
