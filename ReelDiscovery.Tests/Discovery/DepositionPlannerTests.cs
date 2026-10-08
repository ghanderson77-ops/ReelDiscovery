using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Discovery;

public class DepositionPlannerTests
{
    // ---- Eligibility and defaults ----

    [Fact]
    public void Eligible_KeyNonCounselRankedByFactCount()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var eligible = DepositionPlanner.Eligible(draft);

        Assert.Equal(new[] { "CH-003", "CH-004", "CH-001", "CH-002", "CH-013" }, eligible.Select(w => w.Id));
        Assert.Equal(new[] { 7, 6, 5, 4, 3 }, eligible.Select(w => w.FactCount));
        Assert.DoesNotContain(eligible, w => w.Id is "CH-005" or "CH-006" or "CH-007" or "CH-010" or "CH-011" or "CH-012");
        Assert.DoesNotContain(eligible, w => w.Id is "CH-008" or "CH-009");

        Assert.Equal(WitnessSide.Defense, eligible.Single(w => w.Id == "CH-003").Side);
        Assert.Equal(WitnessSide.Plaintiff, eligible.Single(w => w.Id == "CH-001").Side);
        Assert.Equal("party-2", eligible.Single(w => w.Id == "CH-013").PartyId);
        Assert.Equal("Michael Scott", eligible.Single(w => w.Id == "CH-013").FullName);
    }

    [Fact]
    public void Eligible_TiesBrokenByActorCountThenStableRank()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        foreach (var fact in draft.Guide!.Facts)
        {
            fact.KnownByIds.Clear();
            fact.ActorIds.Clear();
        }
        // CH-001 and CH-002 both know one fact; CH-002 acts in it, CH-001 only knows it
        draft.Guide.Facts[0].ActorIds.Add("CH-002");
        draft.Guide.Facts[0].KnownByIds.AddRange(new[] { "CH-002", "CH-001" });

        var eligible = DepositionPlanner.Eligible(draft);

        Assert.Equal("CH-002", eligible[0].Id);
        Assert.Equal("CH-001", eligible[1].Id);
        Assert.Equal(eligible.Select(w => w.Id), DepositionPlanner.Eligible(draft).Select(w => w.Id));
    }

    [Fact]
    public void Eligible_NonPartyWitness_IsEligibleWithNonPartySide()
    {
        var draft = DiscoveryTestData.MemoCriminalDraft();

        var eligible = DepositionPlanner.Eligible(draft);

        Assert.Contains(eligible, w => w.Id == "CH-004" && w.Side == WitnessSide.NonParty);
        Assert.Contains(eligible, w => w.Id == "CH-001" && w.Side == WitnessSide.Defense);
        Assert.DoesNotContain(eligible, w => w.Id is "CH-005" or "CH-006");
    }

    [Fact]
    public void Eligible_NoGuide_IsEmpty()
    {
        Assert.Empty(DepositionPlanner.Eligible(DiscoveryTestData.NoGuideDraft()));
        Assert.Empty(DepositionPlanner.Eligible(null!));
        Assert.Empty(DepositionPlanner.DefaultWitnessIds(DiscoveryTestData.NoGuideDraft()));
    }

    [Theory]
    [InlineData(ComplexityLevel.Simple, new[] { "CH-003" })]
    [InlineData(ComplexityLevel.Standard, new[] { "CH-003", "CH-001" })]
    [InlineData(ComplexityLevel.Complex, new[] { "CH-003", "CH-001", "CH-004" })]
    public void DefaultWitnessIds_FollowPresetDefenseFirstThenAlternate(ComplexityLevel level, string[] expected)
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Complexity = level;

        Assert.Equal(expected, DepositionPlanner.DefaultWitnessIds(draft));
    }

    [Fact]
    public void DefaultWitnessIds_OneSideEmpty_TakesFromTheOther()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Complexity = ComplexityLevel.Complex;
        draft.Guide!.Cast.RemoveAll(c => c.Id is "CH-001" or "CH-002");

        Assert.Equal(new[] { "CH-003", "CH-004", "CH-013" }, DepositionPlanner.DefaultWitnessIds(draft));
    }

    [Fact]
    public void DefaultWitnessIds_NeverMoreThanEligible()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Complexity = ComplexityLevel.Complex;
        draft.Guide!.Cast.RemoveAll(c => c.Id is not ("CH-003" or "CH-001"));

        Assert.Equal(new[] { "CH-003", "CH-001" }, DepositionPlanner.DefaultWitnessIds(draft));
    }

    [Fact]
    public void DocumentTitle_FollowsVariant()
    {
        Assert.Equal("DEPOSITION OF MICHAEL SCOTT", DepositionPlanner.DocumentTitle(DiscoveryVariant.Civil, "Michael Scott"));
        Assert.Equal("TRANSCRIPT OF SWORN WITNESS INTERVIEW OF HECTOR RUIZ", DepositionPlanner.DocumentTitle(DiscoveryVariant.MemoCriminal, "Hector Ruiz"));
        Assert.Equal("TRANSCRIPT OF SWORN WITNESS INTERVIEW OF HECTOR RUIZ", DepositionPlanner.DocumentTitle(DiscoveryVariant.MemoCivil, "Hector Ruiz"));
        Assert.Equal("EXAMINATION UNDER OATH OF HECTOR RUIZ", DepositionPlanner.DocumentTitle(DiscoveryVariant.MemoRegulatory, "Hector Ruiz"));
    }

    // ---- Roles ----

    [Fact]
    public void AssignRoles_DefenseWitness_PlaintiffPartnerExaminesDefenseCounselDefends()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var witness = Witness(draft, "CH-003");

        var roles = DepositionPlanner.AssignRoles(draft, witness);

        Assert.Equal("CH-006", roles.Examining.SpeakerId);
        Assert.Equal(SpeakerRole.Examining, roles.Examining.Role);
        Assert.Equal(string.Empty, roles.Examining.Honorific);
        Assert.NotNull(roles.Defending);
        Assert.Contains(roles.Defending!.SpeakerId, new[] { "CH-011", "CH-012" });
        Assert.Equal(SpeakerRole.Defending, roles.Defending.Role);
        Assert.Contains(roles.Appearances, s => s.SpeakerId == "CH-006");
        Assert.Contains(roles.Appearances, s => s.SpeakerId == roles.Defending.SpeakerId);
        Assert.StartsWith("On behalf of Plaintiff ", roles.Examining.PartyLabel);
        Assert.StartsWith("On behalf of Defendant ", roles.Defending.PartyLabel);
        Assert.Empty(roles.Notes);
    }

    [Fact]
    public void AssignRoles_PlaintiffWitness_DefensePartnerExamines()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var roles = DepositionPlanner.AssignRoles(draft, Witness(draft, "CH-001"));

        Assert.Equal("CH-011", roles.Examining.SpeakerId);
        Assert.Contains(roles.Defending!.SpeakerId, new[] { "CH-006", "CH-007" });
    }

    [Fact]
    public void AssignRoles_NonPartyWitness_PlaintiffSideExaminesNoDefending()
    {
        var draft = DiscoveryTestData.MemoCriminalDraft();
        var witness = DepositionPlanner.Eligible(draft).Single(w => w.Id == "CH-004");

        var roles = DepositionPlanner.AssignRoles(draft, witness);

        Assert.Null(roles.Defending);
        // The United States (Complainant) has no firm in the guide: the core document counsel or a named fallback examines
        Assert.False(string.IsNullOrEmpty(roles.Examining.FullName));
        Assert.Contains(roles.Appearances, s => s.Role == SpeakerRole.Appearance);
    }

    [Fact]
    public void AssignRoles_MissingDefenseFirm_FallsBackWithNote()
    {
        var draft = DiscoveryTestData.MissingDefenseFirmDraft();
        var party = draft.CoreDocument!.Parties.Single(p => p.Id == "party-2").Name;

        var roles = DepositionPlanner.AssignRoles(draft, Witness(draft, "CH-003"));

        Assert.NotNull(roles.Defending);
        Assert.Equal($"Counsel for {party}", roles.Defending!.FullName);
        Assert.Equal(string.Empty, roles.Defending.Honorific);
        Assert.Contains($"Counsel for {party} is not in the cast, so the transcript says \"Counsel for {party}\".", roles.Notes);
    }

    [Fact]
    public void AssignRoles_DefendingChoice_IsDeterministic()
    {
        var a = DiscoveryTestData.LitigationDraft();
        var b = DiscoveryTestData.LitigationDraft();
        b.CaseId = a.CaseId;

        Assert.Equal(DepositionPlanner.AssignRoles(a, Witness(a, "CH-004")).Defending!.SpeakerId,
            DepositionPlanner.AssignRoles(b, Witness(b, "CH-004")).Defending!.SpeakerId);
    }

    // ---- Schedule and reporter ----

    [Fact]
    public void Schedule_SixWitnesses_DistinctBusinessDaysInWindow()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var witnesses = SixWitnesses(draft);

        var schedule = DepositionPlanner.Schedule(draft, witnesses);

        Assert.Equal(6, schedule.Count);
        Assert.Equal(6, schedule.Select(s => s.Date).Distinct().Count());
        var filing = new DateOnly(2025, 2, 14);
        var latestFact = new DateOnly(2024, 10, 22);
        foreach (var s in schedule)
        {
            Assert.True(BusinessCalendar.IsBusinessDay(s.Date), $"{s.Date} is not a business day");
            Assert.True(s.Date > latestFact);
            Assert.True(s.Date >= filing.AddMonths(3), $"{s.Date} before window");
            Assert.True(s.Date <= filing.AddMonths(9), $"{s.Date} after window");
            Assert.Contains(s.StartTime, new[] { "9:30 a.m.", "10:00 a.m." });
            Assert.False(string.IsNullOrWhiteSpace(s.Location));
        }
    }

    [Fact]
    public void Schedule_IsDeterministic()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var witnesses = SixWitnesses(draft);

        var first = DepositionPlanner.Schedule(draft, witnesses);
        var second = DepositionPlanner.Schedule(draft, witnesses);

        Assert.Equal(first.Select(Describe), second.Select(Describe));
    }

    [Fact]
    public void Schedule_NoFilingDate_AnchorsOnLatestFact()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.CoreDocument!.FilingDate = string.Empty;

        var schedule = DepositionPlanner.Schedule(draft, DepositionPlanner.Eligible(draft));

        var latestFact = new DateOnly(2024, 10, 22);
        foreach (var s in schedule)
        {
            Assert.True(s.Date >= latestFact.AddMonths(3));
            Assert.True(s.Date <= latestFact.AddMonths(9));
        }
    }

    [Fact]
    public void Schedule_FactAfterWindow_FloorWins()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Guide!.Facts[0].Date = "2026-03-02";

        var schedule = DepositionPlanner.Schedule(draft, DepositionPlanner.Eligible(draft));

        Assert.All(schedule, s => Assert.True(s.Date > new DateOnly(2026, 3, 2)));
        Assert.All(schedule, s => Assert.True(BusinessCalendar.IsBusinessDay(s.Date)));
    }

    [Fact]
    public void Schedule_ReporterAndVideographer_NeverCastNames()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var castNames = draft.Guide!.Cast.Select(c => c.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var schedule = DepositionPlanner.Schedule(draft, SixWitnesses(draft));
        foreach (var s in schedule)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.ReporterName));
            Assert.False(string.IsNullOrWhiteSpace(s.VideographerName));
            Assert.DoesNotContain(s.ReporterName, castNames);
            Assert.DoesNotContain(s.VideographerName, castNames);
            Assert.Matches(@"^CSR No\. \d{5}$", s.ReporterCsr);
        }

        // Rename every cast member to a reporter or videographer pick: the planner skips them
        var picked = schedule.Select(s => s.ReporterName).Concat(schedule.Select(s => s.VideographerName)).Distinct().ToList();
        var members = draft.Guide.Cast.Take(picked.Count).ToList();
        for (var i = 0; i < members.Count; i++)
        {
            var parts = picked[i].Split(' ');
            members[i].FirstName = parts[0];
            members[i].LastName = parts[1];
        }
        castNames = draft.Guide.Cast.Select(c => c.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in DepositionPlanner.Schedule(draft, SixWitnesses(draft)))
        {
            Assert.DoesNotContain(s.ReporterName, castNames);
            Assert.DoesNotContain(s.VideographerName, castNames);
        }
    }

    [Fact]
    public void Schedule_NoGuideOrEmpty_NeverThrows()
    {
        Assert.Empty(DepositionPlanner.Schedule(DiscoveryTestData.NoGuideDraft(), Array.Empty<WitnessCandidate>()));
        var draft = DiscoveryTestData.LitigationDraft();
        var witnesses = DepositionPlanner.Eligible(draft);
        draft.Guide!.Facts.Clear();
        draft.CoreDocument = null;
        Assert.Equal(witnesses.Count, DepositionPlanner.Schedule(draft, witnesses).Count);
    }

    // ---- Plan: chunks, knowledge slice, exhibits ----

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(40)]
    [InlineData(60)]
    public void Plan_ChunkPagesSumToRequested(int pages)
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var plans = DepositionPlanner.Plan(draft, new[] { "CH-003", "CH-001", "CH-013" }, pages, null, null);

        Assert.Equal(3, plans.Count);
        foreach (var plan in plans)
        {
            Assert.Equal(pages, plan.TestimonyPages);
            Assert.Equal(pages, plan.Chunks.Sum(c => c.Pages));
            Assert.All(plan.Chunks, c => Assert.InRange(c.Pages, 1, DepositionPlanner.MaxChunkPages));
            Assert.All(plan.Chunks, c => Assert.Equal(c.Pages * DepositionPlanner.TurnsPerPage, c.TargetTurns));
            Assert.Equal(Enumerable.Range(0, plan.Chunks.Count), plan.Chunks.Select(c => c.Index));
        }
    }

    [Fact]
    public void Plan_PagesClamped()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        Assert.Equal(10, DepositionPlanner.Plan(draft, new[] { "CH-003" }, 2, null, null)[0].Chunks.Sum(c => c.Pages));
        Assert.Equal(60, DepositionPlanner.Plan(draft, new[] { "CH-003" }, 500, null, null)[0].Chunks.Sum(c => c.Pages));
    }

    [Fact]
    public void Plan_SingleTopicOfTwentyPages_SplitsIntoThreeParts()
    {
        var draft = DiscoveryTestData.MemoCriminalDraft();

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-002" }, 20, null, null).Single();

        Assert.Equal(new[] { 7, 7, 6 }, plan.Chunks.Select(c => c.Pages));
        Assert.Equal(new[] { 1, 2, 3 }, plan.Chunks.Select(c => c.Part));
        Assert.All(plan.Chunks, c => Assert.Equal(3, c.PartCount));
        Assert.All(plan.Chunks, c => Assert.Equal("ISS-02", c.IssueId));
        Assert.Equal("TRANSCRIPT OF SWORN WITNESS INTERVIEW OF HECTOR RUIZ", plan.DocumentTitle);
    }

    [Fact]
    public void Plan_TopicsFollowGuideIssueOrderAndWitnessFacts()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, null, null).Single();

        // CH-003 knows five ISS-01 facts, one ISS-02 fact and one ISS-03 fact
        Assert.Equal(new[] { "ISS-01", "ISS-02", "ISS-03" }, plan.Chunks.Select(c => c.IssueId).Distinct());
        Assert.Equal(3, plan.TopicTitles.Count);
        Assert.Equal(draft.Guide!.Issues.Single(i => i.Id == "ISS-02").Title, plan.TopicTitles[1]);
        Assert.True(plan.Chunks.Where(c => c.IssueId == "ISS-01").Sum(c => c.Pages) > plan.Chunks.Where(c => c.IssueId == "ISS-02").Sum(c => c.Pages));
        Assert.Contains("F-007", plan.Chunks.First(c => c.IssueId == "ISS-01").FactIds);
    }

    [Fact]
    public void Plan_MoreTopicsThanPages_KeepsTopWeightedTopics()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var guide = draft.Guide!;
        for (var n = 4; n <= 13; n++)
        {
            guide.Issues.Add(new CaseIssue { Id = $"ISS-{n:00}", Number = n, Title = $"Extra issue {n}" });
            guide.Facts.Add(ExtraFact($"F-1{n:00}", $"ISS-{n:00}"));
        }
        guide.Facts.Add(ExtraFact("F-205", "ISS-05"));
        guide.Facts.Add(ExtraFact("F-207", "ISS-07"));

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-013" }, 10, null, null).Single();

        Assert.Equal(10, plan.TopicTitles.Count);
        Assert.Equal(10, plan.Chunks.Count);
        Assert.All(plan.Chunks, c => Assert.Equal(1, c.Pages));
        Assert.Contains("Extra issue 5", plan.TopicTitles);
        Assert.Contains("Extra issue 7", plan.TopicTitles);
        Assert.DoesNotContain("Extra issue 13", plan.TopicTitles);
        Assert.Equal("ISS-01", plan.Chunks[0].IssueId);
    }

    [Fact]
    public void Plan_WitnessWithNoFacts_GetsBackgroundTopic()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Guide!.Facts.Clear();

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-013" }, 25, null, null).Single();

        Assert.Equal(new[] { "Background and role" }, plan.TopicTitles);
        Assert.Equal(25, plan.Chunks.Sum(c => c.Pages));
        Assert.All(plan.Chunks, c => Assert.Equal("Background and role", c.TopicTitle));
        Assert.All(plan.Exhibits, e => Assert.Equal(0, e.TopicIndex));
    }

    [Fact]
    public void Plan_KnownFactsRespectDepositionDate()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var corpus = new DiscoveryCorpus
        {
            Usable = true,
            Ledger = new List<LedgerEntry>
            {
                new() { Id = "L1", Date = "2024-07-16", Statement = "Hold on Brightline payments", KnownByIds = new List<string> { "CH-013" } },
                new() { Id = "L2", Date = "2099-01-01", Statement = "Far future entry", KnownByIds = new List<string> { "CH-013" } },
                new() { Id = "L3", Date = "2024-07-16", Statement = "Entry known to another witness", KnownByIds = new List<string> { "CH-001" } },
                new() { Id = "L4", Date = "not a date", Statement = "Garbage", KnownByIds = new List<string> { "CH-013" } }
            }
        };

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-013" }, 10, corpus, null).Single();
        var asOf = plan.Schedule.Date;

        // Only facts CH-013 knows; V-002 (Change Order 7, F-003) is not known to CH-013
        Assert.Equal(new[] { "F-007", "F-009", "F-010" }, plan.KnownFacts.Select(f => f.Id));
        Assert.All(plan.KnownFacts, f => Assert.True(KnowledgeGate.KnowsFact(f, "CH-013", asOf)));
        Assert.Equal(new[] { "L1" }, plan.KnownLedger.Select(l => l.Id));
        Assert.DoesNotContain(plan.KnownValues, v => v.Id == "V-002");

        // An unusable corpus contributes no ledger
        Assert.Empty(DepositionPlanner.Plan(draft, new[] { "CH-013" }, 10, new DiscoveryCorpus { Ledger = corpus.Ledger }, null).Single().KnownLedger);
    }

    [Fact]
    public void Plan_CanonicalExhibits_WhenNoCorpus()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, null, null).Single();

        // CH-003 knows the change order (F-003) and the subcontract (F-001, F-008) but not the invoice (F-005)
        Assert.Equal(2, plan.Exhibits.Count);
        Assert.Contains(plan.Exhibits, e => e.Description == "Change order number: CO-007");
        Assert.Contains(plan.Exhibits, e => e.Description == "Subcontract number: SC-LT-2024-031");
        Assert.All(plan.Exhibits, e => Assert.Equal(string.Empty, e.DocId));
        Assert.Equal(new[] { 1, 2 }, plan.Exhibits.Select(e => e.Number));
    }

    [Fact]
    public void Plan_CorpusExhibitsUseWrittenParticipantDocs()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var corpus = new DiscoveryCorpus
        {
            Usable = true,
            Candidates = new List<ExhibitCandidate>
            {
                Candidate("RD-0001", "2024-01-16", "Subcontract executed", false, new[] { "CH-003", "CH-001" }, "F-001"),
                Candidate("RD-0002", "2024-07-15", "Hold all Brightline payments", true, new[] { "CH-013", "CH-003" }, "F-007"),
                Candidate("RD-0003", "2024-03-19", "CO 7 approved", true, new[] { "CH-004" }, "F-003"),
                Candidate("RD-0004", "2024-09-10", "Quantities on CO 7", false, new[] { "CH-003" }, "F-009"),
                Candidate("RD-0005", "2099-01-01", "Future email", true, new[] { "CH-003" }, "F-007"),
                Candidate("RD-0006", "2024-03-20", "Re: CO 7 approved", false, new[] { "CH-003" }, "F-003")
            }
        };

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, corpus, null).Single();

        Assert.Equal(new[] { "RD-0001", "RD-0002", "RD-0006" }, plan.Exhibits.Select(e => e.DocId).OrderBy(d => d, StringComparer.Ordinal));
        Assert.Equal("RD-0002", plan.Exhibits.Single(e => e.IsImpeachment).DocId);
        Assert.Equal("Email dated July 15, 2024, \"Hold all Brightline payments\" (RD-0002)", plan.Exhibits.Single(e => e.DocId == "RD-0002").Description);
        Assert.Equal(new[] { 1, 2, 3 }, plan.Exhibits.Select(e => e.Number));
        Assert.Equal("ISS-01", plan.Exhibits.Single(e => e.DocId == "RD-0002").IssueId);
    }

    [Fact]
    public void Plan_ExhibitsPerDeposition_ScaleWithPagesAndCapAtEight()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var candidates = Enumerable.Range(1, 12)
            .Select(i => Candidate($"RD-1{i:000}", $"2024-07-{i:00}", $"Email {i}", true, new[] { "CH-003" }, "F-007"))
            .ToList();
        var corpus = new DiscoveryCorpus { Usable = true, Candidates = candidates };

        Assert.Equal(3, DepositionPlanner.Plan(draft, new[] { "CH-003" }, 10, corpus, null).Single().Exhibits.Count);
        Assert.Equal(5, DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, corpus, null).Single().Exhibits.Count);
        Assert.Equal(8, DepositionPlanner.Plan(draft, new[] { "CH-003" }, 60, corpus, null).Single().Exhibits.Count);
    }

    [Fact]
    public void Plan_ExhibitsNumberedAcrossDepositions()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var plans = DepositionPlanner.Plan(draft, new[] { "CH-003", "CH-001" }, 25, null, null);

        Assert.Equal(2, plans.Count);
        Assert.True(plans[0].Schedule.Date < plans[1].Schedule.Date);
        var first = plans[0].Exhibits.Select(e => e.Number).ToList();
        var second = plans[1].Exhibits.Select(e => e.Number).ToList();
        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.Equal(Enumerable.Range(1, first.Count), first);
        Assert.Equal(Enumerable.Range(first.Count + 1, second.Count), second);
        Assert.Equal(plans[0].Exhibits.Count, plans[0].Assignments.Count);
        Assert.All(plans[0].Assignments, a => Assert.Equal(plans[0].Witness.Id, a.WitnessId));
    }

    [Fact]
    public void Plan_ExistingAssignmentsKeepNumbers()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var existing = new List<ExhibitAssignment>
        {
            new() { Number = 5, WitnessId = "CH-001", SourceKey = "VAL:V-004", Description = "Unpaid invoice: INV-2024-118", Date = "2024-05-10", IssueId = "ISS-01" },
            new() { Number = 9, WitnessId = "CH-001", SourceKey = "VAL:V-006", Description = "Subcontract number: SC-LT-2024-031", Date = "2024-01-15", IssueId = "ISS-01" },
            new() { Number = 9, WitnessId = "CH-001", SourceKey = "VAL:V-099", Description = "Duplicate number", IssueId = "ISS-01" },
            new() { Number = -3, WitnessId = "CH-001", SourceKey = "VAL:V-098", Description = "Negative number" },
            new() { Number = 12, WitnessId = "CH-999", SourceKey = "VAL:V-001", Description = "Unselected witness" }
        };

        var plans = DepositionPlanner.Plan(draft, new[] { "CH-001", "CH-003" }, 25, null, existing);

        var b = plans.Single(p => p.Witness.Id == "CH-001");
        Assert.Equal(new[] { 5, 9 }, b.Exhibits.Select(e => e.Number));
        Assert.Equal(new[] { "Unpaid invoice: INV-2024-118", "Subcontract number: SC-LT-2024-031" }, b.Exhibits.Select(e => e.Description));

        var c = plans.Single(p => p.Witness.Id == "CH-003");
        Assert.NotEmpty(c.Exhibits);
        Assert.Equal(13, c.Exhibits.Min(e => e.Number));
    }

    [Fact]
    public void Plan_AdverseWitness_TagsOneImpeachmentExhibit()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var defense = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, null, null).Single();
        Assert.Equal(WitnessSide.Defense, defense.Witness.Side);
        Assert.Single(defense.Exhibits, e => e.IsImpeachment);

        var plaintiff = DepositionPlanner.Plan(draft, new[] { "CH-001" }, 25, null, null).Single();
        Assert.Single(plaintiff.Exhibits, e => e.IsImpeachment);

        var memo = DiscoveryTestData.MemoCriminalDraft();
        var memoCorpus = new DiscoveryCorpus
        {
            Usable = true,
            Candidates = new List<ExhibitCandidate> { Candidate("RD-0100", "2023-08-21", "SAR filed", true, new[] { "CH-004" }, "F-005") }
        };
        var nonParty = DepositionPlanner.Plan(memo, new[] { "CH-004" }, 25, memoCorpus, null).Single();
        Assert.Equal(WitnessSide.NonParty, nonParty.Witness.Side);
        Assert.NotEmpty(nonParty.Exhibits);
        Assert.DoesNotContain(nonParty.Exhibits, e => e.IsImpeachment);

        var empty = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, new DiscoveryCorpus { Usable = true }, null).Single();
        Assert.Empty(empty.Exhibits);
    }

    [Fact]
    public void Plan_ExhibitTopicsPointAtMatchingChunks()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var corpus = new DiscoveryCorpus
        {
            Usable = true,
            Candidates = new List<ExhibitCandidate>
            {
                Candidate("RD-0010", "2024-10-22", "Back-charge approved", true, new[] { "CH-003" }, "F-010"),
                Candidate("RD-0011", "2024-07-15", "Hold payments", true, new[] { "CH-003" }, "F-007"),
                Candidate("RD-0012", "2024-03-18", "CO 7", false, new[] { "CH-003" }, "F-003")
            }
        };

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 40, corpus, null).Single();

        Assert.Equal(3, plan.Exhibits.Count);
        foreach (var exhibit in plan.Exhibits)
        {
            var first = plan.Chunks.First(c => c.TopicIndex == exhibit.TopicIndex);
            Assert.Equal(exhibit.IssueId, first.IssueId);
            Assert.Equal(1, first.Part);
            Assert.Contains(exhibit.Number, first.ExhibitNumbers);
            Assert.All(plan.Chunks.Where(c => c.Part > 1), c => Assert.DoesNotContain(exhibit.Number, c.ExhibitNumbers));
        }
        Assert.Equal("ISS-02", plan.Exhibits.Single(e => e.DocId == "RD-0010").IssueId);
    }

    [Fact]
    public void Plan_SpeakersAndScheduleAreComplete()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var plan = DepositionPlanner.Plan(draft, new[] { "CH-013" }, 10, null, null).Single();

        Assert.Equal("DEPOSITION OF MICHAEL SCOTT", plan.DocumentTitle);
        Assert.Contains(plan.Speakers, s => s.SpeakerId == "CH-013" && s.Role == SpeakerRole.Witness && s.FullName == "Michael Scott");
        Assert.Contains(plan.Speakers, s => s.SpeakerId == "REPORTER" && s.Role == SpeakerRole.Reporter && s.FullName == plan.Schedule.ReporterName);
        Assert.Contains(plan.Speakers, s => s.SpeakerId == "VIDEOGRAPHER" && s.Role == SpeakerRole.Videographer);
        Assert.Contains(plan.Speakers, s => s.Role == SpeakerRole.Examining);
        Assert.Contains(plan.Speakers, s => s.Role == SpeakerRole.Defending);
        Assert.Equal(plan.Speakers.Count, plan.Speakers.Select(s => s.SpeakerId).Distinct().Count());
    }

    [Fact]
    public void Plan_UnknownIdsIgnoredCappedAndDeterministic()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var ids = new[] { "CH-404", "CH-003", "CH-003", "CH-004", "CH-001", "CH-002", "CH-013", "CH-011" };

        var plans = DepositionPlanner.Plan(draft, ids, 25, null, null);
        var again = DepositionPlanner.Plan(draft, ids, 25, null, null);

        Assert.Equal(5, plans.Count);
        Assert.Equal(plans.Select(p => p.Witness.Id), again.Select(p => p.Witness.Id));
        Assert.Equal(plans.SelectMany(p => p.Exhibits.Select(e => $"{e.Number}|{e.Description}")),
            again.SelectMany(p => p.Exhibits.Select(e => $"{e.Number}|{e.Description}")));
        Assert.Equal(plans.Select(p => p.Schedule.Date).OrderBy(d => d), plans.Select(p => p.Schedule.Date));

        Assert.Empty(DepositionPlanner.Plan(DiscoveryTestData.NoGuideDraft(), ids, 25, null, null));
        Assert.Empty(DepositionPlanner.Plan(draft, null!, 25, null, null));
        Assert.Empty(DepositionPlanner.Plan(null!, ids, 25, null, null));
    }

    private static ExhibitCandidate Candidate(string docId, string date, string subject, bool hot, string[] participants, params string[] factIds) => new()
    {
        DocId = docId,
        ThreadId = "T-" + docId,
        Subject = subject,
        Date = date,
        ParticipantIds = participants.ToList(),
        IsHot = hot,
        FactIds = factIds.ToList()
    };

    private static KeyFact ExtraFact(string id, string issueId) => new()
    {
        Id = id,
        Date = "2024-06-01",
        Title = $"Extra fact {id}",
        IssueIds = new List<string> { issueId },
        ActorIds = new List<string> { "CH-013" },
        KnownByIds = new List<string> { "CH-013" },
        Significance = FactSignificance.Supporting
    };

    internal static WitnessCandidate Witness(CaseDraft draft, string id) =>
        DepositionPlanner.Eligible(draft).Single(w => w.Id == id);

    private static List<WitnessCandidate> SixWitnesses(CaseDraft draft)
    {
        var witnesses = DepositionPlanner.Eligible(draft);
        witnesses.Add(new WitnessCandidate { Id = "CH-099", FullName = "Extra Witness", Side = WitnessSide.NonParty });
        return witnesses;
    }

    private static string Describe(DepositionSchedule s) =>
        $"{s.WitnessId}|{s.Date:yyyy-MM-dd}|{s.StartTime}|{s.Location}|{s.Remote}|{s.ReporterName}|{s.ReporterCsr}|{s.VideographerName}";
}
