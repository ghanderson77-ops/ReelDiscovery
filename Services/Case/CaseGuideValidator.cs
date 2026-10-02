using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

public enum GuideArea
{
    Issues,
    Cast,
    Facts,
    Values,
    Guide
}

/// <summary>
/// One-click fix for a Checks finding. The automatic ones (CaseGuideQuickFix.IsAutomatic) are applied by code in
/// CaseGuideQuickFix; the rest start an existing run (with its own dialog or confirmation) from the Case Guide step.
/// </summary>
public enum GuideQuickFix
{
    None,

    // ----- Automatic (code, no model) -----
    AssignNewId,
    RemoveBrokenLinks,
    WidenActiveRange,
    NormalizeDate,
    ResetEmail,
    NewEmailDomain,
    AddActorsToKnownBy,
    ReplaceRealNames,
    LinkParty,
    MarkReviewed,

    // ----- Model runs (the step asks first, like the matching buttons) -----
    RegenerateEntry,
    RegenerateCast,
    RegenerateValues,
    RetryIssueDetails,
    UpdateGuide,
    ResumeGuide
}

/// <summary>
/// Detail field focused when a finding is opened from Checks.
/// </summary>
public enum GuideFindingField
{
    None,
    Date,
    Title,
    Issues,
    Actors,
    KnownBy,
    Name,
    Email,
    Organization,
    ActiveRange,
    Label,
    Value,
    LinkedFacts
}

public class GuideFinding
{
    public IssueSeverity Severity { get; set; }
    public GuideArea Area { get; set; }

    /// <summary>
    /// Id of the entity the finding is about (ISS-01, ORG-02, CH-007, F-012, V-004); empty for guide-level findings.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// How to clear the finding, in plain language that names the entity and value ("Widen David Wallace's active dates to
    /// include 2007-01-08, or change F-001's date."). Every Error has one.
    /// </summary>
    public string FixHint { get; set; } = string.Empty;

    /// <summary>
    /// The one-click fix offered by the Checks card's Fix button; None when the user has to decide.
    /// </summary>
    public GuideQuickFix QuickFix { get; set; }

    /// <summary>
    /// Id the quick fix changes when it is not EntityId: the character whose range is widened, the party to link.
    /// </summary>
    public string FixTargetId { get; set; } = string.Empty;

    /// <summary>
    /// Value the quick fix uses: the date a range must include, the normalized date, the term to replace.
    /// </summary>
    public string FixValue { get; set; } = string.Empty;

    /// <summary>
    /// Detail field that opening the finding focuses.
    /// </summary>
    public GuideFindingField Field { get; set; }

    /// <summary>
    /// Tab a guide-level finding opens (counsel, party and organization findings open the Cast); null keeps Area.
    /// </summary>
    public GuideArea? GoTo { get; set; }

    /// <summary>
    /// True when the quick fix is applied by code (CaseGuideQuickFix), with no model call.
    /// </summary>
    public bool HasAutomaticFix => CaseGuideQuickFix.IsAutomatic(QuickFix);

    /// <summary>
    /// Location label for the Checks card: "Key Facts · F-012", "Cast · CH-007", "Guide".
    /// </summary>
    public string Where
    {
        get
        {
            var label = Area switch
            {
                GuideArea.Issues => "Issues",
                GuideArea.Cast => "Cast",
                GuideArea.Facts => "Key Facts",
                GuideArea.Values => "Canonical Values",
                _ => "Guide"
            };
            return string.IsNullOrEmpty(EntityId) ? label : $"{label} · {EntityId}";
        }
    }
}

/// <summary>
/// Deterministic consistency checks over the case guide (GUIDE-07): orphan links, dates, active ranges,
/// cast identity, counsel coverage, preset ranges, canonical values, real-name leaks, unfinished and stale parts.
/// Static, null-safe and never throws; Errors block Next. Every finding says how to clear it (FixHint) and, where one
/// exists, which quick fix does it (QuickFix).
/// </summary>
public static partial class CaseGuideValidator
{
    internal const string TooLargeToScan = "Some text is too large to scan for real names.";

    // Identifier-like kinds that must appear verbatim somewhere in the core document or the facts
    private static readonly HashSet<ValueKind> TextCheckedKinds = new()
    {
        ValueKind.Amount,
        ValueKind.InvoiceNumber,
        ValueKind.ChangeOrderNumber,
        ValueKind.ContractNumber,
        ValueKind.PurchaseOrder,
        ValueKind.AccountNumber
    };

    // "F-003 was changed after it was written." -> F-003
    [GeneratedRegex(@"\bF-\d+\b", RegexOptions.None, CitationScanner.MatchTimeoutMs)]
    private static partial Regex FactIdReference();

    public static List<GuideFinding> Validate(CaseDraft? draft)
    {
        var findings = new List<GuideFinding>();
        if (draft?.CoreDocument == null || draft.Guide == null)
            return findings;

        Context ctx;
        try
        {
            ctx = new Context(draft);
        }
        catch (Exception ex)
        {
            // Malformed data must never crash the step that shows the checks
            findings.Add(CouldNotRun(ex));
            return findings;
        }

        Run(findings, ctx, CheckIds);
        Run(findings, ctx, CheckFacts);
        Run(findings, ctx, CheckCast);
        Run(findings, ctx, CheckOrganizations);
        Run(findings, ctx, CheckIssues);
        Run(findings, ctx, CheckCounsel);
        Run(findings, ctx, CheckPreset);
        Run(findings, ctx, CheckValues);
        Run(findings, ctx, CheckLeaks);
        Run(findings, ctx, CheckParts);
        Run(findings, ctx, CheckReview);
        Run(findings, ctx, CheckMappingNotes);
        Run(findings, ctx, CheckParties);

        return findings;
    }

    public static int CountErrors(IEnumerable<GuideFinding> findings) =>
        (findings ?? Enumerable.Empty<GuideFinding>()).Count(f => f != null && f.Severity == IssueSeverity.Error);

    public static int CountWarnings(IEnumerable<GuideFinding> findings) =>
        (findings ?? Enumerable.Empty<GuideFinding>()).Count(f => f != null && f.Severity == IssueSeverity.Warning);

    public static List<GuideFinding> FindingsFor(IEnumerable<GuideFinding> findings, string entityId) =>
        string.IsNullOrEmpty(entityId)
            ? new List<GuideFinding>()
            : (findings ?? Enumerable.Empty<GuideFinding>()).Where(f => f != null && f.EntityId == entityId).ToList();

    /// <summary>
    /// Worst severity for an entity (drives the status dots); null when it has no findings.
    /// </summary>
    public static IssueSeverity? WorstFor(IEnumerable<GuideFinding> findings, string entityId)
    {
        var mine = FindingsFor(findings, entityId);
        if (mine.Count == 0)
            return null;

        return mine.Any(f => f.Severity == IssueSeverity.Error) ? IssueSeverity.Error : IssueSeverity.Warning;
    }

    private static void Run(List<GuideFinding> findings, Context ctx, Action<Context, List<GuideFinding>> rule)
    {
        try
        {
            rule(ctx, findings);
        }
        catch (RegexMatchTimeoutException)
        {
            AddTooLarge(findings);
        }
        catch (Exception ex)
        {
            // One broken rule must not hide the others or throw into the UI (T-03-26)
            findings.Add(CouldNotRun(ex));
        }
    }

    private static GuideFinding CouldNotRun(Exception ex) =>
        Warning(GuideArea.Guide, string.Empty, $"A check could not run: {ex.Message.TrimEnd('.')}.")
            .Fix("Edit or regenerate the entry named in the message; the checks run again after every change.");

    private static void AddTooLarge(List<GuideFinding> findings)
    {
        if (!findings.Any(f => f.Message == TooLargeToScan))
            findings.Add(Warning(GuideArea.Guide, string.Empty, TooLargeToScan)
                .Fix("Shorten the longest descriptions in the guide so they can be checked for real names."));
    }

    // ---- Rule 1: ids ----

    private static void CheckIds(Context ctx, List<GuideFinding> findings)
    {
        CheckIdList(findings, GuideArea.Issues, ctx.Issues.Select(x => x.Id));
        CheckIdList(findings, GuideArea.Guide, ctx.Organizations.Select(x => x.Id));
        CheckIdList(findings, GuideArea.Cast, ctx.Cast.Select(x => x.Id));
        CheckIdList(findings, GuideArea.Facts, ctx.Facts.Select(x => x.Id));
        CheckIdList(findings, GuideArea.Values, ctx.Values.Select(x => x.Id));
    }

    private static void CheckIdList(List<GuideFinding> findings, GuideArea area, IEnumerable<string?> ids)
    {
        var all = ids.ToList();
        var noun = EntryNoun(area);
        GuideArea? goTo = area == GuideArea.Guide ? GuideArea.Cast : null;

        if (all.Any(string.IsNullOrWhiteSpace))
            findings.Add(Error(area, string.Empty, "An entry has no ID.")
                .Fix($"Give the {noun} without an ID a new ID, or delete it.", GuideQuickFix.AssignNewId, goTo: goTo));

        foreach (var group in all.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id!, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
                findings.Add(Error(area, group.Key, $"Two entries use the ID {group.Key}.")
                    .Fix($"Give the second {noun} that uses {group.Key} a new ID, or delete it.", GuideQuickFix.AssignNewId, goTo: goTo));
        }
    }

    private static string EntryNoun(GuideArea area) => area switch
    {
        GuideArea.Issues => "issue",
        GuideArea.Cast => "character",
        GuideArea.Facts => "fact",
        GuideArea.Values => "value",
        _ => "organization"
    };

    // ---- Rules 2-9: facts ----

    private static void CheckFacts(Context ctx, List<GuideFinding> findings)
    {
        var doc = ctx.Doc;
        var isPleading = CoreDocumentTemplates.IsPleading(doc.DocumentType);
        var documentDateText = isPleading ? S(doc.FilingDate) : S(doc.Memo?.Date);
        var hasDocumentDate = CaseDraft.TryParseDate(documentDateText, out var documentDate);
        var dateKind = isPleading ? "filing" : "memo";

        var hasTimeline = CaseDraft.TryParseDate(doc.TimelineStart, out var timelineStart)
            & CaseDraft.TryParseDate(doc.TimelineEnd, out var timelineEnd);

        foreach (var fact in ctx.Facts)
        {
            var id = S(fact.Id);
            var issueIds = L(fact.IssueIds);
            var actorIds = L(fact.ActorIds);
            var knownByIds = L(fact.KnownByIds);

            // Rule 2: orphan links
            foreach (var issueId in issueIds.Distinct(StringComparer.Ordinal))
            {
                if (!ctx.IssueIds.Contains(issueId))
                    findings.Add(Error(GuideArea.Facts, id, $"{id} links to issue {issueId}, which does not exist.")
                        .Fix($"Remove the link to {issueId} from {id}, or pick a different issue.", GuideQuickFix.RemoveBrokenLinks,
                            GuideFindingField.Issues, target: issueId));
            }

            foreach (var castId in actorIds.Concat(knownByIds).Distinct(StringComparer.Ordinal))
            {
                if (!ctx.CastById.ContainsKey(castId))
                    findings.Add(Error(GuideArea.Facts, id, $"{id} refers to a character that does not exist ({castId}).")
                        .Fix($"Remove the link to {castId} from {id}, or pick a different character.", GuideQuickFix.RemoveBrokenLinks,
                            actorIds.Contains(castId) ? GuideFindingField.Actors : GuideFindingField.KnownBy, target: castId));
            }

            // Rule 3: required links and title
            if (issueIds.Count == 0)
                findings.Add(Error(GuideArea.Facts, id, $"{id} is not linked to any issue.")
                    .Fix($"Pick at least one issue for {id} under Issues, or click Regenerate Fact.", GuideQuickFix.RegenerateEntry,
                        GuideFindingField.Issues));
            if (actorIds.Count == 0)
                findings.Add(Error(GuideArea.Facts, id, $"{id} has no one under Who did it.")
                    .Fix($"Pick who did it for {id} under Who did it, or click Regenerate Fact.", GuideQuickFix.RegenerateEntry,
                        GuideFindingField.Actors));
            if (string.IsNullOrWhiteSpace(fact.Title))
                findings.Add(Error(GuideArea.Facts, id, $"{id} has no title.")
                    .Fix($"Type a title for {id} under Fact, or click Regenerate Fact.", GuideQuickFix.RegenerateEntry, GuideFindingField.Title));

            // Rule 9: actors always knew
            foreach (var actorId in actorIds.Distinct(StringComparer.Ordinal))
            {
                if (ctx.CastById.TryGetValue(actorId, out var actor) && !knownByIds.Contains(actorId))
                    findings.Add(Warning(GuideArea.Facts, id, $"{id}: {actor.FullName} did it but is not listed under Who knew.")
                        .Fix($"Add {actor.FullName} under Who knew for {id}.", GuideQuickFix.AddActorsToKnownBy, GuideFindingField.KnownBy,
                            target: actorId));
            }

            // Rule 4
            var dateText = S(fact.Date);
            if (!CaseDraft.TryParseDate(dateText, out var date))
            {
                var invalid = Error(GuideArea.Facts, id, $"{id} has no valid date. Use YYYY-MM-DD.");
                findings.Add(CaseGuideQuickFix.TryNormalizeDate(dateText, out var normalized)
                    ? invalid.Fix($"Change {id}'s date from {dateText.Trim()} to {normalized}.", GuideQuickFix.NormalizeDate,
                        GuideFindingField.Date, value: normalized)
                    : invalid.Fix($"Type {id}'s date as YYYY-MM-DD (like 2024-03-03), or click Regenerate Fact.", GuideQuickFix.RegenerateEntry,
                        GuideFindingField.Date));
                continue;
            }

            // Rule 5
            if (hasDocumentDate && date > documentDate)
                findings.Add(Error(GuideArea.Facts, id, $"{id} is dated {dateText}, after the {dateKind} date ({documentDateText}).")
                    .Fix($"Change {id}'s date to {documentDateText} or earlier, or change the {dateKind} date in the core document.",
                        field: GuideFindingField.Date));

            // Rule 6
            if (hasTimeline && (date < timelineStart || date > timelineEnd))
                findings.Add(Warning(GuideArea.Facts, id, $"{id} is dated {dateText}, outside the timeline ({S(doc.TimelineStart)} to {S(doc.TimelineEnd)}).")
                    .Fix($"Change {id}'s date to fall between {S(doc.TimelineStart)} and {S(doc.TimelineEnd)}, or widen the timeline in the core document.",
                        field: GuideFindingField.Date));

            // Rule 7: actors must be active on the date
            foreach (var actorId in actorIds.Distinct(StringComparer.Ordinal))
            {
                if (ctx.CastById.TryGetValue(actorId, out var actor) && IsInactive(actor, date))
                    findings.Add(Error(GuideArea.Facts, id,
                        $"{id} is dated {dateText}, but {actor.FullName} ({actorId}) is only active {S(actor.ActiveFrom)} to {S(actor.ActiveTo)}.")
                        .Fix($"Widen {actor.FullName}'s active dates to include {dateText}, or change {id}'s date.", GuideQuickFix.WidenActiveRange,
                            GuideFindingField.Date, target: actorId, value: dateText));
            }

            // Rule 8: knowers should be active on the date
            foreach (var knowerId in knownByIds.Distinct(StringComparer.Ordinal).Where(k => !actorIds.Contains(k)))
            {
                if (ctx.CastById.TryGetValue(knowerId, out var knower) && IsInactive(knower, date))
                    findings.Add(Warning(GuideArea.Facts, id,
                        $"{id} is dated {dateText}, but {knower.FullName} ({knowerId}), who knew about it, is only active {S(knower.ActiveFrom)} to {S(knower.ActiveTo)}.")
                        .Fix($"Widen {knower.FullName}'s active dates to include {dateText}, or remove {knower.FirstName} from Who knew.",
                            GuideQuickFix.WidenActiveRange, GuideFindingField.KnownBy, target: knowerId, value: dateText));
            }
        }
    }

    // Unparseable ranges are reported by the cast rule, not here
    private static bool IsInactive(CastMember member, DateTime date) =>
        TryRange(member, out var from, out var to) && (date < from || date > to);

    private static bool TryRange(CastMember member, out DateTime from, out DateTime to)
    {
        to = default;
        return CaseDraft.TryParseDate(member.ActiveFrom, out from)
            && CaseDraft.TryParseDate(member.ActiveTo, out to)
            && to >= from;
    }

    // ---- Rules 10-12: cast ----

    private static void CheckCast(Context ctx, List<GuideFinding> findings)
    {
        var emailUse = ctx.Cast
            .Select(c => S(c.Email).Trim())
            .Where(e => e.Length > 0)
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var member in ctx.Cast)
        {
            var id = S(member.Id);
            var name = member.FullName;
            var email = S(member.Email).Trim();
            var label = name.Length > 0 ? name : id;
            ctx.OrganizationById.TryGetValue(S(member.OrganizationId), out var org);

            // Rule 10
            if (name.Length == 0)
                findings.Add(Error(GuideArea.Cast, id, $"{id} has no name.")
                    .Fix($"Type a name for {id} under First name, or click Regenerate Character.", GuideQuickFix.RegenerateEntry,
                        GuideFindingField.Name));
            if (!TryRange(member, out _, out _))
            {
                var range = Error(GuideArea.Cast, id, $"{name} ({id}) has an active range that is missing or ends before it starts.");
                findings.Add(CaseGuideQuickFix.TryRepairRange(ctx.Draft, member, out var from, out var to)
                    ? range.Fix($"Set {label}'s active dates to {from} to {to}, or type them as YYYY-MM-DD.", GuideQuickFix.WidenActiveRange,
                        GuideFindingField.ActiveRange, target: id)
                    : range.Fix($"Type {label}'s active dates as YYYY-MM-DD, with the end on or after the start.",
                        field: GuideFindingField.ActiveRange));
            }

            // Rule 11
            var suggested = CaseGuideQuickFix.SuggestedEmail(ctx.Draft, member);
            if (!EmailIdentity.IsWellFormed(email))
            {
                var invalid = Error(GuideArea.Cast, id, $"{name} ({id}) has an invalid email address.");
                findings.Add(suggested.Length > 0
                    ? invalid.Fix($"Use {suggested}, built from {label}'s name and organization, or type a valid address.",
                        GuideQuickFix.ResetEmail, GuideFindingField.Email, value: suggested)
                    : invalid.Fix($"Type a valid address for {label}, like name@company.com.", field: GuideFindingField.Email));
            }
            else
            {
                if (emailUse.TryGetValue(email, out var uses) && uses > 1)
                {
                    // The address stays with the character it was built for; the fix moves the other one off it
                    var holders = ctx.Cast.Where(c => string.Equals(S(c.Email).Trim(), email, StringComparison.OrdinalIgnoreCase)).ToList();
                    var owner = CaseGuideQuickFix.SharedAddressOwner(ctx.Draft, holders);
                    var mover = member != owner ? member : holders.FirstOrDefault(c => c != owner) ?? member;
                    var moverSuggested = mover == member ? suggested : CaseGuideQuickFix.SuggestedEmail(ctx.Draft, mover);
                    var moverName = mover.FullName.Length > 0 ? mover.FullName : S(mover.Id);
                    var shared = Error(GuideArea.Cast, id, $"{email} is used by more than one character.");
                    findings.Add(moverSuggested.Length > 0
                        ? shared.Fix($"Give {moverName} their own address ({moverSuggested}), or type a different one.", GuideQuickFix.ResetEmail,
                            GuideFindingField.Email, target: S(mover.Id), value: moverSuggested)
                        : shared.Fix($"Type a different address for {moverName}.", field: GuideFindingField.Email));
                }

                if (EmailIdentity.IsReservedDomain(email))
                {
                    var reserved = Warning(GuideArea.Cast, id, $"{name}'s email uses a reserved test domain.");
                    findings.Add(suggested.Length > 0 && !EmailIdentity.IsReservedDomain(suggested)
                        ? reserved.Fix($"Use {suggested}, or type an address on a realistic domain.", GuideQuickFix.ResetEmail,
                            GuideFindingField.Email, value: suggested)
                        : reserved.Fix($"Type an address for {label} on a realistic domain.", field: GuideFindingField.Email));
                }

                if (org != null && !member.EmailIsUserEdited)
                {
                    var orgDomain = S(org.Domain).Trim().TrimEnd('.').ToLowerInvariant();
                    if (orgDomain.Length > 0 && EmailIdentity.DomainOf(email) != orgDomain)
                        findings.Add(Warning(GuideArea.Cast, id, $"{name}'s email does not use {S(org.Name)}'s domain ({orgDomain}).")
                            .Fix($"Use {suggested} so the address matches {S(org.Name)}, or type a different address.", GuideQuickFix.ResetEmail,
                                GuideFindingField.Email, value: suggested));
                }
            }

            // Rule 12
            if (org == null)
                findings.Add(Error(GuideArea.Cast, id, $"{name} ({id}) belongs to an organization that does not exist ({S(member.OrganizationId)}).")
                    .Fix($"Choose an organization for {label} under Organization, or click Regenerate Cast.", GuideQuickFix.RegenerateCast,
                        GuideFindingField.Organization));
        }
    }

    // ---- Rule 13: organizations ----

    private static void CheckOrganizations(Context ctx, List<GuideFinding> findings)
    {
        var seen = new Dictionary<string, CaseOrganization>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in ctx.Organizations)
        {
            var domain = S(org.Domain).Trim().TrimEnd('.').ToLowerInvariant();
            if (domain.Length == 0)
                continue;

            if (seen.TryGetValue(domain, out var first))
            {
                var newDomain = CaseGuideQuickFix.SuggestedDomain(ctx.Draft, org);
                findings.Add(Error(GuideArea.Guide, S(org.Id), $"{S(first.Name)} and {S(org.Name)} use the same email domain ({domain}).")
                    .Fix($"Give {S(org.Name)} its own domain ({newDomain}); its characters' addresses move to it unless you typed them.",
                        GuideQuickFix.NewEmailDomain, goTo: GuideArea.Cast, value: newDomain));
            }
            else
            {
                seen[domain] = org;
            }
        }

        // A law firm must represent organizations that exist (a deleted or merged organization leaves a dangling id)
        foreach (var firm in ctx.Organizations.Where(o => o.Kind == OrganizationKind.LawFirm))
        {
            foreach (var orgId in L(firm.RepresentsOrgIds).Distinct(StringComparer.Ordinal).Where(o => !ctx.OrganizationById.ContainsKey(o)))
            {
                findings.Add(Warning(GuideArea.Guide, S(firm.Id), $"{S(firm.Name)} represents an organization that does not exist ({orgId}).")
                    .Fix($"Remove the link to {orgId} from {S(firm.Name)}.", GuideQuickFix.RemoveBrokenLinks, goTo: GuideArea.Cast, target: orgId));
            }
        }
    }

    // ---- Rules 14-16: issues and key characters ----

    private static void CheckIssues(Context ctx, List<GuideFinding> findings)
    {
        var countIds = new HashSet<string>(
            (ctx.Doc.Counts ?? new()).Where(c => c != null).Select(c => S(c.Id)), StringComparer.Ordinal);
        var linkedIssues = new HashSet<string>(ctx.Facts.SelectMany(f => L(f.IssueIds)), StringComparer.Ordinal);

        foreach (var issue in ctx.Issues)
        {
            var id = S(issue.Id);

            // Rule 14
            if (!countIds.Contains(S(issue.CountId)))
                findings.Add(Warning(GuideArea.Issues, id, $"{id} comes from a count that is no longer in the core document.")
                    .Fix("Click Update Guide to bring the issues in line with the core document.", GuideQuickFix.UpdateGuide));

            // Rule 15
            if (!linkedIssues.Contains(id))
                findings.Add(Warning(GuideArea.Issues, id, $"No fact is linked to {id} ({S(issue.Title)}).")
                    .Fix($"Link a fact to {id} under Issues on the Key Facts tab, or click Regenerate Facts... with guidance."));
        }

        // Rule 16
        var inFacts = new HashSet<string>(ctx.Facts.SelectMany(f => L(f.ActorIds).Concat(L(f.KnownByIds))), StringComparer.Ordinal);
        foreach (var member in ctx.Cast.Where(IsKeyPartyOrStaff))
        {
            var id = S(member.Id);
            if (!inFacts.Contains(id))
                findings.Add(Warning(GuideArea.Cast, id, $"{member.FullName} ({id}) is a key character but appears in no fact.")
                    .Fix($"Add {member.FullName} to a fact under Who did it or Who knew, or change {member.FirstName}'s tier to Background."));
        }
    }

    // Counsel counts on top of the preset key range, so only parties and staff are "key characters"
    private static bool IsKeyPartyOrStaff(CastMember member) =>
        member.Tier == CastTier.Key && member.Role is CastRole.Party or CastRole.Staff;

    // ---- Rule 17: counsel coverage (GUIDE-03, D-15) ----

    // Party organizations and an individual party's personal organization; an individual placed at an employer is covered
    // by the employer's counsel
    private static void CheckCounsel(Context ctx, List<GuideFinding> findings)
    {
        foreach (var org in ctx.Organizations.Where(CaseGuideGenerator.NeedsCounsel))
        {
            var id = S(org.Id);
            var name = S(org.Name);

            // Individuals have no in-house legal department, only outside counsel (W2)
            if (org.Kind == OrganizationKind.Party
                && !ctx.Cast.Any(c => S(c.OrganizationId) == id && c.Role == CastRole.InHouseCounsel))
            {
                findings.Add(Warning(GuideArea.Guide, id, $"{name} has no in-house counsel.")
                    .Fix($"Click Regenerate Cast to add in-house counsel for {name}, or add a character with the In-house counsel role.",
                        GuideQuickFix.RegenerateCast, goTo: GuideArea.Cast));
            }

            var represented = ctx.Organizations.Any(firm =>
                firm.Kind == OrganizationKind.LawFirm
                && L(firm.RepresentsOrgIds).Contains(id)
                && HasOutsideCounsel(ctx, S(firm.Id), "Partner")
                && HasOutsideCounsel(ctx, S(firm.Id), "Associate"));

            if (!represented)
                findings.Add(Warning(GuideArea.Guide, id, $"{name} has no outside law firm with a partner and an associate.")
                    .Fix($"Click Regenerate Cast to add an outside law firm for {name} with a partner and an associate.",
                        GuideQuickFix.RegenerateCast, goTo: GuideArea.Cast));
        }
    }

    private static bool HasOutsideCounsel(Context ctx, string firmId, string titleWord) =>
        ctx.Cast.Any(c => S(c.OrganizationId) == firmId
            && c.Role == CastRole.OutsideCounsel
            && S(c.Title).Contains(titleWord, StringComparison.OrdinalIgnoreCase));

    // ---- Rule 18: preset ranges ----

    private static void CheckPreset(Context ctx, List<GuideFinding> findings)
    {
        var preset = ctx.Draft.Preset;

        var keyCount = ctx.Cast.Count(IsKeyPartyOrStaff);
        if (keyCount < preset.CharactersMin || keyCount > preset.CharactersMax)
            findings.Add(Warning(GuideArea.Guide, string.Empty,
                $"The cast has {keyCount} key characters (not counting counsel); the {preset.Level} preset expects {ComplexityPreset.FormatRange(preset.CharactersMin, preset.CharactersMax)}.")
                .Fix(keyCount < preset.CharactersMin
                    ? "Add key characters, or click Regenerate Cast... with guidance."
                    : "Change some key characters' tier to Background, or click Regenerate Cast... with guidance.", goTo: GuideArea.Cast));

        foreach (var org in ctx.Organizations.Where(o => o.Kind != OrganizationKind.Individual))
        {
            var id = S(org.Id);
            var background = ctx.Cast.Count(c => S(c.OrganizationId) == id && c.Tier == CastTier.Background);
            if (background < preset.BackgroundPerOrg)
                findings.Add(Warning(GuideArea.Guide, id,
                    $"{S(org.Name)} has {background} background staff; the {preset.Level} preset expects about {preset.BackgroundPerOrg}.")
                    .Fix($"Add background characters at {S(org.Name)}, or click Regenerate Cast... to write more.", goTo: GuideArea.Cast));
        }

        var factCount = ctx.Facts.Count;
        if (factCount < preset.FactsMin || factCount > preset.FactsMax)
            findings.Add(Warning(GuideArea.Guide, string.Empty,
                $"The guide has {factCount} key facts; the {preset.Level} preset expects {ComplexityPreset.FormatRange(preset.FactsMin, preset.FactsMax)}.")
                .Fix(factCount < preset.FactsMin
                    ? "Add facts, or click Regenerate Facts... with guidance."
                    : "Delete minor facts, or click Regenerate Facts... with guidance.", goTo: GuideArea.Facts));
    }

    // ---- Rule 19: canonical values (GUIDE-06) ----

    private static void CheckValues(Context ctx, List<GuideFinding> findings)
    {
        var earlier = new List<CanonicalValue>();
        foreach (var value in ctx.Values)
        {
            var id = S(value.Id);
            var label = S(value.Label).Trim();
            var text = S(value.Value).Trim();

            if (text.Length == 0)
                findings.Add(Error(GuideArea.Values, id, $"{id} has no exact value.")
                    .Fix($"Type the exact value for {id} under Value, or click Regenerate Value.", GuideQuickFix.RegenerateEntry,
                        GuideFindingField.Value));

            foreach (var factId in L(value.FactIds).Distinct(StringComparer.Ordinal))
            {
                if (!ctx.FactIds.Contains(factId))
                    findings.Add(Error(GuideArea.Values, id, $"{id} links to fact {factId}, which does not exist.")
                        .Fix($"Remove the link to {factId} from {id}, or pick a different fact.", GuideQuickFix.RemoveBrokenLinks,
                            GuideFindingField.LinkedFacts, target: factId));
            }

            if (text.Length > 0)
            {
                var labelConflict = label.Length == 0 ? null : earlier.FirstOrDefault(e =>
                    string.Equals(S(e.Label).Trim(), label, StringComparison.OrdinalIgnoreCase)
                    && S(e.Value).Trim().Length > 0
                    && !string.Equals(S(e.Value).Trim(), text, StringComparison.Ordinal));
                if (labelConflict != null)
                    findings.Add(Error(GuideArea.Values, id,
                        $"\"{S(labelConflict.Label).Trim()}\" has two different values: {S(labelConflict.Value).Trim()} and {text}.")
                        .Fix($"Make {S(labelConflict.Id)} and {id} use the same value, or give one of them a different label.",
                            field: GuideFindingField.Value));

                var valueTwin = earlier.FirstOrDefault(e =>
                    string.Equals(S(e.Value).Trim(), text, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(S(e.Label).Trim(), label, StringComparison.OrdinalIgnoreCase));
                if (valueTwin != null)
                    findings.Add(Warning(GuideArea.Values, id,
                        $"{S(valueTwin.Value).Trim()} is registered under two labels: {S(valueTwin.Label).Trim()} and {label}.")
                        .Fix($"Use one label for {S(valueTwin.Value).Trim()}: change the label on {id} or {S(valueTwin.Id)}, or delete one of them.",
                            field: GuideFindingField.Label));
            }

            earlier.Add(value);
        }

        CheckScannedValues(ctx, findings);
    }

    private static void CheckScannedValues(Context ctx, List<GuideFinding> findings)
    {
        var coreTexts = (ctx.Doc.Sections ?? new())
            .Where(s => s != null)
            .SelectMany(s => L(s.Paragraphs))
            .ToList();
        var factDescriptions = ctx.Facts.Select(f => S(f.Description)).ToList();

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scannedAmounts = new List<string>();

        // Core document first so a value found in both is reported once, against the core document
        foreach (var (texts, where) in new[] { (coreTexts, "core document"), (factDescriptions, "key facts") })
        {
            foreach (var text in texts)
            {
                foreach (var scanned in CanonicalValueScanner.Scan(text))
                {
                    if (scanned.Kind == ScannedKind.Amount)
                        scannedAmounts.Add(scanned.Text);

                    if (!CanonicalValueScanner.IsRegistered(scanned, ctx.Values) && reported.Add(scanned.Text))
                        findings.Add(Warning(GuideArea.Values, string.Empty,
                            $"{scanned.Text} appears in the {where} but is not in the canonical values.")
                            .Fix($"Click Add Value to register {scanned.Text}, or click Regenerate Values... to collect it.",
                                GuideQuickFix.RegenerateValues));
                }
            }
        }

        var allTexts = coreTexts.Concat(ctx.Facts.Select(f => S(f.Title))).Concat(factDescriptions).ToList();
        foreach (var value in ctx.Values.Where(v => TextCheckedKinds.Contains(v.Kind)))
        {
            var text = S(value.Value).Trim();
            if (text.Length == 0)
                continue;

            var appears = allTexts.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase))
                || (value.Kind == ValueKind.Amount && scannedAmounts.Any(a => CanonicalValueScanner.AmountsEqual(a, text)));

            if (!appears)
                findings.Add(Warning(GuideArea.Values, S(value.Id), $"{S(value.Id)} ({text}) does not appear in the core document or any fact.")
                    .Fix($"Use {text} in the fact it belongs to, or delete {S(value.Id)} if the case does not need it.",
                        field: GuideFindingField.Value));
        }
    }

    // ---- Rule 20: real-name leaks (T-03-27) ----

    private static void CheckLeaks(Context ctx, List<GuideFinding> findings)
    {
        var subs = ctx.Draft.Substitutions ?? new List<EntitySubstitution>();

        foreach (var org in ctx.Organizations)
            ScanLeaks(findings, subs, GuideArea.Guide, S(org.Id), org.Name, org.ShortName);

        foreach (var member in ctx.Cast)
            ScanLeaks(findings, subs, GuideArea.Cast, S(member.Id), member.FullName, member.Title, member.Involvement, member.VoiceNote);

        foreach (var fact in ctx.Facts)
            ScanLeaks(findings, subs, GuideArea.Facts, S(fact.Id), fact.Title, fact.Description);

        foreach (var value in ctx.Values)
            ScanLeaks(findings, subs, GuideArea.Values, S(value.Id), value.Label, value.Value, value.Description);
    }

    private static void ScanLeaks(List<GuideFinding> findings, List<EntitySubstitution> subs, GuideArea area, string id, params string?[] fields)
    {
        var text = string.Join("\n", fields.Where(f => !string.IsNullOrWhiteSpace(f)));
        if (text.Length == 0)
            return;

        List<string> leaks;
        try
        {
            leaks = NameSubstitution.FindLeaks(text, subs);
        }
        catch (RegexMatchTimeoutException)
        {
            AddTooLarge(findings);
            return;
        }

        foreach (var term in leaks)
        {
            var message = $"{id} uses the real name \"{term}\".";

            // A lone surname or short name may be an ordinary word, so it only warns (mirrors CoreDocumentValidator)
            var finding = term.Any(char.IsWhiteSpace)
                ? Error(area, id, message)
                : Warning(area, id, message);

            var substitute = NameSubstitution.Apply(term, subs);
            var hint = substitute.Length > 0 && substitute != term
                ? $"Replace \"{term}\" with \"{substitute}\" in {id}."
                : $"Replace \"{term}\" in {id} with a fictional name.";
            findings.Add(finding.Fix(hint, GuideQuickFix.ReplaceRealNames, goTo: area == GuideArea.Guide ? GuideArea.Cast : null, value: term));
        }
    }

    // ---- Rules 21-22: unfinished and stale parts ----

    private static readonly (GuidePart Part, string Name, bool Plural, GuideArea Area)[] PartNames =
    {
        (GuidePart.Issues, "issues", true, GuideArea.Issues),
        (GuidePart.Cast, "cast", false, GuideArea.Cast),
        (GuidePart.Facts, "key facts", true, GuideArea.Facts),
        (GuidePart.Values, "canonical values", true, GuideArea.Values)
    };

    private static void CheckParts(Context ctx, List<GuideFinding> findings)
    {
        foreach (var (part, name, plural, area) in PartNames)
        {
            var status = ctx.Guide.State(part)?.Status ?? GuidePartStatus.Pending;
            if (status != GuidePartStatus.Ready)
                findings.Add(Error(GuideArea.Guide, string.Empty, $"The {name} {(plural ? "were" : "was")} not written.")
                    .Fix($"Click Resume Guide to write the {name}.", GuideQuickFix.ResumeGuide, goTo: area));
        }

        // Failed, or interrupted (Pending / left Generating) with issues still missing their details (CR-02)
        var issuesReady = ctx.Guide.IssuesState?.Status == GuidePartStatus.Ready;
        if (ctx.Guide.IssueDetailsState?.Status == GuidePartStatus.Failed
            || (issuesReady && ctx.Issues.Any(i => S(i.Description).Length == 0)))
            findings.Add(Warning(GuideArea.Guide, string.Empty, "The issue details were not written.")
                .Fix("Click Retry Issue Details to write them.", GuideQuickFix.RetryIssueDetails, goTo: GuideArea.Issues));

        foreach (var (part, name, plural, area) in PartNames)
        {
            // A null state was never stamped, so it cannot be stale
            if (ctx.Guide.State(part) == null || !GuideStaleness.IsStale(ctx.Draft, part))
                continue;

            var reason = GuideStaleness.StaleReason(ctx.Draft, part);
            findings.Add(Warning(GuideArea.Guide, string.Empty, $"The {name} {(plural ? "are" : "is")} out of date with the {reason}.")
                .Fix($"Click Update Guide to rewrite the {name}; entries you edited are kept.", GuideQuickFix.UpdateGuide, goTo: area));
        }
    }

    private static void CheckReview(Context ctx, List<GuideFinding> findings)
    {
        foreach (var fact in ctx.Facts.Where(f => f.NeedsReview))
            findings.Add(Warning(GuideArea.Facts, S(fact.Id), $"{S(fact.Id)} needs review: {S(fact.ReviewReason)}")
                .Fix($"Check that {S(fact.Id)} still reads correctly, then mark it reviewed.", GuideQuickFix.MarkReviewed));

        foreach (var value in ctx.Values.Where(v => v.NeedsReview))
        {
            var id = S(value.Id);
            var reason = S(value.ReviewReason);
            var factId = FactIdReference().Match(reason) is { Success: true } m ? m.Value : string.Empty;

            // A deleted fact (or a reason without a fact id) keeps the stored reason (W3)
            var finding = factId.Length > 0 && ctx.FactIds.Contains(factId)
                ? Warning(GuideArea.Values, id, $"{id} is linked to {factId}, which you changed.")
                : Warning(GuideArea.Values, id, $"{id} needs review: {reason}");
            findings.Add(finding.Fix($"Check that {id} still matches its facts, then mark it reviewed.", GuideQuickFix.MarkReviewed,
                GuideFindingField.Value));
        }
    }

    // ---- Rule 23: mapping notes ----

    private static void CheckMappingNotes(Context ctx, List<GuideFinding> findings)
    {
        foreach (var fact in ctx.Facts)
        {
            foreach (var note in L(fact.MappingNotes).Where(n => n.Length > 0))
                findings.Add(Warning(GuideArea.Facts, S(fact.Id), $"{S(fact.Id)}: {note}")
                    .Fix($"Check {S(fact.Id)} against the note and edit it if needed, or click Regenerate Fact."));
        }

        foreach (var value in ctx.Values)
        {
            foreach (var note in L(value.MappingNotes).Where(n => n.Length > 0))
                findings.Add(Warning(GuideArea.Values, S(value.Id), $"{S(value.Id)}: {note}")
                    .Fix($"Check {S(value.Id)} against the note and edit it if needed, or click Regenerate Value."));
        }
    }

    // ---- Rule 24: core document parties vs organizations (individual parties vs the cast) ----

    private static void CheckParties(Context ctx, List<GuideFinding> findings)
    {
        var parties = (ctx.Doc.Parties ?? new()).Where(p => p != null).ToList();

        foreach (var party in parties)
        {
            var partyId = S(party.Id);
            var partyName = S(party.Name).Trim();

            // An individual party is a person: some character must be them (linked by PartyId, or by name)
            if (CaseGuideGenerator.IsIndividual(party))
            {
                if (CaseGuideGenerator.FindPartyMember(ctx.Cast, party) == null)
                {
                    var missing = Error(GuideArea.Guide, string.Empty, $"Party {partyName} is not in the cast.");
                    var sameName = CaseGuideQuickFix.ExactNameMember(ctx.Draft, party);
                    findings.Add(sameName != null && partyId.Length > 0
                        ? missing.Fix($"Link {partyName} to {sameName.FullName} ({sameName.Id}), the character with the same name.",
                            GuideQuickFix.LinkParty, goTo: GuideArea.Cast, target: partyId)
                        : missing.Fix($"Click Regenerate Cast to add {partyName} as a character, or add a character named {partyName}.",
                            GuideQuickFix.RegenerateCast, goTo: GuideArea.Cast));
                }
                continue;
            }

            if (!ctx.Organizations.Any(o => partyId.Length > 0 && S(o.PartyId) == partyId))
            {
                var noOrg = Error(GuideArea.Guide, string.Empty, $"Party {partyName} has no organization.");
                var sameName = CaseGuideQuickFix.ExactNameOrganization(ctx.Draft, party);
                findings.Add(sameName != null && partyId.Length > 0
                    ? noOrg.Fix($"Link {partyName} to the organization {S(sameName.Name).Trim()} ({S(sameName.Id)}).",
                        GuideQuickFix.LinkParty, goTo: GuideArea.Cast, target: partyId)
                    : noOrg.Fix($"Click Regenerate Cast to add an organization for {partyName}.", GuideQuickFix.RegenerateCast,
                        goTo: GuideArea.Cast));
            }
        }

        foreach (var org in ctx.Organizations.Where(o => S(o.PartyId).Length > 0))
        {
            var party = parties.FirstOrDefault(p => S(p.Id) == S(org.PartyId));
            if (party == null)
            {
                // The party left the core document; until the guide is updated the organization still drives counsel checks (WR-10)
                findings.Add(Warning(GuideArea.Guide, S(org.Id),
                    $"{S(org.Name).Trim()} is no longer a party in the core document.")
                    .Fix("Click Update Guide to refresh the organizations from the core document's parties.", GuideQuickFix.UpdateGuide,
                        goTo: GuideArea.Cast));
                continue;
            }

            if (!string.Equals(S(org.Name).Trim(), S(party.Name).Trim(), StringComparison.OrdinalIgnoreCase))
                // "Inc." already ends the sentence; avoid a doubled period
                findings.Add(Warning(GuideArea.Guide, S(org.Id), $"{S(org.Name)} name differs from party {S(party.Name).Trim().TrimEnd('.')}.")
                    .Fix($"Use one name in both places: rename the party in the core document, or click Regenerate Cast to refresh the organization.",
                        goTo: GuideArea.Cast));
        }
    }

    // ---- Helpers ----

    private static string S(string? value) => value ?? string.Empty;

    // case.json is untrusted: null lists and null entries read as empty
    private static List<string> L(List<string>? list) =>
        list == null ? new List<string>() : list.Where(x => x != null).ToList();

    private static GuideFinding Error(GuideArea area, string entityId, string message) => new()
    {
        Severity = IssueSeverity.Error,
        Area = area,
        EntityId = entityId,
        Message = message
    };

    private static GuideFinding Warning(GuideArea area, string entityId, string message) => new()
    {
        Severity = IssueSeverity.Warning,
        Area = area,
        EntityId = entityId,
        Message = message
    };

    private static GuideFinding Fix(this GuideFinding finding, string hint, GuideQuickFix quickFix = GuideQuickFix.None,
        GuideFindingField field = GuideFindingField.None, GuideArea? goTo = null, string target = "", string value = "")
    {
        finding.FixHint = hint;
        finding.QuickFix = quickFix;
        finding.Field = field;
        finding.GoTo = goTo;
        finding.FixTargetId = target;
        finding.FixValue = value;
        return finding;
    }

    /// <summary>
    /// Null-cleaned snapshot of the guide lists plus id lookups shared by every rule.
    /// </summary>
    private class Context
    {
        public Context(CaseDraft draft)
        {
            Draft = draft;
            Doc = draft.CoreDocument!;
            Guide = draft.Guide!;

            Issues = Clean(Guide.Issues);
            Organizations = Clean(Guide.Organizations);
            Cast = Clean(Guide.Cast);
            Facts = Clean(Guide.Facts);
            Values = Clean(Guide.Values);

            IssueIds = new HashSet<string>(Issues.Select(i => S(i.Id)).Where(id => id.Length > 0), StringComparer.Ordinal);
            FactIds = new HashSet<string>(Facts.Select(f => S(f.Id)).Where(id => id.Length > 0), StringComparer.Ordinal);

            // First entry wins for duplicated ids; the duplicate itself is reported by rule 1
            CastById = new Dictionary<string, CastMember>(StringComparer.Ordinal);
            foreach (var member in Cast.Where(c => S(c.Id).Length > 0))
                CastById.TryAdd(member.Id, member);

            OrganizationById = new Dictionary<string, CaseOrganization>(StringComparer.Ordinal);
            foreach (var org in Organizations.Where(o => S(o.Id).Length > 0))
                OrganizationById.TryAdd(org.Id, org);
        }

        public CaseDraft Draft { get; }
        public CoreDocument Doc { get; }
        public CaseGuide Guide { get; }
        public List<CaseIssue> Issues { get; }
        public List<CaseOrganization> Organizations { get; }
        public List<CastMember> Cast { get; }
        public List<KeyFact> Facts { get; }
        public List<CanonicalValue> Values { get; }
        public HashSet<string> IssueIds { get; }
        public HashSet<string> FactIds { get; }
        public Dictionary<string, CastMember> CastById { get; }
        public Dictionary<string, CaseOrganization> OrganizationById { get; }

        private static List<T> Clean<T>(List<T>? list) where T : class =>
            list == null ? new List<T>() : list.Where(x => x != null).ToList();
    }
}
