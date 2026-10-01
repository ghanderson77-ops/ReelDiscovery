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
        Assert.Equal(1, copy.FormatVersion);
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

        Assert.Equal(1, draft.FormatVersion);
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
