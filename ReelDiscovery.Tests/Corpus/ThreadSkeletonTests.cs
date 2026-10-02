using System.Text.Json;
using ReelDiscovery.Models;
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

    // ----- Task 2: hot, privileged, attachments, repair -----

    private static HashSet<string> SideOf(SkeletonContext ctx, CastMember counsel)
    {
        var orgs = ctx.Guide.Organizations;
        var clients = new HashSet<string>(StringComparer.Ordinal) { counsel.OrganizationId };
        if (counsel.Role == CastRole.OutsideCounsel)
            clients.UnionWith(orgs.Single(o => o.Id == counsel.OrganizationId).RepresentsOrgIds);

        var side = new HashSet<string>(clients, StringComparer.Ordinal);
        foreach (var org in orgs.Where(o => o.RepresentsOrgIds.Any(clients.Contains)))
            side.Add(org.Id);
        return side;
    }

    private static bool IsCounsel(CastMember m) => m.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel;

    private static void AssertHot(List<PlannedThread> threads, string hotFact, int expected)
    {
        var hot = threads.SelectMany(t => t.Messages.Where(m => m.IsHot)).ToList();
        Assert.Equal(expected, hot.Count);
        Assert.All(hot, m =>
        {
            Assert.Equal(RelevanceBasis.Own, m.Basis);
            Assert.Contains(hotFact, m.FactIds);
        });

        var candidates = threads.SelectMany(t => t.Messages).Where(m => m.Basis == RelevanceBasis.Own && m.FactIds.Contains(hotFact)).ToList();
        var minHot = hot.Min(m => Participants(m).Distinct().Count());
        var maxOther = candidates.Where(m => !m.IsHot).Select(m => Participants(m).Distinct().Count()).DefaultIfEmpty(0).Max();
        Assert.True(minHot >= maxOther, $"hot messages have {minHot} participants, another candidate has {maxOther}");
    }

    [Fact]
    public void Hot_TwoWhenHotFactHasThreeOrMoreThreads()
    {
        var (_, _, threads) = Build();
        Assert.True(threads.Count(t => t.FactIds.Contains("F-003")) >= 3);
        AssertHot(threads, "F-003", 2);
    }

    [Fact]
    public void Hot_OneWhenHotFactHasFewerThanThreeThreads()
    {
        var (_, _, threads) = Build(relevant: 12);
        Assert.InRange(threads.Count(t => t.FactIds.Contains("F-003")), 1, 2);
        AssertHot(threads, "F-003", 1);
    }

    [Fact]
    public void Hot_OverrideThree()
    {
        var (_, _, threads) = Build(settings: new CorpusSettings { HotPerHotFact = 3 });
        AssertHot(threads, "F-003", 3);
    }

    [Fact]
    public void Privileged_AboutEightPercent_CounselAndClientSideOnly()
    {
        var (ctx, _, threads) = Build();

        var privileged = threads.Where(t => t.IsPrivileged).ToList();
        Assert.NotEmpty(privileged);
        Assert.InRange(privileged.Count / (double)threads.Count, 0.04, 0.14);

        foreach (var thread in privileged)
        {
            var people = thread.Messages.SelectMany(Participants).Distinct().Select(id => ctx.CastById[id]).ToList();
            var counsel = people.Where(IsCounsel).ToList();
            Assert.NotEmpty(counsel);
            Assert.Contains(counsel, c =>
            {
                var side = SideOf(ctx, c);
                return people.All(p => side.Contains(p.OrganizationId));
            });
            Assert.True(ThreadScheduler.CheckInvariants(thread, ctx.ZoneFor, out var problem), problem);
        }
    }

    [Fact]
    public void Privileged_OverrideZero_None()
    {
        var (_, _, threads) = Build(settings: new CorpusSettings { PrivilegedPercent = 0 });
        Assert.DoesNotContain(threads, t => t.IsPrivileged);
    }

    [Fact]
    public void Privileged_NoCounsel_None()
    {
        var draft = CorpusTestData.RichDraft();
        foreach (var member in draft.Guide!.Cast.Where(IsCounsel))
            member.Role = CastRole.Staff;

        var (_, _, threads) = Build(draft);
        Assert.DoesNotContain(threads, t => t.IsPrivileged);
    }

    [Fact]
    public void Attachments_OfficeOnly_OnlyOfficeKinds()
    {
        var settings = new CorpusSettings { IncludeCalendar = false, IncludeImages = false, IncludeVoicemail = false };
        var (_, _, threads) = Build(settings: settings, media: true);

        var attachments = threads.SelectMany(t => t.Messages).SelectMany(m => m.Attachments).ToList();
        Assert.NotEmpty(attachments);
        Assert.All(attachments, a => Assert.Contains(a.Kind, new[] { AttachmentType.Word, AttachmentType.Excel, AttachmentType.PowerPoint }));
    }

    [Fact]
    public void Attachments_ShareKindsAndFileNames()
    {
        var (ctx, _, threads) = Build(media: true);
        var messages = threads.SelectMany(t => t.Messages).ToList();

        var share = 100.0 * messages.Count(m => m.Attachments.Count > 0) / messages.Count;
        Assert.InRange(share, ctx.AttachmentPercent - 5, ctx.AttachmentPercent + 5);

        foreach (var thread in threads)
        {
            foreach (var m in thread.Messages)
            {
                Assert.InRange(m.Attachments.Count, 0, 3);
                foreach (var a in m.Attachments)
                {
                    if (a.Kind == AttachmentType.CalendarInvite)
                        Assert.Equal(1, m.Seq);
                    if (a.Kind == AttachmentType.Voicemail)
                        Assert.Single(thread.Messages);

                    var ext = a.Kind switch
                    {
                        AttachmentType.Word => ".docx",
                        AttachmentType.Excel => ".xlsx",
                        AttachmentType.PowerPoint => ".pptx",
                        AttachmentType.CalendarInvite => ".ics",
                        AttachmentType.Image => ".png",
                        _ => ".mp3"
                    };
                    Assert.False(string.IsNullOrWhiteSpace(a.FileName));
                    Assert.EndsWith(ext, a.FileName);
                    Assert.Equal(string.Empty, a.Title);
                }

                Assert.Equal(m.Attachments.Count, m.Attachments.Select(a => a.FileName).Distinct().Count());
            }
        }

        var kinds = messages.SelectMany(m => m.Attachments).Select(a => a.Kind).Distinct().ToList();
        Assert.Contains(AttachmentType.Word, kinds);
        Assert.Contains(AttachmentType.Excel, kinds);
    }

    [Theory]
    [InlineData("2024-05-01", false)]
    [InlineData("2024-04-23", true)]
    public void Repair_InactiveParticipant_SwappedOrTruncated_TotalsKept(string activeTo, bool expectTruncation)
    {
        var draft = CorpusTestData.RichDraft();
        draft.Guide!.Cast.Single(c => c.Id == "CH-002").ActiveTo = activeTo;
        var lastDay = DateOnly.Parse(activeTo, System.Globalization.CultureInfo.InvariantCulture);

        var (ctx, budget, threads) = Build(draft);

        Assert.Equal(budget.TotalMessages, threads.Sum(t => t.Messages.Count));
        foreach (var thread in threads)
        {
            var facts = thread.FactIds.Select(id => ctx.DatedFacts.Single(f => f.Id == id)).ToList();
            Assert.True(ThreadScheduler.CheckInvariants(thread, ctx.ZoneFor, out var problem), problem);
            for (var i = 0; i < thread.Messages.Count; i++)
            {
                var m = thread.Messages[i];
                Assert.Equal(i + 1, m.Seq);
                Assert.Equal($"{thread.Id}-M{i + 1:00}", m.Id);
                var day = LocalDay(ctx, m);
                foreach (var id in Participants(m))
                {
                    Assert.True(ctx.IsActive(id, day), $"{m.Id}: {id} inactive on {day}");
                    Assert.All(facts, f => Assert.Contains(id, Knowers(f)));
                }

                if (day > lastDay)
                    Assert.DoesNotContain("CH-002", Participants(m));
            }
        }

        // F-004 (2024-04-22) is known only by CH-002 and CH-004, so a thread that outlives CH-002 cannot swap and is shortened
        if (expectTruncation)
        {
            Assert.Contains(ctx.Notes, n => n.StartsWith("Thread T-") && n.Contains(" was shortened because Tom Becker was not active on 2024-04-"));
            Assert.Contains(threads, t => t.FactIds.SequenceEqual(new[] { "F-004" }) && t.Messages.All(m => m.FromId == "CH-004"));
        }
    }
}
