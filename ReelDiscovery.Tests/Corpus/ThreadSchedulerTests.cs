using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class ThreadSchedulerTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static SkeletonContext Context(int seed = 42) =>
        SkeletonContext.Create(CorpusTestData.RichDraft(), new CorpusSettings(), false, new Random(seed));

    private static PlannedThread SixMessageThread()
    {
        // CH-001/CH-002 Chicago (ORG-01), CH-003/CH-004 Phoenix (ORG-02)
        var senders = new[] { "CH-001", "CH-003", "CH-002", "CH-004", "CH-001", "CH-003" };
        var thread = new PlannedThread { Id = "T-0001" };
        for (var i = 0; i < senders.Length; i++)
        {
            thread.Messages.Add(new PlannedMessage
            {
                Seq = i + 1,
                ReplyToSeq = i,
                Kind = i == 0 ? MessageKind.New : MessageKind.Reply,
                FromId = senders[i],
                ToIds = new List<string> { senders[(i + 1) % senders.Length] }
            });
        }

        return thread;
    }

    private static void AssertInvariants(PlannedThread thread, SkeletonContext ctx)
    {
        Assert.True(ThreadScheduler.CheckInvariants(thread, ctx.ZoneFor, out var problem), problem);
        foreach (var message in thread.Messages)
            Assert.Equal(ctx.ZoneFor(message.FromId).Id, message.TimeZoneId);
    }

    [Fact]
    public void Schedule_SaturdayAnchor_RootOnMondayInChicagoBusinessHours()
    {
        var ctx = Context();
        var thread = SixMessageThread();
        ThreadScheduler.Schedule(thread, new DateOnly(2024, 6, 1), ctx);

        var root = thread.Messages[0];
        Assert.Equal(new DateOnly(2024, 6, 3), BusinessCalendar.LocalDate(root.SentAt, Chicago));
        Assert.True(BusinessCalendar.IsBusinessTime(root.SentAt, Chicago));
        Assert.Equal("America/Chicago", root.TimeZoneId);
        AssertInvariants(thread, ctx);

        for (var i = 1; i < thread.Messages.Count; i++)
        {
            Assert.True(thread.Messages[i].SentAt > thread.Messages[i - 1].SentAt);
            Assert.True(thread.Messages[i].SentAt >= thread.Messages[i - 1].SentAt.AddMinutes(1));
            Assert.True(BusinessCalendar.IsBusinessTime(thread.Messages[i].SentAt, ctx.ZoneFor(thread.Messages[i].FromId)));
        }
    }

    [Fact]
    public void Schedule_HolidayAnchor_RootNextBusinessDay()
    {
        var ctx = Context();
        var thread = SixMessageThread();
        ThreadScheduler.Schedule(thread, new DateOnly(2024, 7, 4), ctx);

        Assert.Equal(new DateOnly(2024, 7, 5), BusinessCalendar.LocalDate(thread.Messages[0].SentAt, Chicago));
        AssertInvariants(thread, ctx);
    }

    [Fact]
    public void Schedule_NotBefore_RootNeverEarlier()
    {
        var ctx = Context();
        for (var i = 0; i < 50; i++)
        {
            var thread = SixMessageThread();
            ThreadScheduler.Schedule(thread, new DateOnly(2024, 3, 1), ctx, new DateOnly(2024, 3, 18));
            Assert.True(BusinessCalendar.LocalDate(thread.Messages[0].SentAt, Chicago) >= new DateOnly(2024, 3, 18));
            AssertInvariants(thread, ctx);
        }
    }

    [Fact]
    public void Schedule_ThousandRandomThreads_NoViolations_RealisticMedianGap()
    {
        var random = new Random(7);
        var ctx = SkeletonContext.Create(CorpusTestData.RichDraft(), new CorpusSettings(), false, random);
        var people = new[] { "CH-001", "CH-002", "CH-003", "CH-004", "CH-005", "CH-006", "CH-008", "EXT-001" };
        var gaps = new List<double>();

        for (var t = 0; t < 1_000; t++)
        {
            var depth = ThreadShapes.SampleDepth(ThreadShapeClass.Business, random);
            var thread = new PlannedThread { Id = $"T-{t + 1:0000}" };
            for (var seq = 1; seq <= depth; seq++)
            {
                thread.Messages.Add(new PlannedMessage
                {
                    Seq = seq,
                    ReplyToSeq = ThreadShapes.SampleReplyToSeq(seq, random),
                    FromId = people[random.Next(people.Length)],
                    ToIds = new List<string> { people[random.Next(people.Length)] }
                });
            }

            var anchor = new DateOnly(2024, 1, 1).AddDays(random.Next(365));
            ThreadScheduler.Schedule(thread, anchor, ctx);
            Assert.True(ThreadScheduler.CheckInvariants(thread, ctx.ZoneFor, out var problem), $"{thread.Id}: {problem}");

            foreach (var message in thread.Messages.Where(m => m.ReplyToSeq > 0))
            {
                var parent = thread.Messages.Single(m => m.Seq == message.ReplyToSeq);
                gaps.Add(BusinessMinutesBetween(parent.SentAt, message.SentAt, ctx.ZoneFor(message.FromId)));
            }
        }

        gaps.Sort();
        var median = gaps[gaps.Count / 2];
        Assert.InRange(median, 30, 240);
    }

    [Fact]
    public void Schedule_Twice_StillValid()
    {
        var ctx = Context();
        var thread = SixMessageThread();
        ThreadScheduler.Schedule(thread, new DateOnly(2024, 5, 10), ctx);
        AssertInvariants(thread, ctx);
        ThreadScheduler.Schedule(thread, new DateOnly(2024, 5, 10), ctx);
        AssertInvariants(thread, ctx);
    }

    [Fact]
    public void CheckInvariants_DetectsOutOfOrderAndAfterHours()
    {
        var ctx = Context();
        var thread = SixMessageThread();
        ThreadScheduler.Schedule(thread, new DateOnly(2024, 5, 10), ctx);

        var swapped = thread.Messages[2].SentAt;
        thread.Messages[2].SentAt = thread.Messages[1].SentAt.AddSeconds(-30);
        Assert.False(ThreadScheduler.CheckInvariants(thread, ctx.ZoneFor, out _));

        thread.Messages[2].SentAt = swapped;
        thread.Messages[0].SentAt = BusinessCalendar.At(Chicago, new DateOnly(2024, 5, 10), new TimeOnly(20, 0));
        Assert.False(ThreadScheduler.CheckInvariants(thread, ctx.ZoneFor, out var problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Schedule_EmptyThread_DoesNothing()
    {
        var thread = new PlannedThread();
        ThreadScheduler.Schedule(thread, new DateOnly(2024, 5, 10), Context());
        Assert.Empty(thread.Messages);
        Assert.True(ThreadScheduler.CheckInvariants(thread, Context().ZoneFor, out _));
    }

    // Business minutes in [from, to] measured in the given zone
    private static double BusinessMinutesBetween(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo tz)
    {
        var a = TimeZoneInfo.ConvertTime(from, tz).DateTime;
        var b = TimeZoneInfo.ConvertTime(to, tz).DateTime;
        var total = 0.0;
        for (var day = DateOnly.FromDateTime(a); day <= DateOnly.FromDateTime(b); day = day.AddDays(1))
        {
            if (!BusinessCalendar.IsBusinessDay(day))
                continue;
            var start = day.ToDateTime(BusinessCalendar.DayStart);
            var end = day.ToDateTime(BusinessCalendar.DayEnd);
            var lo = a > start ? a : start;
            var hi = b < end ? b : end;
            if (hi > lo)
                total += (hi - lo).TotalMinutes;
        }

        return total;
    }
}
