using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CoreDocumentTemplatesTests
{
    [Fact]
    public void Complaint_SpecsMatchSampleSections()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;

        var specs = CoreDocumentTemplates.GetSectionSpecs(doc);

        Assert.Equal(
            new[] { "nature-of-action", "parties", "jurisdiction", "facts-1", "facts-2", "count-1", "prayer" },
            specs.Select(s => s.Id));
        Assert.Equal(
            new[] { "NATURE OF THE ACTION", "PARTIES", "JURISDICTION AND VENUE", "The Subcontract", "Unpaid Change Orders", "COUNT I — Breach of Contract", "PRAYER FOR RELIEF" },
            specs.Select(s => s.Heading));
        Assert.Equal(doc.Sections.Select(s => s.Kind), specs.Select(s => s.Kind));
        Assert.Equal("count-1", specs.Single(s => s.Id == "count-1").CountId);
    }

    [Fact]
    public void Memo_SpecsMatchSampleSections()
    {
        var doc = CaseTestData.SampleMemoDraft().CoreDocument!;

        var specs = CoreDocumentTemplates.GetSectionSpecs(doc);

        Assert.Equal(doc.Sections.Select(s => s.Id), specs.Select(s => s.Id));
        Assert.Equal(doc.Sections.Select(s => s.Heading), specs.Select(s => s.Heading));
        Assert.Equal(doc.Sections.Select(s => s.Kind), specs.Select(s => s.Kind));
        Assert.Equal(
            new[] { "exec-summary", "background", "scope", "key-individuals", "facts-1", "facts-2", "finding-1", "legal-analysis", "recommendations" },
            specs.Select(s => s.Id));
    }

    [Fact]
    public void Petition_SpecsHaveTexasStyleSections()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        doc.DocumentType = CoreDocumentType.Petition;

        var specs = CoreDocumentTemplates.GetSectionSpecs(doc);

        Assert.Equal(
            new[] { "discovery-control-plan", "claim-for-relief", "parties", "jurisdiction", "facts-1", "facts-2", "count-1", "conditions-precedent", "prayer" },
            specs.Select(s => s.Id));
        Assert.Equal("PRAYER", specs.Last().Heading);
    }

    [Fact]
    public void TargetParagraphs_CountsBoundedAndFactsClamped()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        doc.FactPlan[0].TargetParagraphs = 1;
        doc.FactPlan[1].TargetParagraphs = 100;

        var specs = CoreDocumentTemplates.GetSectionSpecs(doc);

        var count = specs.Single(s => s.Kind == SectionKind.Count);
        Assert.InRange(count.TargetParagraphs, 4, 8);
        Assert.Equal(3, specs.Single(s => s.Id == "facts-1").TargetParagraphs);
        Assert.Equal(CoreDocumentTemplates.MaxParagraphsPerSection, specs.Single(s => s.Id == "facts-2").TargetParagraphs);
    }

    [Fact]
    public void TargetParagraphs_FactsWithinRangeKept()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        doc.FactPlan[0].TargetParagraphs = 12;

        var specs = CoreDocumentTemplates.GetSectionSpecs(doc);

        Assert.Equal(12, specs.Single(s => s.Id == "facts-1").TargetParagraphs);
    }

    [Fact]
    public void IsPleading()
    {
        Assert.True(CoreDocumentTemplates.IsPleading(CoreDocumentType.Complaint));
        Assert.True(CoreDocumentTemplates.IsPleading(CoreDocumentType.Petition));
        Assert.False(CoreDocumentTemplates.IsPleading(CoreDocumentType.InvestigationMemo));
    }
}
