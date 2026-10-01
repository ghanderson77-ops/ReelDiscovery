using ReelDiscovery.Models.Case;
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
