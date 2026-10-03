using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;

namespace ReelDiscovery.Tests.Evidence;

public class RenderAssignmentsTests
{
    private static readonly DateTimeOffset Day0 = new(2024, 3, 4, 15, 0, 0, TimeSpan.Zero);

    private static PlannedThread AddThread(CorpusPlan plan, bool privileged = false,
        ThreadRelevance relevance = ThreadRelevance.Relevant)
    {
        var thread = new PlannedThread
        {
            Id = $"T-{plan.Threads.Count + 1:0000}",
            Relevance = relevance,
            IsPrivileged = privileged
        };
        plan.Threads.Add(thread);
        return thread;
    }

    private static PlannedMessage AddMessage(PlannedThread thread, MessageKind kind = MessageKind.New,
        RelevanceBasis basis = RelevanceBasis.Own, bool hot = false)
    {
        var seq = thread.Messages.Count + 1;
        var message = new PlannedMessage
        {
            Id = $"{thread.Id}-M{seq:00}",
            Seq = seq,
            ReplyToSeq = seq - 1,
            Kind = kind,
            Basis = basis,
            IsHot = hot,
            SentAt = Day0.AddHours(seq),
            TimeZoneId = "America/Chicago"
        };
        thread.Messages.Add(message);
        return message;
    }

    // n single-message own-basis threads; the first `hot` are hot
    private static CorpusPlan OwnPlan(int n, int hot = 0)
    {
        var plan = new CorpusPlan();
        for (var i = 0; i < n; i++)
            AddMessage(AddThread(plan), hot: i < hot);
        return plan;
    }

    private static List<MessageAssignment> All(Dictionary<string, ThreadAssignment> result) =>
        result.Values.SelectMany(t => t.Messages).ToList();

    private static int Count(Dictionary<string, ThreadAssignment> result, Subtlety subtlety) =>
        All(result).Count(m => m.Subtlety == subtlety);

    private static List<string> Fingerprint(Dictionary<string, ThreadAssignment> result) =>
        result.Values
            .OrderBy(t => t.ThreadId, StringComparer.Ordinal)
            .SelectMany(t => t.Messages.Select(m => $"{t.ThreadId}|{t.Marking}|{m.MessageId}|{m.Seq}|{m.Subtlety}|{m.Length}"))
            .ToList();

    [Fact]
    public void Subtlety_ExactMix_NoHot()
    {
        var result = RenderAssignments.Compute(OwnPlan(20));

        Assert.Equal(12, Count(result, Subtlety.Indirect));
        Assert.Equal(6, Count(result, Subtlety.Clear));
        Assert.Equal(2, Count(result, Subtlety.Explicit));
    }

    [Fact]
    public void Subtlety_HotFillsExplicitFirst()
    {
        var plan = OwnPlan(20, hot: 3);
        var result = RenderAssignments.Compute(plan);

        var hotIds = plan.Threads.SelectMany(t => t.Messages).Where(m => m.IsHot).Select(m => m.Id).ToHashSet();
        Assert.Equal(3, Count(result, Subtlety.Explicit));
        Assert.All(All(result).Where(m => hotIds.Contains(m.MessageId)), m => Assert.Equal(Subtlety.Explicit, m.Subtlety));
        Assert.Equal(6, Count(result, Subtlety.Clear));
        Assert.Equal(11, Count(result, Subtlety.Indirect));
    }

    [Fact]
    public void Subtlety_ManyHot_ShrinksIndirect()
    {
        var result = RenderAssignments.Compute(OwnPlan(10, hot: 4));

        Assert.Equal(4, Count(result, Subtlety.Explicit));
        Assert.Equal(3, Count(result, Subtlety.Clear));
        Assert.Equal(3, Count(result, Subtlety.Indirect));
    }

    [Fact]
    public void Subtlety_AllHot_NoNegativeCounts()
    {
        var result = RenderAssignments.Compute(OwnPlan(5, hot: 5));

        Assert.Equal(5, Count(result, Subtlety.Explicit));
    }

    [Fact]
    public void Subtlety_QuotedNoneAndPrivilegedCounsel()
    {
        var plan = OwnPlan(10);
        var normal = AddThread(plan);
        AddMessage(normal);
        var quoted = AddMessage(normal, MessageKind.Reply, RelevanceBasis.Quoted);
        var privileged = AddThread(plan, privileged: true);
        var advice = AddMessage(privileged);
        var counselReply = AddMessage(privileged, MessageKind.Reply);
        var privilegedQuoted = AddMessage(privileged, MessageKind.Reply, RelevanceBasis.Quoted);

        var result = RenderAssignments.Compute(plan);

        Assert.Equal(Subtlety.None, result[normal.Id].For(quoted.Seq)!.Subtlety);
        Assert.Equal(Subtlety.Counsel, result[privileged.Id].For(advice.Seq)!.Subtlety);
        Assert.Equal(Subtlety.Counsel, result[privileged.Id].For(counselReply.Seq)!.Subtlety);
        Assert.Equal(Subtlety.None, result[privileged.Id].For(privilegedQuoted.Seq)!.Subtlety);

        // Pool = 11 own non-privileged messages -> Largest(11, 60/30/10) = 7/3/1
        Assert.Equal(7, Count(result, Subtlety.Indirect));
        Assert.Equal(3, Count(result, Subtlety.Clear));
        Assert.Equal(1, Count(result, Subtlety.Explicit));
    }

    [Fact]
    public void Subtlety_PrivilegedHotIsExplicit_NotCountedInPool()
    {
        var plan = OwnPlan(20);
        var privileged = AddThread(plan, privileged: true);
        var hotAdvice = AddMessage(privileged, hot: true);
        AddMessage(privileged, MessageKind.Reply);
        var normal = AddThread(plan);
        AddMessage(normal);
        var hotQuoted = AddMessage(normal, MessageKind.Forward, RelevanceBasis.Quoted, hot: true);

        var result = RenderAssignments.Compute(plan);

        Assert.Equal(Subtlety.Explicit, result[privileged.Id].For(hotAdvice.Seq)!.Subtlety);
        Assert.Equal(Subtlety.Counsel, result[privileged.Id].For(2)!.Subtlety);
        Assert.Equal(Subtlety.Explicit, result[normal.Id].For(hotQuoted.Seq)!.Subtlety);

        // Pool = 21 own non-privileged messages, none hot -> Largest(21, 60/30/10) = 13/6/2; plus the two hot outside it
        var poolIds = plan.Threads.Where(t => !t.IsPrivileged).SelectMany(t => t.Messages)
            .Where(m => m.Basis == RelevanceBasis.Own).Select(m => m.Id).ToHashSet();
        var pool = All(result).Where(m => poolIds.Contains(m.MessageId)).ToList();
        Assert.Equal(13, pool.Count(m => m.Subtlety == Subtlety.Indirect));
        Assert.Equal(6, pool.Count(m => m.Subtlety == Subtlety.Clear));
        Assert.Equal(2, pool.Count(m => m.Subtlety == Subtlety.Explicit));
        Assert.Equal(4, Count(result, Subtlety.Explicit));
    }

    [Fact]
    public void Length_ExactBands_LongNeverOnReplies()
    {
        var plan = new CorpusPlan();
        for (var i = 0; i < 20; i++)
        {
            var thread = AddThread(plan);
            AddMessage(thread);
            AddMessage(thread, i % 2 == 0 ? MessageKind.Reply : MessageKind.ReplyAll);
        }

        var result = RenderAssignments.Compute(plan);
        var all = All(result);

        Assert.Equal(22, all.Count(m => m.Length == LengthBand.Short));
        Assert.Equal(14, all.Count(m => m.Length == LengthBand.Medium));
        Assert.Equal(4, all.Count(m => m.Length == LengthBand.Long));

        // Seq 2 is the reply in every thread
        var replies = all.Where(m => m.Seq == 2).ToList();
        Assert.Equal(20, replies.Count);
        Assert.All(replies, m => Assert.Equal(LengthBand.Short, m.Length));
    }

    [Fact]
    public void Length_FewNewOrForward_LongShortfallBecomesMedium()
    {
        // 1 New + 19 replies: Largest(20) = 11/7/2, only 1 can be Long
        var plan = new CorpusPlan();
        var thread = AddThread(plan);
        AddMessage(thread);
        for (var i = 0; i < 19; i++)
            AddMessage(thread, MessageKind.Reply);

        var all = All(RenderAssignments.Compute(plan));

        Assert.Equal(1, all.Count(m => m.Length == LengthBand.Long));
        Assert.Equal(LengthBand.Long, all.Single(m => m.Seq == 1).Length);
        Assert.Equal(11, all.Count(m => m.Length == LengthBand.Short));
        Assert.Equal(8, all.Count(m => m.Length == LengthBand.Medium));
    }

    [Fact]
    public void Length_ForwardCanBeLong()
    {
        var plan = new CorpusPlan();
        var thread = AddThread(plan);
        for (var i = 0; i < 9; i++)
            AddMessage(thread, MessageKind.Reply);
        AddMessage(thread, MessageKind.Forward);

        var all = All(RenderAssignments.Compute(plan));

        Assert.Equal(LengthBand.Long, all.Single(m => m.Seq == 10).Length);
    }

    [Fact]
    public void Privilege_SevenThreads_LabeledAndSubjectMix()
    {
        var plan = new CorpusPlan();
        for (var i = 0; i < 7; i++)
            AddMessage(AddThread(plan, privileged: true));
        AddMessage(AddThread(plan));

        var result = RenderAssignments.Compute(plan);
        var privileged = result.Values.Where(t => plan.Threads.First(p => p.Id == t.ThreadId).IsPrivileged).ToList();

        Assert.Equal(4, privileged.Count(t => t.Marking == PrivilegeMarking.Banner));
        Assert.Equal(2, privileged.Count(t => t.Marking == PrivilegeMarking.BannerAndSubject));
        Assert.Equal(1, privileged.Count(t => t.Marking == PrivilegeMarking.Unlabeled));
        Assert.Equal(PrivilegeMarking.None, result["T-0008"].Marking);
    }

    [Fact]
    public void Privilege_SingleThread_IsBanner()
    {
        var plan = new CorpusPlan();
        AddMessage(AddThread(plan, privileged: true));

        var result = RenderAssignments.Compute(plan);

        Assert.Equal(PrivilegeMarking.Banner, result["T-0001"].Marking);
    }

    [Fact]
    public void Compute_NonRelevantThreadsHaveNoEntry()
    {
        var plan = OwnPlan(2);
        var noise = AddThread(plan, relevance: ThreadRelevance.NotRelevant);
        AddMessage(noise, basis: RelevanceBasis.None);

        var result = RenderAssignments.Compute(plan);

        Assert.False(result.ContainsKey(noise.Id));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Compute_NullOrEmptyPlan_Empty()
    {
        Assert.Empty(RenderAssignments.Compute(new CorpusPlan()));
        Assert.Empty(RenderAssignments.Compute(new CorpusPlan { Threads = null! }));
    }

    [Fact]
    public void Compute_IsStableAcrossClonesAndOrder()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();

        var first = RenderAssignments.Compute(plan);
        var clone = PlanFixtures.Clone(plan);
        var reversed = PlanFixtures.Clone(plan);
        reversed.Threads.Reverse();
        foreach (var thread in reversed.Threads)
            thread.Messages.Reverse();

        Assert.NotEmpty(first);
        Assert.Equal(Fingerprint(first), Fingerprint(RenderAssignments.Compute(plan)));
        Assert.Equal(Fingerprint(first), Fingerprint(RenderAssignments.Compute(clone)));
        Assert.Equal(Fingerprint(first), Fingerprint(RenderAssignments.Compute(reversed)));
    }

    [Fact]
    public void Compute_ApprovedCase_HotExplicitAndPrivilegedLabeled()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();

        var result = RenderAssignments.Compute(plan);

        foreach (var thread in plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant))
        {
            var assignment = result[thread.Id];
            Assert.Equal(thread.Messages.Count, assignment.Messages.Count);
            foreach (var message in thread.Messages.Where(m => m.IsHot))
                Assert.Equal(Subtlety.Explicit, assignment.For(message.Seq)!.Subtlety);
            foreach (var message in thread.Messages.Where(m => m.Kind is MessageKind.Reply or MessageKind.ReplyAll))
                Assert.NotEqual(LengthBand.Long, assignment.For(message.Seq)!.Length);
            Assert.Equal(thread.IsPrivileged, assignment.Marking != PrivilegeMarking.None);
        }
    }

    [Fact]
    public void StableRank_IsSha256()
    {
        // First 8 bytes (big-endian) of SHA-256("T-0001-M01"), computed once
        Assert.Equal(15904714948164331430UL, RenderAssignments.StableRank("T-0001-M01"));
        Assert.Equal(RenderAssignments.StableRank(string.Empty), RenderAssignments.StableRank(null!));
    }

    [Fact]
    public void LengthBands_TableAndSpec()
    {
        Assert.Equal(1.0, RenderAssignments.LengthBands.Sum(b => b.Share), 6);
        Assert.Equal(160, RenderAssignments.Spec(LengthBand.Long).MinWords);
        Assert.Equal(320, RenderAssignments.Spec(LengthBand.Long).MaxWords);
        Assert.Equal(8, RenderAssignments.Spec(LengthBand.Short).MinWords);
    }
}
