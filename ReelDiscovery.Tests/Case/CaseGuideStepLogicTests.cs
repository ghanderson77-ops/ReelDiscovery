using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the static state helpers of StepCaseGuide; no control is instantiated.
public class CaseGuideStepLogicTests
{
    private static readonly GuidePart[] AllParts = { GuidePart.Issues, GuidePart.Cast, GuidePart.Facts, GuidePart.Values };

    [Fact]
    public void TabStateFor_NoGuideNotRunning_IsNoGuideForEveryPart()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Guide = null;

        foreach (var part in AllParts)
            Assert.Equal(GuideTabState.NoGuide, StepCaseGuide.TabStateFor(draft, part, running: false, writingPart: null));
    }

    [Fact]
    public void TabStateFor_NoGuideRunning_WritesIssuesAndWaitsForTheRest()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Guide = null;

        Assert.Equal(GuideTabState.Writing, StepCaseGuide.TabStateFor(draft, GuidePart.Issues, true, null));
        Assert.Equal(GuideTabState.Waiting, StepCaseGuide.TabStateFor(draft, GuidePart.Cast, true, null));
        Assert.Equal(GuideTabState.Waiting, StepCaseGuide.TabStateFor(draft, GuidePart.Values, true, GuidePart.Issues));
    }

    [Fact]
    public void TabStateFor_WritingEmptyCast_IsWritingThenWaiting()
    {
        var draft = IssuesOnlyDraft();
        draft.Guide!.CastState.Status = GuidePartStatus.Generating;

        Assert.Null(StepCaseGuide.TabStateFor(draft, GuidePart.Issues, true, GuidePart.Cast));
        Assert.Equal(GuideTabState.Writing, StepCaseGuide.TabStateFor(draft, GuidePart.Cast, true, GuidePart.Cast));
        Assert.Equal(GuideTabState.Waiting, StepCaseGuide.TabStateFor(draft, GuidePart.Facts, true, GuidePart.Cast));
        Assert.Equal(GuideTabState.Waiting, StepCaseGuide.TabStateFor(draft, GuidePart.Values, true, GuidePart.Cast));
    }

    [Fact]
    public void TabStateFor_WritingCastWithKeyCharacters_ShowsThePanel()
    {
        var full = CaseTestData.SampleGuidedDraft();
        var draft = IssuesOnlyDraft();
        draft.Guide!.Organizations.AddRange(full.Guide!.Organizations);
        draft.Guide.Cast.AddRange(full.Guide.Cast.Where(c => c.Tier == CastTier.Key));
        draft.Guide.CastState.Status = GuidePartStatus.Generating;

        Assert.Null(StepCaseGuide.TabStateFor(draft, GuidePart.Cast, true, GuidePart.Cast));
    }

    [Fact]
    public void TabStateFor_WritingCastWithOnlyBackgroundCharacters_IsStillWriting()
    {
        var full = CaseTestData.SampleGuidedDraft();
        var draft = IssuesOnlyDraft();
        draft.Guide!.Cast.AddRange(full.Guide!.Cast.Where(c => c.Tier == CastTier.Background));
        draft.Guide.CastState.Status = GuidePartStatus.Generating;

        Assert.Equal(GuideTabState.Writing, StepCaseGuide.TabStateFor(draft, GuidePart.Cast, true, GuidePart.Cast));
    }

    [Fact]
    public void TabStateFor_FailedFactsWithNoFacts_IsFailed()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.Facts.Clear();
        draft.Guide.FactsState.Status = GuidePartStatus.Failed;
        draft.Guide.FactsState.FailureMessage = "The response was cut off";

        Assert.Equal(GuideTabState.Failed, StepCaseGuide.TabStateFor(draft, GuidePart.Facts, false, null));
    }

    [Fact]
    public void TabStateFor_FailedFactsWithSomeFacts_ShowsThePanel()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.FactsState.Status = GuidePartStatus.Failed;

        Assert.Null(StepCaseGuide.TabStateFor(draft, GuidePart.Facts, false, null));
    }

    [Fact]
    public void TabStateFor_PendingValuesNotRunning_IsNotWritten()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.Values.Clear();
        draft.Guide.ValuesState.Status = GuidePartStatus.Pending;

        Assert.Equal(GuideTabState.NotWritten, StepCaseGuide.TabStateFor(draft, GuidePart.Values, false, null));
    }

    [Fact]
    public void TabStateFor_LeftoverGeneratingStatusNotRunning_IsNotWritten()
    {
        // A case reopened after the app closed mid-run: nothing is writing, so Resume Guide applies
        var draft = IssuesOnlyDraft();
        draft.Guide!.CastState.Status = GuidePartStatus.Generating;

        Assert.Equal(GuideTabState.NotWritten, StepCaseGuide.TabStateFor(draft, GuidePart.Cast, false, null));
    }

    [Fact]
    public void TabStateFor_ReadyParts_ShowThePanel()
    {
        var draft = CaseTestData.SampleGuidedDraft();

        foreach (var part in AllParts)
            Assert.Null(StepCaseGuide.TabStateFor(draft, part, false, null));
    }

    [Fact]
    public void TabStateFor_ReadyButEmpty_ShowsThePanel()
    {
        // The panel shows its own "No canonical values..." text
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.Values.Clear();

        Assert.Null(StepCaseGuide.TabStateFor(draft, GuidePart.Values, false, null));
    }

    [Fact]
    public void UnfinishedParts_CompleteGuide_IsEmpty()
    {
        Assert.Empty(StepCaseGuide.UnfinishedParts(CaseTestData.SampleGuidedDraft()));
    }

    [Fact]
    public void UnfinishedParts_PendingValues_ListsValues()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.ValuesState.Status = GuidePartStatus.Pending;

        Assert.Equal(new[] { GuidePart.Values }, StepCaseGuide.UnfinishedParts(draft));
    }

    [Fact]
    public void UnfinishedParts_FailedCastAndPendingFacts_ListsBothInOrder()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.FactsState.Status = GuidePartStatus.Pending;
        draft.Guide.CastState.Status = GuidePartStatus.Failed;

        Assert.Equal(new[] { GuidePart.Cast, GuidePart.Facts }, StepCaseGuide.UnfinishedParts(draft));
    }

    [Fact]
    public void UnfinishedParts_NoGuide_ListsEveryPart()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Guide = null;

        Assert.Equal(AllParts, StepCaseGuide.UnfinishedParts(draft));
    }

    [Fact]
    public void UnfinishedParts_FailedIssueDetailsOnly_IsFinished()
    {
        // Issues count as written when only their enrichment failed (Retry Issue Details covers it)
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.IssueDetailsState.Status = GuidePartStatus.Failed;

        Assert.Empty(StepCaseGuide.UnfinishedParts(draft));
    }

    [Theory]
    [InlineData("The response was cut off. Click Retry Key Facts.", "The response was cut off.")]
    [InlineData("Details for 3 characters were not written. Click Retry Cast.", "Details for 3 characters were not written.")]
    [InlineData("The request timed out. Click Retry Issue Details", "The request timed out.")]
    [InlineData("The model declined.", "The model declined.")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ReasonWithoutRetryHint_DropsTheGeneratorsTrailingHint(string? reason, string expected)
    {
        Assert.Equal(expected, StepCaseGuide.ReasonWithoutRetryHint(reason));
    }

    [Fact]
    public void StateCard_FailedReason_SaysRetryOnce()
    {
        var body = CaseGuideUiText.StateCardText(GuidePart.Facts, GuideTabState.Failed,
            StepCaseGuide.ReasonWithoutRetryHint("The response was cut off. Click Retry Key Facts.")).Body;

        Assert.Equal("The response was cut off. Click Retry Key Facts to try again. Finished parts were kept.", body);
    }

    [Fact]
    public void VisibleFindings_Idle_ShowsEverything()
    {
        var findings = SampleFindings();

        Assert.Equal(findings.Count, StepCaseGuide.VisibleFindings(CaseTestData.SampleGuidedDraft(), findings, running: false).Count);
    }

    [Fact]
    public void VisibleFindings_MidRunBeforeFacts_HidesGuideLevelAndLinkDependentFindings()
    {
        // Cast is writing: issue findings ("no fact is linked") wait for the facts; guide-level findings wait for the end
        var draft = IssuesOnlyDraft();
        draft.Guide!.CastState.Status = GuidePartStatus.Generating;

        Assert.Empty(StepCaseGuide.VisibleFindings(draft, SampleFindings(), running: true));
    }

    [Fact]
    public void VisibleFindings_MidRunAfterFacts_ShowsFinishedParts()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.ValuesState.Status = GuidePartStatus.Generating;

        var visible = StepCaseGuide.VisibleFindings(draft, SampleFindings(), running: true);

        Assert.Equal(new[] { GuideArea.Issues, GuideArea.Cast, GuideArea.Facts }, visible.Select(f => f.Area));
    }

    private static List<GuideFinding> SampleFindings() => new()
    {
        new GuideFinding { Area = GuideArea.Issues, EntityId = "ISS-01", Severity = IssueSeverity.Warning, Message = "No fact is linked to ISS-01." },
        new GuideFinding { Area = GuideArea.Cast, EntityId = "CH-001", Severity = IssueSeverity.Warning, Message = "CH-001 acts in no fact." },
        new GuideFinding { Area = GuideArea.Facts, EntityId = "F-002", Severity = IssueSeverity.Error, Message = "F-002 refers to CH-099." },
        new GuideFinding { Area = GuideArea.Values, EntityId = "V-001", Severity = IssueSeverity.Warning, Message = "V-001 is unused." },
        new GuideFinding { Area = GuideArea.Guide, EntityId = "ORG-02", Severity = IssueSeverity.Warning, Message = "No in-house counsel." }
    };

    // Issues written, everything after them still pending and empty
    private static CaseDraft IssuesOnlyDraft()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        guide.Organizations.Clear();
        guide.Cast.Clear();
        guide.Facts.Clear();
        guide.Values.Clear();
        guide.CastState.Status = GuidePartStatus.Pending;
        guide.FactsState.Status = GuidePartStatus.Pending;
        guide.ValuesState.Status = GuidePartStatus.Pending;
        return draft;
    }
}
