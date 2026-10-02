using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Key Facts bulk actions without controls: the selection bar state, its copy, and bulk mark-reviewed / delete helpers.
public class FactsBulkActionsLogicTests
{
    private static List<KeyFact> Facts(params bool[] needsReview) =>
        needsReview.Select((r, i) => new KeyFact { Id = $"F-{i + 1:000}", NeedsReview = r }).ToList();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SelectionState_HiddenBelowTwoFacts(int count)
    {
        var state = FactsTabPanel.SelectionState(Facts(Enumerable.Repeat(true, count).ToArray()), busy: false);

        Assert.False(state.Visible);
        Assert.Equal(string.Empty, state.Text);
        Assert.False(state.CanRegenerate);
        Assert.False(state.CanMarkReviewed);
        Assert.False(state.CanDelete);
    }

    [Fact]
    public void SelectionState_TwoOrMore_ShowsCountAndActions()
    {
        var state = FactsTabPanel.SelectionState(Facts(false, true, false), busy: false);

        Assert.True(state.Visible);
        Assert.Equal("3 facts selected", state.Text);
        Assert.True(state.CanRegenerate);
        Assert.True(state.CanMarkReviewed);
        Assert.True(state.CanDelete);
    }

    [Fact]
    public void SelectionState_MarkReviewedNeedsAFactToReview()
    {
        var state = FactsTabPanel.SelectionState(Facts(false, false), busy: false);

        Assert.True(state.Visible);
        Assert.False(state.CanMarkReviewed);
        Assert.True(state.CanRegenerate);
    }

    [Fact]
    public void SelectionState_Busy_KeepsTheBarButDisablesEveryAction()
    {
        var state = FactsTabPanel.SelectionState(Facts(true, true), busy: true);

        Assert.True(state.Visible);
        Assert.Equal("2 facts selected", state.Text);
        Assert.False(state.CanRegenerate);
        Assert.False(state.CanMarkReviewed);
        Assert.False(state.CanDelete);
    }

    [Fact]
    public void SelectedFactsText_UsesPluralAndGrouping()
    {
        Assert.Equal("2 facts selected", CaseGuideUiText.SelectedFactsText(2));
        Assert.Equal("1,000 facts selected", CaseGuideUiText.SelectedFactsText(1000));
    }

    [Theory]
    [InlineData(12, 12, 0, "", 0, "Regenerated 12 of 12 facts.")]
    [InlineData(10, 12, 2, "The model declined: no.", 0, "Regenerated 10 of 12 facts; 2 failed: The model declined: no. Click Regenerate Selected to try again.")]
    [InlineData(4, 5, 1, "The response was cut off.", 0, "Regenerated 4 of 5 facts; 1 failed: The response was cut off. Click Regenerate Fact to try again.")]
    [InlineData(3, 3, 0, "", 2, "Regenerated 3 of 3 facts. Kept 2 facts you edited.")]
    [InlineData(1, 1, 0, "", 1, "Regenerated 1 of 1 fact. Kept 1 fact you edited.")]
    [InlineData(0, 0, 0, "", 2, "Kept 2 facts you edited; nothing was regenerated.")]
    public void RegeneratedFactsText(int regenerated, int attempted, int failed, string error, int skipped, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.RegeneratedFactsText(regenerated, attempted, failed, error, skipped));
    }

    [Fact]
    public void RegenerateFactsStoppedText_SaysWhatWasKept()
    {
        Assert.Equal(
            "Generation stopped: Invalid API key. Regenerated 2 of 6 facts; the rest kept their text. Fix the problem on the API Configuration step, then try again.",
            CaseGuideUiText.RegenerateFactsStoppedText("Invalid API key.", 2, 6));
    }

    [Fact]
    public void RegenerateSelectedDialogCopy()
    {
        Assert.Equal("Regenerate 12 selected facts", CaseGuideUiText.RegenerateSelectedHeadline(12));
        Assert.Equal("Regenerate 1 selected fact", CaseGuideUiText.RegenerateSelectedHeadline(1));
        Assert.DoesNotContain("check the box", CaseGuideUiText.RegenerateSelectedBody(0));
        Assert.Contains("Facts you edited are kept unless you check the box below.", CaseGuideUiText.RegenerateSelectedBody(2));
        Assert.EndsWith("This makes one Story-model call per fact.", CaseGuideUiText.RegenerateSelectedBody(2));
        Assert.Equal("Also replace my 3 edited facts", CaseGuideUiText.ReplaceEditedText(GuidePart.Facts, 3));
    }

    [Fact]
    public void C13MarkAllReviewed()
    {
        Assert.Equal(("Mark All Reviewed", "Mark all 35 facts as reviewed? Their text will stay as it is."), CaseGuideUiText.C13MarkAllReviewed(35));
        Assert.Equal(("Mark All Reviewed", "Mark 1 fact as reviewed? Its text will stay as it is."), CaseGuideUiText.C13MarkAllReviewed(1));
    }

    [Fact]
    public void C14DeleteFacts()
    {
        Assert.Equal(("Delete Facts", "Delete 3 facts? This cannot be undone."), CaseGuideUiText.C14DeleteFacts(3, 0));
        Assert.Equal(("Delete Facts", "Delete 2 facts? They are used by 1 canonical value; that link will be removed. This cannot be undone."),
            CaseGuideUiText.C14DeleteFacts(2, 1));
        Assert.Equal(("Delete Facts", "Delete 4 facts? They are used by 3 canonical values; those links will be removed. This cannot be undone."),
            CaseGuideUiText.C14DeleteFacts(4, 3));
    }

    [Fact]
    public void MarkedAndDeletedText()
    {
        Assert.Equal("Marked 35 facts as reviewed.", CaseGuideUiText.MarkedReviewedText(35));
        Assert.Equal("Marked 1 fact as reviewed.", CaseGuideUiText.MarkedReviewedText(1));
        Assert.Equal("Deleted 2 facts.", CaseGuideUiText.DeletedFactsText(2));
    }

    [Fact]
    public void MarkFactsReviewed_ClearsOnlyTheListedFactsThatNeedReview()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        foreach (var id in new[] { "F-001", "F-002", "F-004" })
        {
            guide.FindFact(id)!.NeedsReview = true;
            guide.FindFact(id)!.ReviewReason = "Tom Becker was changed after it was written.";
        }
        var title = guide.FindFact("F-001")!.Title;
        var revision = draft.Revision;

        // F-003 does not need review, F-099 does not exist, F-001 is listed twice
        var marked = CaseGuideEditing.MarkFactsReviewed(draft, new[] { "F-001", "F-001", "F-002", "F-003", "F-099" });

        Assert.Equal(2, marked);
        Assert.False(guide.FindFact("F-001")!.NeedsReview);
        Assert.Equal(string.Empty, guide.FindFact("F-001")!.ReviewReason);
        Assert.False(guide.FindFact("F-002")!.NeedsReview);
        Assert.True(guide.FindFact("F-004")!.NeedsReview);
        Assert.Equal(title, guide.FindFact("F-001")!.Title);
        Assert.False(guide.FindFact("F-001")!.IsUserEdited);
        Assert.Equal(revision + 1, draft.Revision);
    }

    [Fact]
    public void MarkFactsReviewed_NothingToMark_LeavesRevision()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var revision = draft.Revision;

        Assert.Equal(0, CaseGuideEditing.MarkFactsReviewed(draft, new[] { "F-001", "F-002" }));
        Assert.Equal(revision, draft.Revision);
    }

    [Fact]
    public void MarkAllFactsReviewed_ClearsEveryFact()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        foreach (var fact in guide.Facts.Take(5))
            fact.NeedsReview = true;

        Assert.Equal(5, GuideStaleness.NeedsReviewCount(draft));
        Assert.Equal(5, CaseGuideEditing.MarkAllFactsReviewed(draft));
        Assert.Equal(0, GuideStaleness.NeedsReviewCount(draft));
        Assert.Equal(0, CaseGuideEditing.MarkAllFactsReviewed(draft));
    }

    [Fact]
    public void ValuesReferencingAny_CountsEachValueOnce()
    {
        var draft = CaseTestData.SampleGuidedDraft();

        // V-001 and V-002 cite F-003, V-003 cites F-001, V-004 cites F-005
        Assert.Equal(3, CaseGuideEditing.ValuesReferencingAny(draft, new[] { "F-001", "F-003" }));
        Assert.Equal(0, CaseGuideEditing.ValuesReferencingAny(draft, new[] { "F-002", "F-004" }));
    }
}
