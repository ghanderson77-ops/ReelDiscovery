using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Render;

namespace ReelDiscovery.Tests.Discovery;

/// <summary>
/// Writes a sample deposition transcript and a sample federal RFP set to the render output folder so a person can open
/// them in Microsoft Word and compare the pagination with the code's page count (D-23, ANC-01). Category=Render only.
/// </summary>
[Trait("Category", "Render")]
public class DiscoverySampleExportTests
{
    [Fact]
    public void WritesSampleDocumentsForWordCheck()
    {
        var folder = RenderHarness.OutputFolder;

        // Deposition: 400 turns spans many testimony pages
        var deposition = TranscriptTestData.Sample(400);
        var layout = TranscriptLayout.Build(deposition);
        var depositionBytes = TranscriptDocxBuilder.Build(deposition);
        var (depositionErrors, _) = DocxTestHelper.OpenAndValidate(depositionBytes);
        Assert.True(depositionErrors.Count == 0, DocxTestHelper.ErrorsText(depositionErrors));

        var depositionPath = Path.Combine(folder, "discovery-sample-deposition.docx");
        File.WriteAllBytes(depositionPath, depositionBytes);
        File.WriteAllLines(Path.Combine(folder, "discovery-sample-deposition.pages.txt"), new[]
        {
            $"Total pages (code): {layout.Pages.Count}",
            $"Testimony pages: {layout.FirstTestimonyPage}-{layout.LastTestimonyPage} ({layout.TestimonyPageCount} pages)",
            $"Lines per page: {TranscriptLayout.LinesPerPage}"
        });

        // Federal RFP set with 25 hand-written requests
        var rfp = FederalRfp();
        var rfpBytes = DiscoveryDocxBuilder.Build(rfp);
        var (rfpErrors, _) = DocxTestHelper.OpenAndValidate(rfpBytes);
        Assert.True(rfpErrors.Count == 0, DocxTestHelper.ErrorsText(rfpErrors));
        File.WriteAllBytes(Path.Combine(folder, "discovery-sample-rfp.docx"), rfpBytes);

        Assert.True(File.Exists(depositionPath));
        Assert.True(layout.Pages.Count > 1);
    }

    private static WrittenDiscoveryContent FederalRfp()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var doc = draft.CoreDocument!;
        var propounding = doc.Parties.First(p => p.Id == "party-1");
        var responding = doc.Parties.First(p => p.Id == "party-2");
        const DiscoveryKind kind = DiscoveryKind.RequestsForProduction;
        const string caseNumber = "2:25-cv-01873";
        var variant = DiscoveryVariants.Of(doc);
        var title = WrittenDiscoveryPlanner.SetTitle(doc, kind, propounding, responding);
        var serviceDate = DiscoveryBoilerplate.ServiceDate(draft);

        var subjects = new[]
        {
            "Change Order 7, including its pricing and approval",
            "invoice INV-2024-118",
            "the decision to hold payments to subcontractors on the Project",
            "the approved window submittal package",
            "any substitution of window frame assemblies",
            "water intrusion at level 4 of the east elevation",
            "the weekly owner-architect-contractor meeting minutes",
            "the Project schedule and any schedule updates",
            "delay notices issued on the Project",
            "the punch list for the building envelope",
            "warranty claims for window units",
            "field reports prepared by the envelope consultant",
            "testing of installed window units",
            "remediation proposals for the window installation",
            "backcharges assessed against subcontractors",
            "payment applications submitted on the Project",
            "lien waivers exchanged on the Project",
            "insurance notices concerning the window claims",
            "communications with the window manufacturer",
            "the retention withheld on the window subcontract",
            "the termination or threatened termination of any subcontract",
            "the cost to repair the window installation",
            "internal assessments of the water intrusion",
            "photographs of the window installation",
            "the claims and defenses asserted in this action"
        };

        var requests = subjects.Select((s, i) => new DiscoveryRequest
        {
            Number = i + 1,
            Text = $"All Documents Concerning {s}.",
            IssueIds = new List<string> { "ISS-01" }
        }).ToList();

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
            Requests = requests,
            SignatureText = DiscoveryBoilerplate.SignatureText(draft, propounding, serviceDate),
            CertificateText = DiscoveryBoilerplate.CertificateText(draft, responding, serviceDate),
            ServiceDate = serviceDate,
            PropoundingPartyId = "party-1",
            RespondingPartyId = "party-2"
        };
    }
}
