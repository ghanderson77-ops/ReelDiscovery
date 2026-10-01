using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Writes the core document outline-first: one Story-tier outline call fixes the type, court, parties,
/// counts and timeline, then each template section is written by its own call (CASE-04..07).
/// </summary>
public class CoreDocumentGenerator
{
    public const int OutlineMaxOutputTokens = 16_000;
    public const int SectionMaxOutputTokens = 12_000;
    private const int MaxParallelSections = 3;

    // Byte-stable for prompt caching: never interpolate run data into this text
    private const string SystemPrompt = "You draft realistic United States legal documents for a fictional e-discovery training dataset. Every person, company, law firm and judge must be fictional; never use the name of a real person, company, law firm or judge. Do not cite any court decision, case name or reporter citation (for example Smith v. Jones or 531 U.S. 98). You may cite statutes, regulations and court rules by section number. Describe legal standards in plain words. Write each paragraph as plain prose without any paragraph number, letter or bullet, and never refer to other paragraphs by number. Keep one consistent court, jurisdiction and set of names throughout. For criminal or regulatory matters keep operational methods general and focus on who knew what, when, money flows and cover stories.";

    private readonly ILlmGateway _llm;

    /// <summary>
    /// Top-level response types sent as strict schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(OutlineDto), typeof(SectionDto) };

    public CoreDocumentGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }

    /// <summary>
    /// One outline call; every non-success outcome throws LlmCallException (the UI shows the Refused state).
    /// </summary>
    public async Task<CoreDocument> GenerateOutlineAsync(CaseDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var result = await _llm.GenerateAsync<OutlineDto>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = "Core Document - Outline",
            SystemPrompt = SystemPrompt,
            UserPrompt = BuildOutlinePrompt(draft),
            MaxOutputTokens = OutlineMaxOutputTokens
        }, ct);

        return MapOutline(result.GetValueOrThrow(), draft);
    }

    internal static string BuildOutlinePrompt(CaseDraft draft)
    {
        var p = draft.Preset;
        var sb = new StringBuilder();

        sb.AppendLine("Scenario:");
        sb.AppendLine(draft.EffectiveScenario);
        sb.AppendLine();
        sb.AppendLine($"Complexity preset: {draft.Complexity}");
        // Always print both bounds, even when equal (e.g. "2-2")
        sb.AppendLine($"- Parties: {p.PartiesMin}-{p.PartiesMax}");
        sb.AppendLine($"- Counts or issues: {p.CountsMin}-{p.CountsMax}");
        sb.AppendLine($"- Key characters: {p.CharactersMin}-{p.CharactersMax}");
        sb.AppendLine($"- Timeline span (months): {p.TimelineMonthsMin}-{p.TimelineMonthsMax}");
        sb.AppendLine($"- Factual subsections: {p.FactSubsectionsMin}-{p.FactSubsectionsMax}");
        sb.AppendLine($"- Numbered paragraphs (whole document): {p.ParagraphsMin}-{p.ParagraphsMax}");
        sb.AppendLine($"- Memo findings if this is an investigation memo: {p.FindingsMin}-{p.FindingsMax}");
        // W4: same range the validator checks
        sb.AppendLine($"- Memo length if this is an investigation memo: {p.MemoPagesMin}-{p.MemoPagesMax} pages, about {p.MemoParagraphsMin}-{p.MemoParagraphsMax} paragraphs in total");
        sb.AppendLine();

        sb.AppendLine(draft.DocumentPreference == DocumentTypePreference.Auto
            ? "Document type: choose (Complaint or Petition for a civil dispute, Investigation Memo for a criminal or regulatory matter)"
            : $"Document type: {DescribeDocumentPreference(draft.DocumentPreference)}");
        sb.AppendLine(draft.CourtPreference == CourtPreference.Auto
            ? "Court system: choose"
            : $"Court system: {draft.CourtPreference} (required: the court name must belong to this court system)");
        sb.AppendLine(string.IsNullOrWhiteSpace(draft.StatePreference)
            ? "State: choose"
            : $"State: {draft.StatePreference.Trim()} (required: use a court in this state)");

        var applied = draft.Substitutions
            .Where(s => s.Apply && !string.IsNullOrWhiteSpace(s.Original) && !string.IsNullOrWhiteSpace(s.Substitute))
            .ToList();
        if (applied.Count > 0)
        {
            sb.AppendLine($"Use these fictional names exactly: {string.Join(", ", applied.Select(s => s.Substitute.Trim()))}");
            sb.AppendLine($"Never use these real names: {string.Join(", ", applied.Select(s => s.Original.Trim()))}");
        }

        sb.AppendLine();
        sb.Append("Plan the document: classify the matter, choose one court and venue, list the parties with fictional names, the counts or issues, the timeline (filing or memo date after the timeline ends), the factual subsections in date order with a paragraph target each, and fictional counsel or memo author details.");

        return sb.ToString();
    }

    private static string DescribeDocumentPreference(DocumentTypePreference preference) => preference switch
    {
        DocumentTypePreference.InvestigationMemo => "Investigation Memo",
        _ => preference.ToString()
    };

    private static CoreDocument MapOutline(OutlineDto dto, CaseDraft draft)
    {
        var partyDtos = dto.Parties ?? new List<PartyDto>();
        if (partyDtos.Count == 0)
            throw new InvalidOperationException("The outline listed no parties.");

        var jurisdiction = new Jurisdiction
        {
            CourtSystem = dto.CourtSystem,
            State = Clean(dto.State),
            CourtName = Clean(dto.CourtName),
            District = Clean(dto.District),
            VenueCity = Clean(dto.VenueCity)
        };

        // The user's court/state preferences win. CourtName is deliberately NOT rewritten: if the model
        // ignored the required court system or state, the contradiction is left for CoreDocumentValidator
        // (Federal with a non-"United States" court, or State with a "United States" court, is an Error that
        // blocks Finish until the user regenerates; a state court naming a different state is a Warning).
        if (draft.CourtPreference == CourtPreference.Federal)
            jurisdiction.CourtSystem = CourtSystem.Federal;
        else if (draft.CourtPreference == CourtPreference.State)
            jurisdiction.CourtSystem = CourtSystem.State;

        if (!string.IsNullOrWhiteSpace(draft.StatePreference))
            jurisdiction.State = draft.StatePreference.Trim();

        var doc = new CoreDocument
        {
            MatterKind = dto.MatterKind,
            MemoPerspective = dto.MemoPerspective,
            Jurisdiction = jurisdiction,
            UsesPetitionTerminology = dto.UsesPetitionTerminology,
            DemandsJuryTrial = dto.DemandsJuryTrial,
            Title = Clean(dto.Title),
            FilingDate = Clean(dto.FilingDate),
            TimelineStart = Clean(dto.TimelineStart),
            TimelineEnd = Clean(dto.TimelineEnd)
        };

        doc.DocumentType = DocumentTypeResolver.Resolve(dto.MatterKind, draft.DocumentPreference, jurisdiction.CourtSystem, dto.UsesPetitionTerminology);

        for (var i = 0; i < partyDtos.Count; i++)
        {
            var party = partyDtos[i];
            doc.Parties.Add(new CaseParty
            {
                Id = $"party-{i + 1}",
                Name = Clean(party.Name),
                ShortName = Clean(party.ShortName),
                Role = party.Role,
                EntityType = Clean(party.EntityType),
                Domicile = Clean(party.Domicile),
                Description = Clean(party.Description)
            });
        }

        var countDtos = dto.Counts ?? new List<CountDto>();
        for (var i = 0; i < countDtos.Count; i++)
        {
            var count = countDtos[i];
            var against = (count.AgainstParties ?? new List<string>()).Select(Clean).Where(a => a.Length > 0).ToList();

            doc.Counts.Add(new CaseCount
            {
                Id = $"count-{i + 1}",
                Number = i + 1,
                Title = Clean(count.Title),
                // Unresolved names are dropped
                AgainstPartyIds = doc.Parties
                    .Where(p => against.Any(a =>
                        string.Equals(a, p.ShortName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, p.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(p => p.Id)
                    .ToList(),
                LegalBasis = Clean(count.LegalBasis),
                Summary = Clean(count.Summary)
            });
        }

        var factDtos = dto.FactSubsections ?? new List<FactSubsectionDto>();
        for (var i = 0; i < factDtos.Count; i++)
        {
            var fact = factDtos[i];
            doc.FactPlan.Add(new FactSubsectionPlan
            {
                Id = $"facts-{i + 1}",
                Heading = Clean(fact.Heading),
                StartDate = Clean(fact.StartDate),
                EndDate = Clean(fact.EndDate),
                TargetParagraphs = Math.Clamp(fact.TargetParagraphs, 3, CoreDocumentTemplates.MaxParagraphsPerSection),
                Focus = Clean(fact.Focus)
            });
        }

        var counsel = dto.Counsel ?? new CounselDto();
        doc.Counsel = new CounselInfo
        {
            AttorneyName = Clean(counsel.AttorneyName),
            FirmName = Clean(counsel.FirmName),
            BarNumber = Clean(counsel.BarNumber),
            Address = Clean(counsel.Address),
            // Fictional 555-01xx range reserved for fiction; never a real number (Pitfall 12)
            Phone = $"(555) 555-01{Random.Shared.Next(0, 100).ToString("D2", CultureInfo.InvariantCulture)}",
            Email = Clean(counsel.Email)
        };

        var memo = dto.Memo ?? new MemoDto();
        doc.Memo = new MemoHeader
        {
            To = Clean(memo.To),
            From = Clean(memo.From),
            Cc = Clean(memo.Cc),
            Date = Clean(memo.Date),
            Re = Clean(memo.Re),
            Organization = Clean(memo.Organization)
        };

        doc.Sections = CoreDocumentTemplates.GetSectionSpecs(doc)
            .Select(s => new CoreDocumentSection
            {
                Id = s.Id,
                Kind = s.Kind,
                Heading = s.Heading,
                CountId = s.CountId,
                Status = SectionStatus.Pending
            })
            .ToList();

        doc.SourceHash = draft.ComputeSourceHash();
        return doc;
    }

    private static string Clean(string value) => (value ?? string.Empty).Trim();

    // API Response DTOs (strict schemas: no nullable properties, dates as YYYY-MM-DD strings)
    internal class OutlineDto
    {
        [JsonPropertyName("matterKind")]
        [Description("Civil, Criminal or Regulatory")]
        public MatterKind MatterKind { get; set; }

        [JsonPropertyName("courtSystem")]
        [Description("Federal or State")]
        public CourtSystem CourtSystem { get; set; }

        [JsonPropertyName("state")]
        [Description("Full US state name, e.g. Illinois")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("courtName")]
        [Description("Full court name, e.g. United States District Court for the Northern District of Illinois")]
        public string CourtName { get; set; } = string.Empty;

        [JsonPropertyName("district")]
        [Description("Federal district or state county")]
        public string District { get; set; } = string.Empty;

        [JsonPropertyName("venueCity")]
        [Description("City where the court sits")]
        public string VenueCity { get; set; } = string.Empty;

        [JsonPropertyName("usesPetitionTerminology")]
        [Description("True only for a state court whose pleadings are called petitions, e.g. Texas")]
        public bool UsesPetitionTerminology { get; set; }

        [JsonPropertyName("memoPerspective")]
        [Description("InternalCorporate or Government; who wrote the memo if this is an investigation memo")]
        public MemoPerspective MemoPerspective { get; set; }

        [JsonPropertyName("title")]
        [Description("Document title, e.g. Complaint for Breach of Contract and Fraud, or the memo subject")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("filingDate")]
        [Description("Filing or memo date, YYYY-MM-DD, after the timeline ends")]
        public string FilingDate { get; set; } = string.Empty;

        [JsonPropertyName("timelineStart")]
        [Description("First relevant event, YYYY-MM-DD")]
        public string TimelineStart { get; set; } = string.Empty;

        [JsonPropertyName("timelineEnd")]
        [Description("Last relevant event, YYYY-MM-DD")]
        public string TimelineEnd { get; set; } = string.Empty;

        [JsonPropertyName("demandsJuryTrial")]
        [Description("True when the pleading demands a jury trial")]
        public bool DemandsJuryTrial { get; set; }

        [JsonPropertyName("parties")]
        [Description("Parties with fictional names")]
        public List<PartyDto> Parties { get; set; } = new();

        [JsonPropertyName("counts")]
        [Description("Causes of action for a pleading, or issues/findings for a memo")]
        public List<CountDto> Counts { get; set; } = new();

        [JsonPropertyName("factSubsections")]
        [Description("Factual subsections in date order")]
        public List<FactSubsectionDto> FactSubsections { get; set; } = new();

        [JsonPropertyName("counsel")]
        [Description("Fictional counsel signing a pleading; empty strings for a memo")]
        public CounselDto Counsel { get; set; } = new();

        [JsonPropertyName("memo")]
        [Description("Memo header; empty strings for a pleading")]
        public MemoDto Memo { get; set; } = new();
    }

    internal class PartyDto
    {
        [JsonPropertyName("name")]
        [Description("Full fictional legal name, e.g. Brightline Glazing, LLC")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("shortName")]
        [Description("Short name used in the text, e.g. Brightline")]
        public string ShortName { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [Description("Plaintiff, Defendant, Subject, Complainant or ThirdParty")]
        public PartyRole Role { get; set; }

        [JsonPropertyName("entityType")]
        [Description("e.g. Delaware corporation, Illinois limited liability company, individual")]
        public string EntityType { get; set; } = string.Empty;

        [JsonPropertyName("domicile")]
        [Description("City and state of residence or principal place of business")]
        public string Domicile { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("One sentence on the party's role in the matter")]
        public string Description { get; set; } = string.Empty;
    }

    internal class CountDto
    {
        [JsonPropertyName("title")]
        [Description("Count or issue title, e.g. Breach of Contract")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("againstParties")]
        [Description("Short names of the parties this count is against")]
        public List<string> AgainstParties { get; set; } = new();

        [JsonPropertyName("legalBasis")]
        [Description("Statute, regulation or common-law theory; never a case citation")]
        public string LegalBasis { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        [Description("One or two sentences on the theory of this count")]
        public string Summary { get; set; } = string.Empty;
    }

    internal class FactSubsectionDto
    {
        [JsonPropertyName("heading")]
        [Description("Subsection heading")]
        public string Heading { get; set; } = string.Empty;

        [JsonPropertyName("startDate")]
        [Description("YYYY-MM-DD")]
        public string StartDate { get; set; } = string.Empty;

        [JsonPropertyName("endDate")]
        [Description("YYYY-MM-DD")]
        public string EndDate { get; set; } = string.Empty;

        [JsonPropertyName("targetParagraphs")]
        [Description("Number of paragraphs to write for this subsection")]
        public int TargetParagraphs { get; set; }

        [JsonPropertyName("focus")]
        [Description("What happened in this period")]
        public string Focus { get; set; } = string.Empty;
    }

    internal class CounselDto
    {
        [JsonPropertyName("attorneyName")]
        [Description("Fictional attorney name")]
        public string AttorneyName { get; set; } = string.Empty;

        [JsonPropertyName("firmName")]
        [Description("Fictional law firm name")]
        public string FirmName { get; set; } = string.Empty;

        [JsonPropertyName("barNumber")]
        [Description("Fictional bar number")]
        public string BarNumber { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        [Description("Firm street address, city, state and ZIP")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        [Description("Attorney email at an .example domain")]
        public string Email { get; set; } = string.Empty;
    }

    internal class MemoDto
    {
        [JsonPropertyName("to")]
        [Description("Recipient name and title")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        [Description("Author name and title")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("cc")]
        [Description("Empty string when none")]
        public string Cc { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        [Description("Memo date, YYYY-MM-DD")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("re")]
        [Description("Subject line")]
        public string Re { get; set; } = string.Empty;

        [JsonPropertyName("organization")]
        [Description("Organization issuing the memo")]
        public string Organization { get; set; } = string.Empty;
    }

    internal class SectionDto
    {
        [JsonPropertyName("paragraphs")]
        [Description("Paragraphs in order, plain prose, no numbers or letters")]
        public List<string> Paragraphs { get; set; } = new();
    }
}
