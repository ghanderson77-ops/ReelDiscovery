using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the static text and filter helpers of FactsTabPanel; no control is instantiated.
public class FactsPanelLogicTests
{
    [Theory]
    [InlineData(46, 0, "46 facts")]
    [InlineData(46, 3, "46 facts · 3 need review")]
    [InlineData(1, 1, "1 fact · 1 needs review")]
    [InlineData(0, 0, "0 facts")]
    public void CountText_AddsNeedReviewWhenAny(int facts, int needReview, string expected)
    {
        Assert.Equal(expected, FactsTabPanel.CountText(facts, needReview));
    }

    [Fact]
    public void MetaLine_HasIdLongDateAndSignificance()
    {
        var f = new KeyFact { Id = "F-012", Date = "2024-03-03", Significance = FactSignificance.Hot };

        Assert.Equal("F-012 · March 3, 2024 · Hot", FactsTabPanel.MetaLine(f));
    }

    [Fact]
    public void MetaLine_UnparseableDate_ShownRaw()
    {
        var f = new KeyFact { Id = "F-002", Date = "spring 2024", Significance = FactSignificance.Supporting };

        Assert.Equal("F-002 · spring 2024 · Supporting", FactsTabPanel.MetaLine(f));
    }

    [Fact]
    public void MetaLine_Edited_AddsEditedByYou()
    {
        var f = new KeyFact { Id = "F-003", Date = "2024-01-15", Significance = FactSignificance.Key, IsUserEdited = true };

        Assert.Equal("F-003 · January 15, 2024 · Key · Edited by you", FactsTabPanel.MetaLine(f));
    }

    [Fact]
    public void NoticeText_PrefixesTheReviewReason()
    {
        var f = new KeyFact { NeedsReview = true, ReviewReason = "Maria Delgado was changed after it was written." };

        Assert.Equal("Review this fact: Maria Delgado was changed after it was written.", FactsTabPanel.NoticeText(f));
    }

    [Fact]
    public void IssuesCell_JoinsIds()
    {
        var f = new KeyFact { IssueIds = new List<string> { "ISS-01", "ISS-03" } };

        Assert.Equal("ISS-01, ISS-03", FactsTabPanel.IssuesCell(f));
        Assert.Equal(string.Empty, FactsTabPanel.IssuesCell(new KeyFact()));
    }

    [Fact]
    public void NamesCell_ResolvesNamesAndKeepsUnknownIdsRaw()
    {
        var guide = new CaseGuide();
        guide.Cast.Add(new CastMember { Id = "CH-001", FirstName = "Maria", LastName = "Delgado" });
        guide.Cast.Add(new CastMember { Id = "CH-002", FirstName = "Tom", LastName = "Becker" });

        Assert.Equal("Maria Delgado, Tom Becker", FactsTabPanel.NamesCell(guide, new[] { "CH-001", "CH-002" }));
        Assert.Equal("Tom Becker, CH-099", FactsTabPanel.NamesCell(guide, new[] { "CH-002", "CH-099" }));
        Assert.Equal(string.Empty, FactsTabPanel.NamesCell(guide, Array.Empty<string>()));
    }

    [Theory]
    [InlineData(false, FactFilter.All, true)]
    [InlineData(true, FactFilter.All, true)]
    [InlineData(true, FactFilter.NeedsReview, true)]
    [InlineData(false, FactFilter.NeedsReview, false)]
    public void PassesFilter_NeedsReviewHidesReviewedFacts(bool needsReview, FactFilter filter, bool expected)
    {
        Assert.Equal(expected, FactsTabPanel.PassesFilter(new KeyFact { NeedsReview = needsReview }, filter));
    }
}
