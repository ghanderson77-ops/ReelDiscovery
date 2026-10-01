using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// A required section of a core document: the LLM writes its paragraphs, code owns its id, kind and heading.
/// </summary>
public class SectionSpec
{
    public string Id { get; set; } = string.Empty;
    public SectionKind Kind { get; set; }
    public string Heading { get; set; } = string.Empty;
    public string CountId { get; set; } = string.Empty;
    public int TargetParagraphs { get; set; }
}

/// <summary>
/// Fixed, realistic section lists per core document type (CASE-05).
/// </summary>
public static class CoreDocumentTemplates
{
    // Keeps each section call under the Anthropic non-streaming output cap (Pitfall 1)
    public const int MaxParagraphsPerSection = 40;

    private const int MinFactParagraphs = 3;
    private const int CountParagraphs = 6;

    public static bool IsPleading(CoreDocumentType type) => type != CoreDocumentType.InvestigationMemo;

    public static List<SectionSpec> GetSectionSpecs(CoreDocument doc)
    {
        var partyCount = doc.Parties.Count;

        return doc.DocumentType switch
        {
            CoreDocumentType.Complaint => Concat(
                Spec("nature-of-action", SectionKind.NatureOfAction, "NATURE OF THE ACTION", 3),
                Spec("parties", SectionKind.Parties, "PARTIES", partyCount + 1),
                Spec("jurisdiction", SectionKind.JurisdictionVenue, "JURISDICTION AND VENUE", 4),
                FactSpecs(doc),
                CountSpecs(doc),
                Spec("prayer", SectionKind.PrayerForRelief, "PRAYER FOR RELIEF", 5)),

            CoreDocumentType.Petition => Concat(
                Spec("discovery-control-plan", SectionKind.DiscoveryControlPlan, "DISCOVERY CONTROL PLAN", 1),
                Spec("claim-for-relief", SectionKind.ClaimForRelief, "CLAIM FOR RELIEF", 1),
                Spec("parties", SectionKind.Parties, "PARTIES", partyCount + 1),
                Spec("jurisdiction", SectionKind.JurisdictionVenue, "JURISDICTION AND VENUE", 4),
                FactSpecs(doc),
                CountSpecs(doc),
                Spec("conditions-precedent", SectionKind.ConditionsPrecedent, "CONDITIONS PRECEDENT", 1),
                Spec("prayer", SectionKind.PrayerForRelief, "PRAYER", 5)),

            CoreDocumentType.InvestigationMemo => Concat(
                Spec("exec-summary", SectionKind.ExecutiveSummary, "EXECUTIVE SUMMARY", 4),
                Spec("background", SectionKind.Background, "BACKGROUND", 5),
                Spec("scope", SectionKind.ScopeMethodology, "SCOPE AND METHODOLOGY", 4),
                Spec("key-individuals", SectionKind.KeyIndividuals, "KEY INDIVIDUALS AND ENTITIES", partyCount + 2),
                FactSpecs(doc),
                FindingSpecs(doc),
                Spec("legal-analysis", SectionKind.LegalAnalysis, "LEGAL ANALYSIS AND EXPOSURE", 5),
                Spec("recommendations", SectionKind.Recommendations, "CONCLUSIONS AND RECOMMENDATIONS", 4)),

            _ => throw new ArgumentOutOfRangeException(nameof(doc), doc.DocumentType, "Unknown core document type")
        };
    }

    private static SectionSpec Spec(string id, SectionKind kind, string heading, int targetParagraphs) => new()
    {
        Id = id,
        Kind = kind,
        Heading = heading,
        TargetParagraphs = targetParagraphs
    };

    private static IEnumerable<SectionSpec> FactSpecs(CoreDocument doc) =>
        doc.FactPlan.Select(plan => new SectionSpec
        {
            Id = plan.Id,
            Kind = SectionKind.FactualAllegations,
            Heading = plan.Heading,
            TargetParagraphs = Math.Clamp(plan.TargetParagraphs, MinFactParagraphs, MaxParagraphsPerSection)
        });

    private static IEnumerable<SectionSpec> CountSpecs(CoreDocument doc) =>
        doc.Counts.Select(count => new SectionSpec
        {
            Id = $"count-{count.Number}",
            Kind = SectionKind.Count,
            Heading = $"COUNT {CoreDocumentAssembler.ToRoman(count.Number)} — {count.Title}",
            CountId = count.Id,
            TargetParagraphs = CountParagraphs
        });

    private static IEnumerable<SectionSpec> FindingSpecs(CoreDocument doc) =>
        doc.Counts.Select(count => new SectionSpec
        {
            Id = $"finding-{count.Number}",
            Kind = SectionKind.Finding,
            Heading = $"FINDING {count.Number}: {count.Title}",
            CountId = count.Id,
            TargetParagraphs = CountParagraphs
        });

    private static List<SectionSpec> Concat(params object[] parts)
    {
        var specs = new List<SectionSpec>();
        foreach (var part in parts)
        {
            if (part is SectionSpec spec)
                specs.Add(spec);
            else if (part is IEnumerable<SectionSpec> many)
                specs.AddRange(many);
        }

        return specs;
    }
}
