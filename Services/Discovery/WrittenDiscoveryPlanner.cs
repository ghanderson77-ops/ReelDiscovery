using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Whether a written discovery set is offered for the case's document type, and the label shown for it (D-25).
/// </summary>
public class KindAvailability
{
    public DiscoveryKind Kind { get; set; }
    public bool Visible { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Propounding and responding parties for the written sets (D-16).
/// </summary>
public class PartyChoice
{
    public CaseParty? Propounding { get; set; }
    public CaseParty? Responding { get; set; }

    // A saved party id no longer exists and the default was used instead
    public bool Fallback { get; set; }

    // Both pickers name the same party (kept as chosen; the UI warns)
    public bool Same { get; set; }
}

/// <summary>
/// The requested request count and the count actually planned (D-17, D-18, D-19).
/// </summary>
public class CountChoice
{
    public int Requested { get; set; }
    public int Effective { get; set; }

    // Effective is above the clamped request (every issue needs a request)
    public bool Raised { get; set; }

    // Effective is below the clamped request (RFAs only: too few dated key facts)
    public bool Lowered { get; set; }

    // Dated key facts (RFAs only)
    public int Available { get; set; }

    // Short code-side note for logs and the SUMMARY; UI copy comes from DiscoveryUiText
    public string LoweredReason { get; set; } = string.Empty;
}

/// <summary>
/// One numbered request, keyed by code to an issue, a fact or a produced document (D-04).
/// </summary>
public class RequestSlot
{
    public int Number { get; set; }
    public List<string> IssueIds { get; set; } = new();
    public string FactId { get; set; } = string.Empty;
    public string DocId { get; set; } = string.Empty;
    public string DocSubject { get; set; } = string.Empty;
    public bool IsGenuineness { get; set; }
    public bool IsPersonsWithKnowledge { get; set; }

    // Key characters of the responding party who act in or know the issue's facts
    public List<string> CustodianIds { get; set; } = new();

    // Canonical values (project, contract, change order, amount) linked to the issue's facts
    public List<string> ValueIds { get; set; } = new();

    // yyyy-MM-dd range of the issue's facts
    public string FromDate { get; set; } = string.Empty;
    public string ToDate { get; set; } = string.Empty;

    // Code-owned opening words of the request (RFAs)
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Independent copy (lists are not shared between slots).
    /// </summary>
    public RequestSlot Copy() => new()
    {
        Number = Number,
        IssueIds = new List<string>(IssueIds),
        FactId = FactId,
        DocId = DocId,
        DocSubject = DocSubject,
        IsGenuineness = IsGenuineness,
        IsPersonsWithKnowledge = IsPersonsWithKnowledge,
        CustodianIds = new List<string>(CustodianIds),
        ValueIds = new List<string>(ValueIds),
        FromDate = FromDate,
        ToDate = ToDate,
        Prefix = Prefix
    };
}

/// <summary>
/// Everything canonical about one written discovery set, decided before any model call.
/// </summary>
public class WrittenSetPlan
{
    public DiscoveryKind Kind { get; set; }
    public DiscoveryVariant Variant { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FileDocumentPart { get; set; } = string.Empty;
    public CaseParty Propounding { get; set; } = new();
    public CaseParty Responding { get; set; } = new();
    public CountChoice Count { get; set; } = new();
    public List<RequestSlot> Slots { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Pure written-discovery planning (D-04 "code owns structure", D-16..D-19, D-25): availability, parties, counts,
/// titles and the issue, fact or document every request is keyed to. The model only writes the request text.
/// Nothing here throws.
/// </summary>
public static class WrittenDiscoveryPlanner
{
    public const int MaxInterrogatories = DiscoverySettings.MaxInterrogatories;

    // Requests written per model call (research Pattern 3 output budget)
    public const int MaxSlotsPerCall = 25;

    // Opening interrogatories asking for persons with knowledge (D-18)
    public const int PersonsWithKnowledgeSlots = 2;

    // At most three genuineness RFAs per set (D-19)
    public const int MaxGenuineness = 3;

    public static KindAvailability Availability(CoreDocument? doc, DiscoveryKind kind)
    {
        var variant = DiscoveryVariants.Of(doc);
        var label = (variant, kind) switch
        {
            (_, DiscoveryKind.Deposition) => null,
            (DiscoveryVariant.MemoCriminal, DiscoveryKind.RequestsForProduction) => "Subpoena rider (documents)",
            (DiscoveryVariant.MemoCriminal, _) => null,
            (DiscoveryVariant.MemoRegulatory, DiscoveryKind.RequestsForProduction) => "CID document requests",
            (DiscoveryVariant.MemoRegulatory, DiscoveryKind.Interrogatories) => "CID interrogatories",
            (DiscoveryVariant.MemoRegulatory, _) => null,
            (DiscoveryVariant.MemoCivil, DiscoveryKind.RequestsForProduction) => "Pre-suit document requests",
            (DiscoveryVariant.MemoCivil, DiscoveryKind.Interrogatories) => "Written questions",
            (DiscoveryVariant.MemoCivil, DiscoveryKind.RequestsForAdmission) => "Requests to admit",
            (_, DiscoveryKind.RequestsForProduction) => "Requests for Production",
            (_, DiscoveryKind.Interrogatories) => "Interrogatories",
            (_, DiscoveryKind.RequestsForAdmission) => "Requests for Admission",
            _ => null
        };

        return new KindAvailability { Kind = kind, Visible = label != null, Label = label ?? string.Empty };
    }

    /// <summary>
    /// Saved party ids when they still exist in the core document, else the first plaintiff side serving the first
    /// defense side (D-16). Fallback is set when a saved id no longer exists.
    /// </summary>
    public static PartyChoice ResolveParties(CoreDocument? doc, DiscoverySettings? settings)
    {
        var choice = new PartyChoice();
        if (doc?.Parties == null)
            return choice;

        choice.Propounding = Pick(doc, settings?.PropoundingPartyId, CounselDirectory.FirstPlaintiffSide(doc), choice);
        choice.Responding = Pick(doc, settings?.RespondingPartyId, CounselDirectory.FirstDefenseSide(doc), choice);
        choice.Same = choice.Propounding != null && ReferenceEquals(choice.Propounding, choice.Responding);
        return choice;
    }

    /// <summary>
    /// Hard clamps regardless of stored values (T-08-11): RFP 5-80 raised to the issue count; ROG 1-25 raised to the
    /// issue count (at most 25); RFA 1-50 capped at dated key facts plus usable genuineness candidates.
    /// </summary>
    public static CountChoice EffectiveCount(CaseDraft draft, DiscoveryKind kind, int requested, DiscoveryCorpus? corpus = null)
    {
        var choice = new CountChoice { Requested = requested };
        var guide = draft?.Guide;
        var issueCount = Issues(guide).Count;

        // RFP and ROG never need lowering: their slots are apportioned across issues and several slots may share one
        // issue, so any count in range can be filled
        switch (kind)
        {
            case DiscoveryKind.RequestsForProduction:
            {
                var clamped = Math.Clamp(requested, DiscoverySettings.MinRfp, DiscoverySettings.MaxRfp);
                choice.Effective = Math.Min(DiscoverySettings.MaxRfp, Math.Max(issueCount, clamped));
                choice.Raised = choice.Effective > clamped;
                break;
            }
            case DiscoveryKind.Interrogatories:
            {
                var clamped = Math.Clamp(requested, DiscoverySettings.MinRogs, MaxInterrogatories);
                choice.Effective = Math.Max(Math.Min(issueCount, MaxInterrogatories), clamped);
                choice.Raised = choice.Effective > clamped;
                break;
            }
            case DiscoveryKind.RequestsForAdmission:
            {
                var clamped = Math.Clamp(requested, DiscoverySettings.MinRfa, DiscoverySettings.MaxRfa);
                var dated = DatedFacts(guide).Count;
                var available = dated + GenuinenessCandidates(corpus, DatedFacts(guide).Select(f => f.Id)).Count;
                choice.Available = dated;
                choice.Effective = Math.Min(clamped, available);
                if (choice.Effective < clamped)
                {
                    choice.Lowered = true;
                    choice.LoweredReason = available == 0
                        ? "no dated key facts"
                        : available == dated
                            ? $"only {dated} dated key facts"
                            : $"only {dated} dated key facts and {available - dated} documents to authenticate";
                }
                break;
            }
        }
        return choice;
    }

    public static string SetTitle(CoreDocument doc, DiscoveryKind kind, CaseParty propounding, CaseParty responding)
    {
        var from = Upper(propounding?.Name);
        var to = Upper(responding?.Name);
        return (DiscoveryVariants.Of(doc), kind) switch
        {
            (DiscoveryVariant.MemoCriminal, _) => $"SUBPOENA RIDER: DOCUMENTS TO BE PRODUCED BY {to}",
            (DiscoveryVariant.MemoRegulatory, DiscoveryKind.Interrogatories) => $"CIVIL INVESTIGATIVE DEMAND: WRITTEN INTERROGATORIES TO {to}",
            (DiscoveryVariant.MemoRegulatory, _) => $"CIVIL INVESTIGATIVE DEMAND: DOCUMENT REQUESTS TO {to}",
            (DiscoveryVariant.MemoCivil, DiscoveryKind.Interrogatories) => $"WRITTEN QUESTIONS TO {to}",
            (DiscoveryVariant.MemoCivil, DiscoveryKind.RequestsForAdmission) => $"REQUESTS TO ADMIT DIRECTED TO {to}",
            (DiscoveryVariant.MemoCivil, _) => $"PRE-SUIT REQUEST FOR PRESERVATION AND PRODUCTION OF DOCUMENTS TO {to}",
            _ => $"{RoleWord(doc, propounding)} {from}'S FIRST SET OF {CivilSetName(kind).ToUpperInvariant()} TO {RoleWord(doc, responding)} {to}"
        };
    }

    /// <summary>
    /// Document part of the .docx file name (D-29), e.g. "Brightline's First Set of Requests for Production".
    /// </summary>
    public static string FileDocumentPart(CoreDocument doc, DiscoveryKind kind, CaseParty propounding)
    {
        var shortName = (propounding?.ShortName ?? string.Empty).Trim();
        if (shortName.Length == 0)
            shortName = (propounding?.Name ?? string.Empty).Trim();

        return DiscoveryVariants.Of(doc) == DiscoveryVariant.Civil
            ? $"{shortName}'s First Set of {CivilSetName(kind)}"
            : $"{shortName}'s {Availability(doc, kind).Label}";
    }

    /// <summary>
    /// The plan for one written set; null when the kind is a deposition, hidden for the document type, disabled in
    /// settings, the parties cannot be resolved, or (RFAs) the guide has no dated key facts.
    /// </summary>
    public static WrittenSetPlan? Plan(CaseDraft draft, DiscoveryKind kind, DiscoverySettings settings, DiscoveryCorpus? corpus)
    {
        var doc = draft?.CoreDocument;
        settings ??= new DiscoverySettings();
        if (draft == null || doc == null || kind == DiscoveryKind.Deposition || !Availability(doc, kind).Visible)
            return null;

        var (enabled, requested, key) = kind switch
        {
            DiscoveryKind.RequestsForProduction => (settings.RfpEnabled, settings.RfpCount, "RFP"),
            DiscoveryKind.Interrogatories => (settings.RogEnabled, settings.RogCount, "ROG"),
            _ => (settings.RfaEnabled, settings.RfaCount, "RFA")
        };
        if (!enabled)
            return null;

        var parties = ResolveParties(doc, settings);
        if (parties.Propounding == null || parties.Responding == null)
            return null;

        var count = EffectiveCount(draft, kind, requested, corpus);
        if (kind == DiscoveryKind.RequestsForAdmission && count.Effective == 0)
            return null;

        var plan = new WrittenSetPlan
        {
            Kind = kind,
            Variant = DiscoveryVariants.Of(doc),
            Key = key,
            Title = SetTitle(doc, kind, parties.Propounding, parties.Responding),
            FileDocumentPart = FileDocumentPart(doc, kind, parties.Propounding),
            Propounding = parties.Propounding,
            Responding = parties.Responding,
            Count = count
        };
        if (parties.Fallback)
            plan.Notes.Add("A saved party no longer exists; the default parties were used.");
        if (count.Lowered)
            plan.Notes.Add($"Requests lowered to {count.Effective}: {count.LoweredReason}.");

        var guide = draft.Guide;
        if (guide == null || (kind != DiscoveryKind.RequestsForAdmission && Issues(guide).Count == 0))
        {
            plan.Notes.Add("The case guide has no issues, so no requests were planned.");
            return plan;
        }

        plan.Slots = kind switch
        {
            DiscoveryKind.RequestsForProduction => ProductionSlots(draft, parties.Responding, count.Effective),
            DiscoveryKind.Interrogatories => InterrogatorySlots(draft, parties.Responding, count.Effective),
            _ => AdmissionSlots(guide, count.Effective, corpus)
        };
        for (var i = 0; i < plan.Slots.Count; i++)
            plan.Slots[i].Number = i + 1;

        return plan;
    }

    // ---- Slots ----

    /// <summary>
    /// Every issue gets at least one request; the rest follow the issue's fact count. Ordered by issue (guide order).
    /// </summary>
    private static List<RequestSlot> ProductionSlots(CaseDraft draft, CaseParty responding, int effective)
    {
        var issues = Issues(draft.Guide);
        var shares = Apportion.Largest(effective, IssueWeights(draft.Guide, issues), min: 1);

        var slots = new List<RequestSlot>();
        for (var i = 0; i < issues.Count; i++)
        {
            var anchor = IssueAnchor(draft, responding, new[] { issues[i].Id });
            for (var n = 0; n < shares[i]; n++)
                slots.Add(anchor.Copy());
        }
        return slots;
    }

    /// <summary>
    /// Persons-with-knowledge interrogatories first, then contention interrogatories per issue (min 1). When there are
    /// more issues than contention slots, issues are dealt round-robin by weight into shared slots so every issue
    /// appears (research Pattern 5). Never more than 25 including subparts (FRCP 33(a)(1)).
    /// </summary>
    private static List<RequestSlot> InterrogatorySlots(CaseDraft draft, CaseParty responding, int effective)
    {
        effective = Math.Min(effective, MaxInterrogatories);
        var issues = Issues(draft.Guide);
        var issueIds = issues.Select(i => i.Id).ToList();

        // At least one contention slot always remains
        var persons = Math.Min(PersonsWithKnowledgeSlots, Math.Max(0, effective - 1));
        var contention = effective - persons;

        var slots = new List<RequestSlot>();
        for (var n = 0; n < persons; n++)
            slots.Add(new RequestSlot { IsPersonsWithKnowledge = true, IssueIds = new List<string>(issueIds) });

        var weights = IssueWeights(draft.Guide, issues);
        if (issues.Count <= contention)
        {
            var shares = Apportion.Largest(contention, weights, min: 1);
            for (var i = 0; i < issues.Count; i++)
            {
                var anchor = IssueAnchor(draft, responding, new[] { issues[i].Id });
                for (var n = 0; n < shares[i]; n++)
                    slots.Add(anchor.Copy());
            }
            return slots;
        }

        // Heaviest issues first; slot k takes issues k, k + contention, ... then guide order inside a slot
        var byWeight = Enumerable.Range(0, issues.Count).OrderByDescending(i => weights[i]).ThenBy(i => i).ToList();
        var groups = Enumerable.Range(0, contention).Select(_ => new List<int>()).ToList();
        for (var r = 0; r < byWeight.Count; r++)
            groups[r % contention].Add(byWeight[r]);

        foreach (var group in groups.OrderBy(g => g.Min()))
            slots.Add(IssueAnchor(draft, responding, group.OrderBy(i => i).Select(i => issueIds[i])));
        return slots;
    }

    /// <summary>
    /// One dated fact per request (Hot/Key before Supporting, then chronological), then 1-3 genuineness requests for hot
    /// written documents linked to the selected facts. Genuineness RFAs are part of the requested count (D-19), so the
    /// slot count always equals the effective count.
    /// </summary>
    private static List<RequestSlot> AdmissionSlots(CaseGuide guide, int effective, DiscoveryCorpus? corpus)
    {
        var dated = DatedFacts(guide)
            .OrderBy(f => f.Significance == FactSignificance.Supporting ? 1 : 0)
            .ThenBy(f => f.Date, StringComparer.Ordinal)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

        // Plan revision 3: when EffectiveCount was lowered to dated facts + candidates, genuineness absorbs the
        // remainder so fact slots never exceed the dated facts
        var formula = Math.Min(Math.Min(MaxGenuineness, Math.Max(1, effective / 10)), Math.Max(0, effective - 1));
        var usable = corpus?.Usable == true;
        var genuineness = usable
            ? Math.Min(GenuinenessCandidates(corpus, dated.Select(f => f.Id)).Count, Math.Max(formula, effective - dated.Count))
            : 0;
        var factSlots = Math.Min(effective - genuineness, dated.Count);

        // Candidates must link to a selected fact; take more facts (fewer genuineness requests) until they do
        var selected = dated.Take(factSlots).ToList();
        var candidates = GenuinenessCandidates(corpus, selected.Select(f => f.Id));
        while (candidates.Count < genuineness && factSlots < dated.Count)
        {
            factSlots++;
            genuineness--;
            selected = dated.Take(factSlots).ToList();
            candidates = GenuinenessCandidates(corpus, selected.Select(f => f.Id));
        }
        genuineness = Math.Min(genuineness, candidates.Count);

        var slots = selected.Select(f => new RequestSlot
        {
            IssueIds = CaseGuide.CleanStrings(f.IssueIds).ToList(),
            FactId = f.Id,
            FromDate = f.Date,
            ToDate = f.Date,
            Prefix = $"Admit that on {CaseGuideDocxExporter.LongDate(f.Date)},"
        }).ToList();

        // Rule 36(a)(1)(B) genuineness; "produced as DocID" means the document was already furnished (36(a)(2)).
        // DocIDs come only from the written corpus, never from model output (T-08-12)
        foreach (var candidate in candidates.Take(genuineness))
        {
            var fact = selected.First(f => (candidate.FactIds ?? new List<string>()).Contains(f.Id));
            slots.Add(new RequestSlot
            {
                IsGenuineness = true,
                IssueIds = CaseGuide.CleanStrings(fact.IssueIds).ToList(),
                FactId = fact.Id,
                DocId = candidate.DocId,
                DocSubject = candidate.Subject ?? string.Empty,
                FromDate = candidate.Date ?? string.Empty,
                ToDate = candidate.Date ?? string.Empty,
                Prefix = $"Admit that the document produced as {candidate.DocId} is a true and correct copy of"
            });
        }
        return slots;
    }

    // Facts per issue, at least 1, in issue order
    private static List<double> IssueWeights(CaseGuide? guide, List<CaseIssue> issues)
    {
        var facts = Facts(guide);
        return issues.Select(i => (double)Math.Max(1, facts.Count(f => (f.IssueIds ?? new List<string>()).Contains(i.Id)))).ToList();
    }

    /// <summary>
    /// Custodians, canonical values and date range shared by every request on the given issues.
    /// </summary>
    private static RequestSlot IssueAnchor(CaseDraft draft, CaseParty responding, IEnumerable<string> issueIds)
    {
        var ids = issueIds.ToList();
        var facts = Facts(draft.Guide).Where(f => (f.IssueIds ?? new List<string>()).Any(ids.Contains)).ToList();
        var factIds = facts.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var people = facts
            .SelectMany(f => (f.ActorIds ?? new List<string>()).Concat(f.KnownByIds ?? new List<string>()))
            .ToHashSet(StringComparer.Ordinal);

        var custodians = (draft.Guide?.Cast ?? new List<CastMember>())
            .Where(c => c != null
                && c.Tier == CastTier.Key
                && c.Role is CastRole.Party or CastRole.Staff or CastRole.InHouseCounsel
                && people.Contains(c.Id)
                && string.Equals(CounselDirectory.PartyOf(draft, c)?.Id, responding.Id, StringComparison.Ordinal))
            .Select(c => c.Id)
            .ToList();

        var values = (draft.Guide?.Values ?? new List<CanonicalValue>())
            .Where(v => v != null
                && v.Kind is ValueKind.ProjectName or ValueKind.ContractNumber or ValueKind.ChangeOrderNumber or ValueKind.Amount
                && (v.FactIds ?? new List<string>()).Any(factIds.Contains))
            .Select(v => v.Id)
            .ToList();

        var dates = facts.Where(f => CaseDraft.TryParseDate(f.Date, out _)).Select(f => f.Date).OrderBy(d => d, StringComparer.Ordinal).ToList();

        return new RequestSlot
        {
            IssueIds = ids,
            CustodianIds = custodians,
            ValueIds = values,
            FromDate = dates.FirstOrDefault() ?? string.Empty,
            ToDate = dates.LastOrDefault() ?? string.Empty
        };
    }

    // ---- Helpers ----

    private static CaseParty? Pick(CoreDocument doc, string? savedId, CaseParty? fallback, PartyChoice choice)
    {
        if (string.IsNullOrWhiteSpace(savedId))
            return fallback;

        var saved = doc.Parties.FirstOrDefault(p => p != null && string.Equals(p.Id, savedId.Trim(), StringComparison.Ordinal));
        if (saved != null)
            return saved;

        choice.Fallback = true;
        return fallback;
    }

    private static string CivilSetName(DiscoveryKind kind) => kind switch
    {
        DiscoveryKind.Interrogatories => "Interrogatories",
        DiscoveryKind.RequestsForAdmission => "Requests for Admission",
        _ => "Requests for Production"
    };

    private static string RoleWord(CoreDocument doc, CaseParty? party)
    {
        var petition = doc?.UsesPetitionTerminology == true;
        return party?.Role switch
        {
            PartyRole.Plaintiff => petition ? "PETITIONER" : "PLAINTIFF",
            PartyRole.Defendant => petition ? "RESPONDENT" : "DEFENDANT",
            PartyRole.Complainant => "COMPLAINANT",
            PartyRole.Subject => "SUBJECT",
            _ => "NON-PARTY"
        };
    }

    // Names are kept verbatim apart from case (D-02)
    private static string Upper(string? name) => (name ?? string.Empty).Trim().ToUpperInvariant();

    private static List<CaseIssue> Issues(CaseGuide? guide) =>
        (guide?.Issues ?? new List<CaseIssue>()).Where(i => i != null && !string.IsNullOrEmpty(i.Id)).ToList();

    private static List<KeyFact> Facts(CaseGuide? guide) =>
        (guide?.Facts ?? new List<KeyFact>()).Where(f => f != null && !string.IsNullOrEmpty(f.Id)).ToList();

    private static List<KeyFact> DatedFacts(CaseGuide? guide) =>
        Facts(guide).Where(f => CaseDraft.TryParseDate(f.Date, out _)).ToList();

    /// <summary>
    /// Hot written documents linked to one of the given facts, by date then DocID, distinct, at most three; empty
    /// without a usable corpus. DocIDs come only from the written corpus (T-08-12).
    /// </summary>
    private static List<ExhibitCandidate> GenuinenessCandidates(DiscoveryCorpus? corpus, IEnumerable<string> factIds)
    {
        if (corpus?.Usable != true || corpus.Candidates == null)
            return new List<ExhibitCandidate>();

        var ids = new HashSet<string>(factIds, StringComparer.Ordinal);
        return corpus.Candidates
            .Where(c => c != null && c.IsHot && !string.IsNullOrEmpty(c.DocId) && (c.FactIds ?? new List<string>()).Any(ids.Contains))
            .OrderBy(c => c.Date ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => c.DocId, StringComparer.Ordinal)
            .DistinctBy(c => c.DocId)
            .Take(MaxGenuineness)
            .ToList();
    }
}
