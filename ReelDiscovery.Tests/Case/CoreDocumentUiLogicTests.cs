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
    public void ShowsEmptyState_NoDocumentOrNothingWritten()
    {
        Assert.True(StepCoreDocument.ShowsEmptyState(null, isGenerating: false));
        Assert.False(StepCoreDocument.ShowsEmptyState(null, isGenerating: true));

        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        foreach (var section in doc.Sections)
        {
            section.Status = SectionStatus.Pending;
            section.Paragraphs = new List<string>();
        }
        Assert.True(StepCoreDocument.ShowsEmptyState(doc, isGenerating: false));
    }

    [Fact]
    public void ShowsEmptyState_HandWrittenPendingText_KeepsEditorVisible()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        foreach (var section in doc.Sections)
        {
            section.Status = SectionStatus.Pending;
            section.Paragraphs = new List<string>();
        }
        doc.Sections[0].Paragraphs = new List<string> { "Typed by hand." };

        Assert.False(StepCoreDocument.ShowsEmptyState(doc, isGenerating: false));
    }

    [Fact]
    public void HasUnfinishedSections_OnlyForUnwrittenSectionsWithoutHandText()
    {
        Assert.False(StepCoreDocument.HasUnfinishedSections(null));

        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        Assert.False(StepCoreDocument.HasUnfinishedSections(doc));

        var facts2 = doc.FindSection("facts-2")!;
        facts2.Status = SectionStatus.Pending;
        facts2.IsUserEdited = true;
        Assert.False(StepCoreDocument.HasUnfinishedSections(doc)); // hand-written text is never resumed over

        facts2.IsUserEdited = false;
        Assert.True(StepCoreDocument.HasUnfinishedSections(doc));

        facts2.Status = SectionStatus.Failed;
        Assert.True(StepCoreDocument.HasUnfinishedSections(doc));
    }

    [Fact]
    public void EmptyStateBody_ExplainsCancelOrStop()
    {
        Assert.StartsWith("Click Generate Document to draft", StepCoreDocument.EmptyStateBody(hasOutline: false, reason: null));
        Assert.Equal("Generation cancelled. The outline was kept. Click Generate Document to write the sections.",
            StepCoreDocument.EmptyStateBody(hasOutline: true, reason: "Generation cancelled."));
        Assert.Equal("Generation stopped: Spend limit reached. Click Generate Document to try again.",
            StepCoreDocument.EmptyStateBody(hasOutline: false, reason: "Generation stopped: Spend limit reached."));
    }

    [Fact]
    public void IssuesCaption_CountsBySeverity()
    {
        Assert.Equal("Issues (1 errors, 2 warnings)", StepCoreDocument.IssuesCaption(new[] { Error(), Warning(), Warning() }));
    }

    [Fact]
    public void DocumentSubtitle_CollapsesWhitespace_AndTypeLabelIsSeparate()
    {
        var doc = CaseTestData.SampleMemoDraft().CoreDocument!;
        doc.Memo.Organization = "Marwick Avelin  Cresswell LLP,\t outside counsel";

        Assert.Equal("Investigation Memo", StepCoreDocument.DocumentTypeLabel(doc));
        Assert.Equal("Marwick Avelin Cresswell LLP, outside counsel", StepCoreDocument.DocumentSubtitle(doc));
    }

    [Fact]
    public void VisibleIssues_WhileGenerating_ShowsOnlyFinishedSections()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        var ready = doc.Sections[0];
        var pending = doc.Sections[1];
        ready.Status = SectionStatus.Ready;
        pending.Status = SectionStatus.Pending;
        var issues = new List<CoreDocumentIssue> { Error(ready.Id), Error(pending.Id), Warning(string.Empty) };

        var running = StepCoreDocument.VisibleIssues(doc, issues, isGenerating: true);
        var idle = StepCoreDocument.VisibleIssues(doc, issues, isGenerating: false);

        Assert.Equal(ready.Id, Assert.Single(running).SectionId);
        Assert.Equal(3, idle.Count);
    }

    [Theory]
    [InlineData(SectionStatus.Writing, false, "Writing this section...")]
    [InlineData(SectionStatus.Pending, true, "Not written yet. It will appear here when the generator reaches it.")]
    [InlineData(SectionStatus.Pending, false, "Not written yet. Click Resume All to write it, or type the text here yourself.")]
    [InlineData(SectionStatus.Ready, false, "")]
    public void EditorPlaceholder_ExplainsUnwrittenSections(SectionStatus status, bool generating, string expected)
    {
        Assert.Equal(expected, StepCoreDocument.EditorPlaceholder(Section(status), generating));
    }

    [Fact]
    public void EditorPlaceholder_Failed_IncludesReason()
    {
        var section = Section(SectionStatus.Failed);
        section.FailureMessage = "The response was cut off";

        Assert.Contains("The response was cut off", StepCoreDocument.EditorPlaceholder(section, false));
    }

    [Fact]
    public void DocumentSubtitle_Memo_KeepsOnlyTheOrganizationSentence()
    {
        var doc = CaseTestData.SampleMemoDraft().CoreDocument!;
        doc.Memo.Organization = "Thistle Bracken Advisory Law, retained by Dunder Mifflin. The memorandum will contain approximately 110 paragraphs.";

        Assert.Equal("Thistle Bracken Advisory Law, retained by Dunder Mifflin", StepCoreDocument.DocumentSubtitle(doc));
    }

    [Fact]
    public void DocumentSubtitle_KeepsAbbreviations()
    {
        var memo = CaseTestData.SampleMemoDraft().CoreDocument!;
        memo.Memo.Organization = "Acme Inc. Internal Audit";
        var complaint = CaseTestData.SampleComplaintDraft().CoreDocument!;
        complaint.Jurisdiction.CourtName = "U.S. District Court. Northern District";

        Assert.Equal("Acme Inc. Internal Audit", StepCoreDocument.DocumentSubtitle(memo));
        Assert.Equal("U.S. District Court. Northern District", StepCoreDocument.DocumentSubtitle(complaint));
    }

    [Fact]
    public void ReadyStatus_Memo_CountsAllParagraphs()
    {
        var draft = CaseTestData.SampleMemoDraft();
        var expected = draft.CoreDocument!.Sections.Sum(s => s.Paragraphs.Count);

        var (text, _) = StepCoreDocument.ReadyStatus(draft, Array.Empty<CoreDocumentIssue>());

        Assert.Equal($"Document ready: {draft.CoreDocument.Sections.Count} sections, {expected} paragraphs.", text);
    }
}
