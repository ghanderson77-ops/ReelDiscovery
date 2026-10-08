using System.Globalization;
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

    // ---- Request slots ----

    [Fact]
    public void Rfp_EveryIssueAtLeastOne()
    {
        var draft = WithIssues(4);
        var guide = draft.Guide!;

        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForProduction, new DiscoverySettings { RfpCount = 25 }, null)!;

        Assert.Equal("RFP", plan.Key);
        Assert.Equal(25, plan.Slots.Count);
        Assert.Equal(Enumerable.Range(1, 25), plan.Slots.Select(s => s.Number));
        foreach (var issue in guide.Issues)
            Assert.Contains(plan.Slots, s => s.IssueIds.Contains(issue.Id));
        Assert.All(plan.Slots, s => Assert.Single(s.IssueIds));

        // Slots follow guide issue order
        var order = guide.Issues.Select(i => i.Id).ToList();
        var indexes = plan.Slots.Select(s => order.IndexOf(s.IssueIds[0])).ToList();
        Assert.Equal(indexes.OrderBy(i => i), indexes);

        // Custodians, values and date range of ISS-01
        var slot = plan.Slots.First(s => s.IssueIds[0] == "ISS-01");
        var facts = guide.Facts.Where(f => f.IssueIds.Contains("ISS-01")).ToList();
        Assert.NotEmpty(slot.CustodianIds);
        Assert.All(slot.CustodianIds, id =>
        {
            var member = guide.Cast.Single(c => c.Id == id);
            Assert.Equal("party-2", CounselDirectory.PartyOf(draft, member)?.Id);
            Assert.Contains(facts, f => f.ActorIds.Contains(id) || f.KnownByIds.Contains(id));
        });
        Assert.Contains("V-006", slot.ValueIds);
        Assert.Equal(facts.Min(f => f.Date), slot.FromDate);
        Assert.Equal(facts.Max(f => f.Date), slot.ToDate);

        // ISS-04 has no facts: weight 1, fewer requests than ISS-01, no date range
        var empty = plan.Slots.Where(s => s.IssueIds[0] == "ISS-04").ToList();
        Assert.NotEmpty(empty);
        Assert.True(empty.Count < plan.Slots.Count(s => s.IssueIds[0] == "ISS-01"));
        Assert.All(empty, s => Assert.Equal(string.Empty, s.FromDate));

        // Slots are independent copies
        slot.CustodianIds.Add("CH-999");
        Assert.DoesNotContain("CH-999", plan.Slots.Last(s => s.IssueIds[0] == "ISS-01").CustodianIds);
    }

    [Fact]
    public void Rog_PersonsWithKnowledgeThenContention()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var issues = draft.Guide!.Issues.Select(i => i.Id).ToList();

        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.Interrogatories, new DiscoverySettings { RogCount = 18 }, null)!;

        Assert.Equal(18, plan.Slots.Count);
        Assert.Equal(Enumerable.Range(1, 18), plan.Slots.Select(s => s.Number));
        Assert.True(plan.Slots[0].IsPersonsWithKnowledge);
        Assert.True(plan.Slots[1].IsPersonsWithKnowledge);
        Assert.Equal(issues, plan.Slots[0].IssueIds);
        Assert.All(plan.Slots.Skip(2), s => Assert.False(s.IsPersonsWithKnowledge));
        foreach (var issue in issues)
            Assert.Contains(plan.Slots.Skip(2), s => s.IssueIds.Contains(issue));
    }

    [Fact]
    public void Rog_NeverExceeds25()
    {
        foreach (var issues in new[] { 1, 3, 10, 25, 40 })
            foreach (var requested in new[] { -5, 0, 1, 2, 3, 18, 25, 26, 100 })
            {
                var draft = WithIssues(issues);
                if (issues == 1)
                    draft.Guide!.Issues.RemoveRange(1, draft.Guide.Issues.Count - 1);
                var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.Interrogatories, new DiscoverySettings { RogCount = requested }, null)!;
                Assert.InRange(plan.Slots.Count, 1, 25);
                Assert.Equal(plan.Count.Effective, plan.Slots.Count);
                foreach (var issue in draft.Guide!.Issues)
                    Assert.Contains(plan.Slots, s => s.IssueIds.Contains(issue.Id));
            }
    }

    [Fact]
    public void Rog_ManyIssues_AllCovered()
    {
        var draft = WithIssues(30);

        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.Interrogatories, new DiscoverySettings { RogCount = 18 }, null)!;

        Assert.Equal(25, plan.Slots.Count);
        var contention = plan.Slots.Where(s => !s.IsPersonsWithKnowledge).ToList();
        Assert.Equal(23, contention.Count);
        foreach (var issue in draft.Guide!.Issues)
            Assert.Contains(contention, s => s.IssueIds.Contains(issue.Id));
        Assert.Contains(contention, s => s.IssueIds.Count > 1);
    }

    [Fact]
    public void Rfa_OrderedBySignificanceThenDate()
    {
        var draft = DraftWithDatedFacts(25);
        var guide = draft.Guide!;

        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings { RfaCount = 20 }, null)!;

        Assert.Equal(20, plan.Slots.Count);
        Assert.Equal(Enumerable.Range(1, 20), plan.Slots.Select(s => s.Number));
        Assert.All(plan.Slots, s => Assert.False(s.IsGenuineness));
        Assert.Equal(20, plan.Slots.Select(s => s.FactId).Distinct().Count());

        var facts = plan.Slots.Select(s => guide.Facts.Single(f => f.Id == s.FactId)).ToList();
        var ranks = facts.Select(f => f.Significance == FactSignificance.Supporting ? 1 : 0).ToList();
        Assert.Equal(ranks.OrderBy(r => r), ranks);
        foreach (var group in facts.GroupBy(f => f.Significance == FactSignificance.Supporting))
        {
            var dates = group.Select(f => f.Date).ToList();
            Assert.Equal(dates.OrderBy(d => d, StringComparer.Ordinal), dates);
        }

        // Every Hot/Key dated fact is selected before any Supporting one
        var important = guide.Facts.Count(f => f.Significance != FactSignificance.Supporting);
        Assert.Equal(Math.Min(20, important), ranks.Count(r => r == 0));

        var first = plan.Slots[0];
        var fact = facts[0];
        var longDate = DateTime.ParseExact(fact.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        Assert.Equal($"Admit that on {longDate},", first.Prefix);
        Assert.Equal(fact.IssueIds, first.IssueIds);
    }

    [Fact]
    public void Rfa_UndatedFactsSkipped()
    {
        var draft = SevenDatedFacts();

        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings { RfaCount = 20 }, null)!;

        Assert.Equal(7, plan.Slots.Count);
        Assert.DoesNotContain(plan.Slots, s => s.FactId is "F-008" or "F-009" or "F-010");
    }

    [Fact]
    public void Rfa_GenuinenessOnlyWithUsableCorpus()
    {
        var draft = DraftWithDatedFacts(25);
        var settings = new DiscoverySettings { RfaCount = 20 };

        var unusable = HotCorpusFor25();
        unusable.Usable = false;
        Assert.DoesNotContain(WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, settings, unusable)!.Slots, s => s.IsGenuineness);
        Assert.DoesNotContain(WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, settings, null)!.Slots, s => s.IsGenuineness);

        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, settings, HotCorpusFor25())!;
        var genuine = plan.Slots.Where(s => s.IsGenuineness).ToList();
        Assert.Equal(2, genuine.Count);

        // Genuineness requests come last, ordered by document date
        Assert.All(plan.Slots.TakeLast(2), s => Assert.True(s.IsGenuineness));
        var factIds = plan.Slots.Where(s => !s.IsGenuineness).Select(s => s.FactId).ToHashSet();
        Assert.Equal(new[] { "BRL-000020", "BRL-000210" }, genuine.Select(s => s.DocId));
        Assert.All(genuine, s =>
        {
            Assert.Equal($"Admit that the document produced as {s.DocId} is a true and correct copy of", s.Prefix);
            Assert.False(string.IsNullOrEmpty(s.DocSubject));
            Assert.Contains(s.FactId, factIds);
        });
    }

    [Fact]
    public void Rfa_SlotCountEqualsEffective_WithAndWithoutCorpus()
    {
        var draft = DraftWithDatedFacts(25);

        var plain = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings { RfaCount = 20 }, null)!;
        Assert.Equal(plain.Count.Effective, plain.Slots.Count);
        Assert.Equal(20, plain.Slots.Count(s => !s.IsGenuineness));

        var withCorpus = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings { RfaCount = 20 }, HotCorpusFor25())!;
        Assert.Equal(withCorpus.Count.Effective, withCorpus.Slots.Count);
        Assert.Equal(2, withCorpus.Slots.Count(s => s.IsGenuineness));
        Assert.Equal(18, withCorpus.Slots.Count(s => !s.IsGenuineness));

        var one = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings { RfaCount = 1 }, HotCorpusFor25())!;
        Assert.Single(one.Slots);
        Assert.False(one.Slots[0].IsGenuineness);
    }

    [Fact]
    public void Rfa_FewerDatedFactsThanRequested_LoweredAndSlotCountMatches()
    {
        var draft = SevenDatedFacts();
        var settings = new DiscoverySettings { RfaCount = 20 };

        var plain = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, settings, null)!;
        Assert.Equal(7, plain.Count.Effective);
        Assert.True(plain.Count.Lowered);
        Assert.Equal(7, plain.Slots.Count);
        Assert.NotEmpty(plain.Notes);

        var withCorpus = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, settings, TwoHotCandidates())!;
        Assert.Equal(9, withCorpus.Count.Effective);
        Assert.Equal(9, withCorpus.Slots.Count);
        Assert.Equal(2, withCorpus.Slots.Count(s => s.IsGenuineness));
        Assert.Equal(7, withCorpus.Slots.Count(s => !s.IsGenuineness));
        Assert.Equal(Enumerable.Range(1, 9), withCorpus.Slots.Select(s => s.Number));
    }

    [Fact]
    public void Plan_HiddenKind_ReturnsNull()
    {
        var draft = DiscoveryTestData.MemoCriminalDraft();

        Assert.Null(WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.Interrogatories, new DiscoverySettings(), null));
        Assert.Null(WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings(), null));
        Assert.Null(WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.Deposition, new DiscoverySettings(), null));

        var rider = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForProduction, new DiscoverySettings(), null)!;
        Assert.Equal(DiscoveryVariant.MemoCriminal, rider.Variant);
        Assert.StartsWith("SUBPOENA RIDER", rider.Title);
        Assert.Equal(rider.Count.Effective, rider.Slots.Count);
    }

    [Fact]
    public void Plan_DisabledKind_ReturnsNull()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        Assert.Null(WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForProduction, new DiscoverySettings { RfpEnabled = false }, null));
    }

    [Fact]
    public void Plan_NoGuide_EmptyPlanWithNote()
    {
        var draft = DiscoveryTestData.NoGuideDraft();

        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForProduction, new DiscoverySettings(), null)!;

        Assert.Empty(plan.Slots);
        Assert.NotEmpty(plan.Notes);
        Assert.Null(WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings(), null));
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
    /// A usable corpus for DraftWithDatedFacts(25): hot candidates on Hot facts F-007 and F-102 (both selected for 20
    /// RFAs), a cold one and a hot one on an unknown fact.
    /// </summary>
    internal static DiscoveryCorpus HotCorpusFor25() => new()
    {
        Usable = true,
        Candidates = new List<ExhibitCandidate>
        {
            new() { DocId = "BRL-000210", Subject = "Hold Brightline payments", Date = "2024-07-15", IsHot = true, FactIds = new List<string> { "F-007" } },
            new() { DocId = "BRL-000020", Subject = "Synthetic fact 2", Date = "2023-06-03", IsHot = true, FactIds = new List<string> { "F-102" } },
            new() { DocId = "BRL-000300", Subject = "Lunch", Date = "2024-03-19", IsHot = false, FactIds = new List<string> { "F-007" } },
            new() { DocId = "BRL-000500", Subject = "Other", Date = "2024-03-20", IsHot = true, FactIds = new List<string> { "F-999" } }
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
