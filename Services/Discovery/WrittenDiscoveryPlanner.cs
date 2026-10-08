using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;

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

        return plan;
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
