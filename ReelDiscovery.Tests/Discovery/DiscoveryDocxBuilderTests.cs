using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryDocxBuilderTests
{
    private static WrittenDiscoveryContent Content(CaseDraft draft, DiscoveryKind kind, string propoundingId, string respondingId, string caseNumber, params DiscoveryRequest[] requests)
    {
        var doc = draft.CoreDocument!;
        var propounding = doc.Parties.First(p => p.Id == propoundingId);
        var responding = doc.Parties.First(p => p.Id == respondingId);
        var variant = DiscoveryVariants.Of(doc);
        var title = WrittenDiscoveryPlanner.SetTitle(doc, kind, propounding, responding);
        var serviceDate = DiscoveryBoilerplate.ServiceDate(draft);

        return new WrittenDiscoveryContent
        {
            Kind = kind,
            Variant = variant,
            Title = title,
            CaseNumber = caseNumber,
            CaptionText = DiscoveryBoilerplate.CaptionText(doc, caseNumber, title),
            PreliminaryStatement = DiscoveryBoilerplate.PreliminaryStatement(doc, kind, propounding, responding),
            Definitions = DiscoveryBoilerplate.Definitions(draft, kind, responding),
            Instructions = DiscoveryBoilerplate.Instructions(doc, kind),
            RequestLabel = DiscoveryBoilerplate.RequestLabel(kind, variant),
            Requests = requests.ToList(),
            SignatureText = DiscoveryBoilerplate.SignatureText(draft, propounding, serviceDate),
            CertificateText = DiscoveryBoilerplate.CertificateText(draft, responding, serviceDate),
            ServiceDate = serviceDate,
            PropoundingPartyId = propoundingId,
            RespondingPartyId = respondingId
        };
    }

    private static DiscoveryRequest Request(int number, string text, string prefix = "", bool genuineness = false) => new()
    {
        Number = number,
        Prefix = prefix,
        Text = text,
        IssueIds = new List<string> { "ISS-01" },
        IsGenuineness = genuineness
    };

    // Index of the first paragraph at or after `from` that contains `text`; fails when missing
    private static int IndexOf(List<string> paragraphs, string text, int from)
    {
        for (var i = from; i < paragraphs.Count; i++)
        {
            if (paragraphs[i].Contains(text, StringComparison.Ordinal))
                return i;
        }

        Assert.Fail($"Missing paragraph containing \"{text}\" after index {from}.");
        return -1;
    }

    [Fact]
    public void Build_FederalRfp_ValidatesAndHasSectionsInOrder()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var content = Content(draft, DiscoveryKind.RequestsForProduction, "party-1", "party-2", "2:25-cv-01873",
            Request(1, "All Documents Concerning Change Order 7, including its pricing and approval."),
            Request(2, "All Communications between You and Brightline Concerning invoice INV-2024-118."),
            Request(3, "All Documents Concerning the decision to hold payments to subcontractors on the Project."));

        var (errors, paragraphs) = DocxTestHelper.OpenAndValidate(DiscoveryDocxBuilder.Build(content));

        Assert.Empty(errors);
        var i = IndexOf(paragraphs, "Case No.\t", 0);
        Assert.Contains("2:25-cv-01873", paragraphs[i]);
        i = IndexOf(paragraphs, content.Title, i + 1);
        i = IndexOf(paragraphs, content.PreliminaryStatement, i + 1);
        i = IndexOf(paragraphs, "DEFINITIONS", i + 1);
        foreach (var definition in content.Definitions)
            i = IndexOf(paragraphs, definition.Text, i + 1);
        i = IndexOf(paragraphs, "INSTRUCTIONS", i + 1);
        foreach (var instruction in content.Instructions)
            i = IndexOf(paragraphs, instruction, i + 1);
        i = IndexOf(paragraphs, "REQUESTS FOR PRODUCTION", i + 1);
        for (var n = 1; n <= 3; n++)
            i = IndexOf(paragraphs, $"REQUEST FOR PRODUCTION NO. {n}: ", i + 1);
        i = IndexOf(paragraphs, "/s/ ", i + 1);
        i = IndexOf(paragraphs, "CERTIFICATE OF SERVICE", i + 1);
        IndexOf(paragraphs, content.CertificateText, i + 1);
    }

    [Fact]
    public void Build_NumbersRequestsConsecutively()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var content = Content(draft, DiscoveryKind.RequestsForProduction, "party-1", "party-2", "2:25-cv-01873",
            Request(4, "First."), Request(9, "Second."), Request(2, "Third."));

        var (errors, paragraphs) = DocxTestHelper.OpenAndValidate(DiscoveryDocxBuilder.Build(content));

        Assert.Empty(errors);
        var numbered = paragraphs.Where(p => p.StartsWith("REQUEST FOR PRODUCTION NO.", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "REQUEST FOR PRODUCTION NO. 1: First.", "REQUEST FOR PRODUCTION NO. 2: Second.", "REQUEST FOR PRODUCTION NO. 3: Third." }, numbered);
    }

    [Fact]
    public void Build_StateRog_Validates()
    {
        var draft = DiscoveryTestData.StateCourtDraft();
        var content = Content(draft, DiscoveryKind.Interrogatories, "party-1", "party-2", "2025-CV-123456",
            Request(1, "Identify each Person who approved Change Order 7."),
            Request(2, "Describe the basis for the back-charge issued against Brightline."));

        var (errors, paragraphs) = DocxTestHelper.OpenAndValidate(DiscoveryDocxBuilder.Build(content));

        Assert.Empty(errors);
        Assert.Contains(paragraphs, p => p.Contains("Cause No.", StringComparison.Ordinal) && p.Contains("2025-CV-123456", StringComparison.Ordinal));
        Assert.Contains(paragraphs, p => p == "INTERROGATORIES");
        Assert.Contains(paragraphs, p => p.StartsWith("INTERROGATORY NO. 2: ", StringComparison.Ordinal));
        Assert.Contains(paragraphs, p => p.Contains("Texas Rules of Civil Procedure", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Rfa_KeepsCodePrefixAndGenuineness()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var content = Content(draft, DiscoveryKind.RequestsForAdmission, "party-1", "party-2", "2:25-cv-01873",
            Request(1, "Michael Scott told Harmon's project team to hold all Brightline payments.", "Admit that on July 15, 2024,"),
            Request(2, "Admit that the document produced as BRT-000123 is a true and correct copy of an email Lisa Chen sent on August 5, 2024.", genuineness: true));

        var (errors, paragraphs) = DocxTestHelper.OpenAndValidate(DiscoveryDocxBuilder.Build(content));

        Assert.Empty(errors);
        Assert.Contains("REQUEST FOR ADMISSION NO. 1: Admit that on July 15, 2024, Michael Scott told Harmon's project team to hold all Brightline payments.", paragraphs);
        Assert.Contains(paragraphs, p => p.StartsWith("REQUEST FOR ADMISSION NO. 2: Admit that the document produced as BRT-000123", StringComparison.Ordinal));
        Assert.Contains("REQUESTS FOR ADMISSION", paragraphs);
        Assert.Contains(paragraphs, p => p.Contains("deemed admitted", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_MemoCriminalRider_UsesMemoHeaderAndValidates()
    {
        var draft = DiscoveryTestData.MemoCriminalDraft();
        var content = Content(draft, DiscoveryKind.RequestsForProduction, "party-3", "party-2", "2025-4821",
            Request(1, "All deposit slips for Desert Bloom accounts at First Sandia Bank."),
            Request(2, "All Communications with Hector Ruiz Concerning cash deposits."));

        var (errors, paragraphs) = DocxTestHelper.OpenAndValidate(DiscoveryDocxBuilder.Build(content));

        Assert.Empty(errors);
        Assert.Contains("SOUTHWEST FINANCIAL CRIMES TASK FORCE", paragraphs);
        Assert.Contains("Grand Jury Matter No. 2025-4821", paragraphs);
        Assert.Contains(content.Title, paragraphs);
        Assert.Contains(paragraphs, p => p.StartsWith("ITEM NO. 1: ", StringComparison.Ordinal));
        Assert.Contains("DOCUMENTS TO BE PRODUCED", paragraphs);
        Assert.DoesNotContain(paragraphs, p => p.StartsWith("v.", StringComparison.Ordinal));
        Assert.Contains("CERTIFICATE OF SERVICE", paragraphs);
    }

    [Fact]
    public void Build_ControlCharacters_StillValidate()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var content = Content(draft, DiscoveryKind.RequestsForProduction, "party-1", "party-2", "2:25-cv-01873",
            Request(1, "All Documents\u0001 Concerning\u000B Change Order 7￿."));
        content.PreliminaryStatement += "\u0007";
        content.Definitions[0].Text += "\u001F";

        var (errors, paragraphs) = DocxTestHelper.OpenAndValidate(DiscoveryDocxBuilder.Build(content));

        Assert.Empty(errors);
        Assert.Contains("REQUEST FOR PRODUCTION NO. 1: All Documents Concerning Change Order 7.", paragraphs);
    }
}
