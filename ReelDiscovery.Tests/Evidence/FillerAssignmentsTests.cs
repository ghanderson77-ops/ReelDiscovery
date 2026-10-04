using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class FillerAssignmentsTests
{
    private static readonly DateTimeOffset Day0 = new(2024, 3, 4, 15, 0, 0, TimeSpan.Zero);

    private static PlannedThread AddThread(CorpusPlan plan, NonRelevantCategory category = NonRelevantCategory.DailyBusiness,
        bool privileged = false, ThreadRelevance relevance = ThreadRelevance.NotRelevant)
    {
        var thread = new PlannedThread
        {
            Id = $"T-{plan.Threads.Count + 1:0000}",
            Relevance = relevance,
            Category = relevance == ThreadRelevance.Relevant ? NonRelevantCategory.None : category,
            IsPrivileged = privileged
        };
        plan.Threads.Add(thread);
        return thread;
    }

    private static PlannedMessage AddMessage(PlannedThread thread, MessageKind kind = MessageKind.New, string from = "CH-001")
    {
        var seq = thread.Messages.Count + 1;
        var message = new PlannedMessage
        {
            Id = $"{thread.Id}-M{seq:00}",
            Seq = seq,
            ReplyToSeq = seq - 1,
            Kind = kind,
            FromId = from,
            Basis = thread.Relevance == ThreadRelevance.Relevant ? RelevanceBasis.Own : RelevanceBasis.None,
            SentAt = Day0.AddHours(seq),
            TimeZoneId = "America/Chicago"
        };
        thread.Messages.Add(message);
        return message;
    }

    // 50 business threads (DailyBusiness, Ancillary, Decoy in turn) of New, Reply, ReplyAll, Forward = 200 messages
    private static CorpusPlan BusinessPlan()
    {
        var categories = new[] { NonRelevantCategory.DailyBusiness, NonRelevantCategory.Ancillary, NonRelevantCategory.Decoy };
        var plan = new CorpusPlan();
        for (var i = 0; i < 50; i++)
        {
            var thread = AddThread(plan, categories[i % 3]);
            AddMessage(thread, MessageKind.New);
            AddMessage(thread, MessageKind.Reply);
            AddMessage(thread, MessageKind.ReplyAll);
            AddMessage(thread, MessageKind.Forward);
        }

        return plan;
    }

    private static List<MessageAssignment> All(Dictionary<string, ThreadAssignment> result) =>
        result.Values.SelectMany(t => t.Messages).ToList();

    private static int Count(Dictionary<string, ThreadAssignment> result, LengthBand band) =>
        All(result).Count(m => m.Length == band);

    private static List<string> Fingerprint(Dictionary<string, ThreadAssignment> result) =>
        result.Values
            .OrderBy(t => t.ThreadId, StringComparer.Ordinal)
            .SelectMany(t => t.Messages.Select(m => $"{t.ThreadId}|{t.Marking}|{m.MessageId}|{m.Seq}|{m.Subtlety}|{m.Length}"))
            .ToList();

    [Fact]
    public void MixedCase_EntryForEveryNonRelevantThread_NoneForRelevant()
    {
        var (_, plan) = AttachmentTestData.MixedCase();

        var result = FillerAssignments.Compute(plan);

        var expected = plan.Threads
            .Where(t => t.Relevance != ThreadRelevance.Relevant && t.Messages.Count > 0)
            .Select(t => t.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, result.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.All(plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant),
            t => Assert.False(result.ContainsKey(t.Id)));
    }

    [Fact]
    public void BusinessMessages_SameLengthSharesAsRelevant()
    {
        var plan = BusinessPlan();

        var result = FillerAssignments.Compute(plan);

        var shares = Apportion.Largest(200, RenderAssignments.LengthBands.Select(b => b.Share).ToList());
        Assert.Equal(200, All(result).Count);
        Assert.Equal(shares[0], Count(result, LengthBand.Short));
        Assert.Equal(shares[1], Count(result, LengthBand.Medium));
        Assert.Equal(shares[2], Count(result, LengthBand.Long));
    }

    [Fact]
    public void Long_OnlyForNewOrForward()
    {
        var plan = BusinessPlan();

        var result = FillerAssignments.Compute(plan);

        var byId = plan.Threads.SelectMany(t => t.Messages).ToDictionary(m => m.Id, StringComparer.Ordinal);
        Assert.All(All(result).Where(m => m.Length == LengthBand.Long),
            m => Assert.Contains(byId[m.MessageId].Kind, new[] { MessageKind.New, MessageKind.Forward }));
    }

    [Fact]
    public void Long_ShortfallBecomesMedium()
    {
        // 5 threads of New + 19 replies: 100 messages, Long wants 10 but only 5 New exist
        var plan = new CorpusPlan();
        for (var i = 0; i < 5; i++)
        {
            var thread = AddThread(plan);
            AddMessage(thread, MessageKind.New);
            for (var r = 0; r < 19; r++)
                AddMessage(thread, MessageKind.Reply);
        }

        var result = FillerAssignments.Compute(plan);

        Assert.Equal(5, Count(result, LengthBand.Long));
        Assert.Equal(55, Count(result, LengthBand.Short));
        Assert.Equal(40, Count(result, LengthBand.Medium));
    }

    [Fact]
    public void Bulk_BandedBySenderKind_NotInParityPopulation()
    {
        var plan = BusinessPlan();
        plan.Externals.Add(new ExternalParty { Id = "EXT-001", Kind = ExternalKind.Newsletter });
        plan.Externals.Add(new ExternalParty { Id = "EXT-002", Kind = ExternalKind.Spam });
        plan.Externals.Add(new ExternalParty { Id = "EXT-003", Kind = ExternalKind.Personal });
        var newsletter = AddMessage(AddThread(plan, NonRelevantCategory.NewsletterSpamPersonal), from: "EXT-001");
        var spam = AddMessage(AddThread(plan, NonRelevantCategory.NewsletterSpamPersonal), from: "EXT-002");
        var personal = AddMessage(AddThread(plan, NonRelevantCategory.NewsletterSpamPersonal), from: "EXT-003");

        var result = FillerAssignments.Compute(plan);

        Assert.Equal(LengthBand.Long, result[newsletter.Id[..6]].For(1)!.Length);
        Assert.Equal(LengthBand.Short, result[spam.Id[..6]].For(1)!.Length);
        Assert.Equal(LengthBand.Short, result[personal.Id[..6]].For(1)!.Length);

        // The 200 business messages keep the exact mix
        var business = result.Where(kv => kv.Key != newsletter.Id[..6] && kv.Key != spam.Id[..6] && kv.Key != personal.Id[..6])
            .SelectMany(kv => kv.Value.Messages).ToList();
        var shares = Apportion.Largest(200, RenderAssignments.LengthBands.Select(b => b.Share).ToList());
        Assert.Equal(shares[0], business.Count(m => m.Length == LengthBand.Short));
        Assert.Equal(shares[1], business.Count(m => m.Length == LengthBand.Medium));
        Assert.Equal(shares[2], business.Count(m => m.Length == LengthBand.Long));
    }

    [Fact]
    public void Privileged_SameMarkingMixAsRelevant()
    {
        var plan = new CorpusPlan();
        for (var i = 0; i < 20; i++)
            AddMessage(AddThread(plan, privileged: true));
        for (var i = 0; i < 5; i++)
            AddMessage(AddThread(plan));

        var result = FillerAssignments.Compute(plan);

        var privileged = plan.Threads.Where(t => t.IsPrivileged).Select(t => result[t.Id].Marking).ToList();
        var labeled = privileged.Count(m => m == PrivilegeMarking.Banner || m == PrivilegeMarking.BannerAndSubject);
        Assert.Equal(17, labeled);
        Assert.Equal(3, privileged.Count(m => m == PrivilegeMarking.Unlabeled));
        Assert.Equal(Apportion.Largest(17, new[] { 0.6, 0.4 })[1], privileged.Count(m => m == PrivilegeMarking.BannerAndSubject));
        Assert.All(plan.Threads.Where(t => !t.IsPrivileged), t => Assert.Equal(PrivilegeMarking.None, result[t.Id].Marking));
    }

    [Fact]
    public void Subtlety_AlwaysNone()
    {
        var plan = BusinessPlan();
        AddMessage(AddThread(plan, privileged: true));

        var result = FillerAssignments.Compute(plan);

        Assert.All(All(result), m => Assert.Equal(Subtlety.None, m.Subtlety));
    }

    [Fact]
    public void RelevantAssignments_Unchanged()
    {
        var plan = BusinessPlan();
        for (var i = 0; i < 10; i++)
        {
            var thread = AddThread(plan, privileged: i < 4, relevance: ThreadRelevance.Relevant);
            AddMessage(thread, MessageKind.New);
            AddMessage(thread, MessageKind.Reply);
        }

        var before = Fingerprint(RenderAssignments.Compute(plan));
        FillerAssignments.Compute(plan);
        var after = Fingerprint(RenderAssignments.Compute(plan));

        Assert.Equal(before, after);
    }

    [Fact]
    public void Deterministic_AcrossCallsAndOrderings()
    {
        var plan = BusinessPlan();
        for (var i = 0; i < 6; i++)
            AddMessage(AddThread(plan, privileged: true));

        var first = Fingerprint(FillerAssignments.Compute(plan));
        var second = Fingerprint(FillerAssignments.Compute(plan));
        plan.Threads.Reverse();
        var reversed = Fingerprint(FillerAssignments.Compute(plan));

        Assert.Equal(first, second);
        Assert.Equal(first, reversed);
    }

    [Fact]
    public void RepeatedThreadId_FirstCopyKept()
    {
        var plan = new CorpusPlan();
        var thread = AddThread(plan);
        AddMessage(thread);
        var copy = new PlannedThread { Id = thread.Id, Relevance = ThreadRelevance.NotRelevant, Category = NonRelevantCategory.DailyBusiness };
        plan.Threads.Add(copy);
        AddMessage(copy);
        AddMessage(copy, MessageKind.Reply);

        var result = FillerAssignments.Compute(plan);

        Assert.Single(result);
        Assert.Single(result[thread.Id].Messages);
    }

    [Fact]
    public void NullOrEmptyPlan_Empty()
    {
        Assert.Empty(FillerAssignments.Compute(null!));
        Assert.Empty(FillerAssignments.Compute(new CorpusPlan()));
    }
}
