using System.Text;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Shared system prompt and user-prompt builders for every case guide call (D-01, D-02, D-03).
/// Prompts carry no JSON-format instructions: the strict schema defines the response shape.
/// </summary>
public static class CaseGuidePrompts
{
    // Byte-stable for prompt caching: never interpolate run data into this text (D-01, D-02)
    public const string SystemPrompt = "You build the internal case guide for a fictional e-discovery training dataset from a finished legal document: its issues, its cast of characters, its dated key facts and the exact values the evidence relies on. Keep each person, company and organization named in the scenario or the document exactly as named, including characters and companies from films, TV series, books or games, which are already fictional; never rename them. Invent names only for people, companies and law firms the document does not name, and make those invented names fictional. Never use the name of a real-world person, company, law firm or judge unless the user approved it. Take a firm position: state each fact as what happened, who did it, who knew about it and when, and where the money went, exactly as the document establishes it. Do not hedge, call anything alleged, unproven or unconfirmed, or contradict the document. Stay consistent with the document's names, dates, amounts and court. Refer to existing issues, organizations and characters only by the IDs listed in the request.";

    // Source text caps keep one request well inside the model's context
    private const int IssueSourceMaxChars = 60_000;
    private const int RosterSourceMaxChars = 120_000;

    /// <summary>
    /// Name rules for every guide prompt: keep the document's names, the applied substitutions and the names the user kept (D-01, CASE-03).
    /// </summary>
    internal static void AppendNameRules(StringBuilder sb, CaseDraft draft)
    {
        sb.AppendLine("Keep every name the document uses exactly as written, including names from films, TV series, books or games.");

        var applied = draft.Substitutions
            .Where(s => s != null && s.Apply && !string.IsNullOrWhiteSpace(s.Original) && !string.IsNullOrWhiteSpace(s.Substitute))
            .ToList();
        if (applied.Count > 0)
        {
            sb.AppendLine($"Use these fictional names exactly: {string.Join(", ", applied.Select(s => s.Substitute.Trim()))}");
            sb.AppendLine($"Never use these real names: {string.Join(", ", applied.Select(s => s.Original.Trim()))}");
        }

        // CASE-03 override: names the user confirmed keeping. Stated here, not in the byte-stable system prompt.
        var kept = draft.Substitutions
            .Where(s => s != null && !s.Apply && !string.IsNullOrWhiteSpace(s.Original))
            .Select(s => s.Original.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (kept.Count > 0)
            sb.AppendLine($"The user has approved keeping these names exactly as written; use them unchanged as an exception to the fictional-names rule: {string.Join(", ", kept)}");
    }

    /// <summary>
    /// "Guidance from the user: ..." after a blank line; nothing when the guidance is blank.
    /// </summary>
    internal static void AppendGuidance(StringBuilder sb, string guidance)
    {
        if (string.IsNullOrWhiteSpace(guidance))
            return;

        sb.AppendLine();
        sb.AppendLine("Guidance from the user: " + guidance.Trim());
    }

    internal static string BuildIssueDetailsPrompt(CaseDraft draft, IReadOnlyList<CaseIssue> issues)
    {
        var doc = RequireDocument(draft);
        var sb = new StringBuilder();

        AppendDocumentHeader(sb, doc);
        sb.AppendLine();
        sb.AppendLine("Issues:");
        foreach (var issue in issues)
            sb.AppendLine($"{issue.Id} | {IssueExtractor.SourceLabel(doc, issue)} | {issue.Title} | legal basis: {issue.LegalBasis} | {issue.Summary}");

        sb.AppendLine();
        sb.AppendLine("Source text:");
        sb.AppendLine(IssueSourceText(doc, issues));
        sb.AppendLine();
        sb.AppendLine("For each issue ID write: description (2-3 sentences, firm position), elements (3-6 short items that must be proven), relevanceCriteria (2-3 sentences describing what responsive evidence in an email corpus looks like).");
        AppendNameRules(sb, draft);

        return sb.ToString();
    }

    /// <summary>
    /// Roster call. When the guide already has key characters (Regenerate Cast), they are listed by id for revision;
    /// hand-edited ones are listed as keep-exactly unless <paramref name="replaceEdited"/> (D-19).
    /// </summary>
    internal static string BuildRosterPrompt(CaseDraft draft, string guidance = "", bool replaceEdited = false)
    {
        var doc = RequireDocument(draft);
        var guide = draft.Guide ?? new CaseGuide();
        var preset = draft.Preset;
        var sb = new StringBuilder();

        AppendDocumentHeader(sb, doc);
        AppendTimeline(sb, doc);
        sb.AppendLine();
        AppendOrganizations(sb, guide);

        sb.AppendLine("Parties:");
        foreach (var party in doc.Parties.Where(p => p != null))
            sb.AppendLine($"- {party.Name} ({party.ShortName}), {party.Role}, {party.EntityType}, {party.Domicile}: {party.Description}");

        var existing = guide.Cast.Where(c => c != null && c.Tier == CastTier.Key).ToList();
        AppendExisting(sb, "Current roster (revise by existingId; leave existingId empty for new people):",
            existing.Select(m => (m.IsUserEdited && !replaceEdited, RosterLine(m))));
        if (existing.Any(m => !m.IsUserEdited || replaceEdited))
            sb.AppendLine("Return the whole revised roster: the current people you keep, with their existingId, and any new people. People you leave out are removed.");

        sb.AppendLine();
        sb.AppendLine("Document:");
        sb.AppendLine(Cap(CoreDocumentAssembler.RenderPlainText(doc), RosterSourceMaxChars));
        sb.AppendLine();

        sb.AppendLine("Requirements:");
        sb.AppendLine($"Name {preset.CharactersMin}-{preset.CharactersMax} key people who are not counsel: everyone the document names, then the people the story needs.");
        foreach (var org in guide.Organizations.Where(o => o != null && o.Kind == OrganizationKind.Party))
            sb.AppendLine($"{org.Id} {org.Name}: one in-house counsel, and an outside law firm with at least one partner and one associate (add the firm under organizations).");
        foreach (var org in guide.Organizations.Where(o => o != null && o.Kind == OrganizationKind.Individual))
            sb.AppendLine($"{org.Id} {org.Name} (individual): an outside law firm with at least one partner and one associate.");

        if (CoreDocumentTemplates.IsPleading(doc.DocumentType))
        {
            if (!string.IsNullOrWhiteSpace(doc.Counsel.AttorneyName))
                sb.AppendLine($"{doc.Counsel.AttorneyName.Trim()} of {doc.Counsel.FirmName.Trim()} signed the document and must be in the cast as outside counsel.");
        }
        else if (!string.IsNullOrWhiteSpace(doc.Memo.From))
        {
            sb.AppendLine($"{doc.Memo.From.Trim()} wrote the memo and must be in the cast.");
        }

        sb.AppendLine("Refer to organizations by ID, or by the exact name of an organization you add in this response.");
        sb.AppendLine("For each person give a one-line summary of their involvement.");
        AppendNameRules(sb, draft);
        AppendGuidance(sb, guidance);

        return sb.ToString();
    }

    internal static string BuildCastDetailsPrompt(CaseDraft draft, IReadOnlyList<CastMember> people)
    {
        var doc = RequireDocument(draft);
        var guide = draft.Guide ?? new CaseGuide();
        var sb = new StringBuilder();

        AppendDocumentHeader(sb, doc);
        var summary = doc.Sections
            .Where(s => s != null && s.Kind is SectionKind.NatureOfAction or SectionKind.ExecutiveSummary)
            .SelectMany(s => s.Paragraphs)
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (summary != null)
            sb.AppendLine($"Case summary: {summary.Trim()}");
        AppendTimeline(sb, doc);

        if (guide.Issues.Count > 0)
        {
            sb.AppendLine("Issues:");
            foreach (var issue in guide.Issues.Where(i => i != null))
                sb.AppendLine($"{issue.Id} | {issue.Title} | {(issue.Description.Length > 0 ? issue.Description : issue.Summary)}");
        }

        sb.AppendLine();
        AppendOrganizations(sb, guide);
        sb.AppendLine("Characters:");
        foreach (var member in people)
            sb.AppendLine(CastLine(guide, member));

        sb.AppendLine();
        sb.AppendLine($"For each CH ID write a 2-4 sentence summary of their key involvement stated as fact, the dates they were active in the story (YYYY-MM-DD, inside {doc.TimelineStart} to {StoryEnd(doc)}), a 1-2 line note on how they write email, and a signature style.");
        AppendNameRules(sb, draft);

        return sb.ToString();
    }

    internal static string BuildBackgroundPrompt(CaseDraft draft, CaseOrganization org, int count, string guidance = "")
    {
        var doc = RequireDocument(draft);
        var guide = draft.Guide ?? new CaseGuide();
        var sb = new StringBuilder();

        AppendDocumentHeader(sb, doc);
        sb.AppendLine();

        var names = guide.Cast.Where(c => c != null).Select(c => c.FullName).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        sb.AppendLine($"Write {count} background staff for {org.Name} ({org.Kind}, {org.Location}): ordinary employees who send routine, non-relevant email. " +
            $"Give each a realistic title, department, a one-line role, and active dates inside {doc.TimelineStart} to {StoryEnd(doc)}. " +
            $"Do not reuse these names: {(names.Count == 0 ? "(none)" : string.Join(", ", names))}.");
        AppendNameRules(sb, draft);
        AppendGuidance(sb, guidance);

        return sb.ToString();
    }

    internal static string BuildCounselTopUpPrompt(CaseDraft draft, IReadOnlyList<string> gaps)
    {
        var doc = RequireDocument(draft);
        var guide = draft.Guide ?? new CaseGuide();
        var sb = new StringBuilder();

        AppendDocumentHeader(sb, doc);
        sb.AppendLine();
        AppendOrganizations(sb, guide);

        var counsel = guide.Cast.Where(c => c != null && c.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel).ToList();
        sb.AppendLine("Current counsel:");
        if (counsel.Count == 0)
            sb.AppendLine("(none)");
        foreach (var member in counsel)
            sb.AppendLine($"{member.Id} | {member.FullName} | {member.Title} | {OrgName(guide, member.OrganizationId)} | {member.Role}");

        sb.AppendLine();
        sb.AppendLine("Add only these missing counsel, and any law firm they need under organizations:");
        foreach (var gap in gaps)
            sb.AppendLine("- " + gap);
        sb.AppendLine("Refer to organizations by ID, or by the exact name of an organization you add in this response.");
        sb.AppendLine("For each person give a one-line summary of their involvement.");
        AppendNameRules(sb, draft);

        return sb.ToString();
    }

    /// <summary>
    /// One fact section (or one date slice of it): its paragraphs, every issue, the compact cast and the target count (GUIDE-05, D-02).
    /// <paramref name="existing"/> lists this unit's current facts for regeneration; hand-edited ones are listed as keep-exactly.
    /// </summary>
    internal static string BuildFactsPrompt(CaseDraft draft, FactSubsectionPlan plan, string from, string to, int target, IReadOnlyList<KeyFact> existing, string guidance = "", bool replaceEdited = false)
    {
        var doc = RequireDocument(draft);
        var guide = draft.Guide ?? new CaseGuide();
        var sb = new StringBuilder();

        AppendDocumentHeader(sb, doc);
        AppendTimeline(sb, doc);
        sb.AppendLine();

        sb.AppendLine(doc.DocumentType == CoreDocumentType.InvestigationMemo ? "Findings:" : "Counts:");
        foreach (var count in doc.Counts.Where(c => c != null))
            sb.AppendLine($"{count.Number}. {count.Title}: {count.Summary}");
        sb.AppendLine();

        sb.AppendLine($"Section: {plan.Heading} ({plan.StartDate} to {plan.EndDate})");
        if (!string.IsNullOrWhiteSpace(plan.Focus))
            sb.AppendLine($"Focus: {plan.Focus.Trim()}");
        var paragraphs = doc.FindSection(plan.Id)?.Paragraphs ?? new List<string>();
        sb.AppendLine("Section text:");
        sb.AppendLine(Cap(string.Join("\n\n", paragraphs.Where(p => !string.IsNullOrWhiteSpace(p))), IssueSourceMaxChars));
        sb.AppendLine();

        sb.AppendLine("Issues:");
        foreach (var issue in guide.Issues.Where(i => i != null))
            sb.AppendLine($"{issue.Id} | {issue.Title} | {(issue.Description.Length > 0 ? issue.Description : issue.Summary)}");
        sb.AppendLine();

        sb.AppendLine("Cast:");
        foreach (var member in guide.Cast.Where(c => c != null))
            sb.AppendLine($"{member.Id} | {member.FullName} | {member.Title} | {OrgName(guide, member.OrganizationId)} | active {member.ActiveFrom} to {member.ActiveTo}");

        AppendExisting(sb, "Existing facts (revise by existingId; return new facts with an empty existingId):",
            existing.Where(f => f != null).Select(f => (f.IsUserEdited && !replaceEdited, $"{f.Id} | {f.Date} | {f.Title}")));

        sb.AppendLine();
        sb.AppendLine($"Write about {target} facts dated {from} to {to}, drawn from this section and consistent with the whole document.");
        sb.AppendLine("State each fact as what happened: who did it, who knew about it and when, and the amounts involved.");
        sb.AppendLine("For each fact give the date (YYYY-MM-DD), a short title, a 1-3 sentence description, the issue IDs it supports, the IDs of the people who did it, the IDs of everyone else who knew about it at the time, and its significance (Hot, Key or Supporting).");
        sb.AppendLine("Refer to issues and people only by the IDs listed above.");
        AppendNameRules(sb, draft);
        AppendGuidance(sb, guidance);

        return sb.ToString();
    }

    /// <summary>
    /// The canonical values registry: every fact, plus the amounts and identifiers the scanner found (GUIDE-06).
    /// </summary>
    internal static string BuildValuesPrompt(CaseDraft draft, IReadOnlyList<ScannedValue> scanned, IReadOnlyList<CanonicalValue> existing, string guidance = "", bool replaceEdited = false)
    {
        var doc = RequireDocument(draft);
        var guide = draft.Guide ?? new CaseGuide();
        var sb = new StringBuilder();

        AppendDocumentHeader(sb, doc);
        sb.AppendLine();

        sb.AppendLine("Key facts:");
        foreach (var fact in guide.Facts.Where(f => f != null))
            sb.AppendLine($"{fact.Id} | {fact.Date} | {fact.Title} | {fact.Description}");
        sb.AppendLine();

        var factText = string.Join("\n\n", doc.Sections
            .Where(s => s != null && s.Kind == SectionKind.FactualAllegations)
            .Select(s => $"== {s.Heading} ==\n" + string.Join("\n\n", s.Paragraphs)));
        if (factText.Length > 0)
        {
            sb.AppendLine("Fact sections of the document:");
            sb.AppendLine(Cap(factText, IssueSourceMaxChars));
            sb.AppendLine();
        }

        var hits = scanned.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Text)).Select(s => s.Text.Trim()).ToList();
        if (hits.Count > 0)
            sb.AppendLine($"These amounts and identifiers appear in the document; register each one exactly as written: {string.Join(", ", hits)}");

        AppendExisting(sb, "Existing values (revise by existingId; return new values with an empty existingId):",
            existing.Where(v => v != null).Select(v => (v.IsUserEdited && !replaceEdited, $"{v.Id} | {v.Label} | {v.Value}")));

        sb.AppendLine();
        sb.AppendLine("Register every amount, invoice number, change-order number, contract number, purchase order, account number, document title, project name and address the facts rely on. Use the exact text a document or email would use (for example $184,250.00). Give each a short label and link it to the fact IDs that use it.");
        sb.AppendLine($"Kinds: {string.Join(", ", Enum.GetNames<ValueKind>())}");
        sb.AppendLine("Refer to facts only by the IDs listed above.");
        AppendNameRules(sb, draft);
        AppendGuidance(sb, guidance);

        return sb.ToString();
    }

    // Current entries for regeneration; protected (hand-edited) ones must come back unchanged (D-19)
    private static void AppendExisting(StringBuilder sb, string header, IEnumerable<(bool IsProtected, string Line)> entries)
    {
        var list = entries.ToList();
        var editable = list.Where(e => !e.IsProtected).Select(e => e.Line).ToList();
        var kept = list.Where(e => e.IsProtected).Select(e => e.Line).ToList();

        if (editable.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(header);
            foreach (var line in editable)
                sb.AppendLine(line);
        }

        if (kept.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Keep exactly; do not return these:");
            foreach (var line in kept)
                sb.AppendLine(line);
        }
    }

    /// <summary>
    /// Last date of the story: filing date, else memo date, else timeline end (yyyy-MM-dd; may be empty).
    /// </summary>
    internal static string StoryEnd(CoreDocument doc)
    {
        if (CaseDraft.TryParseDate(doc.FilingDate, out _))
            return doc.FilingDate;
        if (CaseDraft.TryParseDate(doc.Memo?.Date, out _))
            return doc.Memo!.Date;
        return doc.TimelineEnd ?? string.Empty;
    }

    private static void AppendTimeline(StringBuilder sb, CoreDocument doc)
    {
        sb.AppendLine($"Timeline: {doc.TimelineStart} to {doc.TimelineEnd}");
        sb.AppendLine(doc.DocumentType == CoreDocumentType.InvestigationMemo
            ? $"Memo date: {StoryEnd(doc)}"
            : $"Filing date: {StoryEnd(doc)}");
    }

    private static void AppendOrganizations(StringBuilder sb, CaseGuide guide)
    {
        sb.AppendLine("Organizations:");
        foreach (var org in guide.Organizations.Where(o => o != null))
            sb.AppendLine($"{org.Id} | {org.Name} | {org.Kind} | {org.Location}");
    }

    // "CH-001 | Maria Delgado | President | ORG-01 | Party | involvement"
    private static string RosterLine(CastMember member) =>
        $"{member.Id} | {member.FullName} | {member.Title} | {member.OrganizationId} | {member.Role} | {member.Involvement}";

    private static string CastLine(CaseGuide guide, CastMember member) =>
        $"{member.Id} | {member.FullName} | {member.Title} | {OrgName(guide, member.OrganizationId)} | {member.Role} | {member.Involvement}";

    private static string OrgName(CaseGuide guide, string orgId) => guide.FindOrganization(orgId)?.Name ?? orgId;

    private static CoreDocument RequireDocument(CaseDraft draft) =>
        draft.CoreDocument ?? throw new InvalidOperationException("Write the core document before building the case guide.");

    private static void AppendDocumentHeader(StringBuilder sb, CoreDocument doc)
    {
        sb.AppendLine($"Document type: {doc.DocumentType}");
        if (doc.DocumentType == CoreDocumentType.InvestigationMemo)
            sb.AppendLine($"Memo: TO {doc.Memo.To}; FROM {doc.Memo.From}; RE {doc.Memo.Re}; organization {doc.Memo.Organization}; date {doc.Memo.Date}");
        else
            sb.AppendLine($"Court: {doc.Jurisdiction.CourtName} ({doc.Jurisdiction.District})");
        sb.AppendLine($"Title: {doc.Title}");
    }

    // The count/finding sections for the listed issues; the whole document when none are found
    private static string IssueSourceText(CoreDocument doc, IReadOnlyList<CaseIssue> issues)
    {
        var countIds = issues.Select(i => i.CountId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.Ordinal);
        var sections = doc.Sections
            .Where(s => s != null && countIds.Contains(s.Id) && s.Paragraphs.Count > 0)
            .ToList();

        var text = sections.Count > 0
            ? string.Join("\n\n", sections.Select(s => $"== {s.Heading} ==\n" + string.Join("\n\n", s.Paragraphs)))
            : CoreDocumentAssembler.RenderPlainText(doc);

        return Cap(text, IssueSourceMaxChars);
    }

    private static string Cap(string text, int max) => text.Length <= max ? text : text[..max];
}
