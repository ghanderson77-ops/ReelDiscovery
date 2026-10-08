using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class WrittenDiscoveryPlannerTests
{
    // ---- Availability (D-25) ----

    [Fact]
    public void Availability_Civil_AllVisibleWithCivilLabels()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument;

        Assert.Equal("Requests for Production", Visible(doc, DiscoveryKind.RequestsForProduction));
        Assert.Equal("Interrogatories", Visible(doc, DiscoveryKind.Interrogatories));
        Assert.Equal("Requests for Admission", Visible(doc, DiscoveryKind.RequestsForAdmission));
        Assert.False(WrittenDiscoveryPlanner.Availability(doc, DiscoveryKind.Deposition).Visible);
    }

    [Fact]
    public void Availability_MemoCriminal_OnlyDocumentRequests()
    {
        var doc = DiscoveryTestData.MemoCriminalDraft().CoreDocument;

        Assert.Equal("Subpoena rider (documents)", Visible(doc, DiscoveryKind.RequestsForProduction));
        Assert.False(WrittenDiscoveryPlanner.Availability(doc, DiscoveryKind.Interrogatories).Visible);
        Assert.False(WrittenDiscoveryPlanner.Availability(doc, DiscoveryKind.RequestsForAdmission).Visible);
    }

    [Fact]
    public void Availability_MemoRegulatory_HidesAdmissions()
    {
        var doc = DiscoveryTestData.MemoRegulatoryDraft().CoreDocument;

        Assert.Equal("CID document requests", Visible(doc, DiscoveryKind.RequestsForProduction));
        Assert.Equal("CID interrogatories", Visible(doc, DiscoveryKind.Interrogatories));
        Assert.False(WrittenDiscoveryPlanner.Availability(doc, DiscoveryKind.RequestsForAdmission).Visible);
    }

    [Fact]
    public void Availability_MemoCivil_AllVisibleWithMemoLabels()
    {
        var doc = DiscoveryTestData.MemoCivilDraft().CoreDocument;

        Assert.Equal("Pre-suit document requests", Visible(doc, DiscoveryKind.RequestsForProduction));
        Assert.Equal("Written questions", Visible(doc, DiscoveryKind.Interrogatories));
        Assert.Equal("Requests to admit", Visible(doc, DiscoveryKind.RequestsForAdmission));
    }

    [Fact]
    public void Availability_NullDocument_IsCivil()
    {
        Assert.True(WrittenDiscoveryPlanner.Availability(null, DiscoveryKind.RequestsForAdmission).Visible);
    }

    // ---- Parties (D-16) ----

    [Fact]
    public void ResolveParties_EmptyIds_DefaultsToFirstPlaintiffAndDefendant()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument;

        var choice = WrittenDiscoveryPlanner.ResolveParties(doc, new DiscoverySettings());

        Assert.Equal("party-1", choice.Propounding?.Id);
        Assert.Equal("party-2", choice.Responding?.Id);
        Assert.False(choice.Fallback);
        Assert.False(choice.Same);
    }

    [Fact]
    public void ResolveParties_DeletedSavedParty_FallsBack()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument;
        var settings = new DiscoverySettings { PropoundingPartyId = "party-9", RespondingPartyId = "party-2" };

        var choice = WrittenDiscoveryPlanner.ResolveParties(doc, settings);

        Assert.Equal("party-1", choice.Propounding?.Id);
        Assert.Equal("party-2", choice.Responding?.Id);
        Assert.True(choice.Fallback);
    }

    [Fact]
    public void ResolveParties_SameIds_KeptAndFlagged()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument;
        var settings = new DiscoverySettings { PropoundingPartyId = "party-2", RespondingPartyId = "party-2" };

        var choice = WrittenDiscoveryPlanner.ResolveParties(doc, settings);

        Assert.Equal("party-2", choice.Propounding?.Id);
        Assert.Equal("party-2", choice.Responding?.Id);
        Assert.True(choice.Same);
        Assert.False(choice.Fallback);
    }

    [Fact]
    public void ResolveParties_MemoCriminal_ComplainantServesFirstSubject()
    {
        var doc = DiscoveryTestData.MemoCriminalDraft().CoreDocument;

        var choice = WrittenDiscoveryPlanner.ResolveParties(doc, null);

        Assert.Equal("party-3", choice.Propounding?.Id);
        Assert.Equal("party-1", choice.Responding?.Id);
    }

    [Fact]
    public void ResolveParties_NullDocument_NoParties()
    {
        var choice = WrittenDiscoveryPlanner.ResolveParties(null, null);

        Assert.Null(choice.Propounding);
        Assert.Null(choice.Responding);
    }

    // ---- Effective counts (D-17, D-18, D-19) ----

    [Fact]
    public void EffectiveCount_RfpClampedToMinimum()
    {
        var draft = WithIssues(4);

        var count = WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForProduction, 3);

        Assert.Equal(3, count.Requested);
        Assert.Equal(5, count.Effective);
        Assert.False(count.Raised);
        Assert.False(count.Lowered);
    }

    [Fact]
    public void EffectiveCount_RaisedToIssueCount()
    {
        var draft = WithIssues(6);

        var count = WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForProduction, 5);

        Assert.Equal(6, count.Effective);
        Assert.True(count.Raised);
        Assert.False(count.Lowered);
    }

    [Fact]
    public void EffectiveCount_RfpNeverAbove80()
    {
        var draft = WithIssues(90);

        Assert.Equal(80, WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForProduction, 200).Effective);
        Assert.Equal(80, WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForProduction, 5).Effective);
    }

    [Fact]
    public void EffectiveCount_InterrogatoriesCappedAt25()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        Assert.Equal(25, WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.Interrogatories, 40).Effective);
        Assert.Equal(25, WrittenDiscoveryPlanner.EffectiveCount(WithIssues(30), DiscoveryKind.Interrogatories, 18).Effective);
        Assert.Equal(3, WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.Interrogatories, 0).Effective);
    }

    [Fact]
    public void EffectiveCount_RfaClamped()
    {
        var draft = DraftWithDatedFacts(60);

        Assert.Equal(1, WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForAdmission, 0).Effective);
        Assert.Equal(50, WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForAdmission, 90).Effective);
    }

    [Fact]
    public void EffectiveCount_RfaLoweredToDatedFacts()
    {
        var draft = SevenDatedFacts();

        var count = WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForAdmission, 20);

        Assert.Equal(7, count.Effective);
        Assert.True(count.Lowered);
        Assert.Equal(7, count.Available);
        Assert.False(string.IsNullOrEmpty(count.LoweredReason));
    }

    [Fact]
    public void EffectiveCount_RfaLowered_CountsGenuinenessCandidates()
    {
        var draft = SevenDatedFacts();

        var count = WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForAdmission, 20, TwoHotCandidates());

        Assert.Equal(9, count.Effective);
        Assert.True(count.Lowered);
        Assert.Equal(7, count.Available);
    }

    [Fact]
    public void EffectiveCount_RfaEnoughFacts_NotLowered()
    {
        var count = WrittenDiscoveryPlanner.EffectiveCount(DraftWithDatedFacts(25), DiscoveryKind.RequestsForAdmission, 20);

        Assert.Equal(20, count.Effective);
        Assert.False(count.Lowered);
    }

    [Fact]
    public void EffectiveCount_RfaNoDatedFacts_ZeroAndNoPlan()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        foreach (var fact in draft.Guide!.Facts)
            fact.Date = string.Empty;

        var count = WrittenDiscoveryPlanner.EffectiveCount(draft, DiscoveryKind.RequestsForAdmission, 20);

        Assert.Equal(0, count.Effective);
        Assert.True(count.Lowered);
        Assert.Null(WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings(), null));
    }

    // ---- Titles and file names ----

    [Fact]
    public void SetTitle_CivilFederal()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument!;
        var (p, r) = (doc.Parties[0], doc.Parties[1]);

        Assert.Equal("PLAINTIFF BRIGHTLINE GLAZING, LLC'S FIRST SET OF REQUESTS FOR PRODUCTION TO DEFENDANT HARMON BUILDERS, INC.",
            WrittenDiscoveryPlanner.SetTitle(doc, DiscoveryKind.RequestsForProduction, p, r));
        Assert.Equal("PLAINTIFF BRIGHTLINE GLAZING, LLC'S FIRST SET OF INTERROGATORIES TO DEFENDANT HARMON BUILDERS, INC.",
            WrittenDiscoveryPlanner.SetTitle(doc, DiscoveryKind.Interrogatories, p, r));
        Assert.Equal("PLAINTIFF BRIGHTLINE GLAZING, LLC'S FIRST SET OF REQUESTS FOR ADMISSION TO DEFENDANT HARMON BUILDERS, INC.",
            WrittenDiscoveryPlanner.SetTitle(doc, DiscoveryKind.RequestsForAdmission, p, r));
    }

    [Fact]
    public void SetTitle_Petition_UsesPetitionerRespondent()
    {
        var doc = DiscoveryTestData.StateCourtDraft().CoreDocument!;

        Assert.Equal("PETITIONER BRIGHTLINE GLAZING, LLC'S FIRST SET OF INTERROGATORIES TO RESPONDENT HARMON BUILDERS, INC.",
            WrittenDiscoveryPlanner.SetTitle(doc, DiscoveryKind.Interrogatories, doc.Parties[0], doc.Parties[1]));
    }

    [Fact]
    public void SetTitle_MemoVariants()
    {
        var criminal = DiscoveryTestData.MemoCriminalDraft().CoreDocument!;
        var us = criminal.Parties.Single(x => x.Id == "party-3");
        var lane = criminal.Parties.Single(x => x.Id == "party-1");
        Assert.Equal("SUBPOENA RIDER: DOCUMENTS TO BE PRODUCED BY VICTOR LANE",
            WrittenDiscoveryPlanner.SetTitle(criminal, DiscoveryKind.RequestsForProduction, us, lane));

        var regulatory = DiscoveryTestData.MemoRegulatoryDraft().CoreDocument!;
        Assert.Equal("CIVIL INVESTIGATIVE DEMAND: DOCUMENT REQUESTS TO VICTOR LANE",
            WrittenDiscoveryPlanner.SetTitle(regulatory, DiscoveryKind.RequestsForProduction, us, lane));
        Assert.Equal("CIVIL INVESTIGATIVE DEMAND: WRITTEN INTERROGATORIES TO VICTOR LANE",
            WrittenDiscoveryPlanner.SetTitle(regulatory, DiscoveryKind.Interrogatories, us, lane));

        var civil = DiscoveryTestData.MemoCivilDraft().CoreDocument!;
        Assert.Equal("PRE-SUIT REQUEST FOR PRESERVATION AND PRODUCTION OF DOCUMENTS TO VICTOR LANE",
            WrittenDiscoveryPlanner.SetTitle(civil, DiscoveryKind.RequestsForProduction, us, lane));
        Assert.Equal("WRITTEN QUESTIONS TO VICTOR LANE",
            WrittenDiscoveryPlanner.SetTitle(civil, DiscoveryKind.Interrogatories, us, lane));
        Assert.Equal("REQUESTS TO ADMIT DIRECTED TO VICTOR LANE",
            WrittenDiscoveryPlanner.SetTitle(civil, DiscoveryKind.RequestsForAdmission, us, lane));
    }

    [Fact]
    public void FileDocumentPart_CivilAndMemo()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument!;
        var p = doc.Parties[0];

        Assert.Equal("Brightline's First Set of Requests for Production", WrittenDiscoveryPlanner.FileDocumentPart(doc, DiscoveryKind.RequestsForProduction, p));
        Assert.Equal("Brightline's First Set of Interrogatories", WrittenDiscoveryPlanner.FileDocumentPart(doc, DiscoveryKind.Interrogatories, p));
        Assert.Equal("Brightline's First Set of Requests for Admission", WrittenDiscoveryPlanner.FileDocumentPart(doc, DiscoveryKind.RequestsForAdmission, p));

        p.ShortName = string.Empty;
        Assert.Equal("Brightline Glazing, LLC's First Set of Interrogatories", WrittenDiscoveryPlanner.FileDocumentPart(doc, DiscoveryKind.Interrogatories, p));

        var memo = DiscoveryTestData.MemoRegulatoryDraft().CoreDocument!;
        var us = memo.Parties.Single(x => x.Id == "party-3");
        Assert.Equal("United States's CID document requests", WrittenDiscoveryPlanner.FileDocumentPart(memo, DiscoveryKind.RequestsForProduction, us));
    }

    // ---- Helpers ----

    private static string Visible(CoreDocument? doc, DiscoveryKind kind)
    {
        var availability = WrittenDiscoveryPlanner.Availability(doc, kind);
        Assert.True(availability.Visible, $"{kind} should be visible");
        Assert.Equal(kind, availability.Kind);
        return availability.Label;
    }

    /// <summary>
    /// LitigationDraft with synthetic issues ISS-04.. added until the guide has <paramref name="n"/> issues.
    /// </summary>
    internal static CaseDraft WithIssues(int n)
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var guide = draft.Guide!;
        for (var i = guide.Issues.Count + 1; guide.Issues.Count < n; i++)
            guide.Issues.Add(new CaseIssue { Id = $"ISS-{i:00}", Number = i, Title = $"Synthetic issue {i}" });
        return draft;
    }

    /// <summary>
    /// LitigationDraft (ten dated facts) with the dates of F-008..F-010 cleared, leaving seven dated facts.
    /// </summary>
    internal static CaseDraft SevenDatedFacts()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        foreach (var fact in draft.Guide!.Facts.Where(f => f.Id is "F-008" or "F-009" or "F-010"))
            fact.Date = string.Empty;
        Assert.Equal(7, draft.Guide.Facts.Count(f => CaseDraft.TryParseDate(f.Date, out _)));
        return draft;
    }

    /// <summary>
    /// A usable corpus with two hot candidates linked to dated facts F-007 and F-003, plus a cold candidate and a hot
    /// candidate linked to an undated fact (neither counts).
    /// </summary>
    internal static DiscoveryCorpus TwoHotCandidates() => new()
    {
        Usable = true,
        Candidates = new List<ExhibitCandidate>
        {
            new() { DocId = "BRL-000210", Subject = "Hold Brightline payments", Date = "2024-07-15", IsHot = true, FactIds = new List<string> { "F-007" } },
            new() { DocId = "BRL-000045", Subject = "CO 7 approved", Date = "2024-03-18", IsHot = true, FactIds = new List<string> { "F-003" } },
            new() { DocId = "BRL-000300", Subject = "Lunch", Date = "2024-03-19", IsHot = false, FactIds = new List<string> { "F-003" } },
            new() { DocId = "BRL-000400", Subject = "Back-charge", Date = "2024-10-22", IsHot = true, FactIds = new List<string> { "F-010" } }
        }
    };

    /// <summary>
    /// LitigationDraft plus facts F-101.. with distinct dates before the filing date, existing issue ids, cast actors and
    /// a mix of Hot/Key/Supporting, until the guide has <paramref name="n"/> dated facts.
    /// </summary>
    internal static CaseDraft DraftWithDatedFacts(int n)
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var guide = draft.Guide!;
        var issues = guide.Issues.Select(i => i.Id).ToList();
        var actors = new[] { "CH-001", "CH-002", "CH-003", "CH-004", "CH-013" };
        var significance = new[] { FactSignificance.Supporting, FactSignificance.Key, FactSignificance.Hot };
        var start = new DateTime(2023, 6, 1);

        for (var i = 1; guide.Facts.Count(f => CaseDraft.TryParseDate(f.Date, out _)) < n; i++)
        {
            var actor = actors[i % actors.Length];
            guide.Facts.Add(new KeyFact
            {
                Id = $"F-{100 + i:000}",
                Date = start.AddDays(i).ToString("yyyy-MM-dd"),
                Title = $"Synthetic fact {i}",
                Description = $"Synthetic fact {i} for written discovery planning.",
                IssueIds = new List<string> { issues[i % issues.Count] },
                ActorIds = new List<string> { actor },
                KnownByIds = new List<string> { actor },
                Significance = significance[i % significance.Length]
            });
        }
        return draft;
    }
}
