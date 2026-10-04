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

    // ---------- Phase 6: attachment-derived entries (D-18) ----------

    private static LedgerDelta FromAttachment(string attachmentDocId, int seq, string about, string statement, string value = "",
        LedgerKind kind = LedgerKind.Figure, params string[] facts)
    {
        var delta = Delta(seq, about, statement, value, kind, facts);
        delta.FromAttachment = true;
        delta.AttachmentDocId = attachmentDocId;
        return delta;
    }

    private static string Print(IEnumerable<LedgerEntry> entries) => string.Join("\n", entries.Select(e =>
        $"{e.Id}|{e.Date}|{e.Kind}|{e.About}|{e.Statement}|{e.Value}|{string.Join(",", e.KnownByIds)}|{string.Join(",", e.FactIds)}|{string.Join(",", e.IssueIds)}|{e.SourceDocId}|{e.SourceThreadId}|{e.WaveIndex}"));

    // Root CH-001 -> CH-002; seq 2 CH-002 -> CH-001 replies; seq 3 CH-001 -> CH-009 forwards the root (a sibling branch)
    private static PlannedThread Branching(CorpusPlan? plan = null)
    {
        var thread = WavePlannerTests.Thread("T-0090", new DateTimeOffset(2024, 3, 20, 15, 0, 0, TimeSpan.Zero), 3);
        thread.FactIds = new List<string> { "F-003" };
        thread.Messages[0].FromId = "CH-001";
        thread.Messages[0].ToIds = new List<string> { "CH-002" };
        thread.Messages[1].FromId = "CH-002";
        thread.Messages[1].ToIds = new List<string> { "CH-001" };
        thread.Messages[2].FromId = "CH-001";
        thread.Messages[2].ToIds = new List<string> { "CH-009" };
        thread.Messages[2].ReplyToSeq = 1;
        thread.Messages[2].Kind = MessageKind.Forward;
        for (var i = 0; i < 3; i++)
            thread.Messages[i].DocId = $"RD-9900{i + 1}";
        plan?.Threads.Add(thread);
        return thread;
    }

    [Fact]
    public void EntriesFor_AttachmentEntries_StableIdsCapAndSource()
    {
        var (guide, _, thread) = Hot();
        var docA = thread.Messages[0].DocId + ".0001";
        var docB = thread.Messages[1].DocId + ".0001";
        var deltas = Enumerable.Range(1, 14).Select(i => Delta(1, $"Detail {i}", $"Statement {i}.")).ToList();
        deltas.AddRange(Enumerable.Range(1, 4).Select(i => FromAttachment(docA, 1, $"Sheet A line {i}", $"Line {i}.")));
        deltas.AddRange(Enumerable.Range(1, 2).Select(i => FromAttachment(docB, 2, $"Memo B point {i}", $"Point {i}.")));

        var entries = LedgerMerger.EntriesFor(thread, deltas, guide, 1);

        var expected = Enumerable.Range(1, 12).Select(i => $"{thread.Id}-L{i:00}")
            .Concat(new[] { $"{docA}-L01", $"{docA}-L02", $"{docA}-L03", $"{docB}-L01", $"{docB}-L02" });
        Assert.Equal(expected, entries.Select(e => e.Id));
        Assert.Equal(new[] { "Sheet A line 1", "Sheet A line 2", "Sheet A line 3" },
            entries.Where(e => e.Id.StartsWith(docA, StringComparison.Ordinal)).Select(e => e.About));
        Assert.All(entries.Where(e => e.Id.StartsWith(docA, StringComparison.Ordinal)), e => Assert.Equal(docA, e.SourceDocId));
        Assert.All(entries.Where(e => e.Id.StartsWith(docB, StringComparison.Ordinal)), e => Assert.Equal(docB, e.SourceDocId));
        Assert.All(entries, e => Assert.Equal(thread.Id, e.SourceThreadId));
        Assert.Equal(3, LedgerMerger.MaxEntriesPerAttachment);
    }

    [Fact]
    public void EntriesFor_AddingOrReorderingAttachments_NeverRenumbers()
    {
        var (guide, _, thread) = Hot();
        var docA = thread.Messages[0].DocId + ".0001";
        var docB = thread.Messages[0].DocId + ".0002";
        List<LedgerDelta> Own() => new() { Delta(1, "Amount", "Approved.", "$184,250.00"), Delta(2, "Timing", "Soon.") };
        List<LedgerDelta> A() => new() { FromAttachment(docA, 1, "Frames", "Frames line.", "$90,000"), FromAttachment(docA, 1, "Glass", "Glass line.", "$60,000") };
        List<LedgerDelta> B() => new() { FromAttachment(docB, 1, "Sealant", "Sealant line.", "$4,000") };

        var onlyB = LedgerMerger.EntriesFor(thread, Own().Concat(B()).ToList(), guide, 1);
        var aThenB = LedgerMerger.EntriesFor(thread, Own().Concat(A()).Concat(B()).ToList(), guide, 1);
        var bThenA = LedgerMerger.EntriesFor(thread, B().Concat(A()).Concat(Own()).ToList(), guide, 1);

        Assert.Equal(Print(aThenB), Print(bThenA));
        Assert.Equal(Print(onlyB.Take(2)), Print(aThenB.Take(2)));
        Assert.Equal(Print(onlyB.Where(e => e.SourceDocId == docB)), Print(aThenB.Where(e => e.SourceDocId == docB)));
        Assert.Equal($"{docB}-L01", aThenB.Single(e => e.About == "Sealant").Id);
        Assert.Equal(new[] { $"{thread.Id}-L01", $"{thread.Id}-L02" }, aThenB.Take(2).Select(e => e.Id));
    }

    [Fact]
    public void EntriesFor_AttachmentDuplicateOfThreadDetail_IsAbsorbed_ButNotAcrossAttachments()
    {
        var (guide, _, thread) = Hot();
        var docA = thread.Messages[3].DocId + ".0001";
        var docB = thread.Messages[3].DocId + ".0002";

        var entries = LedgerMerger.EntriesFor(thread, new List<LedgerDelta>
        {
            Delta(2, "Change Order 7 amount", "Approved.", "$184,250.00"),
            FromAttachment(docA, 4, "change order 7 AMOUNT", "Per the sheet.", "$184,250"),
            FromAttachment(docA, 4, "Retainage", "Held back.", "$9,000"),
            FromAttachment(docB, 4, "retainage", "Also held back.", "$9,000.00")
        }, guide, 1);

        Assert.Equal(new[] { $"{thread.Id}-L01", $"{docA}-L01", $"{docB}-L01" }, entries.Select(e => e.Id));
        Assert.Equal(thread.Messages[1].DocId, entries[0].SourceDocId);
    }

    [Fact]
    public void AttachmentAbsorb_NeverChangesThreadEntryFacts()
    {
        var (guide, plan, _) = Hot();
        var thread = Branching(plan);
        var meetingDay = guide.FindFact("F-003")!.Date;
        var own = Delta(2, "Site walk", "The teams walk the floors.", meetingDay, LedgerKind.Meeting);
        var attachment = FromAttachment(thread.Messages[2].DocId + ".0001", 3, "site walk", "Agenda for the walk.", meetingDay,
            LedgerKind.Meeting, "F-003");
        var date = DateOnly.ParseExact(meetingDay, "yyyy-MM-dd");

        // Alone, the attachment entry keeps the gated fact (so a FactIds union would change the key to fact:F-003)
        var alone = LedgerMerger.EntriesFor(thread, new[] { attachment }, guide, 1).Single();
        Assert.Equal(new[] { "F-003" }, alone.FactIds);
        Assert.Equal(new[] { "CH-001", "CH-009" }, alone.KnownByIds);

        var before = LedgerMerger.EntriesFor(thread, new[] { own }, guide, 1).Single();
        var after = LedgerMerger.EntriesFor(thread, new[] { own, attachment }, guide, 1).Single();

        Assert.Equal(new[] { "CH-001", "CH-002", "CH-009" }, after.KnownByIds);
        Assert.Empty(after.FactIds);
        Assert.Empty(after.IssueIds);
        Assert.Equal(before.SourceDocId, after.SourceDocId);
        Assert.Equal(before.About, after.About);
        Assert.Equal(before.Value, after.Value);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(InviteScheduler.MeetingKeyFor(before, date, guide), InviteScheduler.MeetingKeyFor(after, date, guide));
        Assert.StartsWith("mtg:", InviteScheduler.MeetingKeyFor(after, date, guide));

        // Build sees the same thread entry
        var waves = new Dictionary<string, int> { [thread.Id] = 1 };
        var builtBefore = LedgerMerger.Build(plan, guide, waves, new Dictionary<string, List<LedgerDelta>> { [thread.Id] = new() { own } });
        var builtAfter = LedgerMerger.Build(plan, guide, waves, new Dictionary<string, List<LedgerDelta>> { [thread.Id] = new() { own, attachment } });
        var held = builtAfter.Entries.Single();
        Assert.Empty(held.FactIds);
        Assert.Equal(new[] { "CH-001", "CH-002", "CH-009" }, held.KnownByIds);
        Assert.Equal(InviteScheduler.MeetingKeyFor(builtBefore.Entries.Single(), date, guide), InviteScheduler.MeetingKeyFor(held, date, guide));
    }

    [Fact]
    public void Build_AttachmentAbsorbedByEarlierEntry_AddsKnowersOnly()
    {
        var (guide, plan, hot) = Hot();
        var vendor = PlanFixtures.Thread(plan, "F-004");
        var waves = new Dictionary<string, int> { [hot.Id] = 1, [vendor.Id] = 2 };
        var hotDoc = hot.Messages[0].DocId + ".0001";
        var vendorDoc = vendor.Messages[0].DocId + ".0001";
        var deltas = new Dictionary<string, List<LedgerDelta>>
        {
            [hot.Id] = new() { Delta(4, "Change Order 7 amount", "Forwarded.", "$184,250.00"), FromAttachment(hotDoc, 1, "Retainage", "Held.", "$9,000") },
            [vendor.Id] = new()
            {
                FromAttachment(vendorDoc, 1, "change order 7 amount", "Vendor sheet.", "$184,250", facts: "F-004"),
                FromAttachment(vendorDoc, 2, "retainage", "Vendor copy.", "$9,000", facts: "F-004")
            }
        };

        var ledger = LedgerMerger.Build(plan, guide, waves, deltas);

        Assert.Equal(new[] { $"{hot.Id}-L01", $"{hotDoc}-L01" }, ledger.Entries.Select(e => e.Id));
        Assert.All(ledger.Entries, e => Assert.Empty(e.FactIds));
        Assert.Contains(EvidenceTestData.VendorId, ledger.Entries[0].KnownByIds);
        Assert.Contains(EvidenceTestData.VendorId, ledger.Entries[1].KnownByIds);
        Assert.Equal(hot.Messages[3].DocId, ledger.Entries[0].SourceDocId);
        Assert.Equal(hotDoc, ledger.Entries[1].SourceDocId);
    }

    [Fact]
    public void EntriesFor_AttachmentUnknownSeqDropped_KnownByIsParentParticipants()
    {
        var (guide, _, thread) = Hot();
        var doc = thread.Messages[0].DocId + ".0001";

        var entries = LedgerMerger.EntriesFor(thread, new List<LedgerDelta>
        {
            FromAttachment(doc, 9, "Lost", "Unknown seq."),
            FromAttachment(doc, 1, "Pricing", "On the sheet.")
        }, guide, 1);

        var entry = Assert.Single(entries);
        Assert.Equal($"{doc}-L01", entry.Id);

        // Seq 1 is CH-004 -> CH-002; the later reply-all and forward participants do not receive the root's attachment
        Assert.Equal(new[] { "CH-002", "CH-004" }, entry.KnownByIds);
    }

    [Fact]
    public void Build_ThreadEntriesFirst_LateAttachmentNeverAbsorbsLaterThreadEntry()
    {
        var (guide, plan, hot) = Hot();
        var vendor = PlanFixtures.Thread(plan, "F-004");
        var waves = new Dictionary<string, int> { [hot.Id] = 1, [vendor.Id] = 2 };
        var hotDoc = hot.Messages[0].DocId + ".0001";
        var deltas = new Dictionary<string, List<LedgerDelta>>
        {
            [hot.Id] = new() { Delta(1, "Timing", "Thirty days."), FromAttachment(hotDoc, 1, "Glass delivery", "Ships Monday.", "2024-04-29", LedgerKind.Commitment) },
            [vendor.Id] = new() { Delta(1, "Glass delivery", "Vendor confirms Monday.", "April 29, 2024", LedgerKind.Commitment, "F-004") }
        };

        var ledger = LedgerMerger.Build(plan, guide, waves, deltas);

        Assert.Equal(new[] { $"{hot.Id}-L01", $"{vendor.Id}-L01" }, ledger.Entries.Select(e => e.Id));
        var held = ledger.Entries[1];
        Assert.Equal(new[] { "F-004" }, held.FactIds);
        Assert.Equal(2, held.WaveIndex);
        Assert.Contains("CH-004", held.KnownByIds);
    }

    [Fact]
    public void Build_AttachmentEntriesFollowEveryThreadEntry()
    {
        var (guide, plan, hot) = Hot();
        var vendor = PlanFixtures.Thread(plan, "F-004");
        var waves = new Dictionary<string, int> { [hot.Id] = 1, [vendor.Id] = 2 };
        var hotDoc = hot.Messages[0].DocId + ".0001";
        var deltas = new Dictionary<string, List<LedgerDelta>>
        {
            [hot.Id] = new() { FromAttachment(hotDoc, 1, "Frames", "Frames line.", "$90,000"), Delta(1, "Timing", "Thirty days.") },
            [vendor.Id] = new() { Delta(1, "Delivery", "Glass ships Monday.") }
        };

        var ledger = LedgerMerger.Build(plan, guide, waves, deltas);

        Assert.Equal(new[] { $"{hot.Id}-L01", $"{vendor.Id}-L01", $"{hotDoc}-L01" }, ledger.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Build_WithoutAttachmentDeltas_MatchesPhase5Ledger()
    {
        var (guide, plan, hot) = Hot();
        var vendor = PlanFixtures.Thread(plan, "F-004");
        var privileged = PlanFixtures.Thread(plan, "F-001");
        var waves = new Dictionary<string, int> { [hot.Id] = 1, [vendor.Id] = 2, [privileged.Id] = 1 };
        var deltas = new Dictionary<string, List<LedgerDelta>>
        {
            [hot.Id] = new()
            {
                Delta(1, "Change Order 7 amount", "Approved.", "$184,250.00", LedgerKind.Figure, "F-003"),
                Delta(3, "Payment timing", "Thirty days.", kind: LedgerKind.Commitment),
                Delta(4, "change order 7 amount", "Forwarded.", "$184,250")
            },
            [vendor.Id] = new()
            {
                Delta(2, "Change Order 7 amount", "Vendor repeats it.", "$184,250", LedgerKind.Figure, "F-004"),
                Delta(1, "Delivery", "Glass ships Monday.", "2024-04-29", LedgerKind.Commitment)
            },
            [privileged.Id] = new() { Delta(2, "Advice", "Counsel advises holding payment.", kind: LedgerKind.Decision, facts: "F-001") }
        };

        var ledger = LedgerMerger.Build(plan, guide, waves, deltas);

        Assert.Equal(Phase5Golden, Print(ledger.Entries));
    }

    // Captured from LedgerMerger.Build before 06-08 on the fixture above
    private const string Phase5Golden =
        "T-0001-L01|2024-01-16|Decision|Advice|Counsel advises holding payment.||CH-001,CH-005,CH-007|F-001|ISS-01|RD-000002|T-0001|1\n"
        + "T-0007-L01|2024-03-19|Figure|Change Order 7 amount|Approved.|$184,250.00|CH-001,CH-002,CH-003,CH-004,EXT-002|F-003|ISS-01|RD-000008|T-0007|1\n"
        + "T-0007-L02|2024-03-20|Commitment|Payment timing|Thirty days.||CH-001,CH-002,CH-003,CH-004|||RD-000010|T-0007|1\n"
        + "T-0011-L02|2024-04-23|Commitment|Delivery|Glass ships Monday.|2024-04-29|CH-002,EXT-002|||RD-000015|T-0011|2";

    [Fact]
    public void EntriesFor_LegacyAttachmentDeltasWithoutDocId_GroupedUnderThreadAndCapped()
    {
        var (guide, _, thread) = Hot();
        var deltas = Enumerable.Range(1, 5).Select(i => FromAttachment(string.Empty, 1, $"Legacy {i}", $"Legacy {i}.")).ToList();
        deltas.Insert(0, Delta(1, "Own", "Own detail."));

        var entries = LedgerMerger.EntriesFor(thread, deltas, guide, 1);

        Assert.Equal(new[] { $"{thread.Id}-L01", $"{thread.Id}-A-L01", $"{thread.Id}-A-L02", $"{thread.Id}-A-L03" },
            entries.Select(e => e.Id));
    }
}
