using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;

namespace ReelDiscovery.Tests.Evidence;

public class WavePlannerTests
{
    private static readonly DateTimeOffset Day0 = new(2024, 3, 4, 15, 0, 0, TimeSpan.Zero);

    internal static PlannedThread Thread(string id, DateTimeOffset first, int messages = 1,
        ThreadRelevance relevance = ThreadRelevance.Relevant)
    {
        var thread = new PlannedThread { Id = id, Relevance = relevance };
        for (var i = 0; i < messages; i++)
        {
            thread.Messages.Add(new PlannedMessage
            {
                Id = $"{id}-M{i + 1:00}",
                Seq = i + 1,
                ReplyToSeq = i,
                Kind = i == 0 ? MessageKind.New : MessageKind.Reply,
                SentAt = first.AddHours(i),
                TimeZoneId = "America/Chicago",
                Basis = RelevanceBasis.Own
            });
        }

        return thread;
    }

    private static CorpusPlan PlanOfDays(params double[] days)
    {
        var plan = new CorpusPlan();
        for (var i = 0; i < days.Length; i++)
            plan.Threads.Add(Thread($"T-{i + 1:0000}", Day0.AddDays(days[i])));
        return plan;
    }

    private static List<string> Fingerprint(List<GenerationWave> waves) =>
        waves.Select(w => $"{w.Index}|{w.Start:yyyy-MM-dd}|{w.End:yyyy-MM-dd}|{w.MessageCount}|{string.Join(",", w.ThreadIds)}").ToList();

    [Fact]
    public void Plan_DuplicateThreadId_ScheduledOnce()
    {
        var plan = PlanOfDays(0, 2);
        plan.Threads.Add(Thread("T-0001", Day0.AddDays(1)));

        var waves = WavePlanner.Plan(plan);

        var ids = waves.SelectMany(w => w.ThreadIds).ToList();
        Assert.Equal(ids.Distinct(StringComparer.Ordinal).Count(), ids.Count);
        Assert.Equal(new[] { "T-0001", "T-0002" }, ids.OrderBy(i => i, StringComparer.Ordinal));
        Assert.Equal(1, WavePlanner.WaveIndexByThread(waves)["T-0001"]);
    }

    [Fact]
    public void Plan_ClosesAtSevenDays_WhenWaveHasThree()
    {
        var waves = WavePlanner.Plan(PlanOfDays(0, 2, 6, 9, 30));

        Assert.Equal(3, waves.Count);
        Assert.Equal(new[] { "T-0001", "T-0002", "T-0003" }, waves[0].ThreadIds);
        Assert.Equal(new[] { "T-0004" }, waves[1].ThreadIds);
        Assert.Equal(new[] { "T-0005" }, waves[2].ThreadIds);
        Assert.Equal(new[] { 1, 2, 3 }, waves.Select(w => w.Index));
    }

    [Fact]
    public void Plan_ExtendsToFourteenDays_WhenWaveIsSmall()
    {
        var waves = WavePlanner.Plan(PlanOfDays(0, 8, 12));

        Assert.Single(waves);
        Assert.Equal(new[] { "T-0001", "T-0002", "T-0003" }, waves[0].ThreadIds);
    }

    [Fact]
    public void Plan_ExtendedWindowStillClosesAtFourteenDays()
    {
        var waves = WavePlanner.Plan(PlanOfDays(0, 14));

        Assert.Equal(2, waves.Count);
    }

    [Fact]
    public void Plan_SplitsAboveTwelveAtLargestGap()
    {
        // 15 threads in 5 days; the gap between thread 9 and 10 is the largest
        var days = new List<double>();
        for (var i = 0; i < 9; i++)
            days.Add(i * 0.1);
        for (var i = 0; i < 6; i++)
            days.Add(3 + i * 0.1);

        var waves = WavePlanner.Plan(PlanOfDays(days.ToArray()));

        Assert.Equal(2, waves.Count);
        Assert.Equal(9, waves[0].ThreadIds.Count);
        Assert.Equal(6, waves[1].ThreadIds.Count);
        Assert.All(waves, w => Assert.True(w.ThreadIds.Count <= WavePlanner.MaxThreadsPerWave));
        Assert.Equal("T-0010", waves[1].ThreadIds[0]);
    }

    [Fact]
    public void Plan_SplitsRepeatedlyUntilBounded()
    {
        // 30 evenly spaced threads within one window
        var days = Enumerable.Range(0, 30).Select(i => i * 0.1).ToArray();

        var waves = WavePlanner.Plan(PlanOfDays(days));

        Assert.All(waves, w => Assert.InRange(w.ThreadIds.Count, 1, WavePlanner.MaxThreadsPerWave));
        Assert.Equal(30, waves.Sum(w => w.ThreadIds.Count));
        Assert.Equal(Enumerable.Range(1, waves.Count), waves.Select(w => w.Index));
    }

    [Fact]
    public void Plan_ExcludesNonRelevantAndEmptyThreads()
    {
        var plan = PlanOfDays(0, 1);
        plan.Threads.Add(Thread("T-0003", Day0.AddDays(2), relevance: ThreadRelevance.NotRelevant));
        var empty = Thread("T-0004", Day0.AddDays(3));
        empty.Messages.Clear();
        plan.Threads.Add(empty);

        var waves = WavePlanner.Plan(plan);

        var ids = waves.SelectMany(w => w.ThreadIds).ToList();
        Assert.Equal(new[] { "T-0001", "T-0002" }, ids);
    }

    [Fact]
    public void Plan_NullOrEmptyPlan_NoWaves()
    {
        Assert.Empty(WavePlanner.Plan(new CorpusPlan()));
        Assert.Empty(WavePlanner.Plan(new CorpusPlan { Threads = null! }));
    }

    [Fact]
    public void Plan_IsDeterministicAndOrderIndependent()
    {
        var plan = PlanOfDays(0, 2, 2, 6, 9, 9, 30, 31);

        var first = WavePlanner.Plan(plan);
        var reversed = PlanFixtures.Clone(plan);
        reversed.Threads.Reverse();

        Assert.Equal(Fingerprint(first), Fingerprint(WavePlanner.Plan(plan)));
        Assert.Equal(Fingerprint(first), Fingerprint(WavePlanner.Plan(reversed)));
    }

    [Fact]
    public void Plan_TiesOrderedById()
    {
        var plan = new CorpusPlan();
        plan.Threads.Add(Thread("T-0002", Day0));
        plan.Threads.Add(Thread("T-0001", Day0));

        var waves = WavePlanner.Plan(plan);

        Assert.Equal(new[] { "T-0001", "T-0002" }, waves[0].ThreadIds);
    }

    [Fact]
    public void Plan_StartEndAndMessageCount()
    {
        var plan = new CorpusPlan();
        plan.Threads.Add(Thread("T-0001", Day0, messages: 3));
        plan.Threads.Add(Thread("T-0002", Day0.AddDays(4), messages: 2));

        var wave = Assert.Single(WavePlanner.Plan(plan));

        Assert.Equal(new DateOnly(2024, 3, 4), wave.Start);
        Assert.Equal(new DateOnly(2024, 3, 8), wave.End);
        Assert.Equal(5, wave.MessageCount);
    }

    [Fact]
    public void Plan_StartUsesSenderLocalDate()
    {
        // 03:00 UTC is the previous evening in Chicago
        var plan = new CorpusPlan();
        plan.Threads.Add(Thread("T-0001", new DateTimeOffset(2024, 3, 5, 3, 0, 0, TimeSpan.Zero)));

        var wave = Assert.Single(WavePlanner.Plan(plan));

        Assert.Equal(new DateOnly(2024, 3, 4), wave.Start);
    }

    [Fact]
    public void WaveIndexByThread_MapsEveryRelevantThread()
    {
        var plan = PlanOfDays(0, 2, 6, 9, 30);
        var waves = WavePlanner.Plan(plan);

        var map = WavePlanner.WaveIndexByThread(waves);

        Assert.Equal(5, map.Count);
        Assert.Equal(1, map["T-0003"]);
        Assert.Equal(2, map["T-0004"]);
        Assert.Equal(3, map["T-0005"]);
    }

    [Fact]
    public void Plan_ApprovedCase_CoversEveryRelevantThreadOnce()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();

        var waves = WavePlanner.Plan(plan);
        var ids = waves.SelectMany(w => w.ThreadIds).ToList();

        var relevant = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant && t.Messages.Count > 0).Select(t => t.Id);
        Assert.Equal(relevant.OrderBy(i => i, StringComparer.Ordinal), ids.OrderBy(i => i, StringComparer.Ordinal));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        for (var i = 1; i < waves.Count; i++)
            Assert.True(waves[i].Start >= waves[i - 1].Start);
    }
}
