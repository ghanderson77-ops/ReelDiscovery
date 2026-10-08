using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Content-hash staleness for generated discovery documents (D-27): each document stores the hashes of the guide,
/// the core document, the caption inputs and the corpus plan id it was built from.
/// </summary>
public static class DiscoveryStaleness
{
    /// <summary>
    /// Hash of what the caption and signature are built from. CoreDocumentHash excludes the signature block and the
    /// jurisdiction, so this is tracked separately (research Pattern 10).
    /// </summary>
    public static string CaptionHash(CoreDocument doc) => CaseDraft.ComputeHash(JsonSerializer.Serialize(new
    {
        doc.Jurisdiction,
        doc.Counsel,
        doc.DocumentType,
        doc.MatterKind,
        doc.MemoPerspective,
        doc.Title,
        doc.FilingDate,
        Parties = (doc.Parties ?? new()).Where(p => p != null).Select(p => new { p.Id, p.Name, p.ShortName, p.Role })
    }));

    /// <summary>
    /// Records the current sources on the document; the plan id is kept only when a usable corpus grounded it.
    /// </summary>
    public static void Stamp(DiscoveryDocument document, CaseDraft draft, DiscoveryCorpus? corpus)
    {
        document.GuideHash = draft.Guide?.ComputeHash() ?? string.Empty;
        document.CoreHash = draft.CoreDocument == null ? string.Empty : GuideStaleness.CoreDocumentHash(draft.CoreDocument);
        document.CaptionHash = draft.CoreDocument == null ? string.Empty : CaptionHash(draft.CoreDocument);
        document.CorpusPlanId = corpus is { Usable: true } ? corpus.PlanId : null;
    }

    /// <summary>
    /// First source that changed since the document was generated, in the order Guide, Core, Caption, CorpusPlan;
    /// None when current or when the document has no content.
    /// </summary>
    public static StaleSource Check(DiscoveryDocument document, CaseDraft draft)
    {
        if (!document.HasContent)
            return StaleSource.None;

        var guideHash = draft.Guide?.ComputeHash() ?? string.Empty;
        if (!string.Equals(document.GuideHash, guideHash, StringComparison.Ordinal))
            return StaleSource.Guide;

        var coreHash = draft.CoreDocument == null ? string.Empty : GuideStaleness.CoreDocumentHash(draft.CoreDocument);
        if (!string.Equals(document.CoreHash, coreHash, StringComparison.Ordinal))
            return StaleSource.Core;

        var captionHash = draft.CoreDocument == null ? string.Empty : CaptionHash(draft.CoreDocument);
        if (!string.Equals(document.CaptionHash, captionHash, StringComparison.Ordinal))
            return StaleSource.Caption;

        if (document.CorpusPlanId != null && document.CorpusPlanId != draft.Corpus?.PlanId)
            return StaleSource.CorpusPlan;

        return StaleSource.None;
    }
}
