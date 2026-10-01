using System.Drawing;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the static, UI-free helpers of StepCoreDocument; the UserControl is never instantiated.
public class CoreDocumentUiLogicTests
{
    private static readonly Color Accent = Color.FromArgb(0, 120, 215);

    private static CoreDocumentIssue Warning(string sectionId = "facts-1") =>
        new() { Severity = IssueSeverity.Warning, Category = IssueCategory.Citation, SectionId = sectionId, Message = "w" };

    private static CoreDocumentIssue Error(string sectionId = "facts-1") =>
        new() { Severity = IssueSeverity.Error, Category = IssueCategory.Citation, SectionId = sectionId, Message = "e" };

    private static CoreDocumentSection Section(SectionStatus status, bool edited = false) =>
        new() { Id = "facts-1", Heading = "Factual Allegations", Status = status, IsUserEdited = edited };

    [Theory]
    [InlineData(SectionStatus.Writing, " — writing...")]
    [InlineData(SectionStatus.Pending, " — pending")]
    [InlineData(SectionStatus.Failed, " — failed")]
    public void SectionSuffix_StatusSuffixes(SectionStatus status, string expected)
    {
        Assert.Equal(expected, StepCoreDocument.SectionSuffix(Section(status), Array.Empty<CoreDocumentIssue>()));
    }

    [Fact]
    public void SectionSuffix_ReadyWithIssues_ShowsCount()
    {
        var issues = new[] { Warning(), Warning() };
        Assert.Equal(" — 2 issues", StepCoreDocument.SectionSuffix(Section(SectionStatus.Ready), issues));
    }

    [Fact]
    public void SectionSuffix_ReadyEditedNoIssues_ShowsEdited()
    {
        Assert.Equal(" — edited", StepCoreDocument.SectionSuffix(Section(SectionStatus.Ready, edited: true), Array.Empty<CoreDocumentIssue>()));
    }

    [Fact]
    public void SectionSuffix_ReadyCleanNotEdited_IsEmpty()
    {
        Assert.Equal(string.Empty, StepCoreDocument.SectionSuffix(Section(SectionStatus.Ready), Array.Empty<CoreDocumentIssue>()));
    }

    [Fact]
    public void SectionSuffixColor_ByStatusAndIssues()
    {
        var none = Array.Empty<CoreDocumentIssue>();
        Assert.Equal(Accent, StepCoreDocument.SectionSuffixColor(Section(SectionStatus.Writing), none));
        Assert.Equal(Color.DimGray, StepCoreDocument.SectionSuffixColor(Section(SectionStatus.Pending), none));
        Assert.Equal(Color.Red, StepCoreDocument.SectionSuffixColor(Section(SectionStatus.Failed), none));
        Assert.Equal(Color.Red, StepCoreDocument.SectionSuffixColor(Section(SectionStatus.Ready), new[] { Warning(), Error() }));
        Assert.Equal(Color.DarkOrange, StepCoreDocument.SectionSuffixColor(Section(SectionStatus.Ready), new[] { Warning() }));
        Assert.Equal(SystemColors.ControlText, StepCoreDocument.SectionSuffixColor(Section(SectionStatus.Ready, edited: true), none));
    }

    [Fact]
    public void DocumentTitle_Complaint_UsesCourtName()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        Assert.Equal("Complaint — United States District Court for the Northern District of Illinois", StepCoreDocument.DocumentTitle(doc));
    }

    [Fact]
    public void DocumentTitle_Memo_UsesOrganization()
    {
        var doc = CaseTestData.SampleMemoDraft().CoreDocument!;
        Assert.Equal("Investigation Memo — Southwest Financial Crimes Task Force", StepCoreDocument.DocumentTitle(doc));
    }

    [Fact]
    public void DocumentTitle_Petition_UsesCourtName()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        doc.DocumentType = CoreDocumentType.Petition;
        doc.Jurisdiction.CourtName = "District Court of Harris County, Texas";
        Assert.Equal("Petition — District Court of Harris County, Texas", StepCoreDocument.DocumentTitle(doc));
    }

    [Fact]
    public void ReadyStatus_NoIssues_IsGreenWithCounts()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var doc = draft.CoreDocument!;
        var expected = $"Document ready: {doc.Sections.Count} sections, {CoreDocumentAssembler.CountNumberedParagraphs(doc)} numbered paragraphs.";

        var (text, color) = StepCoreDocument.ReadyStatus(draft, Array.Empty<CoreDocumentIssue>());

        Assert.Equal(expected, text);
        Assert.Equal(Color.Green, color);
    }

    [Fact]
    public void ReadyStatus_WithIssues_IsDarkOrange()
    {
        var draft = CaseTestData.SampleComplaintDraft();

        var (text, color) = StepCoreDocument.ReadyStatus(draft, new[] { Error(), Warning(), Warning() });

        Assert.Equal("Document ready with 1 errors and 2 warnings. Review the Issues list.", text);
        Assert.Equal(Color.DarkOrange, color);
    }

    [Fact]
    public void IssuesCaption_CountsBySeverity()
    {
        Assert.Equal("Issues (1 errors, 2 warnings)", StepCoreDocument.IssuesCaption(new[] { Error(), Warning(), Warning() }));
    }
}
