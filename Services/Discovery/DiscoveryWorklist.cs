using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// One document the current selections would write: its stable sidecar key, file name, plan and pricing inputs.
/// </summary>
public class DiscoveryWorkItem
{
    // Stable sidecar key: DEP-{witnessId}, RFP, ROG, RFA (D-29)
    public string Key { get; set; } = string.Empty;
    public DiscoveryKind Kind { get; set; }
    public DiscoveryVariant Variant { get; set; }
    public string WitnessId { get; set; } = string.Empty;
    public string WitnessName { get; set; } = string.Empty;

    // Plain fallback title (plan title); the UI formats its own short title from the raw parts
    public string ShortTitle { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    // Deposition date; null for written sets
    public DateOnly? Date { get; set; }

    public DepositionPlan? Deposition { get; set; }
    public WrittenSetPlan? Written { get; set; }

    // Pricing inputs (GenerationCostEstimator.PriceDiscovery*)
    public int ChunkCount { get; set; }
    public int CallCount { get; set; }
    public int TestimonyPages { get; set; }
    public int RequestCount { get; set; }
}

/// <summary>
/// Composes the deposition and written-discovery planners into the ordered list of documents to write (D-10, D-29):
/// depositions in deposition-date order, then RFP, ROG, RFA. Pure; never throws.
/// </summary>
public static class DiscoveryWorklist
{
    public const string DepositionKeyPrefix = "DEP-";

    private static readonly DiscoveryKind[] WrittenKinds =
    {
        DiscoveryKind.RequestsForProduction, DiscoveryKind.Interrogatories, DiscoveryKind.RequestsForAdmission
    };

    public static string DepositionKey(string witnessId) => DepositionKeyPrefix + (witnessId ?? string.Empty).Trim();

    public static string WrittenKey(DiscoveryKind kind) => kind switch
    {
        DiscoveryKind.RequestsForProduction => "RFP",
        DiscoveryKind.Interrogatories => "ROG",
        DiscoveryKind.RequestsForAdmission => "RFA",
        _ => string.Empty
    };

    /// <summary>
    /// Work items for the current selections; empty without a guide or a core document.
    /// </summary>
    public static List<DiscoveryWorkItem> Build(CaseDraft draft, DiscoverySettings settings, DiscoveryCorpus? corpus,
        DiscoveryDocumentSet? set)
    {
        var items = new List<DiscoveryWorkItem>();
        if (draft?.Guide == null || draft.CoreDocument == null)
            return items;

        try
        {
            settings ??= new DiscoverySettings();
            var plans = DepositionPlanner.Plan(draft, settings.WitnessIds ?? new List<string>(), settings.PagesPerExcerpt,
                corpus, set?.ExhibitAssignments);
            items.AddRange(plans.Select(p => FromDeposition(draft, p, set)));

            foreach (var kind in WrittenKinds)
            {
                var plan = WrittenDiscoveryPlanner.Plan(draft, kind, settings, corpus);
                if (plan != null)
                    items.Add(FromWritten(draft, plan, set));
            }
        }
        catch (Exception)
        {
            // Planners are pure but read an untrusted, hand-editable guide; a malformed graph yields no work rather than a crash
            items.Clear();
        }
        return items;
    }

    /// <summary>
    /// The single work item for Regenerate... of an existing document: a deposition keeps its witness even when it is no
    /// longer checked; a written set uses the current settings (UI-SPEC S1 interaction rules). Null when it cannot be planned.
    /// </summary>
    public static DiscoveryWorkItem? ForDocument(CaseDraft draft, DiscoveryDocument document, DiscoverySettings settings,
        DiscoveryCorpus? corpus, DiscoveryDocumentSet set)
    {
        if (draft?.Guide == null || draft.CoreDocument == null || document == null)
            return null;

        try
        {
            settings ??= new DiscoverySettings();
            if (document.Kind == DiscoveryKind.Deposition)
            {
                if (string.IsNullOrWhiteSpace(document.WitnessId))
                    return null;
                var plan = DepositionPlanner.Plan(draft, new[] { document.WitnessId.Trim() }, settings.PagesPerExcerpt,
                    corpus, set?.ExhibitAssignments).FirstOrDefault();
                return plan == null ? null : FromDeposition(draft, plan, set);
            }

            // The set's checkbox may be cleared since it was written; regenerating it still uses the current count and parties
            var copy = Copy(settings);
            switch (document.Kind)
            {
                case DiscoveryKind.RequestsForProduction:
                    copy.RfpEnabled = true;
                    break;
                case DiscoveryKind.Interrogatories:
                    copy.RogEnabled = true;
                    break;
                case DiscoveryKind.RequestsForAdmission:
                    copy.RfaEnabled = true;
                    break;
            }
            var written = WrittenDiscoveryPlanner.Plan(draft, document.Kind, copy, corpus);
            return written == null ? null : FromWritten(draft, written, set);
        }
        catch (Exception)
        {
            // Same as Build: an unplannable hand-edited guide means no work item, never a crash
            return null;
        }
    }

    /// <summary>
    /// Case part of every discovery file name: the case folder's name, else the default folder name.
    /// </summary>
    public static string CaseFolderPart(CaseDraft draft)
    {
        var folder = Path.GetFileName((draft.FolderPath ?? string.Empty).TrimEnd('\\', '/'));
        return string.IsNullOrWhiteSpace(folder) ? CaseFolderNaming.DefaultFolderName(draft) : folder;
    }

    /// <summary>
    /// Document part of a deposition file name (D-29), e.g. "Deposition of Maria Delgado (Excerpt)".
    /// </summary>
    public static string DepositionDocumentPart(DiscoveryVariant variant, string fullName)
    {
        var name = (fullName ?? string.Empty).Trim();
        var prefix = variant switch
        {
            DiscoveryVariant.MemoRegulatory => "Examination Under Oath of",
            DiscoveryVariant.MemoCriminal or DiscoveryVariant.MemoCivil => "Interview of",
            _ => "Deposition of"
        };
        return $"{prefix} {name} (Excerpt)";
    }

    private static DiscoveryWorkItem FromDeposition(CaseDraft draft, DepositionPlan plan, DiscoveryDocumentSet? set)
    {
        var variant = DiscoveryVariants.Of(draft.CoreDocument);
        var key = DepositionKey(plan.Witness.Id);
        return new DiscoveryWorkItem
        {
            Key = key,
            Kind = DiscoveryKind.Deposition,
            Variant = variant,
            WitnessId = plan.Witness.Id,
            WitnessName = plan.Witness.FullName,
            ShortTitle = plan.DocumentTitle,
            FileName = FileName(draft, set, key, DepositionDocumentPart(variant, plan.Witness.FullName)),
            Date = plan.Schedule.Date,
            Deposition = plan,
            ChunkCount = plan.Chunks.Count,
            TestimonyPages = plan.TestimonyPages
        };
    }

    private static DiscoveryWorkItem FromWritten(CaseDraft draft, WrittenSetPlan plan, DiscoveryDocumentSet? set)
    {
        var requests = plan.Slots.Count;
        return new DiscoveryWorkItem
        {
            Key = plan.Key,
            Kind = plan.Kind,
            Variant = plan.Variant,
            ShortTitle = plan.Title,
            FileName = FileName(draft, set, plan.Key, plan.FileDocumentPart),
            Written = plan,
            RequestCount = requests,

            // At most MaxSlotsPerCall requests per model call
            CallCount = (requests + WrittenDiscoveryPlanner.MaxSlotsPerCall - 1) / WrittenDiscoveryPlanner.MaxSlotsPerCall
        };
    }

    // A saved name is kept so a regenerate overwrites the same file; an unsafe sidecar name is replaced (T-08-02)
    private static string FileName(CaseDraft draft, DiscoveryDocumentSet? set, string key, string documentPart)
    {
        var saved = set?.Find(key)?.FileName;
        return DiscoveryStore.IsDiscoveryFileName(saved)
            ? saved!
            : DiscoveryStore.FileNameFor(CaseFolderPart(draft), documentPart);
    }

    private static DiscoverySettings Copy(DiscoverySettings s) => new()
    {
        WitnessIds = new List<string>(s.WitnessIds ?? new List<string>()),
        PagesPerExcerpt = s.PagesPerExcerpt,
        RfpEnabled = s.RfpEnabled,
        RfpCount = s.RfpCount,
        RogEnabled = s.RogEnabled,
        RogCount = s.RogCount,
        RfaEnabled = s.RfaEnabled,
        RfaCount = s.RfaCount,
        PropoundingPartyId = s.PropoundingPartyId,
        RespondingPartyId = s.RespondingPartyId,
        UserTouched = s.UserTouched
    };
}
