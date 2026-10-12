using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Content-hash staleness for generated discovery documents (D-27): each document stores the hashes of the guide,
/// the core document, the caption inputs and the corpus plan id it was built from, plus the journal stamps of the
/// emails it cites (09 W3).
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
    /// Per written email DocId, the Utc of its thread's ThreadDone journal line. Attachment completions are excluded on
    /// purpose (09 Pitfall 4): an attachment finishing later does not change the email a document cites, so it must not
    /// mark the document Out of date. Regenerate Thread followed by a new write moves the stamp; Start Over removes it.
    /// </summary>
    public static Dictionary<string, DateTime> EmailStamps(JournalReplay? replay)
    {
        var stamps = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var record in (replay?.LastByThread ?? new()).Values)
        {
            if (record == null || record.Type != JournalRecordType.ThreadDone)
                continue;
            foreach (var message in record.Messages ?? new())
            {
                if (message != null && !string.IsNullOrEmpty(message.DocId))
                    stamps.TryAdd(message.DocId, record.Utc);
            }
        }

        return stamps;
    }

    /// <summary>
    /// Email DocIds the document cites: deposition exhibits and written-discovery requests, blanks skipped.
    /// </summary>
    public static IReadOnlyCollection<string> CitedDocIds(DiscoveryDocument document)
    {
        var exhibitIds = (document.Deposition?.Exhibits ?? new()).Where(e => e != null).Select(e => e.DocId);
        var requestIds = (document.Written?.Requests ?? new()).Where(r => r != null).Select(r => r.DocId);

        return exhibitIds.Concat(requestIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// True when any document in the set cites at least one email.
    /// </summary>
    public static bool AnyCitesEmails(DiscoveryDocumentSet? set) =>
        (set?.Documents ?? new()).Any(d => d != null && CitedDocIds(d).Count > 0);

    /// <summary>
    /// Records the current sources on the document; the plan id is kept only when a usable corpus grounded it, and
    /// exhibit stamps only for cited emails the corpus reports as written.
    /// </summary>
    public static void Stamp(DiscoveryDocument document, CaseDraft draft, DiscoveryCorpus? corpus)
    {
        document.GuideHash = draft.Guide?.ComputeHash() ?? string.Empty;
        document.CoreHash = draft.CoreDocument == null ? string.Empty : GuideStaleness.CoreDocumentHash(draft.CoreDocument);
        document.CaptionHash = draft.CoreDocument == null ? string.Empty : CaptionHash(draft.CoreDocument);
        document.CorpusPlanId = corpus is { Usable: true } ? corpus.PlanId : null;

        document.ExhibitStamps = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        if (corpus?.WrittenStamps != null)
        {
            foreach (var id in CitedDocIds(document))
            {
                if (corpus.WrittenStamps.TryGetValue(id, out var stamp))
                    document.ExhibitStamps[id] = stamp;
            }
        }
    }

    /// <summary>
    /// First source that changed since the document was generated, in the order Guide, Core, Caption, CorpusPlan,
    /// Exhibits; None when current or when the document has no content. <paramref name="currentStamps"/> are the
    /// written-email stamps now (<see cref="EmailStamps"/>); null = unknown and skips the exhibit check. A cited email
    /// missing from them is Exhibits; a changed stamp is Exhibits only when the document recorded one (legacy documents
    /// get the existence check only).
    /// </summary>
    public static StaleSource Check(DiscoveryDocument document, CaseDraft draft, IReadOnlyDictionary<string, DateTime>? currentStamps = null)
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

        if (currentStamps != null)
        {
            foreach (var id in CitedDocIds(document))
            {
                if (!currentStamps.TryGetValue(id, out var now))
                    return StaleSource.Exhibits;
                if (document.ExhibitStamps != null && document.ExhibitStamps.TryGetValue(id, out var then) && then != now)
                    return StaleSource.Exhibits;
            }
        }

        return StaleSource.None;
    }
}
