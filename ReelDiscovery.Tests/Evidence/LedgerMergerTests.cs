using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;

namespace ReelDiscovery.Tests.Evidence;

public class LedgerMergerTests
{
    private static (CaseGuide Guide, CorpusPlan Plan, PlannedThread Hot) Hot()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        return (draft.Guide!, plan, PlanFixtures.Thread(plan, "F-003"));
    }

    private static LedgerDelta Delta(int seq, string about, string statement, string value = "", LedgerKind kind = LedgerKind.Figure,
        params string[] facts) => new()
    {
        Seq = seq,
        Kind = kind,
        About = about,
        Statement = statement,
        Value = value,
        FactIds = facts.ToList()
    };

    [Fact]
    public void EntriesFor_AssignsIdsDatesAndSources()
    {
        var (guide, _, thread) = Hot();
        var deltas = new List<LedgerDelta>
        {
            Delta(1, "Change Order 7", "Change Order 7 was approved at the stated amount.", "$184,250.00"),
            Delta(1, "Upper floors", "The work covers the upper-floor windows.", kind: LedgerKind.Decision),
            Delta(3, "Payment timing", "Payment is due thirty days after invoice.", kind: LedgerKind.Commitment)
        };

        var entries = LedgerMerger.EntriesFor(thread, deltas, guide, 4);

        Assert.Equal(new[] { $"{thread.Id}-L01", $"{thread.Id}-L02", $"{thread.Id}-L03" }, entries.Select(e => e.Id));
        var seq1 = thread.Messages.Single(m => m.Seq == 1);
        var seq3 = thread.Messages.Single(m => m.Seq == 3);
        Assert.Equal(MessageTime.LocalDate(seq1).ToString("yyyy-MM-dd"), entries[0].Date);
        Assert.Equal("2024-03-19", entries[0].Date);
        Assert.Equal(MessageTime.LocalDate(seq3).ToString("yyyy-MM-dd"), entries[2].Date);
        Assert.Equal(seq1.DocId, entries[0].SourceDocId);
        Assert.Equal(seq3.DocId, entries[2].SourceDocId);
        Assert.All(entries, e => Assert.Equal(thread.Id, e.SourceThreadId));
        Assert.All(entries, e => Assert.Equal(4, e.WaveIndex));
        Assert.Equal(LedgerKind.Commitment, entries[2].Kind);
    }

    [Fact]
    public void EntriesFor_KnownByIncludesLaterQuotingMessages()
    {
        var (guide, _, thread) = Hot();

        var entries = LedgerMerger.EntriesFor(thread, new List<LedgerDelta> { Delta(1, "Approval", "Approved in writing.") }, guide, 1);

        // Seq 1 is CH-004 -> CH-002; the reply-all adds CH-003 and the forward adds CH-001
        Assert.Equal(new[] { "CH-001", "CH-002", "CH-003", "CH-004" }, entries[0].KnownByIds);
    }

    [Fact]
    public void EntriesFor_BranchDoesNotLeakToSiblingMessages()
    {
        var (guide, _, _) = Hot();
        var thread = WavePlannerTests.Thread("T-0090", new DateTimeOffset(2024, 3, 20, 15, 0, 0, TimeSpan.Zero), 3);
        thread.Messages[0].FromId = "CH-001";
        thread.Messages[0].ToIds = new List<string> { "CH-002" };
        thread.Messages[1].FromId = "CH-002";
        thread.Messages[1].ToIds = new List<string> { "CH-001" };
        thread.Messages[2].FromId = "CH-001";
        thread.Messages[2].ToIds = new List<string> { "CH-009" };
        thread.Messages[2].ReplyToSeq = 1;
        thread.Messages[2].Kind = MessageKind.Forward;

        var entries = LedgerMerger.EntriesFor(thread, new List<LedgerDelta>
        {
            Delta(1, "Root detail", "Stated in the root."),
            Delta(2, "Reply detail", "Stated in the reply.")
        }, guide, 1);

        Assert.Equal(new[] { "CH-001", "CH-002", "CH-009" }, entries[0].KnownByIds);
        Assert.Equal(new[] { "CH-001", "CH-002" }, entries[1].KnownByIds);
    }

    [Fact]
    public void EntriesFor_KeepsOnlyGatedCanonicalFacts()
    {
        var (guide, _, thread) = Hot();

        var entries = LedgerMerger.EntriesFor(thread, new List<LedgerDelta>
        {
            Delta(1, "Approval", "Approved in writing.", "", LedgerKind.Decision, " f-003 ", "F-999", "F-005", "F-003")
        }, guide, 1);

        Assert.Equal(new[] { "F-003" }, entries[0].FactIds);
        Assert.Equal(new[] { "ISS-01" }, entries[0].IssueIds);
    }

    [Fact]
    public void EntriesFor_CleansAndDropsBadDeltas()
    {
        var (guide, _, thread) = Hot();
        var longText = new string('a', 500);

        var entries = LedgerMerger.EntriesFor(thread, new List<LedgerDelta>
        {
            Delta(9, "Unknown seq", "Dropped."),
            Delta(1, "Empty", "  CH-004 F-003 \r\n "),
            null!,
            Delta(1, "CH-099 Change\r\nOrder F-003", "Approved\r\nby T-0003 and EXT-002 in writing.", "V-001 $184,250.00", (LedgerKind)99),
            Delta(2, longText, longText + " " + longText, longText, LedgerKind.Person)
        }, guide, 1);

        Assert.Equal(2, entries.Count);
        Assert.Equal(LedgerKind.Other, entries[0].Kind);
        Assert.Equal("Change Order", entries[0].About);
        Assert.Equal("Approved by and in writing.", entries[0].Statement);
        Assert.Equal("$184,250.00", entries[0].Value);
        Assert.Equal($"{thread.Id}-L01", entries[0].Id);
        Assert.Equal($"{thread.Id}-L02", entries[1].Id);
        Assert.True(entries[1].About.Length <= 120);
        Assert.True(entries[1].Statement.Length <= 300);
        Assert.True(entries[1].Value.Length <= 120);
    }

    [Fact]
    public void EntriesFor_MergesDuplicatesAndCapsAtTwelve()
    {
        var (guide, _, thread) = Hot();

        var dupes = LedgerMerger.EntriesFor(thread, new List<LedgerDelta>
        {
            Delta(1, "Change Order 7 amount", "The amount is seven and a half million.", "$7.5 million"),
            Delta(3, "change order 7 AMOUNT!", "Confirmed at the amount.", "$7,500,000")
        }, guide, 1);
        Assert.Single(dupes);

        var many = Enumerable.Range(1, 15).Select(i => Delta(1, $"Detail {i}", $"Statement {i}.")).ToList();
        var capped = LedgerMerger.EntriesFor(thread, many, guide, 1);
        Assert.Equal(LedgerMerger.MaxEntriesPerThread, capped.Count);
        Assert.Equal("Detail 12", capped[^1].About);
        Assert.Equal($"{thread.Id}-L12", capped[^1].Id);
    }

    [Fact]
    public void SameValue_AndNormalizeAbout()
    {
        Assert.True(LedgerMerger.SameValue("", ""));
        Assert.True(LedgerMerger.SameValue("$7.5 million", "$7,500,000"));
        Assert.True(LedgerMerger.SameValue("2024-03-18", "March 18, 2024"));
        Assert.True(LedgerMerger.SameValue("CO-007", "co-007"));
        Assert.False(LedgerMerger.SameValue("$7.5 million", "$7,400,000"));
        Assert.False(LedgerMerger.SameValue("", "$1"));
        Assert.False(LedgerMerger.SameValue("2024-03-18", "March 19, 2024"));

        Assert.Equal("change order 7 amount", LedgerMerger.NormalizeAbout("  Change-Order 7\r\n AMOUNT! "));
    }

    [Fact]
    public void Build_MergesAcrossThreadsKeepingEarlierWave()
    {
        var (guide, plan, hot) = Hot();
        var later = PlanFixtures.Thread(plan, "F-004");
        var waves = new Dictionary<string, int> { [hot.Id] = 1, [later.Id] = 2 };
        var deltas = new Dictionary<string, List<LedgerDelta>>
        {
            [later.Id] = new() { Delta(2, "Change Order 7 amount", "Vendor repeats it.", "$184,250"), Delta(1, "Delivery", "Glass ships Monday.") },
            [hot.Id] = new() { Delta(1, "Change Order 7 amount", "Approved.", "$184,250.00"), Delta(3, "Timing", "Thirty days.") }
        };

        var ledger = LedgerMerger.Build(plan, guide, waves, deltas);

        Assert.Equal(new[] { $"{hot.Id}-L01", $"{hot.Id}-L02", $"{later.Id}-L02" }, ledger.Entries.Select(e => e.Id));
        var merged = ledger.Entries[0];
        Assert.Equal(1, merged.WaveIndex);
        Assert.Contains(EvidenceTestData.VendorId, merged.KnownByIds);
        Assert.Contains("CH-004", merged.KnownByIds);
        Assert.Equal(merged.KnownByIds.OrderBy(k => k, StringComparer.Ordinal), merged.KnownByIds);
    }

    [Fact]
    public void Build_OnlyThreadsInWaveMapAndOrdersByWaveThenTime()
    {
        var (guide, plan, hot) = Hot();
        var privileged = PlanFixtures.Thread(plan, "F-001");
        var vendor = PlanFixtures.Thread(plan, "F-004");
        var waves = new Dictionary<string, int> { [vendor.Id] = 1, [hot.Id] = 1 };
        var deltas = new Dictionary<string, List<LedgerDelta>>
        {
            [vendor.Id] = new() { Delta(1, "Vendor", "Vendor detail.") },
            [hot.Id] = new() { Delta(1, "Hot", "Hot detail.") },
            [privileged.Id] = new() { Delta(1, "Privileged", "Not in a wave.") }
        };

        var ledger = LedgerMerger.Build(plan, guide, waves, deltas);

        // Hot (2024-03-19) precedes the vendor thread (2024-04-23) in the same wave
        Assert.Equal(new[] { $"{hot.Id}-L01", $"{vendor.Id}-L01" }, ledger.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Build_IsDeterministicAcrossInsertionOrder()
    {
        var (guide, plan, hot) = Hot();
        var vendor = PlanFixtures.Thread(plan, "F-004");
        var a = new Dictionary<string, int> { [hot.Id] = 1, [vendor.Id] = 2 };
        var b = new Dictionary<string, int> { [vendor.Id] = 2, [hot.Id] = 1 };
        List<LedgerDelta> Hd() => new() { Delta(1, "Amount", "Approved.", "$184,250.00"), Delta(2, "Timing", "Soon.") };
        List<LedgerDelta> Vd() => new() { Delta(1, "Amount", "Repeated.", "$184,250"), Delta(2, "Delivery", "Monday.") };
        var da = new Dictionary<string, List<LedgerDelta>> { [hot.Id] = Hd(), [vendor.Id] = Vd() };
        var db = new Dictionary<string, List<LedgerDelta>> { [vendor.Id] = Vd(), [hot.Id] = Hd() };

        var first = LedgerMerger.Build(plan, guide, a, da);
        var second = LedgerMerger.Build(plan, guide, b, db);

        string Print(NarrativeLedger l) => string.Join("\n", l.Entries.Select(e =>
            $"{e.Id}|{e.Date}|{e.Kind}|{e.About}|{e.Statement}|{e.Value}|{string.Join(",", e.KnownByIds)}|{string.Join(",", e.FactIds)}|{e.WaveIndex}"));
        Assert.Equal(Print(first), Print(second));
        Assert.Equal(3, first.Entries.Count);
    }
}
