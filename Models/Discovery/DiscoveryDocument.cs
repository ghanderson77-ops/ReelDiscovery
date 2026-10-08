using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Models.Discovery;

public enum DiscoveryKind
{
    Deposition,
    RequestsForProduction,
    Interrogatories,
    RequestsForAdmission
}

public enum DiscoveryStatus
{
    Failed,
    Generated,
    GeneratedWithWarnings
}

// Row states shown on the discovery card (UI-SPEC S1 Card 3)
public enum DiscoveryRowState
{
    NotGenerated,
    Writing,
    Generated,
    Warnings,
    Failed,
    OutOfDate,
    NotSaved,
    FileMissing
}

// Civil discovery or the investigation-memo equivalents (D-25)
public enum DiscoveryVariant
{
    Civil,
    MemoCriminal,
    MemoRegulatory,
    MemoCivil
}

// First case source that changed since a document was generated (D-27)
public enum StaleSource
{
    None,
    Guide,
    Core,
    Caption,
    CorpusPlan
}

public static class DiscoveryVariants
{
    /// <summary>
    /// Civil for complaints, petitions and a missing core document; the memo variant follows the matter kind (D-25).
    /// </summary>
    public static DiscoveryVariant Of(CoreDocument? doc)
    {
        if (doc == null || doc.DocumentType != CoreDocumentType.InvestigationMemo)
            return DiscoveryVariant.Civil;

        return doc.MatterKind switch
        {
            MatterKind.Criminal => DiscoveryVariant.MemoCriminal,
            MatterKind.Regulatory => DiscoveryVariant.MemoRegulatory,
            _ => DiscoveryVariant.MemoCivil
        };
    }

    public static bool IsMemo(DiscoveryVariant v) => v != DiscoveryVariant.Civil;
}

/// <summary>
/// One generated discovery document and the case sources it was built from.
/// </summary>
public class DiscoveryDocument
{
    // Stable: DEP-{witnessId}, RFP, ROG, RFA
    public string Key { get; set; } = string.Empty;

    public DiscoveryKind Kind { get; set; }
    public string WitnessId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DiscoveryStatus Status { get; set; }

    // Non-empty = the last attempt failed; content of an earlier success is kept (UI-SPEC U54)
    public string FailureReason { get; set; } = string.Empty;

    public bool FileSaved { get; set; }
    public List<DiscoveryViolation> Warnings { get; set; } = new();
    public List<string> PlannerNotes { get; set; } = new();

    // Source stamp (D-27)
    public string GuideHash { get; set; } = string.Empty;
    public string CoreHash { get; set; } = string.Empty;
    public string CaptionHash { get; set; } = string.Empty;
    public Guid? CorpusPlanId { get; set; }

    public decimal CostUsd { get; set; }
    public CostBreakdown CostDetail { get; set; } = new();
    public DateTime GeneratedUtc { get; set; }
    public int TestimonyPages { get; set; }
    public int RequestCount { get; set; }

    public DepositionContent? Deposition { get; set; }
    public WrittenDiscoveryContent? Written { get; set; }

    [JsonIgnore]
    public bool HasContent => Deposition != null || Written != null;

    /// <summary>
    /// Repairs a document loaded from the untrusted sidecar; never throws.
    /// </summary>
    public void Normalize()
    {
        Key ??= string.Empty;
        WitnessId ??= string.Empty;
        Title ??= string.Empty;
        FileName ??= string.Empty;
        FailureReason ??= string.Empty;
        GuideHash ??= string.Empty;
        CoreHash ??= string.Empty;
        CaptionHash ??= string.Empty;
        CostDetail ??= new();
        CostUsd = Math.Max(0m, CostUsd);
        TestimonyPages = Math.Max(0, TestimonyPages);
        RequestCount = Math.Max(0, RequestCount);
        PlannerNotes = CaseGuide.CleanStrings(PlannerNotes);

        Warnings = CaseGuide.CleanList(Warnings);
        foreach (var warning in Warnings)
        {
            warning.Value ??= string.Empty;
            warning.Canonical ??= string.Empty;
        }

        Deposition?.Normalize();
        Written?.Normalize();
    }
}
