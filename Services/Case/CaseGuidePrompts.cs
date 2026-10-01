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
