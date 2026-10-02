using System.Text;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Shared system prompt and guide-context helpers for every corpus-planning call (D-01, D-02, Pitfall 8).
/// Prompts carry no format instructions: the strict schema defines the response shape.
/// </summary>
public static class CorpusPlanPrompts
{
    // Byte-stable for prompt caching: never interpolate run data into this text (D-01, D-02)
    public const string SystemPrompt = "You plan and describe the email evidence for an e-discovery training dataset built from a finished case guide. Keep each person, company and organization exactly as named in the guide, including characters and companies from films, TV series, books or games. Take a firm position on what happened, as the case guide states it. Do not hedge. Write subjects, purposes, intents and points in plain business language, the way coworkers write, with no legal terms such as alleged, plaintiff, defendant or liability. Use the exact names, dates and values you are given and never invent people or IDs.";

    /// <summary>
    /// Case title, core document type and one line per issue: "ISS-01 Title: RelevanceCriteria".
    /// </summary>
    internal static void AppendCaseSynopsis(StringBuilder sb, CaseDraft draft)
    {
        sb.AppendLine($"Case: {CaseFolderNaming.CaseTitle(draft)}");

        var doc = draft.CoreDocument;
        if (doc != null)
        {
            sb.AppendLine($"Core document: {DocumentTypeText(doc.DocumentType)}" + (string.IsNullOrWhiteSpace(doc.Title) ? string.Empty : $", \"{doc.Title.Trim()}\""));
        }

        var issues = draft.Guide?.Issues ?? new List<CaseIssue>();
        if (issues.Count == 0)
            return;

        sb.AppendLine("Issues:");
        foreach (var issue in issues)
            sb.AppendLine($"{issue.Id} {issue.Title}: {issue.RelevanceCriteria}");
    }

    /// <summary>
    /// One line per cast member: "CH-001 Maria Delgado, President, Brightline Glazing, LLC (Party)".
    /// When <paramref name="onlyIds"/> is given, only those members are listed (in cast order).
    /// </summary>
    internal static void AppendCast(StringBuilder sb, CaseGuide guide, IEnumerable<string>? onlyIds = null)
    {
        var filter = onlyIds == null ? null : new HashSet<string>(onlyIds, StringComparer.OrdinalIgnoreCase);
        foreach (var member in guide.Cast)
        {
            if (filter != null && !filter.Contains(member.Id))
                continue;
            sb.AppendLine($"{member.Id} {Describe(guide, member)} ({RoleText(member)})");
        }
    }

    /// <summary>
    /// One line per canonical value: "{Label} = {Value}".
    /// </summary>
    internal static void AppendValues(StringBuilder sb, CaseGuide guide)
    {
        foreach (var value in guide.Values)
        {
            if (string.IsNullOrWhiteSpace(value.Label) && string.IsNullOrWhiteSpace(value.Value))
                continue;
            sb.AppendLine($"{value.Label.Trim()} = {value.Value.Trim()}");
        }
    }

    /// <summary>
    /// The review protocol prose request. Built from the case guide only: never plan data, document numbers,
    /// per-document labels or counts (D-34, T-04-19).
    /// </summary>
    public static string BuildProtocolPrompt(CaseDraft draft)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var sb = new StringBuilder();

        CaseGuidePrompts.AppendNameRules(sb, draft);
        sb.AppendLine();
        AppendCaseSynopsis(sb, draft);

        var parties = draft.CoreDocument?.Parties?.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList() ?? new List<CaseParty>();
        if (parties.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Parties:");
            foreach (var party in parties)
                sb.AppendLine($"{party.Name.Trim()} ({party.Role})" + (string.IsNullOrWhiteSpace(party.Description) ? string.Empty : $": {party.Description.Trim()}"));
        }

        var keyPeople = guide.Cast.Where(c => c.Tier == CastTier.Key && !IsCounsel(c)).ToList();
        if (keyPeople.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Key people:");
            foreach (var member in keyPeople)
                sb.AppendLine(Describe(guide, member) + (string.IsNullOrWhiteSpace(member.Involvement) ? string.Empty : $": {member.Involvement.Trim()}"));
        }

        var counsel = guide.Cast.Where(IsCounsel).ToList();
        if (counsel.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Counsel:");
            foreach (var member in counsel)
                sb.AppendLine($"{Describe(guide, member)} ({RoleText(member)})");
        }

        var firms = guide.Organizations.Where(o => o.Kind == OrganizationKind.LawFirm).ToList();
        if (firms.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Law firms:");
            foreach (var firm in firms)
            {
                var clients = firm.RepresentsOrgIds
                    .Select(id => guide.FindOrganization(id)?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                sb.AppendLine(clients.Count > 0 ? $"{firm.Name} represents {string.Join(", ", clients)}" : firm.Name);
            }
        }

        sb.AppendLine();
        sb.AppendLine("Write the prose sections of a document review protocol for contract reviewers: the purpose and scope, a short matter background, guidance on hot documents, and privilege guidance (attorney-client and work product, waiver when third parties are copied, redaction of partly privileged documents). For each issue, give two or three short hypothetical examples of relevant documents and two of documents that are not relevant. Do not describe specific emails, document numbers or how many documents exist.");
        return sb.ToString();
    }

    internal static bool IsCounsel(CastMember member) => member.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel;

    internal static string RoleText(CastMember member)
    {
        var role = member.Role switch
        {
            CastRole.Party => "Party",
            CastRole.InHouseCounsel => "In-house counsel",
            CastRole.OutsideCounsel => "Outside counsel",
            _ => "Staff"
        };
        return member.Tier == CastTier.Background ? role + ", background" : role;
    }

    // "Maria Delgado, President, Brightline Glazing, LLC" (empty parts skipped)
    private static string Describe(CaseGuide guide, CastMember member)
    {
        var parts = new List<string> { member.FullName };
        if (!string.IsNullOrWhiteSpace(member.Title))
            parts.Add(member.Title.Trim());
        var org = guide.FindOrganization(member.OrganizationId)?.Name;
        if (!string.IsNullOrWhiteSpace(org))
            parts.Add(org.Trim());
        return string.Join(", ", parts);
    }

    private static string DocumentTypeText(CoreDocumentType type) => type switch
    {
        CoreDocumentType.Complaint => "Complaint",
        CoreDocumentType.Petition => "Petition",
        CoreDocumentType.InvestigationMemo => "Investigation memo",
        _ => type.ToString()
    };
}
