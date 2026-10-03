using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class LedgerSelectorTests
{
    // First message 2024-03-20 10:00 Chicago
    private static readonly DateTimeOffset First = new(2024, 3, 20, 15, 0, 0, TimeSpan.Zero);

    private static readonly string[] People = { "CH-001", "CH-002" };

    private static PlannedThread Thread(params string[] factIds)
    {
        var thread = WavePlannerTests.Thread("T-0050", First, 2);
        thread.FactIds = factIds.ToList();
        thread.IssueIds = new List<string> { "ISS-01", "ISS-02" };
        return thread;
    }

    private static LedgerEntry Entry(string id, string date, int wave = 1, string[]? knownBy = null, string[]? facts = null,
        string[]? issues = null) => new()
    {
        Id = id,
        Date = date,
        Kind = LedgerKind.Figure,
        About = "about " + id,
        Statement = "statement " + id,
        KnownByIds = (knownBy ?? new[] { "CH-001" }).ToList(),
        FactIds = (facts ?? Array.Empty<string>()).ToList(),
        IssueIds = (issues ?? Array.Empty<string>()).ToList(),
        WaveIndex = wave
    };

    private static List<string> Ids(List<LedgerEntry> entries) => entries.Select(e => e.Id).ToList();

    [Fact]
    public void Select_HidesSameOrLaterWaveEntries()
    {
        var ledger = new List<LedgerEntry>
        {
            Entry("T-0001-L01", "2024-03-01", wave: 1),
            Entry("T-0002-L01", "2024-03-02", wave: 2),
            Entry("T-0003-L01", "2024-03-03", wave: 3)
        };

        var selected = LedgerSelector.Select(ledger, Thread(), 2, People);

        Assert.Equal(new[] { "T-0001-L01" }, Ids(selected));
    }

    [Fact]
    public void Select_HidesEntriesOnOrAfterFirstLocalDate()
    {
        var ledger = new List<LedgerEntry>
        {
            Entry("T-0001-L01", "2024-03-19"),
            Entry("T-0001-L02", "2024-03-20"),
            Entry("T-0001-L03", "2024-03-25"),
            Entry("T-0001-L04", "not a date")
        };

        var selected = LedgerSelector.Select(ledger, Thread(), 2, People);

        Assert.Equal(new[] { "T-0001-L01" }, Ids(selected));
    }

    [Fact]
    public void Select_HidesEntriesNoParticipantKnows()
    {
        var ledger = new List<LedgerEntry>
        {
            Entry("T-0001-L01", "2024-03-01", knownBy: new[] { "CH-003" }),
            Entry("T-0001-L02", "2024-03-01", knownBy: new[] { "CH-003", "CH-002" }),
            Entry("T-0001-L03", "2024-03-01", knownBy: Array.Empty<string>())
        };

        var selected = LedgerSelector.Select(ledger, Thread(), 2, People);

        Assert.Equal(new[] { "T-0001-L02" }, Ids(selected));
    }

    [Fact]
    public void Select_RanksBySharedFactsThenIssuesThenRecency()
    {
        var thread = Thread("F-001", "F-002");

        // Shared facts beat everything
        var byFacts = LedgerSelector.Select(new List<LedgerEntry>
        {
            Entry("T-0001-L01", "2024-03-10", facts: new[] { "F-001" }, issues: new[] { "ISS-01", "ISS-02" }),
            Entry("T-0001-L02", "2024-01-10", facts: new[] { "F-001", "F-002" }),
            Entry("T-0001-L03", "2024-03-15")
        }, thread, 2, People, cap: 1);
        Assert.Equal(new[] { "T-0001-L02" }, Ids(byFacts));

        // Equal facts: more shared issues wins
        var byIssues = LedgerSelector.Select(new List<LedgerEntry>
        {
            Entry("T-0001-L01", "2024-03-10", facts: new[] { "F-001" }, issues: new[] { "ISS-01" }),
            Entry("T-0001-L02", "2024-01-10", facts: new[] { "F-002" }, issues: new[] { "ISS-01", "ISS-02" }),
            Entry("T-0001-L03", "2024-03-15", facts: new[] { "F-003" }, issues: new[] { "ISS-01", "ISS-02" })
        }, thread, 2, People, cap: 1);
        Assert.Equal(new[] { "T-0001-L02" }, Ids(byIssues));

        // Equal facts and issues: later date wins, then id
        var byDate = LedgerSelector.Select(new List<LedgerEntry>
        {
            Entry("T-0001-L03", "2024-03-01"),
            Entry("T-0001-L02", "2024-03-10"),
            Entry("T-0001-L01", "2024-03-10"),
            Entry("T-0001-L04", "2024-02-01")
        }, thread, 2, People, cap: 2);
        Assert.Equal(new[] { "T-0001-L01", "T-0001-L02" }, Ids(byDate));
    }

    [Fact]
    public void Select_ReturnsChronologicalOrder()
    {
        var thread = Thread("F-001");
        var selected = LedgerSelector.Select(new List<LedgerEntry>
        {
            Entry("T-0001-L01", "2024-03-10"),
            Entry("T-0001-L02", "2024-01-10", facts: new[] { "F-001" }),
            Entry("T-0001-L03", "2024-02-10")
        }, thread, 2, People, cap: 2);

        // L02 ranked first (shared fact), L01 second (recency); output sorted by date
        Assert.Equal(new[] { "T-0001-L02", "T-0001-L01" }, Ids(selected));
    }

    [Fact]
    public void Select_CapsAtMaxLedgerEntries()
    {
        var ledger = Enumerable.Range(1, 45)
            .Select(i => Entry($"T-0001-L{i:00}", new DateOnly(2024, 1, 1).AddDays(i).ToString("yyyy-MM-dd")))
            .ToList();

        var selected = LedgerSelector.Select(ledger, Thread(), 2, People);

        Assert.Equal(30, LedgerSelector.MaxLedgerEntries);
        Assert.Equal(LedgerSelector.MaxLedgerEntries, selected.Count);
        // The 30 most recent, oldest first
        Assert.Equal("T-0001-L16", selected[0].Id);
        Assert.Equal("T-0001-L45", selected[^1].Id);
    }

    [Fact]
    public void Select_EmptyInputs()
    {
        Assert.Empty(LedgerSelector.Select(new List<LedgerEntry>(), Thread(), 2, People));
        Assert.Empty(LedgerSelector.Select(new List<LedgerEntry> { Entry("T-0001-L01", "2024-03-01") }, Thread(), 2, Array.Empty<string>()));
        Assert.Empty(LedgerSelector.Select(new List<LedgerEntry> { Entry("T-0001-L01", "2024-03-01") }, Thread(), 2, People, cap: 0));
    }
}
