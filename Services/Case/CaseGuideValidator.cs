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
/// Static, null-safe and never throws; Errors block Next.
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
        Warning(GuideArea.Guide, string.Empty, $"A check could not run: {ex.Message.TrimEnd('.')}.");

    private static void AddTooLarge(List<GuideFinding> findings)
    {
        if (!findings.Any(f => f.Message == TooLargeToScan))
            findings.Add(Warning(GuideArea.Guide, string.Empty, TooLargeToScan));
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

        if (all.Any(string.IsNullOrWhiteSpace))
            findings.Add(Error(area, string.Empty, "An entry has no ID."));

        foreach (var group in all.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id!, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
                findings.Add(Error(area, group.Key, $"Two entries use the ID {group.Key}."));
        }
    }

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
                    findings.Add(Error(GuideArea.Facts, id, $"{id} links to issue {issueId}, which does not exist."));
            }

            foreach (var castId in actorIds.Concat(knownByIds).Distinct(StringComparer.Ordinal))
            {
                if (!ctx.CastById.ContainsKey(castId))
                    findings.Add(Error(GuideArea.Facts, id, $"{id} refers to a character that does not exist ({castId})."));
            }

            // Rule 3: required links and title
            if (issueIds.Count == 0)
                findings.Add(Error(GuideArea.Facts, id, $"{id} is not linked to any issue."));
            if (actorIds.Count == 0)
                findings.Add(Error(GuideArea.Facts, id, $"{id} has no one under Who did it."));
            if (string.IsNullOrWhiteSpace(fact.Title))
                findings.Add(Error(GuideArea.Facts, id, $"{id} has no title."));

            // Rule 9: actors always knew
            foreach (var actorId in actorIds.Distinct(StringComparer.Ordinal))
            {
                if (ctx.CastById.TryGetValue(actorId, out var actor) && !knownByIds.Contains(actorId))
                    findings.Add(Warning(GuideArea.Facts, id, $"{id}: {actor.FullName} did it but is not listed under Who knew."));
            }

            // Rule 4
            var dateText = S(fact.Date);
            if (!CaseDraft.TryParseDate(dateText, out var date))
            {
                findings.Add(Error(GuideArea.Facts, id, $"{id} has no valid date. Use YYYY-MM-DD."));
                continue;
            }

            // Rule 5
            if (hasDocumentDate && date > documentDate)
                findings.Add(Error(GuideArea.Facts, id, $"{id} is dated {dateText}, after the {dateKind} date ({documentDateText})."));

            // Rule 6
            if (hasTimeline && (date < timelineStart || date > timelineEnd))
                findings.Add(Warning(GuideArea.Facts, id, $"{id} is dated {dateText}, outside the timeline ({S(doc.TimelineStart)} to {S(doc.TimelineEnd)})."));

            // Rule 7: actors must be active on the date
            foreach (var actorId in actorIds.Distinct(StringComparer.Ordinal))
            {
                if (ctx.CastById.TryGetValue(actorId, out var actor) && IsInactive(actor, date))
                    findings.Add(Error(GuideArea.Facts, id,
                        $"{id} is dated {dateText}, but {actor.FullName} ({actorId}) is only active {S(actor.ActiveFrom)} to {S(actor.ActiveTo)}."));
            }

            // Rule 8: knowers should be active on the date
            foreach (var knowerId in knownByIds.Distinct(StringComparer.Ordinal).Where(k => !actorIds.Contains(k)))
            {
                if (ctx.CastById.TryGetValue(knowerId, out var knower) && IsInactive(knower, date))
                    findings.Add(Warning(GuideArea.Facts, id,
                        $"{id} is dated {dateText}, but {knower.FullName} ({knowerId}), who knew about it, is only active {S(knower.ActiveFrom)} to {S(knower.ActiveTo)}."));
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

            // Rule 10
            if (name.Length == 0)
                findings.Add(Error(GuideArea.Cast, id, $"{id} has no name."));
            if (!TryRange(member, out _, out _))
                findings.Add(Error(GuideArea.Cast, id, $"{name} ({id}) has an active range that is missing or ends before it starts."));

            // Rule 11
            if (!EmailIdentity.IsWellFormed(email))
            {
                findings.Add(Error(GuideArea.Cast, id, $"{name} ({id}) has an invalid email address."));
            }
            else
            {
                if (emailUse.TryGetValue(email, out var uses) && uses > 1)
                    findings.Add(Error(GuideArea.Cast, id, $"{email} is used by more than one character."));

                if (EmailIdentity.IsReservedDomain(email))
                    findings.Add(Warning(GuideArea.Cast, id, $"{name}'s email uses a reserved test domain."));

                if (ctx.OrganizationById.TryGetValue(S(member.OrganizationId), out var org) && !member.EmailIsUserEdited)
                {
                    var orgDomain = S(org.Domain).Trim().TrimEnd('.').ToLowerInvariant();
                    if (orgDomain.Length > 0 && EmailIdentity.DomainOf(email) != orgDomain)
                        findings.Add(Warning(GuideArea.Cast, id, $"{name}'s email does not use {S(org.Name)}'s domain ({orgDomain})."));
                }
            }

            // Rule 12
            if (!ctx.OrganizationById.ContainsKey(S(member.OrganizationId)))
                findings.Add(Error(GuideArea.Cast, id, $"{name} ({id}) belongs to an organization that does not exist ({S(member.OrganizationId)})."));
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
                findings.Add(Error(GuideArea.Guide, S(org.Id), $"{S(first.Name)} and {S(org.Name)} use the same email domain ({domain})."));
            else
                seen[domain] = org;
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
                findings.Add(Warning(GuideArea.Issues, id, $"{id} comes from a count that is no longer in the core document."));

            // Rule 15
            if (!linkedIssues.Contains(id))
                findings.Add(Warning(GuideArea.Issues, id, $"No fact is linked to {id} ({S(issue.Title)})."));
        }

        // Rule 16
        var inFacts = new HashSet<string>(ctx.Facts.SelectMany(f => L(f.ActorIds).Concat(L(f.KnownByIds))), StringComparer.Ordinal);
        foreach (var member in ctx.Cast.Where(IsKeyPartyOrStaff))
        {
            var id = S(member.Id);
            if (!inFacts.Contains(id))
                findings.Add(Warning(GuideArea.Cast, id, $"{member.FullName} ({id}) is a key character but appears in no fact."));
        }
    }

    // Counsel counts on top of the preset key range, so only parties and staff are "key characters"
    private static bool IsKeyPartyOrStaff(CastMember member) =>
        member.Tier == CastTier.Key && member.Role is CastRole.Party or CastRole.Staff;

    // ---- Rule 17: counsel coverage (GUIDE-03, D-15) ----

    private static void CheckCounsel(Context ctx, List<GuideFinding> findings)
    {
        foreach (var org in ctx.Organizations.Where(o => o.Kind is OrganizationKind.Party or OrganizationKind.Individual))
        {
            var id = S(org.Id);
            var name = S(org.Name);

            // Individuals have no in-house legal department, only outside counsel (W2)
            if (org.Kind == OrganizationKind.Party
                && !ctx.Cast.Any(c => S(c.OrganizationId) == id && c.Role == CastRole.InHouseCounsel))
            {
                findings.Add(Warning(GuideArea.Guide, id, $"{name} has no in-house counsel."));
            }

            var represented = ctx.Organizations.Any(firm =>
                firm.Kind == OrganizationKind.LawFirm
                && L(firm.RepresentsOrgIds).Contains(id)
                && HasOutsideCounsel(ctx, S(firm.Id), "Partner")
                && HasOutsideCounsel(ctx, S(firm.Id), "Associate"));

            if (!represented)
                findings.Add(Warning(GuideArea.Guide, id, $"{name} has no outside law firm with a partner and an associate."));
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
                $"The cast has {keyCount} key characters (not counting counsel); the {preset.Level} preset expects {ComplexityPreset.FormatRange(preset.CharactersMin, preset.CharactersMax)}."));

        foreach (var org in ctx.Organizations.Where(o => o.Kind != OrganizationKind.Individual))
        {
            var id = S(org.Id);
            var background = ctx.Cast.Count(c => S(c.OrganizationId) == id && c.Tier == CastTier.Background);
            if (background < preset.BackgroundPerOrg)
                findings.Add(Warning(GuideArea.Guide, id,
                    $"{S(org.Name)} has {background} background staff; the {preset.Level} preset expects about {preset.BackgroundPerOrg}."));
        }

        var factCount = ctx.Facts.Count;
        if (factCount < preset.FactsMin || factCount > preset.FactsMax)
            findings.Add(Warning(GuideArea.Guide, string.Empty,
                $"The guide has {factCount} key facts; the {preset.Level} preset expects {ComplexityPreset.FormatRange(preset.FactsMin, preset.FactsMax)}."));
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
                findings.Add(Error(GuideArea.Values, id, $"{id} has no exact value."));

            foreach (var factId in L(value.FactIds).Distinct(StringComparer.Ordinal))
            {
                if (!ctx.FactIds.Contains(factId))
                    findings.Add(Error(GuideArea.Values, id, $"{id} links to fact {factId}, which does not exist."));
            }

            if (text.Length > 0)
            {
                var labelConflict = label.Length == 0 ? null : earlier.FirstOrDefault(e =>
                    string.Equals(S(e.Label).Trim(), label, StringComparison.OrdinalIgnoreCase)
                    && S(e.Value).Trim().Length > 0
                    && !string.Equals(S(e.Value).Trim(), text, StringComparison.Ordinal));
                if (labelConflict != null)
                    findings.Add(Error(GuideArea.Values, id,
                        $"\"{S(labelConflict.Label).Trim()}\" has two different values: {S(labelConflict.Value).Trim()} and {text}."));

                var valueTwin = earlier.FirstOrDefault(e =>
                    string.Equals(S(e.Value).Trim(), text, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(S(e.Label).Trim(), label, StringComparison.OrdinalIgnoreCase));
                if (valueTwin != null)
                    findings.Add(Warning(GuideArea.Values, id,
                        $"{S(valueTwin.Value).Trim()} is registered under two labels: {S(valueTwin.Label).Trim()} and {label}."));
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
                            $"{scanned.Text} appears in the {where} but is not in the canonical values."));
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
                findings.Add(Warning(GuideArea.Values, S(value.Id), $"{S(value.Id)} ({text}) does not appear in the core document or any fact."));
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
            findings.Add(term.Any(char.IsWhiteSpace)
                ? Error(area, id, message)
                : Warning(area, id, message));
        }
    }

    // ---- Rules 21-22: unfinished and stale parts ----

    private static readonly (GuidePart Part, string Name, bool Plural)[] PartNames =
    {
        (GuidePart.Issues, "issues", true),
        (GuidePart.Cast, "cast", false),
        (GuidePart.Facts, "key facts", true),
        (GuidePart.Values, "canonical values", true)
    };

    private static void CheckParts(Context ctx, List<GuideFinding> findings)
    {
        foreach (var (part, name, plural) in PartNames)
        {
            var status = ctx.Guide.State(part)?.Status ?? GuidePartStatus.Pending;
            if (status != GuidePartStatus.Ready)
                findings.Add(Error(GuideArea.Guide, string.Empty, $"The {name} {(plural ? "were" : "was")} not written."));
        }

        if (ctx.Guide.IssueDetailsState?.Status == GuidePartStatus.Failed)
            findings.Add(Warning(GuideArea.Guide, string.Empty, "The issue details were not written. Click Retry Issue Details."));

        foreach (var (part, name, plural) in PartNames)
        {
            // A null state was never stamped, so it cannot be stale
            if (ctx.Guide.State(part) == null || !GuideStaleness.IsStale(ctx.Draft, part))
                continue;

            var reason = GuideStaleness.StaleReason(ctx.Draft, part);
            findings.Add(Warning(GuideArea.Guide, string.Empty, $"The {name} {(plural ? "are" : "is")} out of date with the {reason}."));
        }
    }

    private static void CheckReview(Context ctx, List<GuideFinding> findings)
    {
        foreach (var fact in ctx.Facts.Where(f => f.NeedsReview))
            findings.Add(Warning(GuideArea.Facts, S(fact.Id), $"{S(fact.Id)} needs review: {S(fact.ReviewReason)}"));

        foreach (var value in ctx.Values.Where(v => v.NeedsReview))
        {
            var id = S(value.Id);
            var reason = S(value.ReviewReason);
            var factId = FactIdReference().Match(reason) is { Success: true } m ? m.Value : string.Empty;

            // A deleted fact (or a reason without a fact id) keeps the stored reason (W3)
            findings.Add(factId.Length > 0 && ctx.FactIds.Contains(factId)
                ? Warning(GuideArea.Values, id, $"{id} is linked to {factId}, which you changed.")
                : Warning(GuideArea.Values, id, $"{id} needs review: {reason}"));
        }
    }

    // ---- Rule 23: mapping notes ----

    private static void CheckMappingNotes(Context ctx, List<GuideFinding> findings)
    {
        foreach (var fact in ctx.Facts)
        {
            foreach (var note in L(fact.MappingNotes).Where(n => n.Length > 0))
                findings.Add(Warning(GuideArea.Facts, S(fact.Id), $"{S(fact.Id)}: {note}"));
        }

        foreach (var value in ctx.Values)
        {
            foreach (var note in L(value.MappingNotes).Where(n => n.Length > 0))
                findings.Add(Warning(GuideArea.Values, S(value.Id), $"{S(value.Id)}: {note}"));
        }
    }

    // ---- Rule 24: core document parties vs organizations ----

    private static void CheckParties(Context ctx, List<GuideFinding> findings)
    {
        var parties = (ctx.Doc.Parties ?? new()).Where(p => p != null).ToList();

        foreach (var party in parties)
        {
            var partyId = S(party.Id);
            if (!ctx.Organizations.Any(o => partyId.Length > 0 && S(o.PartyId) == partyId))
                findings.Add(Error(GuideArea.Guide, string.Empty,
                    $"Party {S(party.Name)} has no organization. Click Update Guide or Regenerate Cast."));
        }

        foreach (var org in ctx.Organizations.Where(o => S(o.PartyId).Length > 0))
        {
            var party = parties.FirstOrDefault(p => S(p.Id) == S(org.PartyId));
            if (party == null)
                continue;

            if (!string.Equals(S(org.Name).Trim(), S(party.Name).Trim(), StringComparison.OrdinalIgnoreCase))
                // "Inc." already ends the sentence; avoid a doubled period
                findings.Add(Warning(GuideArea.Guide, S(org.Id), $"{S(org.Name)} name differs from party {S(party.Name).Trim().TrimEnd('.')}."));
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
