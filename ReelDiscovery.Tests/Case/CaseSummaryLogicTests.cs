using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the pure StepCaseSummary line helpers; no control is instantiated.
public class CaseSummaryLogicTests
{
    [Theory]
    [InlineData(CoreDocumentType.Complaint, "Complaint")]
    [InlineData(CoreDocumentType.Petition, "Petition")]
    [InlineData(CoreDocumentType.InvestigationMemo, "Investigation Memo")]
    public void DocumentTypeLabel_NamesEveryType(CoreDocumentType type, string expected)
    {
        Assert.Equal(expected, StepCaseSummary.DocumentTypeLabel(type));
    }

    [Fact]
    public void CoreDocumentLine_CountsSectionsAndNumberedParagraphs()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        var sections = doc.Sections.Count;
        var paragraphs = CoreDocumentAssembler.CountNumberedParagraphs(doc);

        var expected = $"Complaint · {CaseGuideUiText.Plural(sections, "section", "sections")} · {CaseGuideUiText.Plural(paragraphs, "paragraph", "paragraphs")}";
        Assert.Equal(expected, StepCaseSummary.CoreDocumentLine(doc));
        Assert.True(paragraphs > 0);
    }

    [Fact]
    public void CastLine_CountsKeyBackgroundAndOrganizations()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide!;
        Assert.Equal("7 key and 2 background characters · 3 organizations", StepCaseSummary.CastLine(guide));
    }

    [Fact]
    public void FactsLine_UsesFirstAndLastFactDates()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide!;
        Assert.Equal("6 facts · Jan 15, 2024 to Jun 20, 2024", StepCaseSummary.FactsLine(guide));
    }

    [Fact]
    public void FactsLine_NoFacts_ShowsCountOnly()
    {
        Assert.Equal("0 facts", StepCaseSummary.FactsLine(new CaseGuide()));
    }

    [Fact]
    public void ValuesLine_SaysWhereValuesLive()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide!;
        Assert.Equal("4 values · saved in case.json", StepCaseSummary.ValuesLine(guide));
    }

    [Theory]
    [InlineData(0, 0, "No problems found")]
    [InlineData(1, 2, "1 error, 2 warnings")]
    [InlineData(0, 1, "0 errors, 1 warning")]
    public void ChecksLine_SummarizesFindings(int errors, int warnings, string expected)
    {
        Assert.Equal(expected, StepCaseSummary.ChecksLine(errors, warnings));
    }

    [Fact]
    public void ExportedAt_MissingFile_IsNull()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-" + Guid.NewGuid().ToString("N"));
        Assert.Null(StepCaseSummary.ExportedAt(folder, "missing.docx"));
        Assert.Null(StepCaseSummary.ExportedAt(string.Empty, "missing.docx"));
    }

    [Fact]
    public void ExportedAt_ExistingFile_IsItsLastWriteTime()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "export.docx");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var stamp = new DateTime(2024, 3, 18, 14, 5, 0, DateTimeKind.Local);
            File.SetLastWriteTime(path, stamp);

            Assert.Equal(stamp, StepCaseSummary.ExportedAt(folder, "export.docx"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
