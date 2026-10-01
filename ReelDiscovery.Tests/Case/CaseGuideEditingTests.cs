using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CaseGuideEditingTests
{
    private static CaseDraft Draft() => CaseTestData.SampleGuidedDraft();

    private static CastMember Cast(CaseDraft draft, string id) => draft.Guide!.FindCast(id)!;

    private static KeyFact Fact(CaseDraft draft, string id) => draft.Guide!.FindFact(id)!;

    [Fact]
    public void AddCharacter_CreatesKeyCharacterInFirstParty()
    {
        var draft = Draft();
        var revision = draft.Revision;

        var member = CaseGuideEditing.AddCharacter(draft);

        Assert.Equal("CH-010", member.Id);
        Assert.Equal("New", member.FirstName);
        Assert.Equal("character", member.LastName);
        Assert.Equal("New character", member.FullName);
        Assert.Equal("ORG-01", member.OrganizationId);
        Assert.Equal(CastRole.Staff, member.Role);
        Assert.Equal(CastTier.Key, member.Tier);
        Assert.Equal("2024-01-08", member.ActiveFrom);
        Assert.Equal("2024-09-16", member.ActiveTo);
        Assert.Equal("new.character@brightline.com", member.Email);
        Assert.True(member.IsUserEdited);
        Assert.Contains("New character", member.Signature);
        Assert.Contains("Brightline Glazing, LLC", member.Signature);
        Assert.Same(member, draft.Guide!.FindCast("CH-010"));
        Assert.Equal(revision + 1, draft.Revision);
    }

    [Fact]
    public void AddCharacter_NoFilingDate_UsesTimelineEnd()
    {
        var draft = Draft();
        draft.CoreDocument!.FilingDate = "";

        var member = CaseGuideEditing.AddCharacter(draft);

        Assert.Equal("2024-06-28", member.ActiveTo);
    }

    [Fact]
    public void SetCastField_Title_UpdatesSignature()
    {
        var draft = Draft();
        var revision = draft.Revision;

        var result = CaseGuideEditing.SetCastField(draft, "CH-002", CastField.Title, "Senior Project Manager");

        var member = Cast(draft, "CH-002");
        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Empty(result.AffectedFactIds);
        Assert.Equal("Senior Project Manager", member.Title);
        Assert.Contains("Senior Project Manager", member.Signature);
        Assert.True(member.IsUserEdited);
        Assert.Equal(revision + 1, draft.Revision);
        Assert.DoesNotContain(draft.Guide!.Facts, f => f.NeedsReview);
    }

    [Fact]
    public void SetCastField_Unchanged_NoRevision()
    {
        var draft = Draft();
        var revision = draft.Revision;

        var result = CaseGuideEditing.SetCastField(draft, "CH-002", CastField.Title, "Project Manager");

        Assert.True(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(revision, draft.Revision);
        Assert.False(Cast(draft, "CH-002").IsUserEdited);
    }

    [Fact]
    public void SetCastField_UnknownId_NoChange()
    {
        var draft = Draft();
        var revision = draft.Revision;

        var result = CaseGuideEditing.SetCastField(draft, "CH-999", CastField.Title, "Anything");

        Assert.False(result.Changed);
        Assert.Equal(revision, draft.Revision);
    }

    [Fact]
    public void SetCastField_EmptyFirstName_Rejected()
    {
        var draft = Draft();
        var revision = draft.Revision;

        var result = CaseGuideEditing.SetCastField(draft, "CH-001", CastField.FirstName, "  ");

        Assert.Equal("First name cannot be empty.", result.Error);
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal("Maria", Cast(draft, "CH-001").FirstName);
        Assert.Equal(revision, draft.Revision);
    }

    [Fact]
    public void SetCastField_BadDate_Rejected()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetCastField(draft, "CH-001", CastField.ActiveFrom, "3/3/2024");

        Assert.Equal(CaseGuideEditing.DateFormatError, result.Error);
        Assert.Equal("Use a date like 2024-03-03 (YYYY-MM-DD).", CaseGuideEditing.DateFormatError);
        Assert.Equal("2023-10-01", Cast(draft, "CH-001").ActiveFrom);
    }

    [Fact]
    public void SetCastField_ActiveRangeReversed_Rejected()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetCastField(draft, "CH-001", CastField.ActiveTo, "2023-01-01");

        Assert.Equal("The active range ends before it starts.", result.Error);
        Assert.Equal("2024-12-31", Cast(draft, "CH-001").ActiveTo);
    }

    [Fact]
    public void SetCastField_ActiveFrom_FlagsFacts()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetCastField(draft, "CH-003", CastField.ActiveFrom, "2024-02-01");

        Assert.True(result.Changed);
        Assert.Equal(new[] { "F-001", "F-003", "F-006" }, result.AffectedFactIds);
        Assert.True(Fact(draft, "F-006").NeedsReview);
        Assert.Equal("Raymond Harmon was changed after it was written.", Fact(draft, "F-006").ReviewReason);
    }

    [Fact]
    public void SetCastField_Rename_UpdatesEmailSignatureAndFacts()
    {
        var draft = Draft();
        Fact(draft, "F-001").Description = "Maria Delgado signed for Brightline; Maria Delgadoesque is not a match.";
        Fact(draft, "F-005").Title = "Maria Delgado sends the invoice";

        var result = CaseGuideEditing.SetCastField(draft, "CH-001", CastField.LastName, "Delgado-Ruiz");

        var member = Cast(draft, "CH-001");
        Assert.True(result.Changed);
        Assert.Equal("maria.delgado-ruiz@brightline.com", member.Email);
        Assert.Contains("Maria Delgado-Ruiz", member.Signature);
        Assert.Contains("maria.delgado-ruiz@brightline.com", member.Signature);

        var expected = new[] { "F-001", "F-002", "F-005", "F-006" };
        Assert.Equal(expected, result.AffectedFactIds);
        foreach (var id in expected)
        {
            Assert.True(Fact(draft, id).NeedsReview);
            Assert.Equal("Maria Delgado-Ruiz was changed after it was written.", Fact(draft, id).ReviewReason);
        }

        Assert.Equal("Maria Delgado-Ruiz signed for Brightline; Maria Delgadoesque is not a match.", Fact(draft, "F-001").Description);
        Assert.Equal("Maria Delgado-Ruiz sends the invoice", Fact(draft, "F-005").Title);
        Assert.False(Fact(draft, "F-003").NeedsReview);
    }

    [Fact]
    public void SetCastField_FullName_SplitsAtLastSpace()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetCastField(draft, "CH-001", CastField.FullName, "Maria Elena Ortiz");

        var member = Cast(draft, "CH-001");
        Assert.True(result.Changed);
        Assert.Equal("Maria Elena", member.FirstName);
        Assert.Equal("Ortiz", member.LastName);
        Assert.Equal("Name cannot be empty.", CaseGuideEditing.SetCastField(draft, "CH-001", CastField.FullName, "").Error);
    }

    [Fact]
    public void SetCastField_EmailHandEdit_KeptOnRename()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetCastField(draft, "CH-001", CastField.Email, "m.d@brightline.com");
        Assert.True(result.Changed);
        Assert.True(Cast(draft, "CH-001").EmailIsUserEdited);
        Assert.Contains("m.d@brightline.com", Cast(draft, "CH-001").Signature);

        CaseGuideEditing.SetCastField(draft, "CH-001", CastField.LastName, "Delgado-Ruiz");
        Assert.Equal("m.d@brightline.com", Cast(draft, "CH-001").Email);

        var bad = CaseGuideEditing.SetCastField(draft, "CH-001", CastField.Email, "bad");
        Assert.Equal("Use an address like name@company.com.", bad.Error);
        Assert.Equal("m.d@brightline.com", Cast(draft, "CH-001").Email);
    }

    [Fact]
    public void SetCastField_Organization_ChangesDomainAndFlagsFacts()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetCastField(draft, "CH-002", CastField.OrganizationId, "ORG-02");

        var member = Cast(draft, "CH-002");
        Assert.True(result.Changed);
        Assert.Equal("tom.becker@harmon.com", member.Email);
        Assert.Contains("Harmon Builders, Inc.", member.Signature);
        Assert.Equal(new[] { "F-002", "F-003", "F-004" }, result.AffectedFactIds);

        var unknown = CaseGuideEditing.SetCastField(draft, "CH-002", CastField.OrganizationId, "ORG-99");
        Assert.Equal("Choose an organization from the list.", unknown.Error);
        Assert.Equal("ORG-02", member.OrganizationId);
    }

    [Fact]
    public void SetCastField_Tier_BackgroundClearsSignature()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetCastField(draft, "CH-002", CastField.Tier, "Background");

        Assert.True(result.Changed);
        Assert.Equal(CastTier.Background, Cast(draft, "CH-002").Tier);
        Assert.Equal(string.Empty, Cast(draft, "CH-002").Signature);
    }

    [Fact]
    public void SetCastField_CounselSync()
    {
        var draft = Draft();
        Assert.Equal("Morgan Ellis", draft.CoreDocument!.Counsel.AttorneyName);

        var result = CaseGuideEditing.SetCastField(draft, "CH-006", CastField.LastName, "Ellison");

        Assert.True(result.Changed);
        Assert.Equal("Morgan Ellison", draft.CoreDocument.Counsel.AttorneyName);
        Assert.Equal(Cast(draft, "CH-006").Email, draft.CoreDocument.Counsel.Email);
        Assert.EndsWith("@ellispark.com", draft.CoreDocument.Counsel.Email);
    }

    [Fact]
    public void SetCastField_KeepsDownstreamFresh()
    {
        var draft = Draft();
        foreach (var part in GuideStaleness.Order)
            GuideStaleness.Stamp(draft, part);
        Fact(draft, "F-001").Description = "Maria Delgado signed the subcontract.";
        GuideStaleness.Stamp(draft, GuidePart.Values);

        CaseGuideEditing.SetCastField(draft, "CH-001", CastField.LastName, "Delgado-Ruiz");

        Assert.Empty(GuideStaleness.StaleParts(draft));
    }

    [Fact]
    public void FactsReferencing_ActorsAndKnowers()
    {
        var draft = Draft();

        var ids = CaseGuideEditing.FactsReferencing(draft, "CH-002").Select(f => f.Id);

        Assert.Equal(new[] { "F-002", "F-003", "F-004" }, ids);
        Assert.Empty(CaseGuideEditing.FactsReferencing(draft, "CH-999"));
    }

    [Fact]
    public void DeleteCharacter_RemoveFromFacts()
    {
        var draft = Draft();
        var revision = draft.Revision;

        Assert.True(CaseGuideEditing.DeleteCharacter(draft, "CH-002", removeFromFacts: true));

        Assert.Null(draft.Guide!.FindCast("CH-002"));
        Assert.DoesNotContain(draft.Guide.Facts, f => f.ActorIds.Contains("CH-002") || f.KnownByIds.Contains("CH-002"));
        foreach (var id in new[] { "F-002", "F-003", "F-004" })
        {
            Assert.True(Fact(draft, id).NeedsReview);
            Assert.Equal("Tom Becker was removed from the cast.", Fact(draft, id).ReviewReason);
        }

        Assert.False(Fact(draft, "F-001").NeedsReview);
        Assert.Equal(revision + 1, draft.Revision);
        Assert.False(CaseGuideEditing.DeleteCharacter(draft, "CH-002", removeFromFacts: true));
    }

    [Fact]
    public void DeleteCharacter_KeepLinks()
    {
        var draft = Draft();

        Assert.True(CaseGuideEditing.DeleteCharacter(draft, "CH-002", removeFromFacts: false));

        Assert.Null(draft.Guide!.FindCast("CH-002"));
        Assert.Contains("CH-002", Fact(draft, "F-003").ActorIds);
        Assert.DoesNotContain(draft.Guide.Facts, f => f.NeedsReview);
    }

    [Fact]
    public void AddFact_Defaults()
    {
        var draft = Draft();
        var revision = draft.Revision;

        var fact = CaseGuideEditing.AddFact(draft);

        Assert.Equal("F-007", fact.Id);
        Assert.Equal("2024-01-08", fact.Date);
        Assert.Equal("New fact", fact.Title);
        Assert.Equal(FactSignificance.Supporting, fact.Significance);
        Assert.True(fact.IsUserEdited);
        Assert.Same(fact, draft.Guide!.FindFact("F-007"));
        Assert.Equal(revision + 1, draft.Revision);
    }

    [Fact]
    public void SetFactField_Validation()
    {
        var draft = Draft();

        Assert.Equal(CaseGuideEditing.DateFormatError, CaseGuideEditing.SetFactField(draft, "F-001", FactField.Date, "Jan 5").Error);
        Assert.Equal("Fact cannot be empty.", CaseGuideEditing.SetFactField(draft, "F-001", FactField.Title, " ").Error);
        Assert.Equal("2024-01-15", Fact(draft, "F-001").Date);

        var significance = CaseGuideEditing.SetFactField(draft, "F-001", FactField.Significance, "Hot");
        Assert.True(significance.Changed);
        Assert.Equal(FactSignificance.Hot, Fact(draft, "F-001").Significance);
        Assert.True(Fact(draft, "F-001").IsUserEdited);

        var date = CaseGuideEditing.SetFactField(draft, "F-001", FactField.Date, "2024-01-16");
        Assert.True(date.Changed);
        Assert.Equal("2024-01-16", Fact(draft, "F-001").Date);

        Assert.False(CaseGuideEditing.SetFactField(draft, "F-001", FactField.Date, "2024-01-16").Changed);
    }

    [Fact]
    public void SetFactLinks_Actors_KnownByIsSuperset()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetFactLinks(draft, "F-004", FactLinkKind.Actors, new[] { "CH-003" });

        var fact = Fact(draft, "F-004");
        Assert.True(result.Changed);
        Assert.Equal(new[] { "CH-003" }, fact.ActorIds);
        Assert.Contains("CH-003", fact.KnownByIds);
        Assert.True(fact.ActorIds.All(fact.KnownByIds.Contains));
        Assert.True(fact.IsUserEdited);
    }

    [Fact]
    public void SetFactLinks_KnownBy_ActorsCannotBeRemoved()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetFactLinks(draft, "F-003", FactLinkKind.KnownBy, new[] { "CH-001" });

        var fact = Fact(draft, "F-003");
        Assert.True(result.Changed);
        Assert.Equal(new[] { "CH-001", "CH-004", "CH-002" }, fact.KnownByIds);
    }

    [Fact]
    public void SetFactLinks_UnknownIdsIgnored()
    {
        var draft = Draft();
        var before = Fact(draft, "F-004").ActorIds.ToList();

        var unchanged = CaseGuideEditing.SetFactLinks(draft, "F-004", FactLinkKind.Actors, new[] { "CH-002", "CH-999" });
        Assert.True(unchanged.Succeeded);
        Assert.False(unchanged.Changed);
        Assert.Equal(before, Fact(draft, "F-004").ActorIds);

        var issues = CaseGuideEditing.SetFactLinks(draft, "F-004", FactLinkKind.Issues, new[] { "ISS-99" });
        Assert.True(issues.Succeeded);
        Assert.True(issues.Changed);
        Assert.Empty(Fact(draft, "F-004").IssueIds);
    }

    [Fact]
    public void MarkReviewed_ClearsFlag()
    {
        var draft = Draft();
        CaseGuideEditing.SetCastField(draft, "CH-003", CastField.LastName, "Harmonson");
        Assert.True(Fact(draft, "F-006").NeedsReview);

        Assert.True(CaseGuideEditing.MarkReviewed(draft, "F-006"));
        Assert.False(Fact(draft, "F-006").NeedsReview);
        Assert.Equal(string.Empty, Fact(draft, "F-006").ReviewReason);
        Assert.False(CaseGuideEditing.MarkReviewed(draft, "F-006"));
        Assert.False(CaseGuideEditing.MarkReviewed(draft, "F-999"));
    }

    [Fact]
    public void DeleteFact_RemovesFromValues()
    {
        var draft = Draft();

        Assert.Equal(2, CaseGuideEditing.ValuesReferencing(draft, "F-003"));
        Assert.True(CaseGuideEditing.DeleteFact(draft, "F-003"));

        Assert.Null(draft.Guide!.FindFact("F-003"));
        Assert.DoesNotContain(draft.Guide.Values, v => v.FactIds.Contains("F-003"));
        Assert.Equal(0, CaseGuideEditing.ValuesReferencing(draft, "F-003"));
        Assert.False(CaseGuideEditing.DeleteFact(draft, "F-003"));
    }

    [Fact]
    public void AddValue_Defaults()
    {
        var draft = Draft();

        var value = CaseGuideEditing.AddValue(draft);

        Assert.Equal("V-005", value.Id);
        Assert.Equal(ValueKind.Other, value.Kind);
        Assert.Equal("New value", value.Label);
        Assert.Equal(string.Empty, value.Value);
        Assert.True(value.IsUserEdited);
    }

    [Fact]
    public void SetValueField_Validation()
    {
        var draft = Draft();

        Assert.Equal("Label cannot be empty.", CaseGuideEditing.SetValueField(draft, "V-001", ValueField.Label, "").Error);

        Assert.True(CaseGuideEditing.SetValueField(draft, "V-001", ValueField.Kind, "ProjectName").Changed);
        Assert.Equal(ValueKind.ProjectName, draft.Guide!.FindValue("V-001")!.Kind);

        var value = CaseGuideEditing.SetValueField(draft, "V-003", ValueField.Value, "Lakeview Tower II");
        Assert.True(value.Changed);
        Assert.Equal("Lakeview Tower II", draft.Guide.FindValue("V-003")!.Value);
        Assert.True(draft.Guide.FindValue("V-003")!.IsUserEdited);
    }

    [Fact]
    public void SetValueFacts_UnknownDropped()
    {
        var draft = Draft();

        var result = CaseGuideEditing.SetValueFacts(draft, "V-001", new[] { "F-003", "F-999", "F-005", "F-003" });

        Assert.True(result.Changed);
        Assert.Equal(new[] { "F-003", "F-005" }, draft.Guide!.FindValue("V-001")!.FactIds);
    }

    [Fact]
    public void FactEdit_FlagsLinkedValues()
    {
        var draft = Draft();

        CaseGuideEditing.SetFactField(draft, "F-003", FactField.Description, "Harmon approved Change Order 7 for $190,000.00.");

        foreach (var id in new[] { "V-001", "V-002" })
        {
            var value = draft.Guide!.FindValue(id)!;
            Assert.True(value.NeedsReview);
            Assert.Equal("F-003 was changed after it was written.", value.ReviewReason);
        }

        Assert.False(draft.Guide!.FindValue("V-003")!.NeedsReview);
        Assert.Equal(2, GuideStaleness.ValuesNeedingReviewCount(draft));
    }

    [Fact]
    public void FactLinksAndDelete_FlagLinkedValues()
    {
        var draft = Draft();
        CaseGuideEditing.SetFactLinks(draft, "F-001", FactLinkKind.Actors, new[] { "CH-001" });
        Assert.True(draft.Guide!.FindValue("V-003")!.NeedsReview);
        Assert.Equal("F-001 was changed after it was written.", draft.Guide.FindValue("V-003")!.ReviewReason);

        CaseGuideEditing.DeleteFact(draft, "F-003");
        Assert.True(draft.Guide.FindValue("V-001")!.NeedsReview);
        Assert.Equal("F-003 was deleted.", draft.Guide.FindValue("V-001")!.ReviewReason);
    }

    [Fact]
    public void MarkValueReviewed_ClearsFlag()
    {
        var draft = Draft();
        CaseGuideEditing.SetFactField(draft, "F-005", FactField.Title, "Brightline invoices CO-007");
        Assert.True(draft.Guide!.FindValue("V-004")!.NeedsReview);

        Assert.True(CaseGuideEditing.MarkValueReviewed(draft, "V-004"));
        Assert.False(draft.Guide.FindValue("V-004")!.NeedsReview);
        Assert.Equal(string.Empty, draft.Guide.FindValue("V-004")!.ReviewReason);
        Assert.False(CaseGuideEditing.MarkValueReviewed(draft, "V-004"));
    }

    [Fact]
    public void DeleteValue_Removes()
    {
        var draft = Draft();

        Assert.True(CaseGuideEditing.DeleteValue(draft, "V-002"));
        Assert.Null(draft.Guide!.FindValue("V-002"));
        Assert.False(CaseGuideEditing.DeleteValue(draft, "V-002"));
    }

    [Fact]
    public void FactEdit_KeepsValuesFresh()
    {
        var draft = Draft();
        foreach (var part in GuideStaleness.Order)
            GuideStaleness.Stamp(draft, part);

        CaseGuideEditing.SetFactField(draft, "F-003", FactField.Title, "Harmon signs Change Order 7");
        CaseGuideEditing.AddFact(draft);
        CaseGuideEditing.DeleteFact(draft, "F-006");

        Assert.Empty(GuideStaleness.StaleParts(draft));
    }
}
