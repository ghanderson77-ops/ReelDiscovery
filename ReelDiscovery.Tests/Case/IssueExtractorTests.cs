using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class IssueExtractorTests
{
    private static CoreDocument TwoCountComplaint()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        doc.Counts.Add(new CaseCount
        {
            Id = "count-2",
            Number = 2,
            Title = "Unjust Enrichment",
            AgainstPartyIds = new List<string> { "party-2" },
            LegalBasis = "Illinois common law",
            Summary = "Harmon retained the benefit of the extra glazing work."
        });
        return doc;
    }

    private static CaseIssue Detailed(string id, string countId, int number, string title) => new()
    {
        Id = id,
        CountId = countId,
        Number = number,
        Title = title,
        Description = $"Details for {title}",
        Elements = new List<string> { "Element A", "Element B" },
        RelevanceCriteria = $"Relevant when it shows {title}"
    };

    private static CaseGuide GuideWithBothIssues()
    {
        var guide = new CaseGuide();
        guide.Issues.Add(Detailed("ISS-01", "count-1", 1, "Breach of Contract"));
        guide.Issues.Add(Detailed("ISS-02", "count-2", 2, "Unjust Enrichment"));
        guide.IdCounters["ISS"] = 2;
        return guide;
    }

    [Fact]
    public void Extract_FirstTime()
    {
        var doc = TwoCountComplaint();
        var guide = new CaseGuide();

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Equal(2, issues.Count);
        Assert.Equal("ISS-01", issues[0].Id);
        Assert.Equal("ISS-02", issues[1].Id);
        Assert.Equal("count-1", issues[0].CountId);
        Assert.Equal(1, issues[0].Number);
        Assert.Equal("Breach of Contract", issues[0].Title);
        Assert.Equal("Illinois common law", issues[0].LegalBasis);
        Assert.Equal("Harmon failed to pay approved change orders.", issues[0].Summary);
        Assert.Equal(new[] { "party-2" }, issues[0].AgainstPartyIds);
        Assert.NotSame(doc.Counts[0].AgainstPartyIds, issues[0].AgainstPartyIds);
        Assert.Equal(string.Empty, issues[0].Description);
        Assert.False(issues[0].HasDetails);
        // Extract never assigns guide.Issues; the caller does
        Assert.Empty(guide.Issues);
    }

    [Fact]
    public void Extract_KeepsIdsAndDetails()
    {
        var doc = TwoCountComplaint();
        var guide = GuideWithBothIssues();

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Equal(new[] { "ISS-01", "ISS-02" }, issues.Select(i => i.Id));
        Assert.Equal("Details for Breach of Contract", issues[0].Description);
        Assert.Equal(new[] { "Element A", "Element B" }, issues[0].Elements);
        Assert.Equal("Relevant when it shows Breach of Contract", issues[0].RelevanceCriteria);
        Assert.True(issues[1].HasDetails);
        Assert.Equal(2, guide.IdCounters["ISS"]);
    }

    [Fact]
    public void Extract_ReorderedMatchesByTitle()
    {
        var doc = TwoCountComplaint();
        doc.Counts[0].Title = "Unjust Enrichment";
        doc.Counts[1].Title = "Breach of Contract";
        var guide = GuideWithBothIssues();

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Equal("ISS-02", issues[0].Id);
        Assert.Equal("count-1", issues[0].CountId);
        Assert.Equal("Details for Unjust Enrichment", issues[0].Description);
        Assert.Equal("ISS-01", issues[1].Id);
        Assert.Equal("count-2", issues[1].CountId);
        Assert.Equal("Details for Breach of Contract", issues[1].Description);
    }

    [Fact]
    public void Extract_TitleMatchIgnoresCaseAndPunctuation()
    {
        var doc = TwoCountComplaint();
        doc.Counts[0].Title = "  BREACH   of contract. ";
        var guide = GuideWithBothIssues();

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Equal("ISS-01", issues[0].Id);
        Assert.True(issues[0].HasDetails);
        Assert.Equal("  BREACH   of contract. ", issues[0].Title);
    }

    [Fact]
    public void Extract_TitleChangedSameCountId()
    {
        var doc = TwoCountComplaint();
        doc.Counts[0].Title = "Breach of Implied Warranty";
        var guide = GuideWithBothIssues();

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Equal("ISS-01", issues[0].Id);
        Assert.Equal("Breach of Implied Warranty", issues[0].Title);
        Assert.False(issues[0].HasDetails);
        Assert.Empty(issues[0].Elements);
        Assert.Equal(string.Empty, issues[0].RelevanceCriteria);
        Assert.Equal("ISS-02", issues[1].Id);
        Assert.True(issues[1].HasDetails);
    }

    [Fact]
    public void Extract_RetitledCountDoesNotStealClaimedIssue()
    {
        // count-1 retitled to count-2's old title, count-2 retitled to something new:
        // ISS-02 is claimed by title, so count-2 cannot keep ISS-02 and gets a new id.
        var doc = TwoCountComplaint();
        doc.Counts[0].Title = "Unjust Enrichment";
        doc.Counts[1].Title = "Promissory Estoppel";
        var guide = GuideWithBothIssues();

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Equal("ISS-02", issues[0].Id);
        Assert.True(issues[0].HasDetails);
        Assert.Equal("ISS-03", issues[1].Id);
        Assert.False(issues[1].HasDetails);
    }

    [Fact]
    public void Extract_NewCountGetsNewId()
    {
        var doc = TwoCountComplaint();
        doc.Counts.Add(new CaseCount { Id = "count-3", Number = 3, Title = "Mechanics Lien Foreclosure" });
        var guide = GuideWithBothIssues();

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Equal(new[] { "ISS-01", "ISS-02", "ISS-03" }, issues.Select(i => i.Id));
        Assert.Equal(3, guide.IdCounters["ISS"]);
    }

    [Fact]
    public void Extract_RemovedCountDropsIssue_AndIdIsNotReused()
    {
        var doc = TwoCountComplaint();
        doc.Counts.RemoveAt(1);
        var guide = GuideWithBothIssues();

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Single(issues);
        Assert.Equal("ISS-01", issues[0].Id);

        guide.Issues = issues;
        doc.Counts.Add(new CaseCount { Id = "count-2", Number = 2, Title = "Fraud" });
        var again = IssueExtractor.Extract(doc, guide);
        Assert.Equal("ISS-03", again[1].Id);
    }

    [Fact]
    public void Extract_NullSafe()
    {
        var doc = TwoCountComplaint();
        doc.Counts[0].AgainstPartyIds = null!;
        var guide = new CaseGuide { Issues = null! };

        var issues = IssueExtractor.Extract(doc, guide);

        Assert.Equal(2, issues.Count);
        Assert.Empty(issues[0].AgainstPartyIds);

        doc.Counts = null!;
        Assert.Empty(IssueExtractor.Extract(doc, new CaseGuide()));
    }

    [Fact]
    public void SourceLabel_ComplaintAndMemo()
    {
        var complaint = TwoCountComplaint();
        var issues = IssueExtractor.Extract(complaint, new CaseGuide());
        Assert.Equal("Count II", IssueExtractor.SourceLabel(complaint, issues[1]));

        var memo = CaseTestData.SampleMemoDraft().CoreDocument!;
        Assert.Equal("Finding 2", IssueExtractor.SourceLabel(memo, new CaseIssue { Number = 2 }));
    }

    [Theory]
    [InlineData("  Breach   of\tContract. ", "breach of contract")]
    [InlineData("Fraud (Common-Law)", "fraud commonlaw")]
    [InlineData(null, "")]
    public void NormalizeTitle_Cases(string? input, string expected)
    {
        Assert.Equal(expected, IssueExtractor.NormalizeTitle(input));
    }
}
