using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CoreDocumentValidatorTests
{
    private static List<CoreDocumentIssue> Errors(List<CoreDocumentIssue> issues) =>
        issues.Where(i => i.Severity == IssueSeverity.Error).ToList();

    private static List<CoreDocumentIssue> Warnings(List<CoreDocumentIssue> issues) =>
        issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();

    private static CaseDraft StateCourtDraft(string state, string courtName)
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Jurisdiction.CourtSystem = CourtSystem.State;
        draft.CoreDocument.Jurisdiction.State = state;
        draft.CoreDocument.Jurisdiction.CourtName = courtName;
        return draft;
    }

    private static void AppendParagraph(CaseDraft draft, string sectionId, string text) =>
        draft.CoreDocument!.FindSection(sectionId)!.Paragraphs.Add(text);

    [Fact]
    public void SampleComplaint_OnlyParagraphCountWarning()
    {
        var issues = CoreDocumentValidator.Validate(CaseTestData.SampleComplaintDraft());

        Assert.Empty(Errors(issues));
        var warning = Assert.Single(Warnings(issues));
        Assert.Equal(IssueCategory.Preset, warning.Category);
        Assert.Equal("The document has 15 numbered paragraphs; the Simple preset expects 40-70.", warning.Message);
    }

    [Fact]
    public void SampleMemo_OnlyMemoLengthWarning()
    {
        var issues = CoreDocumentValidator.Validate(CaseTestData.SampleMemoDraft());

        Assert.Empty(Errors(issues));
        var warning = Assert.Single(Warnings(issues));
        Assert.Equal(IssueCategory.Preset, warning.Category);
        Assert.Equal("The memo has 20 paragraphs; the Simple preset expects 36-60.", warning.Message);
    }

    [Fact]
    public void Memo_WithinLength_NoMemoLengthWarning()
    {
        var draft = CaseTestData.SampleMemoDraft();
        var background = draft.CoreDocument!.FindSection("background")!;
        for (var i = 0; i < 20; i++)
            background.Paragraphs.Add($"Additional background detail number {i + 1}.");

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.Empty(issues);
    }

    [Fact]
    public void MissingRequiredSection_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Sections.RemoveAll(s => s.Id == "prayer");

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal(IssueCategory.Structure, issue.Category);
        Assert.Equal("prayer", issue.SectionId);
        Assert.Equal("Required section PRAYER FOR RELIEF is missing.", issue.Message);
    }

    [Fact]
    public void FailedSection_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var section = draft.CoreDocument!.FindSection("facts-1")!;
        section.Status = SectionStatus.Failed;
        section.FailureMessage = "timeout";

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal("The Subcontract failed: timeout", issue.Message);
        Assert.Equal("facts-1", issue.SectionId);
    }

    [Fact]
    public void PendingSection_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.FindSection("facts-2")!.Status = SectionStatus.Pending;

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal("Unpaid Change Orders has not been written yet.", issue.Message);
    }

    [Fact]
    public void ReporterCitation_IsCitationError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        AppendParagraph(draft, "facts-1", "See 531 U.S. 98 (2000).");

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal(IssueCategory.Citation, issue.Category);
        Assert.Equal("facts-1", issue.SectionId);
        Assert.StartsWith("Case citation: ", issue.Message);
    }

    [Fact]
    public void CaseName_IsCitationWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        AppendParagraph(draft, "facts-1", "The parties acted as in Smith v. Jones when they signed.");

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.Empty(Errors(issues));
        var issue = Assert.Single(issues, i => i.Category == IssueCategory.Citation);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.StartsWith("Possible case name or citation phrase: ", issue.Message);
    }

    [Fact]
    public void FullRealName_IsRealNameError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        AppendParagraph(draft, "facts-2", "Acme Windows shipped the glass late.");

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal(IssueCategory.RealName, issue.Category);
        Assert.Equal("facts-2", issue.SectionId);
        Assert.Equal("Real name \"Acme Windows\" appears in this section.", issue.Message);
    }

    [Fact]
    public void RealName_NotApplied_NoIssue()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Substitutions[0].Apply = false;
        AppendParagraph(draft, "facts-2", "Acme Windows shipped the glass late.");

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.DoesNotContain(issues, i => i.Category == IssueCategory.RealName);
    }

    [Fact]
    public void SingleTokenAlias_IsRealNameWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        AppendParagraph(draft, "facts-2", "Acme shipped the glass late.");

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.Empty(Errors(issues));
        var issue = Assert.Single(issues, i => i.Category == IssueCategory.RealName);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal("Possible real name \"Acme\" appears in this section.", issue.Message);
    }

    [Fact]
    public void RealName_InOutline_IsReported()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Counsel.FirmName = "Acme Windows Legal LLP";

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal("outline", issue.SectionId);
        Assert.Equal(IssueCategory.RealName, issue.Category);
    }

    [Fact]
    public void RealName_InMemoHeader_IsReported()
    {
        var draft = CaseTestData.SampleMemoDraft();
        draft.Substitutions = new List<EntitySubstitution>
        {
            new() { Original = "Walter White", Substitute = "Hank Brennan", Kind = EntityKind.Person, Apply = true }
        };
        draft.CoreDocument!.Memo.From = "Walter White, Special Agent";

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal("outline", issue.SectionId);
        Assert.Equal(IssueCategory.RealName, issue.Category);
    }

    [Theory]
    [InlineData("court")]
    [InlineData("district")]
    [InlineData("address")]
    [InlineData("organization")]
    public void RealName_InOtherRenderedOutlineFields_IsReported(string field)
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var doc = draft.CoreDocument!;
        const string leak = "Acme Windows";
        switch (field)
        {
            case "court": doc.Jurisdiction.CourtName += " (" + leak + ")"; break;
            case "district": doc.Jurisdiction.District = leak + " District"; break;
            case "address": doc.Counsel.Address = "1 " + leak + " Plaza, Chicago, Illinois"; break;
            case "organization": doc.Memo.Organization = leak + " Compliance"; break;
        }

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.Contains(issues, i => i.SectionId == "outline" && i.Category == IssueCategory.RealName && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void ReporterCitation_InMemoRe_IsCitationError()
    {
        var draft = CaseTestData.SampleMemoDraft();
        draft.CoreDocument!.Memo.Re = "Exposure under 531 U.S. 98";

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal("outline", issue.SectionId);
        Assert.Equal(IssueCategory.Citation, issue.Category);
    }

    [Fact]
    public void FederalPleading_StateCourtName_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Jurisdiction.CourtName = "Circuit Court of Cook County, Illinois";

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal(IssueCategory.Jurisdiction, issue.Category);
        Assert.Equal("The court name does not match the federal court system.", issue.Message);
    }

    [Fact]
    public void FederalPleading_BankruptcyCourt_NoJurisdictionIssue()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Jurisdiction.CourtName = "United States Bankruptcy Court for the District of Delaware";

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.DoesNotContain(issues, i => i.Category == IssueCategory.Jurisdiction);
    }

    [Fact]
    public void FederalMemo_AgencyForum_NoJurisdictionIssue()
    {
        var draft = CaseTestData.SampleMemoDraft();
        draft.CoreDocument!.Jurisdiction.CourtName = "Securities and Exchange Commission, Division of Enforcement";

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.DoesNotContain(issues, i => i.Category == IssueCategory.Jurisdiction);
    }

    [Fact]
    public void StateCourt_FederalCourtName_IsError()
    {
        var draft = StateCourtDraft("Illinois", "United States District Court for the Northern District of Illinois");

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal(IssueCategory.Jurisdiction, issue.Category);
        Assert.Equal("The court name is a federal court but the court system is State.", issue.Message);
    }

    [Fact]
    public void StateCourt_OtherStateInTrailingSegment_IsWarning()
    {
        var draft = StateCourtDraft("Ohio", "District Court of Harris County, Texas");

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error && i.Category == IssueCategory.Jurisdiction);
        var issue = Assert.Single(issues, i => i.Message == "The court name mentions Texas but the case is in Ohio.");
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal(IssueCategory.Jurisdiction, issue.Category);
    }

    [Theory]
    [InlineData("Oregon", "Circuit Court of Washington County, Oregon")]
    [InlineData("Pennsylvania", "Court of Common Pleas of Delaware County, Pennsylvania")]
    [InlineData("West Virginia", "Circuit Court of Kanawha County, West Virginia")]
    [InlineData("Arkansas", "Circuit Court of Pulaski County, Arkansas")]
    public void StateCourt_MatchingState_NoContradiction(string state, string courtName)
    {
        var draft = StateCourtDraft(state, courtName);

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.DoesNotContain(issues, i => i.Category == IssueCategory.Jurisdiction);
    }

    [Fact]
    public void StateCourt_VirginiaCaseInWestVirginiaCourt_IsWarning()
    {
        var draft = StateCourtDraft("Virginia", "Circuit Court of Kanawha County, West Virginia");

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning
            && i.Message == "The court name mentions West Virginia but the case is in Virginia.");
    }

    [Fact]
    public void PartiesOutsidePreset_IsWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Parties.Add(new CaseParty { Id = "party-3", Name = "Third Party Co.", Role = PartyRole.Defendant });
        draft.CoreDocument.Parties.Add(new CaseParty { Id = "party-4", Name = "Fourth Party Co.", Role = PartyRole.Defendant });

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Message == "The document has 4 parties; the Simple preset expects 2.");
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal(IssueCategory.Preset, issue.Category);
    }

    [Fact]
    public void CountsOutsidePreset_IsWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Complexity = ComplexityLevel.Standard;
        draft.CoreDocument!.Counts.Clear();
        draft.CoreDocument.Sections.RemoveAll(s => s.Kind == SectionKind.Count);
        draft.CoreDocument.SourceHash = draft.ComputeSourceHash();

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Message == "The document has 0 counts or issues; the Standard preset expects 3-4.");
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void PleadingWithNoCounts_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Counts.Clear();
        draft.CoreDocument.Sections.RemoveAll(s => s.Kind == SectionKind.Count);

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal(IssueCategory.Structure, issue.Category);
        Assert.Equal("The pleading has no causes of action. Regenerate the document.", issue.Message);
    }

    [Fact]
    public void NoFactSubsections_IsError()
    {
        var draft = CaseTestData.SampleMemoDraft();
        draft.CoreDocument!.FactPlan.Clear();
        draft.CoreDocument.Sections.RemoveAll(s => s.Kind == SectionKind.FactualAllegations);

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(Errors(issues));
        Assert.Equal("The document has no factual subsections. Regenerate the document.", issue.Message);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message == "The document has 0 factual subsections; the Simple preset expects 2.");
    }

    [Fact]
    public void FactSubsectionsOutsidePreset_IsWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Complexity = ComplexityLevel.Complex;
        draft.CoreDocument!.SourceHash = draft.ComputeSourceHash();

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Message == "The document has 2 factual subsections; the Complex preset expects 6-8.");
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal(IssueCategory.Preset, issue.Category);
    }

    [Fact]
    public void ReadySectionWithNoText_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        Assert.True(CoreDocumentEditing.ApplyEdit(draft, "facts-1", "   "));

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal("facts-1", issue.SectionId);
        Assert.Equal(IssueCategory.Structure, issue.Category);
        Assert.Equal("The Subcontract is empty. Write it or click Regenerate Section.", issue.Message);
    }

    [Fact]
    public void MemoFindingsOutsidePreset_UsesFindingsNoun()
    {
        var draft = CaseTestData.SampleMemoDraft();
        draft.Complexity = ComplexityLevel.Standard;
        draft.CoreDocument!.SourceHash = draft.ComputeSourceHash();

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.Contains(issues, i => i.Message == "The document has 1 findings; the Standard preset expects 3-4.");
    }

    [Fact]
    public void TimelineSpanOutsidePreset_IsWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.TimelineEnd = "2024-02-15";

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning
            && i.Category == IssueCategory.Preset
            && i.Message == "The timeline covers 1 months; the Simple preset expects 3-6 months.");
    }

    [Fact]
    public void UnparseableTimelineStart_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.TimelineStart = "March 2024";

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal(IssueCategory.Timeline, issue.Category);
        Assert.Equal("The timeline start date must be a real date in yyyy-MM-dd form.", issue.Message);
    }

    [Fact]
    public void TimelineEndBeforeStart_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.TimelineEnd = "2023-12-01";

        var issues = Errors(CoreDocumentValidator.Validate(draft));

        Assert.Contains(issues, i => i.Category == IssueCategory.Timeline && i.Message == "The timeline ends before it starts.");
    }

    [Fact]
    public void TimelineEndAfterFiling_IsWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.FilingDate = "2024-05-01";

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Message == "The timeline ends after the filing date.");
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal(IssueCategory.Timeline, issue.Category);
    }

    [Fact]
    public void FactPlanOutsideTimeline_IsWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.FactPlan[1].EndDate = "2024-08-15";

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Category == IssueCategory.Timeline);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal("facts-2", issue.SectionId);
        Assert.Equal("The dates for \"Unpaid Change Orders\" fall outside the case timeline.", issue.Message);
    }

    [Fact]
    public void FederalPleading_NoStatutoryBasis_IsWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var jurisdiction = draft.CoreDocument!.FindSection("jurisdiction")!;
        jurisdiction.Paragraphs[0] = "This Court has subject matter jurisdiction because the parties are of diverse citizenship.";

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Category == IssueCategory.Jurisdiction);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal("The jurisdiction section does not state a federal basis (28 U.S.C. § 1331 or § 1332).", issue.Message);
    }

    [Fact]
    public void StateCourt_StateNotMentioned_IsWarning()
    {
        var draft = StateCourtDraft("Ohio", "Court of Common Pleas of Franklin County");

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Category == IssueCategory.Jurisdiction);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal("The court and venue do not mention Ohio.", issue.Message);
    }

    [Fact]
    public void EmptyCourtName_IsError()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Jurisdiction.CourtName = "  ";

        var issue = Assert.Single(Errors(CoreDocumentValidator.Validate(draft)));

        Assert.Equal(IssueCategory.Jurisdiction, issue.Category);
        Assert.Equal("The court name is missing.", issue.Message);
    }

    [Fact]
    public void ParagraphNumberReference_IsWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        AppendParagraph(draft, "count-1", "Harmon acted willfully, as alleged in paragraphs 23-31 above.");

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Category == IssueCategory.Structure);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal("count-1", issue.SectionId);
        Assert.Equal("Refers to paragraph numbers, which change when sections are edited.", issue.Message);
    }

    [Fact]
    public void SourceHashMismatch_IsStaleWarning()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.EffectiveScenario += " Updated.";

        var issues = CoreDocumentValidator.Validate(draft);

        var issue = Assert.Single(issues, i => i.Category == IssueCategory.Stale);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal(string.Empty, issue.SectionId);
        Assert.Equal("Document is out of date with the scenario.", issue.Message);
    }

    [Fact]
    public void NullCoreDocument_ReturnsEmpty()
    {
        var draft = new CaseDraft();

        Assert.Empty(CoreDocumentValidator.Validate(draft));
    }

    [Fact]
    public void CountHelpers_ReturnCorrectNumbers()
    {
        var issues = new List<CoreDocumentIssue>
        {
            new() { Severity = IssueSeverity.Error, Category = IssueCategory.Citation },
            new() { Severity = IssueSeverity.Error, Category = IssueCategory.Citation },
            new() { Severity = IssueSeverity.Error, Category = IssueCategory.RealName },
            new() { Severity = IssueSeverity.Warning, Category = IssueCategory.Citation },
            new() { Severity = IssueSeverity.Warning, Category = IssueCategory.Preset }
        };

        Assert.Equal(3, CoreDocumentValidator.CountErrors(issues));
        Assert.Equal(2, CoreDocumentValidator.CountWarnings(issues));
        Assert.Equal(2, CoreDocumentValidator.CountCitationErrors(issues));
    }

    [Fact]
    public void MalformedData_DoesNotThrow()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var doc = draft.CoreDocument!;
        doc.TimelineStart = string.Empty;
        doc.TimelineEnd = "not a date";
        doc.FilingDate = "2024-13-45";
        doc.FactPlan[0].StartDate = "garbage";

        var issues = CoreDocumentValidator.Validate(draft);

        Assert.Contains(issues, i => i.Message == "The timeline end date must be a real date in yyyy-MM-dd form.");
    }
}
