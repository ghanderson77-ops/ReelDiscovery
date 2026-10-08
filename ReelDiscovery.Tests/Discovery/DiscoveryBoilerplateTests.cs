using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryBoilerplateTests
{
    private const string Title = "PLAINTIFF BRIGHTLINE GLAZING, LLC'S FIRST SET OF REQUESTS FOR PRODUCTION TO DEFENDANT HARMON BUILDERS, INC.";

    private static CaseParty Party(CaseDraft draft, string id) => draft.CoreDocument!.Parties.First(p => p.Id == id);

    private static List<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    [Fact]
    public void BuildCaption_Default_StillHasBlankCaseNumber()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument!;

        var caption = CoreDocumentAssembler.BuildCaption(doc);

        Assert.Contains("Case No. ____________", caption);
        Assert.Equal(CoreDocumentAssembler.BuildCaption(doc, "____________", Lines(caption)[^1]), caption);
    }

    [Fact]
    public void BuildCaption_Overload_UsesNumberAndTitle()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument!;

        var caption = CoreDocumentAssembler.BuildCaption(doc, "2:25-cv-01873", Title);

        Assert.Contains("Case No. 2:25-cv-01873", caption);
        Assert.DoesNotContain("____", caption);
        Assert.Equal(Title, Lines(caption)[^1]);
        Assert.DoesNotContain(doc.Title.ToUpperInvariant(), caption);
    }

    [Fact]
    public void BuildCaption_Petition_UsesCauseNumber()
    {
        var doc = DiscoveryTestData.StateCourtDraft().CoreDocument!;

        var caption = CoreDocumentAssembler.BuildCaption(doc, "2025-CV-123456", Title);

        Assert.Contains("Cause No. 2025-CV-123456", caption);
        Assert.Contains("HARRIS COUNTY", caption);
    }

    [Fact]
    public void CaptionText_Civil_MatchesAssemblerOverload()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument!;

        Assert.Equal(CoreDocumentAssembler.BuildCaption(doc, "2:25-cv-01873", Title), DiscoveryBoilerplate.CaptionText(doc, "2:25-cv-01873", Title));
    }

    [Fact]
    public void CaptionText_Memo_HasMatterLinesAndNoVersusLine()
    {
        var doc = DiscoveryTestData.MemoCriminalDraft().CoreDocument!;
        const string riderTitle = "SUBPOENA RIDER: DOCUMENTS TO BE PRODUCED BY DESERT BLOOM LAUNDROMATS LLC";

        var lines = Lines(DiscoveryBoilerplate.CaptionText(doc, "2025-4821", riderTitle));

        Assert.Contains("SOUTHWEST FINANCIAL CRIMES TASK FORCE", lines);
        Assert.Contains($"IN THE MATTER OF {doc.Title.ToUpperInvariant()}", lines);
        Assert.Contains("Grand Jury Matter No. 2025-4821", lines);
        Assert.Equal(riderTitle, lines[^1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("v.", StringComparison.Ordinal));
    }

    [Fact]
    public void TranscriptCaptionLines_Pleading_DropsTitle()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument!;

        var lines = DiscoveryBoilerplate.TranscriptCaptionLines(doc, "2:25-cv-01873");

        Assert.Contains(lines, l => l.Contains("Case No. 2:25-cv-01873", StringComparison.Ordinal));
        Assert.Contains("Brightline Glazing, LLC,", lines.Select(l => l.Trim()));
        Assert.DoesNotContain(lines, l => l.Contains(doc.Title.ToUpperInvariant(), StringComparison.Ordinal));
        Assert.NotEqual(string.Empty, lines[^1].Trim());
    }

    [Fact]
    public void TranscriptCaptionLines_Memo_HasMatterHeader()
    {
        var doc = DiscoveryTestData.MemoRegulatoryDraft().CoreDocument!;

        var lines = DiscoveryBoilerplate.TranscriptCaptionLines(doc, "2025-4821");

        Assert.Contains(lines, l => l.Contains("CID No. 2025-4821", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("IN THE MATTER OF", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("v.", StringComparison.Ordinal));
    }

    [Fact]
    public void Instructions_FederalRfp_CiteRulesAndCoverEsiPrivilegeSupplement()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument!;

        var text = string.Join("\n", DiscoveryBoilerplate.Instructions(doc, DiscoveryKind.RequestsForProduction));
        var prelim = DiscoveryBoilerplate.PreliminaryStatement(doc, DiscoveryKind.RequestsForProduction, Party(DiscoveryTestData.LitigationDraft(), "party-1"), Party(DiscoveryTestData.LitigationDraft(), "party-2"));

        Assert.Contains("Federal Rules of Civil Procedure 26 and 34", text + prelim);
        Assert.Contains("thirty (30) days", text);
        Assert.Contains("native format with all metadata", text);
        Assert.Contains("privilege log", text);
        Assert.Contains("supplement", text);
        Assert.Contains("January 8, 2024 through November 30, 2024", text);
        Assert.Contains("Brightline Glazing, LLC", prelim);
        Assert.Contains("Harmon Builders, Inc.", prelim);
    }

    [Fact]
    public void Instructions_FederalRog_CitesRule33AndForbidsSubparts()
    {
        var doc = DiscoveryTestData.LitigationDraft().CoreDocument!;

        var text = string.Join("\n", DiscoveryBoilerplate.Instructions(doc, DiscoveryKind.Interrogatories));

        Assert.Contains("Rule 33", text);
        Assert.Contains("Each interrogatory is a single question", text);
    }

    [Fact]
    public void Instructions_Rfa_DeemedAdmitted()
    {
        var federal = string.Join("\n", DiscoveryBoilerplate.Instructions(DiscoveryTestData.LitigationDraft().CoreDocument!, DiscoveryKind.RequestsForAdmission));
        var state = string.Join("\n", DiscoveryBoilerplate.Instructions(DiscoveryTestData.StateCourtDraft().CoreDocument!, DiscoveryKind.RequestsForAdmission));

        Assert.Contains("deemed admitted", federal);
        Assert.Contains("Rule 36", federal);
        Assert.Contains("deemed admitted", state);
    }

    [Theory]
    [InlineData(DiscoveryKind.RequestsForProduction)]
    [InlineData(DiscoveryKind.Interrogatories)]
    [InlineData(DiscoveryKind.RequestsForAdmission)]
    public void StateCourt_CitesStateRulesWithoutRuleNumbers(DiscoveryKind kind)
    {
        var draft = DiscoveryTestData.StateCourtDraft();
        var doc = draft.CoreDocument!;

        var text = DiscoveryBoilerplate.PreliminaryStatement(doc, kind, Party(draft, "party-1"), Party(draft, "party-2"))
            + "\n" + string.Join("\n", DiscoveryBoilerplate.Instructions(doc, kind));

        Assert.Contains("Texas Rules of Civil Procedure", text);
        Assert.DoesNotMatch(@"Rule \d", text);
        Assert.DoesNotContain("Federal", text);
    }

    [Theory]
    [InlineData(DiscoveryVariant.MemoCriminal, DiscoveryKind.RequestsForProduction, "rider")]
    [InlineData(DiscoveryVariant.MemoRegulatory, DiscoveryKind.Interrogatories, "Civil Investigative Demand")]
    [InlineData(DiscoveryVariant.MemoCivil, DiscoveryKind.RequestsForProduction, "preserve and produce")]
    public void PreliminaryStatement_Memo_UsesVariantWordingWithoutRuleNumbers(DiscoveryVariant variant, DiscoveryKind kind, string expected)
    {
        var draft = variant switch
        {
            DiscoveryVariant.MemoCriminal => DiscoveryTestData.MemoCriminalDraft(),
            DiscoveryVariant.MemoRegulatory => DiscoveryTestData.MemoRegulatoryDraft(),
            _ => DiscoveryTestData.MemoCivilDraft()
        };
        var doc = draft.CoreDocument!;

        var text = DiscoveryBoilerplate.PreliminaryStatement(doc, kind, Party(draft, "party-3"), Party(draft, "party-2"))
            + "\n" + string.Join("\n", DiscoveryBoilerplate.Instructions(doc, kind));

        Assert.Contains(expected, text);
        Assert.Contains("Desert Bloom Laundromats LLC", text);
        Assert.DoesNotMatch(@"Rule \d", text);
        Assert.DoesNotMatch(@"\d+ U\.S\.C\.", text);
    }

    [Fact]
    public void Definitions_CoverStandardTermsRelevantPeriodAndCanonicalValues()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var definitions = DiscoveryBoilerplate.Definitions(draft, DiscoveryKind.RequestsForProduction, Party(draft, "party-2"));
        var terms = definitions.Select(d => d.Term).ToList();

        Assert.Contains("You\" and \"Your", terms);
        Assert.Contains("Harmon Builders, Inc.", definitions.First(d => d.Term == "You\" and \"Your").Text);
        foreach (var term in new[] { "Document", "Communication", "Concerning", "Person", "Identify", "Relevant Period" })
            Assert.Contains(term, terms);
        Assert.Equal("means January 8, 2024 through November 30, 2024.", definitions.First(d => d.Term == "Relevant Period").Text);
        Assert.Contains(definitions, d => d.Term == "Project name" && d.Text.Contains("Lakeview Tower", StringComparison.Ordinal));
        Assert.Contains(definitions, d => d.Term == "Subcontract number" && d.Text.Contains("SC-LT-2024-031", StringComparison.Ordinal));
        Assert.DoesNotContain(definitions, d => d.Term == "Change Order 7 amount");
    }

    [Fact]
    public void RequestLabel_PerKindAndVariant()
    {
        Assert.Equal("REQUEST FOR PRODUCTION NO.", DiscoveryBoilerplate.RequestLabel(DiscoveryKind.RequestsForProduction, DiscoveryVariant.Civil));
        Assert.Equal("INTERROGATORY NO.", DiscoveryBoilerplate.RequestLabel(DiscoveryKind.Interrogatories, DiscoveryVariant.Civil));
        Assert.Equal("REQUEST FOR ADMISSION NO.", DiscoveryBoilerplate.RequestLabel(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.Civil));
        Assert.Equal("ITEM NO.", DiscoveryBoilerplate.RequestLabel(DiscoveryKind.RequestsForProduction, DiscoveryVariant.MemoCriminal));
        Assert.Equal("INTERROGATORY NO.", DiscoveryBoilerplate.RequestLabel(DiscoveryKind.Interrogatories, DiscoveryVariant.MemoRegulatory));
        Assert.Equal("REQUEST TO ADMIT NO.", DiscoveryBoilerplate.RequestLabel(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoCivil));
        Assert.Equal("QUESTION NO.", DiscoveryBoilerplate.RequestLabel(DiscoveryKind.Interrogatories, DiscoveryVariant.MemoCivil));
    }

    [Fact]
    public void ServiceDate_FilingPlus45_NextBusinessDay()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        // 2025-02-14 + 45 days = 2025-03-31 (Monday)
        Assert.Equal("2025-03-31", DiscoveryBoilerplate.ServiceDate(draft));

        // 2025-05-21 + 45 = 2025-07-05 (Saturday) -> Monday July 7
        draft.CoreDocument!.FilingDate = "2025-05-21";
        Assert.Equal("2025-07-07", DiscoveryBoilerplate.ServiceDate(draft));
    }

    [Fact]
    public void ServiceDate_NoFilingDate_UsesTimelineEnd()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.CoreDocument!.FilingDate = string.Empty;

        // 2024-11-30 + 45 = 2025-01-14 (Tuesday)
        Assert.Equal("2025-01-14", DiscoveryBoilerplate.ServiceDate(draft));
    }

    [Fact]
    public void SignatureText_PropoundingPartner()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var lines = Lines(DiscoveryBoilerplate.SignatureText(draft, Party(draft, "party-2"), "2025-03-31"));

        Assert.Equal("Dated: March 31, 2025", lines[0]);
        Assert.Contains("Thistle & Bracken LLP", lines);
        Assert.Contains("By: /s/ Nora Quist", lines);
        Assert.Contains("nora.quist@thistlebracken.com", lines);
        Assert.Contains(lines, l => l.StartsWith("Tel: ", StringComparison.Ordinal));
        Assert.Equal("Attorneys for Defendant Harmon Builders, Inc.", lines[^1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("Bar No.", StringComparison.Ordinal));
    }

    [Fact]
    public void SignatureText_PlaintiffWithoutFirm_UsesCoreCounsel()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Guide!.Organizations.RemoveAll(o => o.Id == "ORG-03");
        draft.Guide.Cast.RemoveAll(c => c.OrganizationId == "ORG-03");
        var counsel = draft.CoreDocument!.Counsel;

        var lines = Lines(DiscoveryBoilerplate.SignatureText(draft, Party(draft, "party-1"), "2025-03-31"));

        Assert.Contains(counsel.FirmName, lines);
        Assert.Contains($"By: /s/ {counsel.AttorneyName}", lines);
        Assert.Contains(counsel.Email, lines);
        Assert.Equal("Attorneys for Plaintiff Brightline Glazing, LLC", lines[^1]);
    }

    [Fact]
    public void SignatureText_Memo_UsesMemoAuthor()
    {
        var draft = DiscoveryTestData.MemoCriminalDraft();
        var memo = draft.CoreDocument!.Memo;

        var lines = Lines(DiscoveryBoilerplate.SignatureText(draft, Party(draft, "party-3"), "2025-03-31"));

        Assert.Equal("Dated: March 31, 2025", lines[0]);
        Assert.Contains(memo.From, lines);
        Assert.Contains(memo.Organization, lines);
    }

    [Fact]
    public void CertificateText_NamesRespondingCounselEmail()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var text = DiscoveryBoilerplate.CertificateText(draft, Party(draft, "party-2"), "2025-03-31");

        Assert.Contains("March 31, 2025", text);
        Assert.Contains("Harmon Builders, Inc.", text);
        Assert.Contains("nora.quist@thistlebracken.com", text);
    }

    [Fact]
    public void CertificateText_NoRespondingFirm_SaysCounselOfRecord()
    {
        var draft = DiscoveryTestData.MissingDefenseFirmDraft();

        var text = DiscoveryBoilerplate.CertificateText(draft, Party(draft, "party-2"), "2025-03-31");

        Assert.Contains("counsel of record", text);
        Assert.Contains("Harmon Builders, Inc.", text);
        Assert.DoesNotContain("@", text);
    }
}
