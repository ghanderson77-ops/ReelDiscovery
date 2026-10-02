using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class ThreadSkeletonTests
{
    private static SkeletonContext Context(CaseDraft? draft = null, CorpusSettings? settings = null, bool media = false, int seed = 42) =>
        SkeletonContext.Create(draft ?? CorpusTestData.RichDraft(), settings ?? new CorpusSettings(), media, new Random(seed));

    private static (SkeletonContext Ctx, RelevantBudget Budget, List<PlannedThread> Threads) Build(CaseDraft? draft = null,
        CorpusSettings? settings = null, int relevant = 200, bool media = false, int seed = 42)
    {
        var ctx = Context(draft, settings, media, seed);
        var budget = BudgetAllocator.AllocateRelevant(ctx.DatedFacts, relevant, null, ctx.Random);
        var threads = ThreadSkeletonBuilder.BuildRelevant(ctx, budget);
        return (ctx, budget, threads);
    }

    private static IEnumerable<string> Participants(PlannedMessage m) => new[] { m.FromId }.Concat(m.ToIds).Concat(m.CcIds);

    private static DateOnly LocalDay(SkeletonContext ctx, PlannedMessage m) => BusinessCalendar.LocalDate(m.SentAt, ctx.ZoneFor(m.FromId));

    private static DateOnly FactDate(SkeletonContext ctx, string factId) =>
        SkeletonContext.FactDate(ctx.DatedFacts.Single(f => f.Id == factId))!.Value;

    private static HashSet<string> Knowers(KeyFact fact) => fact.ActorIds.Concat(fact.KnownByIds).ToHashSet(StringComparer.Ordinal);

    private static int BusinessDaysBetween(DateOnly a, DateOnly b)
    {
        if (b < a)
            (a, b) = (b, a);
        var count = 0;
        for (var d = a.AddDays(1); d <= b; d = d.AddDays(1))
        {
            if (BusinessCalendar.IsBusinessDay(d))
                count++;
        }

        return count;
    }

    /// <summary>
    /// RichDraft with two concurrent facts from different issues sharing two knowers: F-004 (ISS-01) moved to 2024-05-03
    /// and CH-002 added to F-005's knowers (ISS-01 + ISS-02, 2024-05-10). RichDraft itself has no such pair.
    /// </summary>
    private static CaseDraft ConcurrentDraft()
    {
        var draft = CorpusTestData.RichDraft();
        var facts = draft.Guide!.Facts;
        facts.Single(f => f.Id == "F-004").Date = "2024-05-03";
        facts.Single(f => f.Id == "F-005").KnownByIds.Add("CH-002");
        return draft;
    }

    [Fact]
    public void BuildRelevant_TotalsAndThreadFields()
    {
        var (ctx, budget, threads) = Build();

        Assert.Equal(budget.TotalMessages, threads.Sum(t => t.Messages.Count));
        Assert.Equal(threads.Count, threads.Select(t => t.Id).Distinct().Count());
        foreach (var thread in threads)
        {
            Assert.Matches(@"^T-\d{4}$", thread.Id);
            Assert.Equal(ThreadRelevance.Relevant, thread.Relevance);
            Assert.Equal(NonRelevantCategory.None, thread.Category);
            Assert.NotEmpty(thread.FactIds);
            Assert.NotEmpty(thread.IssueIds);
            var issues = thread.FactIds.SelectMany(id => ctx.DatedFacts.Single(f => f.Id == id).IssueIds).Distinct().OrderBy(i => i);
            Assert.Equal(issues, thread.IssueIds.OrderBy(i => i));
            Assert.InRange(thread.Messages.Count, 1, ThreadShapes.MaxThreadDepth);
            for (var i = 0; i < thread.Messages.Count; i++)
                Assert.Equal($"{thread.Id}-M{i + 1:00}", thread.Messages[i].Id);
        }
    }

    [Fact]
    public void BuildRelevant_ParticipantsAreActiveKnowers_StructureValid()
    {
        var (ctx, _, threads) = Build();

        foreach (var thread in threads)
        {
            var facts = thread.FactIds.Select(id => ctx.DatedFacts.Single(f => f.Id == id)).ToList();
            for (var i = 0; i < thread.Messages.Count; i++)
            {
                var m = thread.Messages[i];
                Assert.Equal(i + 1, m.Seq);
                if (i == 0)
                {
                    Assert.Equal(0, m.ReplyToSeq);
                    Assert.Equal(MessageKind.New, m.Kind);
                }
                else
                {
                    Assert.InRange(m.ReplyToSeq, 1, m.Seq - 1);
                    Assert.NotEqual(MessageKind.New, m.Kind);
                }

                Assert.NotEmpty(m.ToIds);
                var day = LocalDay(ctx, m);
                foreach (var id in Participants(m))
                {
                    Assert.True(ctx.CastById.ContainsKey(id), $"{m.Id}: {id} not in cast");
                    Assert.All(facts, f => Assert.Contains(id, Knowers(f)));
                    Assert.True(ctx.IsActive(id, day), $"{m.Id}: {id} inactive on {day}");
                }
            }

            Assert.True(ThreadScheduler.CheckInvariants(thread, ctx.ZoneFor, out var problem), problem);
        }
    }

    [Fact]
    public void BuildRelevant_BasisAndKnowledgeDates()
    {
        var (ctx, _, threads) = Build();

        foreach (var thread in threads)
        {
            var latest = thread.FactIds.Max(id => FactDate(ctx, id));
            var root = thread.Messages[0];
            Assert.Equal(RelevanceBasis.Own, root.Basis);
            Assert.Equal(thread.FactIds, root.FactIds);
            foreach (var m in thread.Messages)
            {
                Assert.True(LocalDay(ctx, m) >= latest, $"{m.Id} sent {LocalDay(ctx, m)} before {latest}");
                if (m.Basis == RelevanceBasis.Own)
                    Assert.Equal(thread.FactIds, m.FactIds);
                else
                {
                    Assert.Equal(RelevanceBasis.Quoted, m.Basis);
                    Assert.Empty(m.FactIds);
                }
            }
        }

        var later = threads.SelectMany(t => t.Messages.Skip(1)).ToList();
        Assert.Contains(later, m => m.Basis == RelevanceBasis.Own);
        Assert.Contains(later, m => m.Basis == RelevanceBasis.Quoted);
    }

    [Fact]
    public void BuildRelevant_WeekendAndHolidayFacts_StartOnNextBusinessDay()
    {
        var (ctx, _, threads) = Build();

        var f007 = threads.Where(t => t.FactIds.Contains("F-007")).ToList();
        var f008 = threads.Where(t => t.FactIds.Contains("F-008")).ToList();
        Assert.NotEmpty(f007);
        Assert.NotEmpty(f008);
        Assert.All(f007, t => Assert.True(LocalDay(ctx, t.Messages[0]) >= new DateOnly(2024, 6, 3)));
        Assert.All(f008, t => Assert.True(LocalDay(ctx, t.Messages[0]) >= new DateOnly(2024, 7, 5)));
    }

    [Fact]
    public void BuildRelevant_SingleKnower_NotesToSelfAndOneNote()
    {
        var (ctx, _, threads) = Build();

        var f008 = threads.Where(t => t.FactIds.SequenceEqual(new[] { "F-008" })).SelectMany(t => t.Messages).ToList();
        Assert.NotEmpty(f008);
        Assert.All(f008, m =>
        {
            Assert.Equal("CH-008", m.FromId);
            Assert.Equal(new[] { "CH-008" }, m.ToIds);
            Assert.Empty(m.CcIds);
        });

        const string note = "F-008 has only one person who knew about it, so its emails are notes to self. Add people under Who knew to get real threads.";
        Assert.Single(ctx.Notes, n => n == note);
    }

    [Fact]
    public void BuildRelevant_ConcurrentFacts_MultiIssueThreads()
    {
        var (ctx, budget, threads) = Build(ConcurrentDraft());

        Assert.Equal(budget.TotalMessages, threads.Sum(t => t.Messages.Count));
        var multi = threads.Where(t => t.FactIds.Count > 1).ToList();
        Assert.NotEmpty(multi);
        Assert.InRange(multi.Count / (double)threads.Count, 0.05, 0.20);

        foreach (var thread in multi)
        {
            Assert.Equal(2, thread.FactIds.Count);
            var a = ctx.DatedFacts.Single(f => f.Id == thread.FactIds[0]);
            var b = ctx.DatedFacts.Single(f => f.Id == thread.FactIds[1]);
            Assert.False(a.IssueIds.ToHashSet().SetEquals(b.IssueIds));
            Assert.True(thread.IssueIds.Count >= 2);
            Assert.True(BusinessDaysBetween(FactDate(ctx, a.Id), FactDate(ctx, b.Id)) <= ThreadSkeletonBuilder.MultiIssueWindowBusinessDays);
        }
    }

    [Fact]
    public void BuildRelevant_SameSeed_IdenticalJson()
    {
        var first = JsonSerializer.Serialize(Build(seed: 7).Threads, CaseDraft.JsonOptions);
        var second = JsonSerializer.Serialize(Build(seed: 7).Threads, CaseDraft.JsonOptions);
        Assert.Equal(first, second);

        var third = JsonSerializer.Serialize(Build(ConcurrentDraft(), seed: 7).Threads, CaseDraft.JsonOptions);
        var fourth = JsonSerializer.Serialize(Build(ConcurrentDraft(), seed: 7).Threads, CaseDraft.JsonOptions);
        Assert.Equal(third, fourth);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(50)]
    [InlineData(1000)]
    public void BuildRelevant_AnyBudget_ExactTotals(int relevant)
    {
        var (_, budget, threads) = Build(relevant: relevant);
        Assert.Equal(budget.TotalMessages, threads.Sum(t => t.Messages.Count));
        Assert.All(threads, t => Assert.InRange(t.Messages.Count, 1, ThreadShapes.MaxThreadDepth));
    }
}
