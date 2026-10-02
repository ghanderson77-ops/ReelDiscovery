using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Tests.Case;

public class CaseGuideModelTests
{
    [Fact]
    public void RoundTrip_PreservesGuide()
    {
        var original = CaseTestData.SampleGuidedDraft();

        var copy = CaseDraft.FromJson(original.ToJson());

        Assert.Equal(CaseStage.CaseGuide, copy.Stage);
        Assert.Equal(original.CaseId, copy.CaseId);
        Assert.Equal(CaseDraft.CurrentFormatVersion, copy.FormatVersion);
        Assert.NotNull(copy.Guide);

        var guide = original.Guide!;
        var guideCopy = copy.Guide!;
        Assert.Equal(guide.AllIds().ToList(), guideCopy.AllIds().ToList());
        Assert.Equal(guide.Cast[0].Email, guideCopy.Cast[0].Email);
        Assert.Equal(guide.Facts[2].KnownByIds, guideCopy.Facts[2].KnownByIds);
        Assert.Equal(guide.Values[0].Value, guideCopy.Values[0].Value);
        Assert.Equal(9, guideCopy.IdCounters["CH"]);
        Assert.Equal(guide.ComputeHash(), guideCopy.ComputeHash());
    }

    [Fact]
    public void Json_UsesStringEnums()
    {
        var json = CaseTestData.SampleGuidedDraft().ToJson();

        Assert.Contains("\"Background\"", json);
        Assert.Contains("\"OutsideCounsel\"", json);
        Assert.Contains("\"Hot\"", json);
        Assert.Contains("\"ChangeOrderNumber\"", json);
        Assert.Contains("\"CaseGuide\"", json);
        Assert.Contains("\"LawFirm\"", json);
    }

    [Fact]
    public void Json_HasNoFolderPathOrSecrets()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.FolderPath = @"C:\x\y";

        var json = draft.ToJson();

        Assert.DoesNotContain("FolderPath", json);
        Assert.DoesNotContain("ApiKey", json);
        Assert.DoesNotContain("sk-", json);
        Assert.DoesNotContain("ModelId", json);
        Assert.DoesNotContain("Provider", json);
    }

    [Fact]
    public void Normalize_RepairsNulls()
    {
        const string json = """
            {"Scenario":null,"Substitutions":null,"Guide":{"Cast":null,"Facts":[null,{"Id":"F-001","IssueIds":null}],"IdCounters":null,"CastState":null}}
            """;

        var draft = CaseDraft.FromJson(json);
        draft.Normalize();

        Assert.Equal(string.Empty, draft.Scenario);
        Assert.Empty(draft.Substitutions);
        var guide = draft.Guide!;
        Assert.Empty(guide.Cast);
        Assert.Single(guide.Facts);
        Assert.Empty(guide.Facts[0].IssueIds);
        Assert.Equal(string.Empty, guide.Facts[0].Title);
        Assert.Empty(guide.IdCounters);
        Assert.NotNull(guide.CastState);
        Assert.NotNull(guide.State(GuidePart.Cast));
    }

    [Fact]
    public void Normalize_FixesVersionAndCaseId()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.FormatVersion = 0;
        draft.CaseId = Guid.Empty;

        draft.Normalize();

        Assert.Equal(CaseDraft.CurrentFormatVersion, draft.FormatVersion);
        Assert.NotEqual(Guid.Empty, draft.CaseId);
    }

    [Fact]
    public void AdvanceStage_NeverMovesBack()
    {
        var draft = new CaseDraft { Stage = CaseStage.CaseGuide };

        draft.AdvanceStage(CaseStage.CoreDocument);
        Assert.Equal(CaseStage.CaseGuide, draft.Stage);

        draft.AdvanceStage(CaseStage.Summary);
        Assert.Equal(CaseStage.Summary, draft.Stage);
    }

    [Fact]
    public void Lookups_AndAllIds()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;

        var partner = guide.FindCast("CH-006");
        Assert.NotNull(partner);
        Assert.Equal(CastRole.OutsideCounsel, partner!.Role);
        Assert.Equal("Partner", partner.Title);
        Assert.Equal(draft.CoreDocument!.Counsel.AttorneyName, partner.FullName);

        Assert.Null(guide.FindFact("missing"));
        Assert.Null(guide.FindIssue(null));
        Assert.Null(guide.FindIssue(string.Empty));

        var ids = guide.AllIds().ToList();
        Assert.Contains("ISS-01", ids);
        Assert.Contains("ORG-03", ids);
        Assert.Contains("CH-009", ids);
        Assert.Contains("F-006", ids);
        Assert.Contains("V-004", ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void FullName_JoinsTrimmedParts()
    {
        var member = new CastMember { FirstName = " Maria ", LastName = "Delgado" };
        Assert.Equal("Maria Delgado", member.FullName);

        member.LastName = "";
        Assert.Equal("Maria", member.FullName);
    }

    [Fact]
    public void ComputeHash_ChangesOnEdit()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide!;
        var before = guide.ComputeHash();

        guide.Facts[0].Title += " (revised)";

        Assert.NotEqual(before, guide.ComputeHash());
    }

    [Fact]
    public void ComputeHash_IgnoresUiState()
    {
        var mutations = new (string Name, Action<CaseGuide> Apply)[]
        {
            ("DismissedStaleKey", g => g.DismissedStaleKey = "stale-key"),
            ("IdCounters", g => g.IdCounters["F"] = 999),
            ("IssuesState.Status", g => g.IssuesState.Status = GuidePartStatus.Failed),
            ("IssueDetailsState.FailureMessage", g => g.IssueDetailsState.FailureMessage = "boom"),
            ("CastState.BasisHash", g => g.CastState.BasisHash = "abc"),
            ("FactsState.BasisComponents", g => g.FactsState.BasisComponents["core"] = "def"),
            ("FactsState.GeneratedUtc", g => g.FactsState.GeneratedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
            ("ValuesState.CompletedUnits", g => g.ValuesState.CompletedUnits.Add("unit-1")),
            ("Fact.NeedsReview", g => g.Facts[0].NeedsReview = !g.Facts[0].NeedsReview),
            ("Fact.ReviewReason", g => g.Facts[0].ReviewReason = "Check this."),
            ("Fact.IsUserEdited", g => g.Facts[0].IsUserEdited = !g.Facts[0].IsUserEdited),
            ("Fact.MappingNotes", g => g.Facts[0].MappingNotes.Add("note")),
            ("Fact.SourceSectionId", g => g.Facts[0].SourceSectionId = "S-99"),
            ("Cast.IsUserEdited", g => g.Cast[0].IsUserEdited = !g.Cast[0].IsUserEdited),
            ("Cast.EmailIsUserEdited", g => g.Cast[0].EmailIsUserEdited = !g.Cast[0].EmailIsUserEdited),
            ("Cast.DetailsPending", g => g.Cast[0].DetailsPending = !g.Cast[0].DetailsPending),
            ("Value.NeedsReview", g => g.Values[0].NeedsReview = !g.Values[0].NeedsReview),
            ("Value.ReviewReason", g => g.Values[0].ReviewReason = "Check this."),
            ("Value.IsUserEdited", g => g.Values[0].IsUserEdited = !g.Values[0].IsUserEdited),
            ("Value.MappingNotes", g => g.Values[0].MappingNotes.Add("note"))
        };

        foreach (var (name, apply) in mutations)
        {
            var guide = CaseTestData.SampleGuidedDraft().Guide!;
            var before = guide.ComputeHash();
            apply(guide);
            Assert.True(before == guide.ComputeHash(), $"{name} changed the hash");
        }
    }

    [Fact]
    public void ComputeHash_ChangesOnContent()
    {
        var mutations = new (string Name, Action<CaseGuide> Apply)[]
        {
            ("Fact.Date", g => g.Facts[0].Date = "2001-01-01"),
            ("Fact.Title", g => g.Facts[0].Title += " x"),
            ("Fact.Description", g => g.Facts[0].Description += " x"),
            ("Fact.IssueIds", g => g.Facts[0].IssueIds.Add("ISS-99")),
            ("Fact.ActorIds", g => g.Facts[0].ActorIds.Add("CH-99")),
            ("Fact.KnownByIds", g => g.Facts[0].KnownByIds.Add("CH-98")),
            ("Fact.Significance", g => g.Facts[0].Significance = g.Facts[0].Significance == FactSignificance.Hot ? FactSignificance.Supporting : FactSignificance.Hot),
            ("Cast.FirstName", g => g.Cast[0].FirstName += "x"),
            ("Cast.LastName", g => g.Cast[0].LastName += "x"),
            ("Cast.Email", g => g.Cast[0].Email = "someone.else@example.com"),
            ("Cast.ActiveFrom", g => g.Cast[0].ActiveFrom = "2001-01-01"),
            ("Cast.ActiveTo", g => g.Cast[0].ActiveTo = "2031-01-01"),
            ("Cast.Role", g => g.Cast[0].Role = g.Cast[0].Role == CastRole.Staff ? CastRole.Party : CastRole.Staff),
            ("Cast.Tier", g => g.Cast[0].Tier = g.Cast[0].Tier == CastTier.Key ? CastTier.Background : CastTier.Key),
            ("Cast.OrganizationId", g => g.Cast[0].OrganizationId = "ORG-99"),
            ("Org.TimeZoneId", g => g.Organizations[0].TimeZoneId = "Pacific Standard Time"),
            ("Org.Domain", g => g.Organizations[0].Domain = "other.example"),
            ("Org.Name", g => g.Organizations[0].Name += " x"),
            ("Issue.Title", g => g.Issues[0].Title += " x"),
            ("Issue.RelevanceCriteria", g => g.Issues[0].RelevanceCriteria += " x"),
            ("Value.Value", g => g.Values[0].Value += "1")
        };

        foreach (var (name, apply) in mutations)
        {
            var guide = CaseTestData.SampleGuidedDraft().Guide!;
            var before = guide.ComputeHash();
            apply(guide);
            Assert.True(before != guide.ComputeHash(), $"{name} did not change the hash");
        }
    }

    [Fact]
    public void ComputeHash_NullListsAndEntries_DoNotThrow()
    {
        var guide = new CaseGuide
        {
            Issues = null!,
            Organizations = new() { null! },
            Cast = null!,
            Facts = new() { null!, new KeyFact { IssueIds = null!, ActorIds = null!, KnownByIds = null! } },
            Values = null!
        };

        var hash = guide.ComputeHash();

        Assert.False(string.IsNullOrEmpty(hash));
    }

    [Fact]
    public void CaseStage_OrderAndAdvance()
    {
        Assert.Equal(
            new[] { CaseStage.Described, CaseStage.CoreDocument, CaseStage.CaseGuide, CaseStage.CorpusSettings, CaseStage.ReviewPlan, CaseStage.Summary },
            Enum.GetValues<CaseStage>());

        var draft = CaseTestData.SampleGuidedDraft();
        Assert.Equal(CaseStage.CaseGuide, draft.Stage);

        draft.AdvanceStage(CaseStage.CorpusSettings);
        Assert.Equal(CaseStage.CorpusSettings, draft.Stage);

        draft.AdvanceStage(CaseStage.ReviewPlan);
        draft.AdvanceStage(CaseStage.CaseGuide);
        Assert.Equal(CaseStage.ReviewPlan, draft.Stage);
    }

    [Fact]
    public void CaseStage_StoredByName_SummaryStillLoads()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.AdvanceStage(CaseStage.Summary);

        var json = draft.ToJson();
        Assert.Contains("\"Summary\"", json);

        var copy = CaseDraft.FromJson(json);
        Assert.Equal(CaseStage.Summary, copy.Stage);
    }

    [Fact]
    public void State_MapsEachPart()
    {
        var guide = new CaseGuide();

        Assert.Same(guide.IssuesState, guide.State(GuidePart.Issues));
        Assert.Same(guide.CastState, guide.State(GuidePart.Cast));
        Assert.Same(guide.FactsState, guide.State(GuidePart.Facts));
        Assert.Same(guide.ValuesState, guide.State(GuidePart.Values));
    }

    [Fact]
    public void SampleGuidedDraft_IsInternallyConsistent()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide!;

        foreach (var issue in guide.Issues)
            Assert.Contains(guide.Facts, f => f.IssueIds.Contains(issue.Id));

        foreach (var member in guide.Cast.Where(c => c.Tier == CastTier.Key && c.Role is CastRole.Party or CastRole.Staff))
            Assert.Contains(guide.Facts, f => f.ActorIds.Contains(member.Id));

        foreach (var fact in guide.Facts)
        {
            Assert.All(fact.ActorIds, id => Assert.Contains(id, fact.KnownByIds));
            Assert.All(fact.IssueIds, id => Assert.NotNull(guide.FindIssue(id)));
            Assert.All(fact.KnownByIds, id => Assert.NotNull(guide.FindCast(id)));
            Assert.True(CaseDraft.TryParseDate(fact.Date, out _));
        }

        foreach (var member in guide.Cast)
            Assert.NotNull(guide.FindOrganization(member.OrganizationId));

        foreach (var value in guide.Values)
            Assert.All(value.FactIds, id => Assert.NotNull(guide.FindFact(id)));
    }
}
