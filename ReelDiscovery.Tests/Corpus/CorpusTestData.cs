using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Corpus;

/// <summary>
/// Phase 4 corpus planner fixtures built on the Phase 3 guided draft.
/// </summary>
internal static partial class CorpusTestData
{
    [GeneratedRegex(@"T-\d{4}")]
    private static partial Regex ThreadIdPattern();

    [GeneratedRegex(@"S-\d{4}")]
    private static partial Regex SlotIdPattern();

    // Matches RelevantThreadFiller.BuildChunkPrompt lines "T-0001 msg 2 | ..."
    [GeneratedRegex(@"^\s*(T-\d{4}) msg (\d+)\b", RegexOptions.Multiline)]
    private static partial Regex ThreadSeqPattern();

    /// <summary>
    /// SampleCounselDraft enriched for planning: 2 issues, Chicago (ORG-01) and Phoenix (ORG-02) parties, New York law firms,
    /// a Saturday fact (F-007, 2024-06-01), a federal-holiday fact known by one person (F-008, 2024-07-04), a narrow active
    /// range (CH-009), counsel on both sides and Hot/Key/Supporting facts.
    /// </summary>
    public static CaseDraft RichDraft()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var guide = draft.Guide!;

        SetZone(guide, "ORG-01", "America/Chicago", "Chicago, Illinois");
        SetZone(guide, "ORG-02", "America/Phoenix", "Phoenix, Arizona");
        SetZone(guide, "ORG-03", "America/New_York", "New York, New York");
        SetZone(guide, "ORG-04", "America/New_York", "New York, New York");

        guide.Issues.Add(new CaseIssue
        {
            Id = "ISS-02",
            Number = 2,
            Title = "Prompt payment violation",
            LegalBasis = "Illinois Contractor Prompt Payment Act",
            Summary = "Harmon paid nothing within the statutory period after Brightline's invoice.",
            AgainstPartyIds = new List<string> { "party-2" },
            Description = "Whether Harmon violated the prompt payment statute by withholding the Change Order 7 payment past the statutory deadline.",
            Elements = new List<string>
            {
                "Brightline submitted a proper payment demand.",
                "Harmon failed to pay within the statutory period."
            },
            RelevanceCriteria = "Documents about the timing of payment demands, notices and late-payment interest on Lakeview Tower invoices."
        });

        foreach (var fact in guide.Facts)
        {
            fact.IssueIds = fact.Id is "F-005" or "F-006"
                ? new List<string> { "ISS-01", "ISS-02" }
                : new List<string> { "ISS-01" };
        }

        guide.Facts.Add(new KeyFact
        {
            Id = "F-007",
            Date = "2024-06-01",
            Title = "Brightline sends notice of late payment",
            Description = "Brightline's president sent Harmon a Saturday notice that INV-2024-118 was past due and accruing late-payment interest.",
            IssueIds = new List<string> { "ISS-02" },
            ActorIds = new List<string> { "CH-001" },
            KnownByIds = new List<string> { "CH-001", "CH-005" },
            Significance = FactSignificance.Key,
            SourceSectionId = "facts-2"
        });
        guide.Facts.Add(new KeyFact
        {
            Id = "F-008",
            Date = "2024-07-04",
            Title = "Harmon accounting flags the invoice",
            Description = "Harmon's accounts payable specialist flagged INV-2024-118 as past the prompt payment deadline in an internal note.",
            IssueIds = new List<string> { "ISS-02" },
            ActorIds = new List<string> { "CH-008" },
            KnownByIds = new List<string> { "CH-008" },
            Significance = FactSignificance.Supporting,
            SourceSectionId = "facts-2"
        });

        foreach (var member in guide.Cast)
        {
            member.ActiveFrom = "2023-06-01";
            member.ActiveTo = "2025-06-30";
        }

        var foreman = guide.Cast.Single(c => c.Id == "CH-009");
        foreman.ActiveFrom = "2024-05-01";
        foreman.ActiveTo = "2024-06-15";

        guide.IdCounters["F"] = 8;
        guide.IdCounters["ISS"] = 2;

        foreach (var state in new[] { guide.IssuesState, guide.IssueDetailsState, guide.CastState, guide.FactsState, guide.ValuesState })
            state.Status = GuidePartStatus.Ready;

        return draft;
    }

    public static (LlmGateway Gateway, FakeLlmProvider Fake) CreateGateway() => CaseTestData.CreateGateway();

    /// <summary>
    /// Distinct thread ids (T-0001) in order of first appearance.
    /// </summary>
    public static List<string> ThreadIdsIn(string prompt) =>
        ThreadIdPattern().Matches(prompt).Select(m => m.Value).Distinct().ToList();

    /// <summary>
    /// Thread ids with the message sequence numbers listed for each, from "{threadId} msg {seq}" lines, in order.
    /// </summary>
    public static List<(string ThreadId, List<int> Seqs)> ThreadSeqsIn(string prompt)
    {
        var result = new List<(string ThreadId, List<int> Seqs)>();
        foreach (Match match in ThreadSeqPattern().Matches(prompt))
        {
            var threadId = match.Groups[1].Value;
            var seq = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var index = result.FindIndex(r => r.ThreadId == threadId);
            if (index < 0)
                result.Add((threadId, new List<int> { seq }));
            else if (!result[index].Seqs.Contains(seq))
                result[index].Seqs.Add(seq);
        }

        return result;
    }

    /// <summary>
    /// Distinct slot ids (S-0001) in order of first appearance.
    /// </summary>
    public static List<string> SlotIdsIn(string prompt) =>
        SlotIdPattern().Matches(prompt).Select(m => m.Value).Distinct().ToList();

    public static int DatedFactCount(CaseDraft draft) =>
        draft.Guide?.Facts.Count(f => CaseDraft.TryParseDate(f.Date, out _)) ?? 0;

    private static void SetZone(CaseGuide guide, string orgId, string zone, string location)
    {
        var org = guide.FindOrganization(orgId)!;
        org.TimeZoneId = zone;
        org.Location = location;
    }
}
