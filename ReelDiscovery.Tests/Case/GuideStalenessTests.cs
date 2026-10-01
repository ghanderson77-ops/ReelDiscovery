using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class GuideStalenessTests
{
    private static CaseDraft StampedDraft()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        foreach (var part in GuideStaleness.Order)
            GuideStaleness.Stamp(draft, part);
        return draft;
    }

    [Fact]
    public void Unstamped_NeverStale()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.CoreDocument!.Sections[0].Paragraphs[0] = "Changed.";

        Assert.Empty(GuideStaleness.StaleParts(draft));
        Assert.Equal(string.Empty, GuideStaleness.StaleKey(draft));
    }

    [Fact]
    public void Stamp_SetsBasisAndTime()
    {
        var draft = StampedDraft();
        var state = draft.Guide!.FactsState;

        Assert.Equal(GuideStaleness.BasisFor(draft, GuidePart.Facts), state.BasisHash);
        Assert.NotEqual(string.Empty, state.BasisHash);
        Assert.NotNull(state.GeneratedUtc);
        Assert.Contains("core", state.BasisComponents.Keys);
        Assert.Contains("cast", state.BasisComponents.Keys);
        Assert.Empty(GuideStaleness.StaleParts(draft));
    }

    [Fact]
    public void CoreEdit_MakesAllPartsStale()
    {
        var draft = StampedDraft();
        var paragraphs = draft.CoreDocument!.Sections[0].Paragraphs;
        var original = paragraphs[0];

        paragraphs[0] = original + " Edited.";
        Assert.Equal(new[] { GuidePart.Issues, GuidePart.Cast, GuidePart.Facts, GuidePart.Values }, GuideStaleness.StaleParts(draft));

        paragraphs[0] = original;
        Assert.Empty(GuideStaleness.StaleParts(draft));
    }

    [Fact]
    public void CounselEdit_NotStale()
    {
        var draft = StampedDraft();
        draft.CoreDocument!.Counsel.Email = "someone.else@ellispark.com";

        Assert.Empty(GuideStaleness.StaleParts(draft));
    }

    [Fact]
    public void CastRegenerated_MakesFactsStale()
    {
        var draft = StampedDraft();
        draft.Guide!.Cast[0].LastName = "Regenerated";

        Assert.Equal(new[] { GuidePart.Facts }, GuideStaleness.StaleParts(draft));
    }

    [Fact]
    public void FactsRegenerated_MakesValuesStale()
    {
        var draft = StampedDraft();
        draft.Guide!.Facts[0].Title = "Regenerated title";

        Assert.Equal(new[] { GuidePart.Values }, GuideStaleness.StaleParts(draft));
    }

    [Fact]
    public void AbsorbHandEdit_KeepsDownstreamFresh()
    {
        var draft = StampedDraft();

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () => draft.Guide!.Cast[0].LastName = "X");

        Assert.Empty(GuideStaleness.StaleParts(draft));
    }

    [Fact]
    public void AbsorbHandEdit_DoesNotHideExistingStaleness()
    {
        var draft = StampedDraft();
        draft.CoreDocument!.Sections[0].Paragraphs[0] = "Edited core text.";

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () => draft.Guide!.Cast[0].LastName = "X");

        var stale = GuideStaleness.StaleParts(draft);
        Assert.Contains(GuidePart.Issues, stale);
        Assert.Contains(GuidePart.Cast, stale);
        Assert.Contains(GuidePart.Facts, stale);
    }

    [Fact]
    public void NotReady_NeverStale()
    {
        var draft = StampedDraft();
        draft.Guide!.CastState.Status = GuidePartStatus.Failed;
        draft.CoreDocument!.Sections[0].Paragraphs[0] = "Edited core text.";

        Assert.False(GuideStaleness.IsStale(draft, GuidePart.Cast));
        Assert.True(GuideStaleness.IsStale(draft, GuidePart.Issues));
    }

    [Fact]
    public void MissingDocumentOrGuide_NeverStale()
    {
        var draft = StampedDraft();
        draft.CoreDocument = null;
        Assert.Empty(GuideStaleness.StaleParts(draft));

        var noGuide = CaseTestData.SampleComplaintDraft();
        Assert.Empty(GuideStaleness.StaleParts(noGuide));
        Assert.Equal(string.Empty, GuideStaleness.BasisFor(noGuide, GuidePart.Facts));
        Assert.Equal(0, GuideStaleness.NeedsReviewCount(noGuide));
    }

    [Fact]
    public void StaleKey_TracksStaleSetAndBasis()
    {
        var draft = StampedDraft();
        Assert.Equal(string.Empty, GuideStaleness.StaleKey(draft));

        draft.Guide!.Cast[0].LastName = "Regenerated";
        var first = GuideStaleness.StaleKey(draft);
        Assert.NotEqual(string.Empty, first);
        Assert.Equal(first, GuideStaleness.StaleKey(draft));

        draft.Guide.Cast[0].LastName = "Again";
        var second = GuideStaleness.StaleKey(draft);
        Assert.NotEqual(first, second);

        draft.CoreDocument!.Sections[0].Paragraphs[0] = "Edited core text.";
        Assert.NotEqual(second, GuideStaleness.StaleKey(draft));
    }

    [Fact]
    public void ReviewCounts()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        Assert.Equal(0, GuideStaleness.NeedsReviewCount(draft));
        Assert.Equal(0, GuideStaleness.ValuesNeedingReviewCount(draft));

        draft.Guide!.Facts[0].NeedsReview = true;
        draft.Guide.Facts[2].NeedsReview = true;
        draft.Guide.Values[1].NeedsReview = true;

        Assert.Equal(2, GuideStaleness.NeedsReviewCount(draft));
        Assert.Equal(1, GuideStaleness.ValuesNeedingReviewCount(draft));
    }

    [Fact]
    public void StaleReason_NamesChangedUpstream()
    {
        var draft = StampedDraft();
        Assert.Equal(string.Empty, GuideStaleness.StaleReason(draft, GuidePart.Facts));

        draft.CoreDocument!.Sections[0].Paragraphs[0] = "Edited core text.";
        Assert.Equal("core document", GuideStaleness.StaleReason(draft, GuidePart.Facts));
        Assert.Equal("core document", GuideStaleness.StaleReason(draft, GuidePart.Issues));

        var regenerated = StampedDraft();
        regenerated.Guide!.Cast[0].LastName = "Regenerated";
        Assert.Equal("cast", GuideStaleness.StaleReason(regenerated, GuidePart.Facts));

        var factsChanged = StampedDraft();
        factsChanged.Guide!.Facts[0].Description = "Rewritten.";
        Assert.Equal("key facts", GuideStaleness.StaleReason(factsChanged, GuidePart.Values));
    }

    [Fact]
    public void StampedHashes_SurviveRoundTrip()
    {
        var draft = StampedDraft();
        var reloaded = CaseDraft.FromJson(draft.ToJson());
        reloaded.Normalize();

        Assert.Empty(GuideStaleness.StaleParts(reloaded));

        reloaded.Guide!.Cast[0].LastName = "Regenerated";
        Assert.Equal("cast", GuideStaleness.StaleReason(reloaded, GuidePart.Facts));
    }
}
