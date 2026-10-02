using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using static ReelDiscovery.Services.Case.CaseGuideDocxExporter;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// The review protocol / coding manual (KEY-05, D-34): code builds the structure from the case guide and one Story-tier
/// call writes the prose. The prompt and the document never carry plan data, document numbers, labels or counts (T-04-19).
/// </summary>
public partial class ReviewProtocolBuilder
{
    public const int ProseMaxOutputTokens = 8_000;
    public const string OperationName = "Corpus Plan - Review protocol";
    public const string DocumentTitle = "Document Review Protocol";

    // Portrait text width in twips (letter, 0.75" margins)
    private const int PortraitWidth = 10080;

    private const string FamilyRule = "Code an email and its attachments as one family: if any member is relevant, code every member Relevant with the same issue codes. A reply or forward that quotes a relevant email is relevant.";
    private const string EscalationRule = "Raise any document you cannot code with confidence with the review lead before moving on: documents that may be privileged but are not clearly so, documents in a language you cannot read, documents that appear damaged or incomplete, and anything that suggests a new issue or a person not named in this protocol. Do not guess; mark the document for second-level review and note the question.";

    private readonly ILlmGateway _llm;

    public ReviewProtocolBuilder(ILlmGateway llm)
    {
        _llm = llm;
    }

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(ReviewProtocolProseDto) };

    [GeneratedRegex(@"\b[A-Z]{2,6}-\d{6}(?:\.\d{4})?\b")]
    private static partial Regex DocIdPattern();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex RepeatedSpaces();

    /// <summary>
    /// One Story-tier call for the protocol prose. Fatal provider failures (Auth/SpendLimit) propagate so the planner stops;
    /// any other failure, or a response with empty sections, returns <see cref="FallbackProse"/> (IsFallback true).
    /// </summary>
    public async Task<ReviewProtocolProse> GenerateProseAsync(CaseDraft draft, CancellationToken ct = default)
    {
        ReviewProtocolProseDto dto;
        try
        {
            var result = await _llm.GenerateAsync<ReviewProtocolProseDto>(new LlmRequest
            {
                Tier = LlmTier.Story,
                OperationName = OperationName,
                SystemPrompt = CorpusPlanPrompts.SystemPrompt,
                UserPrompt = CorpusPlanPrompts.BuildProtocolPrompt(draft),
                MaxOutputTokens = ProseMaxOutputTokens
            }, ct);
            dto = result.GetValueOrThrow();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            throw;
        }
        catch (LlmCallException)
        {
            return FallbackProse(draft);
        }

        return Map(draft, dto);
    }

    /// <summary>
    /// Criteria-only prose assembled from the case title, issue titles and relevance criteria (IsFallback true).
    /// </summary>
    public static ReviewProtocolProse FallbackProse(CaseDraft draft)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var title = CaseFolderNaming.CaseTitle(draft);
        var issueTitles = guide.Issues.Select(i => i.Title.Trim()).Where(t => t.Length > 0).ToList();

        var background = $"This review supports {title}.";
        if (issueTitles.Count > 0)
            background += $" The matter concerns {JoinList(issueTitles.Select(t => t.ToLowerInvariant()))}.";
        var parties = draft.CoreDocument?.Parties?.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.Name.Trim()).ToList() ?? new List<string>();
        if (parties.Count > 0)
            background += $" The parties are {JoinList(parties)}.";

        var counsel = guide.Cast.Where(CorpusPlanPrompts.IsCounsel).Select(c => c.FullName).Where(n => n.Length > 0).ToList();
        var firms = guide.Organizations.Where(o => o.Kind == OrganizationKind.LawFirm).Select(o => o.Name.Trim()).Where(n => n.Length > 0).ToList();
        var privilege = "Code a document Privileged when it is a confidential communication with a lawyer for the purpose of legal advice, or when a lawyer or someone working at a lawyer's direction prepared it in anticipation of litigation.";
        if (counsel.Count > 0)
            privilege += $" The lawyers in this matter are {JoinList(counsel)}.";
        if (firms.Count > 0)
            privilege += $" Outside counsel work at {JoinList(firms)}.";
        privilege += " Copying a third party outside the client and its lawyers usually waives the privilege. When only part of a document is privileged, code it Privileged and mark the privileged passage for redaction.";

        return new ReviewProtocolProse
        {
            Purpose = $"This protocol tells reviewers how to code the documents collected in {title}. Code every document for relevance, apply each issue code whose criteria it meets, and flag hot and privileged documents.",
            Background = background,
            HotGuidance = "Code a document Hot when it is strong, direct evidence of a key fact: it shows who decided, approved, refused or knew something central to an issue, or it states an amount, date or commitment the case turns on. A document that only mentions a topic in passing is not hot.",
            PrivilegeGuidance = privilege,
            Issues = guide.Issues.Select(FallbackIssue).ToList(),
            IsFallback = true
        };
    }

    public static string FileNameFor(string folderName) => $"{folderName} - Review Protocol.docx";

    /// <summary>
    /// Deterministic, validator-clean .docx bytes for the protocol. Structure comes from the guide; prose fills the sections.
    /// </summary>
    public static byte[] Build(CaseDraft draft, ReviewProtocolProse prose)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var word = new CaseGuideDocxExporter.WordBuilder();

        word.Paragraph(DocumentTitle, new ParaFormat { Style = "Title" });
        word.Paragraph(CaseFolderNaming.CaseTitle(draft), new ParaFormat { Style = "Subtitle" });
        word.Paragraph("Privileged & Confidential — Attorney Work Product", new ParaFormat { Bold = true });

        Heading(word, "1. Purpose and Scope");
        Prose(word, prose.Purpose);

        Heading(word, "2. Matter Background");
        Prose(word, prose.Background);

        Heading(word, "3. Parties and Key People");
        var keyPeople = guide.Cast.Where(c => c.Tier == CastTier.Key && !CorpusPlanPrompts.IsCounsel(c)).ToList();
        if (keyPeople.Count == 0)
            word.Paragraph("(none)");
        else
            word.Table(
                new[] { "Name", "Title", "Organization", "Role" },
                keyPeople.Select(c => (IReadOnlyList<Cell>)new Cell[] { c.FullName, c.Title, OrganizationName(guide, c.OrganizationId), CorpusPlanPrompts.RoleText(c) }),
                new[] { 2600, 2600, 3080, 1800 });

        Heading(word, "4. Review Fields and Decision Rules");
        word.Table(
            new[] { "Field", "Values and rule" },
            new[]
            {
                Row("Relevance", "Relevant / Not Relevant: relevant when the document meets the criteria of at least one issue code."),
                Row("Issue Codes", "Apply every issue code whose criteria the document meets."),
                Row("Hot", "Y when the document is strong, direct evidence of a key fact."),
                Row("Privileged", "Y when an attorney-client communication or attorney work product.")
            },
            new[] { 2200, PortraitWidth - 2200 });

        Heading(word, "5. Issue Codes");
        if (guide.Issues.Count == 0)
            word.Paragraph("(none)");
        foreach (var issue in guide.Issues)
        {
            var issueProse = prose.Issues.FirstOrDefault(p => SameId(p.IssueId, issue.Id)) ?? FallbackIssue(issue);
            word.Paragraph($"{issue.Id} {issue.Title}", new ParaFormat { Style = "Heading2" });
            word.Paragraph(issue.RelevanceCriteria, new ParaFormat { BoldLead = "Relevance criteria: " });
            Bullets(word, "Examples of relevant documents", issueProse.RelevantExamples);
            Bullets(word, "Examples of documents that are not relevant", issueProse.NotRelevantExamples);
        }

        Heading(word, "6. Hot Documents");
        Prose(word, prose.HotGuidance);

        Heading(word, "7. Privilege");
        var counsel = guide.Cast.Where(CorpusPlanPrompts.IsCounsel).ToList();
        if (counsel.Count > 0)
            word.Table(
                new[] { "Name", "Title", "Firm or Organization", "Represents" },
                counsel.Select(c => (IReadOnlyList<Cell>)new Cell[] { c.FullName, c.Title, OrganizationName(guide, c.OrganizationId), Represents(guide, c) }),
                new[] { 2400, 2000, 3080, 2600 });
        Prose(word, prose.PrivilegeGuidance);

        Heading(word, "8. Families and Threads");
        word.Paragraph(FamilyRule, new ParaFormat { BoldLead = "Family rule: " });

        Heading(word, "9. Questions and Escalation");
        word.Paragraph(EscalationRule);

        return word.Save(DocumentTitle, landscape: false);
    }

    private static ReviewProtocolProse Map(CaseDraft draft, ReviewProtocolProseDto dto)
    {
        var purpose = Clean(dto.Purpose);
        var background = Clean(dto.Background);
        var hot = Clean(dto.HotGuidance);
        var privilege = Clean(dto.PrivilegeGuidance);
        if (purpose.Length == 0 || background.Length == 0 || hot.Length == 0 || privilege.Length == 0)
            return FallbackProse(draft);

        var guide = draft.Guide ?? new CaseGuide();
        var issues = new List<IssueProse>();
        foreach (var issue in guide.Issues)
        {
            // First response entry per guide issue wins; unknown ids are dropped
            var match = (dto.Issues ?? new List<IssueProseDto>()).FirstOrDefault(i => i != null && SameId(i.IssueId, issue.Id));
            var relevant = CleanList(match?.RelevantExamples);
            if (match == null || relevant.Count == 0)
            {
                // Missing examples for one issue fall back to its criteria; the rest of the prose is kept
                issues.Add(FallbackIssue(issue));
                continue;
            }

            issues.Add(new IssueProse
            {
                IssueId = issue.Id,
                RelevantExamples = relevant,
                NotRelevantExamples = CleanList(match.NotRelevantExamples)
            });
        }

        return new ReviewProtocolProse
        {
            Purpose = purpose,
            Background = background,
            HotGuidance = hot,
            PrivilegeGuidance = privilege,
            Issues = issues,
            IsFallback = false
        };
    }

    private static IssueProse FallbackIssue(CaseIssue issue) => new()
    {
        IssueId = issue.Id,
        RelevantExamples = new List<string> { $"A document that fits this description: {issue.RelevanceCriteria.Trim().TrimEnd('.')}." },
        NotRelevantExamples = new List<string>()
    };

    private static bool SameId(string? raw, string id) =>
        string.Equals(raw?.Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase);

    // T-04-19: document-number tokens are removed from model prose in code
    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var stripped = DocIdPattern().Replace(text, string.Empty);
        return RepeatedSpaces().Replace(stripped, " ").Replace(" .", ".").Replace(" ,", ",").Trim();
    }

    private static List<string> CleanList(IEnumerable<string>? items) =>
        (items ?? Enumerable.Empty<string>()).Select(Clean).Where(s => s.Length > 0).ToList();

    private static void Heading(WordBuilder word, string text) =>
        word.Paragraph(text, new ParaFormat { Style = "Heading1" });

    private static void Prose(WordBuilder word, string text)
    {
        foreach (var paragraph in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            word.Paragraph(paragraph);
    }

    private static void Bullets(WordBuilder word, string caption, IReadOnlyList<string> items)
    {
        word.Paragraph(caption, new ParaFormat { Bold = true, SpaceAfter = 60, KeepNext = true });
        if (items.Count == 0)
        {
            word.Paragraph("(none)", new ParaFormat { IndentLeft = 360 });
            return;
        }
        foreach (var item in items)
            word.Paragraph("•\t" + item, new ParaFormat { IndentLeft = 360, Hanging = 360, SpaceAfter = 60 });
    }

    private static IReadOnlyList<Cell> Row(string field, string rule) => new Cell[] { new(field), new(rule) };

    private static string OrganizationName(CaseGuide guide, string id) => guide.FindOrganization(id)?.Name ?? string.Empty;

    // Outside counsel: the firm's clients; in-house counsel: their own organization
    private static string Represents(CaseGuide guide, CastMember member)
    {
        var org = guide.FindOrganization(member.OrganizationId);
        if (org == null)
            return string.Empty;
        if (org.Kind != OrganizationKind.LawFirm)
            return org.Name;
        return string.Join(", ", org.RepresentsOrgIds.Select(id => guide.FindOrganization(id)?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
    }

    private static string JoinList(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            2 => $"{list[0]} and {list[1]}",
            _ => string.Join(", ", list.Take(list.Count - 1)) + ", and " + list[^1]
        };
    }

    internal class ReviewProtocolProseDto
    {
        [JsonPropertyName("purpose")]
        [Description("Purpose and scope of the review, two to four sentences")]
        public string Purpose { get; set; } = string.Empty;

        [JsonPropertyName("background")]
        [Description("Short matter background for reviewers, one or two paragraphs")]
        public string Background { get; set; } = string.Empty;

        [JsonPropertyName("hotGuidance")]
        [Description("How to recognize a hot document in this matter")]
        public string HotGuidance { get; set; } = string.Empty;

        [JsonPropertyName("privilegeGuidance")]
        [Description("Attorney-client and work product guidance, waiver by third-party copies, redaction of partly privileged documents")]
        public string PrivilegeGuidance { get; set; } = string.Empty;

        [JsonPropertyName("issues")]
        [Description("One entry per issue in the request")]
        public List<IssueProseDto> Issues { get; set; } = new();
    }

    internal class IssueProseDto
    {
        [JsonPropertyName("issueId")]
        [Description("Issue ID from the request, e.g. ISS-01")]
        public string IssueId { get; set; } = string.Empty;

        [JsonPropertyName("relevantExamples")]
        [Description("Two or three short hypothetical examples of relevant documents")]
        public List<string> RelevantExamples { get; set; } = new();

        [JsonPropertyName("notRelevantExamples")]
        [Description("Two short hypothetical examples of documents that are not relevant")]
        public List<string> NotRelevantExamples { get; set; } = new();
    }
}
