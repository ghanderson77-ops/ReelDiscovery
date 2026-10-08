using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Deterministic case number for the discovery documents (D-24). The number is persisted once in the discovery sidecar
/// so a later derivation tweak never renumbers existing documents; the core document keeps "Case No. ____".
/// </summary>
public static class CaseNumber
{
    /// <summary>
    /// Federal "{division}:{yy}-cv-{nnnnn}", state "{yyyy}-CV-{nnnnnn}", memos "{yyyy}-{nnnn}". Empty for a null draft.
    /// </summary>
    public static string For(CaseDraft draft)
    {
        if (draft == null)
            return string.Empty;

        // SHA-256 of the case id: stable across processes and machines
        var h = RenderAssignments.StableRank(draft.CaseId.ToString("N"));
        var year = Year(draft);
        var doc = draft.CoreDocument;

        if (DiscoveryVariants.IsMemo(DiscoveryVariants.Of(doc)))
            return string.Create(CultureInfo.InvariantCulture, $"{year}-{1000 + h % 9000:0000}");

        if (doc?.Jurisdiction?.CourtSystem == CourtSystem.State)
            return string.Create(CultureInfo.InvariantCulture, $"{year}-CV-{h % 1_000_000:000000}");

        // Division prefix 1-4, two-digit year, "cv", five digits
        return string.Create(CultureInfo.InvariantCulture, $"{1 + h % 4}:{year % 100:00}-cv-{10000 + h % 90000:00000}");
    }

    /// <summary>
    /// Caption label in front of the number; Petition matches CoreDocumentAssembler.BuildCaption ("Cause No.").
    /// </summary>
    public static string Label(CoreDocument doc)
    {
        if (doc == null)
            return "Case No.";

        return DiscoveryVariants.Of(doc) switch
        {
            DiscoveryVariant.MemoCriminal => "Grand Jury Matter No.",
            DiscoveryVariant.MemoRegulatory => "CID No.",
            DiscoveryVariant.MemoCivil => "Matter No.",
            _ => doc.DocumentType == CoreDocumentType.Petition ? "Cause No." : "Case No."
        };
    }

    private static int Year(CaseDraft draft)
    {
        var doc = draft.CoreDocument;
        if (CaseDraft.TryParseDate(doc?.FilingDate, out var filed))
            return filed.Year;
        if (CaseDraft.TryParseDate(doc?.TimelineEnd, out var end))
            return end.Year;
        return draft.CreatedUtc.Year;
    }
}
