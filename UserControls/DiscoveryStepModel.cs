using System.Globalization;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Pure state behind the Discovery Documents step (08-UI-SPEC S1): eligibility, defaults, rows, documents to write and the
/// estimate. The step, the generator and Case Summary all read it so they agree (D-10, D-27, D-28). Never throws.
/// </summary>
public class DiscoveryStepModel
{
    private static readonly DiscoveryKind[] WrittenKinds =
    {
        DiscoveryKind.RequestsForProduction, DiscoveryKind.Interrogatories, DiscoveryKind.RequestsForAdmission
    };

    public bool HasGuide { get; set; }
    public DiscoveryVariant Variant { get; set; }
    public bool IsMemo { get; set; }
    public bool CorpusUsable { get; set; }
    public List<WitnessCandidate> Witnesses { get; set; } = new();
    public List<KindAvailability> Kinds { get; set; } = new();
    public List<CaseParty> Parties { get; set; } = new();
    public PartyChoice PartyChoice { get; set; } = new();
    public Dictionary<DiscoveryKind, CountChoice> Counts { get; set; } = new();

    // Every document the current selections would create (worklist order)
    public List<DiscoveryWorkItem> Selected { get; set; } = new();

    public List<DiscoveryRowInfo> Rows { get; set; } = new();

    // Selected documents that are Not generated or Failed: the only ones Generate Documents writes (D-11, D-27)
    public List<DiscoveryWorkItem> ToWrite { get; set; } = new();

    public decimal EstimateUsd { get; set; }
    public Dictionary<string, decimal> EstimateByKey { get; set; } = new(StringComparer.Ordinal);
    public decimal SpentUsd { get; set; }

    // True when a visible written set is checked (the parties must then differ)
    public bool AnyWrittenChecked { get; set; }

    /// <summary>
    /// Preset defaults for the case (D-12, D-15..D-19): preset pages and counts, default witnesses and default parties.
    /// </summary>
    public static DiscoverySettings Defaults(CaseDraft draft)
    {
        var settings = DiscoverySettings.ForPreset(draft?.Complexity ?? ComplexityLevel.Standard);
        if (draft == null)
            return settings;

        try
        {
            settings.WitnessIds = DepositionPlanner.DefaultWitnessIds(draft);
            var parties = WrittenDiscoveryPlanner.ResolveParties(draft.CoreDocument, null);
            settings.PropoundingPartyId = parties.Propounding?.Id ?? string.Empty;
            settings.RespondingPartyId = parties.Responding?.Id ?? string.Empty;
        }
        catch (Exception)
        {
            // A malformed hand-edited guide only loses the default witnesses and parties; the preset counts still apply
        }
        return settings;
    }

    /// <summary>
    /// Replaces the selections with the preset defaults while the user has not touched them; otherwise leaves them alone.
    /// </summary>
    public static void ApplyDefaults(CaseDraft draft)
    {
        if (draft == null)
            return;
        if (draft.Discovery == null || !draft.Discovery.UserTouched)
            draft.Discovery = Defaults(draft);
    }

    public static DiscoveryStepModel Build(CaseDraft draft, DiscoveryDocumentSet set, DiscoveryCorpus corpus,
        AIModelConfig? storyModel, string caseFolder, Func<string, bool>? fileExists = null)
    {
        var model = new DiscoveryStepModel();
        set ??= new DiscoveryDocumentSet();
        model.SpentUsd = set.TotalCostUsd;
        if (draft == null)
            return model;

        model.HasGuide = draft.Guide != null;
        model.Variant = DiscoveryVariants.Of(draft.CoreDocument);
        model.IsMemo = DiscoveryVariants.IsMemo(model.Variant);
        model.CorpusUsable = corpus?.Usable == true;
        if (!model.HasGuide)
            return model;

        try
        {
            var settings = draft.Discovery ?? Defaults(draft);
            var doc = draft.CoreDocument;

            model.Witnesses = DepositionPlanner.Eligible(draft);
            model.Kinds = WrittenKinds.Select(k => WrittenDiscoveryPlanner.Availability(doc, k)).ToList();
            model.Parties = (doc?.Parties ?? new List<CaseParty>()).Where(p => p != null).ToList();
            model.PartyChoice = WrittenDiscoveryPlanner.ResolveParties(doc, settings);
            foreach (var kind in model.Kinds.Where(k => k.Visible).Select(k => k.Kind))
            {
                var (enabled, requested) = Requested(settings, kind);
                model.Counts[kind] = WrittenDiscoveryPlanner.EffectiveCount(draft, kind, requested, corpus);
                model.AnyWrittenChecked |= enabled;
            }

            model.Selected = DiscoveryWorklist.Build(draft, settings, corpus, set);
            model.Rows = BuildRows(draft, set, model.Selected, model.Variant, caseFolder, fileExists);

            var states = model.Rows.ToDictionary(r => r.Key, r => r.State, StringComparer.Ordinal);
            model.ToWrite = model.Selected
                .Where(i => states.TryGetValue(i.Key, out var s) && s is DiscoveryRowState.NotGenerated or DiscoveryRowState.Failed)
                .ToList();

            foreach (var item in model.ToWrite)
            {
                var cost = item.Kind == DiscoveryKind.Deposition
                    ? GenerationCostEstimator.PriceDiscoveryDeposition(item.ChunkCount, item.TestimonyPages, storyModel)
                    : GenerationCostEstimator.PriceDiscoveryWrittenSet(item.CallCount, item.RequestCount, storyModel);
                model.EstimateByKey[item.Key] = cost;
                model.EstimateUsd += cost;
            }
            foreach (var row in model.Rows)
                row.EstimateUsd = model.EstimateByKey.TryGetValue(row.Key, out var c) ? c : 0m;
        }
        catch (Exception)
        {
            // The guide and sidecar are hand-editable; a malformed graph shows an empty step rather than crashing the wizard
            model.Rows.Clear();
            model.ToWrite.Clear();
            model.EstimateByKey.Clear();
            model.EstimateUsd = 0m;
        }
        return model;
    }

    /// <summary>
    /// Rows for the documents saved in the sidecar only (no planning, no corpus probe), with the same state resolution;
    /// used by the Case Summary row (Plan 08-18).
    /// </summary>
    public static List<DiscoveryRowInfo> SavedRows(CaseDraft draft, DiscoveryDocumentSet set, string caseFolder,
        Func<string, bool>? fileExists = null)
    {
        if (draft == null || set == null)
            return new List<DiscoveryRowInfo>();
        try
        {
            return BuildRows(draft, set, new List<DiscoveryWorkItem>(), DiscoveryVariants.Of(draft.CoreDocument), caseFolder,
                fileExists);
        }
        catch (Exception)
        {
            // Same as Build: a malformed sidecar yields no rows rather than a crash
            return new List<DiscoveryRowInfo>();
        }
    }

    /// <summary>
    /// The second hint sentence for a count: raised when every issue needs a request, lowered when admissions are capped
    /// at the dated key facts; empty otherwise.
    /// </summary>
    public string CountHint(DiscoveryKind kind)
    {
        if (!Counts.TryGetValue(kind, out var count))
            return "";
        var label = DiscoveryUiText.KindSentenceLabel(kind, Variant);
        if (count.Raised)
            return DiscoveryUiText.RaisedSentence(label, count.Effective);
        if (count.Lowered && count.Effective > 0)
            return DiscoveryUiText.LoweredSentence(label, count.Effective, count.Available);
        return "";
    }

    public DiscoveryUiText.EstimateCondition Condition(bool keyReady)
    {
        if (!keyReady)
            return DiscoveryUiText.EstimateCondition.NoKey;
        if (ToWrite.Count > 0)
            return DiscoveryUiText.EstimateCondition.SomeToWrite;
        return Selected.Count == 0
            ? DiscoveryUiText.EstimateCondition.NothingSelected
            : DiscoveryUiText.EstimateCondition.AllWritten;
    }

    /// <summary>
    /// Generate Documents is enabled only with something to write, valid fields, different parties (when a written set is
    /// checked) and a tested key (UI-SPEC S1 Run button).
    /// </summary>
    public bool CanGenerate(bool keyReady, bool fieldsValid) =>
        keyReady && fieldsValid && ToWrite.Count > 0 && !(AnyWrittenChecked && PartyChoice.Same);

    /// <summary>
    /// Testimony pages per excerpt, 10 to 60 (D-15); digits only, invariant culture (T-08-29).
    /// </summary>
    public static bool TryParsePages(string? text, out int pages) =>
        TryParseRange(text, DiscoverySettings.MinPages, DiscoverySettings.MaxPages, out pages);

    /// <summary>
    /// Request count for a written set: RFP 5-80, ROG 1-25, RFA 1-50 (D-17..D-19); digits only, invariant culture.
    /// </summary>
    public static bool TryParseCount(DiscoveryKind kind, string? text, out int count)
    {
        var (min, max) = kind switch
        {
            DiscoveryKind.RequestsForProduction => (DiscoverySettings.MinRfp, DiscoverySettings.MaxRfp),
            DiscoveryKind.Interrogatories => (DiscoverySettings.MinRogs, DiscoverySettings.MaxInterrogatories),
            DiscoveryKind.RequestsForAdmission => (DiscoverySettings.MinRfa, DiscoverySettings.MaxRfa),
            _ => (0, -1)
        };
        return TryParseRange(text, min, max, out count);
    }

    private static bool TryParseRange(string? text, int min, int max, out int value)
    {
        value = 0;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > 4 || !trimmed.All(c => c is >= '0' and <= '9'))
            return false;
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < min || parsed > max)
            return false;
        value = parsed;
        return true;
    }

    private static (bool Enabled, int Requested) Requested(DiscoverySettings settings, DiscoveryKind kind) => kind switch
    {
        DiscoveryKind.RequestsForProduction => (settings.RfpEnabled, settings.RfpCount),
        DiscoveryKind.Interrogatories => (settings.RogEnabled, settings.RogCount),
        _ => (settings.RfaEnabled, settings.RfaCount)
    };

    // Union of saved documents and selected-but-unwritten ones (UI-SPEC S1 Card 3): depositions by date, then RFP, ROG, RFA
    private static List<DiscoveryRowInfo> BuildRows(CaseDraft draft, DiscoveryDocumentSet set, List<DiscoveryWorkItem> selected,
        DiscoveryVariant variant, string caseFolder, Func<string, bool>? fileExists)
    {
        var byKey = selected.ToDictionary(i => i.Key, StringComparer.Ordinal);
        var saved = (set.Documents ?? new List<DiscoveryDocument>())
            .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Key))
            .GroupBy(d => d.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var depositionKeys = saved.Values.Where(d => d.Kind == DiscoveryKind.Deposition).Select(d => d.Key)
            .Concat(selected.Where(i => i.Kind == DiscoveryKind.Deposition).Select(i => i.Key))
            .Distinct(StringComparer.Ordinal)
            .Select(key => (Key: key, Date: DepositionDate(saved.GetValueOrDefault(key), byKey.GetValueOrDefault(key))))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key);

        var writtenKeys = WrittenKinds
            .Select(DiscoveryWorklist.WrittenKey)
            .Where(key => saved.ContainsKey(key) || byKey.ContainsKey(key));

        var probe = fileExists ?? File.Exists;
        return depositionKeys.Concat(writtenKeys)
            .Select(key => Row(draft, saved.GetValueOrDefault(key), byKey.GetValueOrDefault(key), key, variant, caseFolder, probe))
            .ToList();
    }

    private static DiscoveryRowInfo Row(CaseDraft draft, DiscoveryDocument? doc, DiscoveryWorkItem? item, string key,
        DiscoveryVariant variant, string caseFolder, Func<string, bool> probe)
    {
        var stale = doc == null ? StaleSource.None : DiscoveryStaleness.Check(doc, draft);

        // Paths only through DocxPath: a validated name inside Discovery\ (T-08-28)
        var exists = doc != null && !string.IsNullOrWhiteSpace(caseFolder)
            && DiscoveryStore.DocxPath(caseFolder, doc.FileName) is { } path && probe(path);

        var kind = doc?.Kind ?? item!.Kind;
        var hasContent = doc?.HasContent == true;
        var witnessId = doc?.WitnessId is { Length: > 0 } id ? id : item?.WitnessId ?? string.Empty;
        var requests = hasContent ? doc!.RequestCount : item?.RequestCount ?? doc?.RequestCount ?? 0;
        var pages = hasContent ? doc!.TestimonyPages : item?.TestimonyPages ?? doc?.TestimonyPages ?? 0;

        return new DiscoveryRowInfo
        {
            Key = key,
            Kind = kind,
            WitnessId = witnessId,
            State = DiscoveryUiText.ResolveState(false, doc, stale, exists),
            ShortTitle = DiscoveryUiText.ShortTitle(kind, variant, WitnessName(draft, doc, item, witnessId), requests),
            GeneratedUtc = hasContent ? doc!.GeneratedUtc : null,
            TestimonyPages = pages,
            RequestCount = requests,
            Warnings = doc?.Warnings ?? new List<DiscoveryViolation>(),
            FailureReason = doc?.FailureReason ?? string.Empty,
            PreviousKept = hasContent && !string.IsNullOrWhiteSpace(doc!.FailureReason) && !DiscoveryGenerator.IsWriteFailure(doc.FailureReason),
            FileName = doc != null && !string.IsNullOrWhiteSpace(doc.FileName) ? doc.FileName : item?.FileName ?? string.Empty,
            Stale = stale,
            PlannerNotes = doc?.PlannerNotes is { Count: > 0 } notes ? notes : PlanNotes(item),
            FileExists = exists,
            CostUsd = doc?.CostUsd ?? 0m
        };
    }

    private static List<string> PlanNotes(DiscoveryWorkItem? item) =>
        new(item?.Deposition?.Notes ?? item?.Written?.Notes ?? new List<string>());

    private static string WitnessName(CaseDraft draft, DiscoveryDocument? doc, DiscoveryWorkItem? item, string witnessId)
    {
        if (!string.IsNullOrWhiteSpace(item?.WitnessName))
            return item!.WitnessName;
        if (!string.IsNullOrWhiteSpace(doc?.Deposition?.WitnessName))
            return doc!.Deposition!.WitnessName;
        var cast = draft.Guide?.FindCast(witnessId);
        return cast != null && !string.IsNullOrWhiteSpace(cast.FullName) ? cast.FullName : witnessId;
    }

    // Saved content date first (the written transcript), else the planned date; undated rows sort last
    private static DateOnly DepositionDate(DiscoveryDocument? doc, DiscoveryWorkItem? item)
    {
        if (doc?.HasContent == true
            && DateOnly.TryParseExact(doc.Deposition?.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var saved))
            return saved;
        return item?.Date ?? DateOnly.MaxValue;
    }
}
