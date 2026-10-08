using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Case;

namespace ReelDiscovery.Tests.Discovery;

public class CounselDirectoryTests
{
    [Fact]
    public void PartyOf_UsesMemberPartyIdThenOrganizationParty()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var guide = draft.Guide!;

        // CH-002 has no PartyId; ORG-01 belongs to party-1
        Assert.Equal("party-1", CounselDirectory.PartyOf(draft, guide.FindCast("CH-002"))?.Id);

        // An explicit PartyId wins over the organization
        var member = guide.FindCast("CH-004")!;
        member.PartyId = "party-1";
        Assert.Equal("party-1", CounselDirectory.PartyOf(draft, member)?.Id);

        // Law firm staff are not parties
        Assert.Null(CounselDirectory.PartyOf(draft, guide.FindCast("CH-011")));
        Assert.Null(CounselDirectory.PartyOf(draft, null));
    }

    [Theory]
    [InlineData(PartyRole.Plaintiff, true)]
    [InlineData(PartyRole.Complainant, true)]
    [InlineData(PartyRole.Defendant, false)]
    [InlineData(PartyRole.Subject, false)]
    [InlineData(PartyRole.ThirdParty, false)]
    public void IsPlaintiffSide_MatchesCaptionGrouping(PartyRole role, bool expected)
    {
        Assert.Equal(expected, CounselDirectory.IsPlaintiffSide(role));
    }

    [Fact]
    public void FirmFor_ReturnsTheFirmRepresentingThePartyOrganization()
    {
        var draft = CaseTestData.SampleCounselDraft();

        Assert.Equal("ORG-03", CounselDirectory.FirmFor(draft, "party-1")?.Id);
        Assert.Equal("ORG-04", CounselDirectory.FirmFor(draft, "party-2")?.Id);
        Assert.Null(CounselDirectory.FirmFor(draft, "party-99"));
        Assert.Null(CounselDirectory.FirmFor(draft, null));
    }

    [Fact]
    public void PartnerAndAssociate_ResolveOutsideCounselAtTheFirm()
    {
        var draft = CaseTestData.SampleCounselDraft();

        Assert.Equal("CH-011", CounselDirectory.Partner(draft, "party-2")?.Id);
        Assert.Equal("CH-012", CounselDirectory.Associate(draft, "party-2")?.Id);
        Assert.Equal("CH-006", CounselDirectory.Partner(draft, "party-1")?.Id);
        Assert.Equal("CH-007", CounselDirectory.Associate(draft, "party-1")?.Id);
    }

    [Fact]
    public void Associate_FirmWithoutAssociate_ReturnsNull()
    {
        var draft = CaseTestData.SampleCounselDraft();
        draft.Guide!.Cast.RemoveAll(c => c.Id == "CH-012");

        Assert.Equal("CH-011", CounselDirectory.Partner(draft, "party-2")?.Id);
        Assert.Null(CounselDirectory.Associate(draft, "party-2"));
    }

    [Fact]
    public void AllLookups_WithoutGuideOrCoreDocument_ReturnNull()
    {
        var noGuide = CaseTestData.SampleCounselDraft();
        var member = noGuide.Guide!.FindCast("CH-002");
        noGuide.Guide = null;

        Assert.Null(CounselDirectory.PartyOf(noGuide, member));
        Assert.Empty(CounselDirectory.PartyOrganizationIds(noGuide, "party-1"));
        Assert.Null(CounselDirectory.FirmFor(noGuide, "party-1"));
        Assert.Null(CounselDirectory.Partner(noGuide, "party-1"));
        Assert.Null(CounselDirectory.Associate(noGuide, "party-1"));

        var noCore = CaseTestData.SampleCounselDraft();
        noCore.CoreDocument = null;
        Assert.Null(CounselDirectory.PartyOf(noCore, noCore.Guide!.FindCast("CH-002")));
        Assert.Null(CounselDirectory.FirstPlaintiffSide(null));
        Assert.Null(CounselDirectory.FirstDefenseSide(null));
    }

    [Fact]
    public void FirstSides_DefaultToFirstPlaintiffAndFirstDefendant()
    {
        var doc = CaseTestData.SampleCounselDraft().CoreDocument!;
        Assert.Equal("party-1", CounselDirectory.FirstPlaintiffSide(doc)?.Id);
        Assert.Equal("party-2", CounselDirectory.FirstDefenseSide(doc)?.Id);

        // Memo: Subject is the defense side; no plaintiff side
        var memo = CaseTestData.SampleMemoDraft().CoreDocument!;
        Assert.Null(CounselDirectory.FirstPlaintiffSide(memo));
        Assert.Equal("party-1", CounselDirectory.FirstDefenseSide(memo)?.Id);

        // ThirdParty is the defense fallback
        doc.Parties[1].Role = PartyRole.ThirdParty;
        Assert.Equal("party-2", CounselDirectory.FirstDefenseSide(doc)?.Id);
    }
}
