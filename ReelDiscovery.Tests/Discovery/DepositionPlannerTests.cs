using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Discovery;

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
        // The United States (Complainant) has no firm in the guide and is not the plaintiff for doc.Counsel purposes
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
