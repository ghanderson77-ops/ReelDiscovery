using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CoreDocumentAssemblerTests
{
    private static CoreDocument Complaint() => CaseTestData.SampleComplaintDraft().CoreDocument!;

    private static CoreDocument Memo() => CaseTestData.SampleMemoDraft().CoreDocument!;

    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(14, "XIV")]
    [InlineData(40, "XL")]
    [InlineData(1994, "MCMXCIV")]
    public void ToRoman(int number, string expected)
    {
        Assert.Equal(expected, CoreDocumentAssembler.ToRoman(number));
    }

    [Fact]
    public void Number_Complaint_ConsecutiveWithIncorporation()
    {
        var numbered = CoreDocumentAssembler.Number(Complaint());

        Assert.Equal(Enumerable.Range(1, 15), numbered.Select(p => p.Number));
        Assert.Equal(
            new[] { "nature-of-action", "nature-of-action", "parties", "parties", "jurisdiction", "jurisdiction",
                    "facts-1", "facts-1", "facts-1", "facts-2", "facts-2", "facts-2", "count-1", "count-1", "count-1" },
            numbered.Select(p => p.SectionId));

        var incorporation = numbered[12];
        Assert.True(incorporation.IsIncorporation);
        Assert.Equal(13, incorporation.Number);
        Assert.Equal("Plaintiff repeats and realleges paragraphs 1 through 12 as if fully set forth herein.", incorporation.Text);
        Assert.Single(numbered, p => p.IsIncorporation);
        Assert.DoesNotContain(numbered, p => p.SectionId == "prayer");
    }

    [Fact]
    public void Number_AfterEdit_IncorporationUpdates()
    {
        var doc = Complaint();
        doc.FindSection("facts-2")!.Paragraphs.Add("Harmon later admitted the change orders were approved.");

        var numbered = CoreDocumentAssembler.Number(doc);

        var incorporation = numbered.Single(p => p.IsIncorporation);
        Assert.Equal(14, incorporation.Number);
        Assert.Equal("Plaintiff repeats and realleges paragraphs 1 through 13 as if fully set forth herein.", incorporation.Text);
        Assert.Equal(16, numbered.Count);
    }

    [Fact]
    public void Number_MultipleCounts_EachIncorporatesFacts()
    {
        var doc = Complaint();
        doc.Sections.Insert(6, new CoreDocumentSection
        {
            Id = "count-2",
            Kind = SectionKind.Count,
            Heading = "COUNT II — Unjust Enrichment",
            Paragraphs = new List<string> { "Harmon was unjustly enriched." }
        });

        var numbered = CoreDocumentAssembler.Number(doc);

        var incorporations = numbered.Where(p => p.IsIncorporation).ToList();
        Assert.Equal(2, incorporations.Count);
        Assert.All(incorporations, p => Assert.Contains("paragraphs 1 through 12 ", p.Text));
        Assert.Equal(16, incorporations[1].Number);
    }

    [Fact]
    public void PleadingPartyLabel_TwoPlaintiffs_Plural()
    {
        var doc = Complaint();
        Assert.Equal("Plaintiff", CoreDocumentAssembler.PleadingPartyLabel(doc));

        doc.Parties.Add(new CaseParty { Id = "party-3", Name = "Second Glazing Co.", Role = PartyRole.Plaintiff });

        Assert.Equal("Plaintiffs", CoreDocumentAssembler.PleadingPartyLabel(doc));
        Assert.StartsWith("Plaintiffs repeats", CoreDocumentAssembler.IncorporationText(doc, 12));
    }

    [Fact]
    public void Number_Memo_Empty()
    {
        Assert.Empty(CoreDocumentAssembler.Number(Memo()));
    }

    [Theory]
    [InlineData("12. The parties met.", "The parties met.")]
    [InlineData("  3) Text", "Text")]
    [InlineData("1.5 million dollars were paid", "1.5 million dollars were paid")]
    [InlineData("No number here.", "No number here.")]
    public void StripLeadingNumber(string input, string expected)
    {
        Assert.Equal(expected, CoreDocumentAssembler.StripLeadingNumber(input));
    }

    [Fact]
    public void BuildCaption_Complaint()
    {
        var caption = CoreDocumentAssembler.BuildCaption(Complaint());

        Assert.Contains("UNITED STATES DISTRICT COURT FOR THE NORTHERN DISTRICT OF ILLINOIS", caption);
        Assert.Contains("Brightline Glazing, LLC", caption);
        Assert.Contains("Plaintiff,", caption);
        Assert.Contains("v.", caption);
        Assert.Contains("Harmon Builders, Inc.", caption);
        Assert.Contains("Defendant.", caption);
        Assert.Contains("Case No. ____________", caption);
        Assert.Contains("COMPLAINT FOR BREACH OF CONTRACT AND UNJUST ENRICHMENT AND DEMAND FOR JURY TRIAL", caption);
        Assert.DoesNotContain("Judge", caption);
        Assert.DoesNotContain("Hon.", caption);
    }

    [Fact]
    public void BuildCaption_StatePetition_UsesCauseNoAndDistrictLine()
    {
        var doc = Complaint();
        doc.DocumentType = CoreDocumentType.Petition;
        doc.Jurisdiction = new Jurisdiction
        {
            CourtSystem = CourtSystem.State,
            State = "Texas",
            CourtName = "In the District Court of Harris County, Texas",
            District = "Harris County"
        };

        var caption = CoreDocumentAssembler.BuildCaption(doc);

        Assert.Contains("HARRIS COUNTY\r\n", caption);
        Assert.Contains("Cause No. ____________", caption);
        Assert.DoesNotContain("DEMAND FOR JURY TRIAL", caption);
    }

    [Fact]
    public void RenderPlainText_Complaint()
    {
        var doc = Complaint();

        var text = CoreDocumentAssembler.RenderPlainText(doc);

        Assert.Contains("DEMAND FOR JURY TRIAL", text);
        Assert.Contains("A. ", text);
        Assert.Contains("C. Brightline asks the Court to grant any further relief", text);
        Assert.Contains("WHEREFORE, Plaintiff respectfully requests", text);
        Assert.Contains("/s/ Morgan Ellis", text);
        Assert.Contains("Attorneys for Plaintiff", text);
        Assert.Contains("Dated: September 16, 2024", text);
        Assert.Contains("13. Plaintiff repeats and realleges paragraphs 1 through 12", text);
        Assert.Contains("FACTUAL ALLEGATIONS", text);

        var last = -1;
        foreach (var heading in doc.Sections.Select(s => s.Heading))
        {
            var index = text.IndexOf(heading, last + 1, StringComparison.Ordinal);
            Assert.True(index > last, $"Heading '{heading}' missing or out of order");
            last = index;
        }
    }

    [Fact]
    public void RenderPlainText_Memo_Government()
    {
        var text = CoreDocumentAssembler.RenderPlainText(Memo());

        Assert.StartsWith("LAW ENFORCEMENT SENSITIVE", text);
        Assert.Contains("MEMORANDUM", text);
        Assert.Contains("TO: Assistant U.S. Attorney Rachel Kim", text);
        Assert.Contains("DATE: October 2, 2023", text);
        Assert.Contains("RE: Suspected distribution conspiracy - Desert Bloom Laundromats", text);
        Assert.DoesNotContain("CC:", text);
        Assert.Contains("FINDING 1: Money laundering through cash-intensive business", text);
    }

    [Fact]
    public void RenderPlainText_Memo_InternalCorporate()
    {
        var doc = Memo();
        doc.MemoPerspective = MemoPerspective.InternalCorporate;

        var text = CoreDocumentAssembler.RenderPlainText(doc);

        Assert.StartsWith("PRIVILEGED & CONFIDENTIAL", text);
    }

    [Fact]
    public void CountNumberedParagraphs_Complaint()
    {
        Assert.Equal(15, CoreDocumentAssembler.CountNumberedParagraphs(Complaint()));
    }
}
