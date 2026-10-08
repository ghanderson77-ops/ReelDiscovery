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

    [Fact]
    public void LitigationDraft_HasFirmsWithPartnerAndAssociateOnBothSides()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var doc = draft.CoreDocument!;
        var plaintiff = CounselDirectory.FirstPlaintiffSide(doc)!;
        var defense = CounselDirectory.FirstDefenseSide(doc)!;

        foreach (var party in new[] { plaintiff, defense })
        {
            Assert.NotNull(CounselDirectory.FirmFor(draft, party.Id));
            Assert.NotNull(CounselDirectory.Partner(draft, party.Id));
            Assert.NotNull(CounselDirectory.Associate(draft, party.Id));
        }
        Assert.NotEqual(CounselDirectory.FirmFor(draft, plaintiff.Id)!.Id, CounselDirectory.FirmFor(draft, defense.Id)!.Id);

        // At least two key non-counsel witnesses on each side
        var witnesses = DiscoveryTestData.WitnessIds(draft).Select(id => draft.Guide!.FindCast(id)!).ToList();
        Assert.True(witnesses.Count(w => CounselDirectory.PartyOf(draft, w)?.Id == plaintiff.Id) >= 2);
        Assert.True(witnesses.Count(w => CounselDirectory.PartyOf(draft, w)?.Id == defense.Id) >= 2);

        var guide = draft.Guide!;
        Assert.True(guide.Issues.Count >= 3);
        Assert.True(guide.Facts.Count >= 9);
        Assert.All(guide.Facts, f => Assert.True(string.CompareOrdinal(f.Date, doc.FilingDate) < 0));
        foreach (var kind in new[] { ValueKind.Amount, ValueKind.ContractNumber, ValueKind.ChangeOrderNumber, ValueKind.InvoiceNumber })
            Assert.Contains(guide.Values, v => v.Kind == kind && v.FactIds.Count > 0);

        // Witness fact counts are distinct so ranking is deterministic
        var counts = witnesses.Select(w => guide.Facts.Count(f => f.KnownByIds.Contains(w.Id))).ToList();
        Assert.Equal(counts.Count, counts.Distinct().Count());
    }

    [Fact]
    public void LitigationDraft_KeepsMichaelScott()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var scott = Assert.Single(draft.Guide!.Cast, c => c.FullName == "Michael Scott");

        Assert.Equal("Michael", scott.FirstName);
        Assert.Equal("Scott", scott.LastName);
        Assert.Contains(scott.Id, DiscoveryTestData.WitnessIds(draft));
        Assert.Equal(CounselDirectory.FirstDefenseSide(draft.CoreDocument!)!.Id, CounselDirectory.PartyOf(draft, scott)?.Id);
    }

    [Fact]
    public void MissingDefenseFirmDraft_DefensePartnerIsNull()
    {
        var draft = DiscoveryTestData.MissingDefenseFirmDraft();
        var doc = draft.CoreDocument!;
        var defense = CounselDirectory.FirstDefenseSide(doc)!;
        var plaintiff = CounselDirectory.FirstPlaintiffSide(doc)!;

        Assert.Null(CounselDirectory.FirmFor(draft, defense.Id));
        Assert.Null(CounselDirectory.Partner(draft, defense.Id));
        Assert.Null(CounselDirectory.Associate(draft, defense.Id));
        Assert.NotNull(CounselDirectory.Partner(draft, plaintiff.Id));
    }

    [Fact]
    public void Fixtures_CoverStateCourtMemosAndNoGuide()
    {
        var state = DiscoveryTestData.StateCourtDraft().CoreDocument!;
        Assert.Equal(CourtSystem.State, state.Jurisdiction.CourtSystem);
        Assert.Equal(CoreDocumentType.Petition, state.DocumentType);
        Assert.True(state.UsesPetitionTerminology);

        var memo = DiscoveryTestData.MemoCriminalDraft();
        Assert.Equal(MatterKind.Criminal, memo.CoreDocument!.MatterKind);
        Assert.NotNull(CounselDirectory.FirstPlaintiffSide(memo.CoreDocument));
        Assert.True(DiscoveryTestData.WitnessIds(memo).Count >= 2);
        Assert.True(memo.Guide!.Facts.Count >= 5);
        Assert.Contains(memo.Guide.Values, v => v.Kind == ValueKind.Amount);
        Assert.Equal("CH-005", CounselDirectory.Partner(memo, "party-2")?.Id);
        // Lane is an individual party placed at Desert Bloom
        Assert.Equal("party-1", CounselDirectory.PartyOf(memo, memo.Guide.FindCast("CH-001"))?.Id);

        Assert.Equal(MatterKind.Regulatory, DiscoveryTestData.MemoRegulatoryDraft().CoreDocument!.MatterKind);
        var civil = DiscoveryTestData.MemoCivilDraft().CoreDocument!;
        Assert.Equal(MatterKind.Civil, civil.MatterKind);
        Assert.Equal(MemoPerspective.InternalCorporate, civil.MemoPerspective);

        var noGuide = DiscoveryTestData.NoGuideDraft();
        Assert.Null(noGuide.Guide);
        Assert.Empty(DiscoveryTestData.WitnessIds(noGuide));
    }
}
