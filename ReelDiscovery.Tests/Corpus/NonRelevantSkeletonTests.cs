using System.Text.Json;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class NonRelevantSkeletonTests
{
    private static readonly NonRelevantCategory[] BusinessLike =
    {
        NonRelevantCategory.DailyBusiness, NonRelevantCategory.Ancillary, NonRelevantCategory.Decoy
    };

    private static Dictionary<NonRelevantCategory, int> Mix(int daily = 0, int ancillary = 0, int newsletter = 0, int decoy = 0) => new()
    {
        [NonRelevantCategory.DailyBusiness] = daily,
        [NonRelevantCategory.Ancillary] = ancillary,
        [NonRelevantCategory.NewsletterSpamPersonal] = newsletter,
        [NonRelevantCategory.Decoy] = decoy
    };

    private static (SkeletonContext Ctx, List<PlannedThread> Relevant, NonRelevantSkeleton Skeleton) Build(
        Dictionary<NonRelevantCategory, int> mix, CaseDraft? draft = null, CorpusSettings? settings = null, bool media = false,
        int seed = 42, int relevant = 200)
    {
        var ctx = SkeletonContext.Create(draft ?? CorpusTestData.RichDraft(), settings ?? new CorpusSettings(), media, new Random(seed));
        var budget = BudgetAllocator.AllocateRelevant(ctx.DatedFacts, relevant, null, ctx.Random);
        var relevantThreads = ThreadSkeletonBuilder.BuildRelevant(ctx, budget);
        var skeleton = NonRelevantSkeletonBuilder.Build(ctx, mix, relevantThreads);
        return (ctx, relevantThreads, skeleton);
    }

    private static IEnumerable<string> Participants(PlannedMessage m) => new[] { m.FromId }.Concat(m.ToIds).Concat(m.CcIds);

    private static IEnumerable<string> ThreadPeople(PlannedThread t) => t.Messages.SelectMany(Participants).Distinct(StringComparer.Ordinal);

    private static DateOnly LocalDay(SkeletonContext ctx, PlannedMessage m) => BusinessCalendar.LocalDate(m.SentAt, ctx.ZoneFor(m.FromId));

    private static string OrgOf(SkeletonContext ctx, string id) => ctx.CastById.TryGetValue(id, out var m) ? m.OrganizationId : string.Empty;

    private static List<PlannedThread> Of(NonRelevantSkeleton s, NonRelevantCategory c) => s.Threads.Where(t => t.Category == c).ToList();

    private static Dictionary<int, double> DepthShares(List<PlannedThread> threads, (int Min, int Max, int Percent)[] table)
    {
        var result = new Dictionary<int, double>();
        for (var i = 0; i < table.Length; i++)
        {
            var (min, max, _) = table[i];
            result[i] = threads.Count(t => t.Messages.Count >= min && t.Messages.Count <= max) * 100.0 / threads.Count;
        }

        return result;
    }

    [Fact]
    public void Build_CategoryTotalsExactAndThreadFields()
    {
        var mix = Mix(daily: 400, ancillary: 160, decoy: 80);
        var (_, _, skeleton) = Build(mix);

        foreach (var category in BusinessLike)
            Assert.Equal(mix[category], Of(skeleton, category).Sum(t => t.Messages.Count));

        Assert.All(skeleton.Threads, t =>
        {
            Assert.Equal(ThreadRelevance.NotRelevant, t.Relevance);
            Assert.NotEqual(NonRelevantCategory.None, t.Category);
            Assert.Empty(t.FactIds);
            Assert.Matches(@"^N-\d{4}$", t.Id);
            Assert.All(t.Messages, m =>
            {
                Assert.Empty(m.FactIds);
                Assert.Equal(RelevanceBasis.None, m.Basis);
                Assert.False(m.IsHot);
                Assert.Equal($"{t.Id}-M{m.Seq:00}", m.Id);
            });
            Assert.InRange(t.Messages.Count, 1, ThreadShapes.MaxThreadDepth);
        });
        Assert.Equal(skeleton.Threads.Count, skeleton.Threads.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void Daily_SendersAreCastWeightedByRelevantVolume()
    {
        var (ctx, relevant, skeleton) = Build(Mix(daily: 400));
        var daily = Of(skeleton, NonRelevantCategory.DailyBusiness);

        Assert.All(daily.SelectMany(t => t.Messages), m => Assert.True(ctx.CastById.ContainsKey(m.FromId)));

        var topRelevant = relevant.SelectMany(t => t.Messages).GroupBy(m => m.FromId)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
        var topDaily = daily.GroupBy(t => t.Messages[0].FromId)
            .OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key).ToList();
        Assert.Contains(topRelevant, topDaily);
    }

    [Fact]
    public void Daily_AboutAFifthOfThreadsIncludeAnotherOrganization()
    {
        var (ctx, _, skeleton) = Build(Mix(daily: 400));
        var daily = Of(skeleton, NonRelevantCategory.DailyBusiness).Where(t => !t.IsPrivileged).ToList();

        var cross = daily.Count(t =>
        {
            var org = OrgOf(ctx, t.Messages[0].FromId);
            return ThreadPeople(t).Any(id => OrgOf(ctx, id) != org);
        });
        Assert.InRange(cross * 100.0 / daily.Count, 10, 30);
    }

    [Fact]
    public void Daily_DepthFollowsBusinessTable()
    {
        var (_, _, skeleton) = Build(Mix(daily: 3000));
        var shares = DepthShares(Of(skeleton, NonRelevantCategory.DailyBusiness), ThreadShapes.BusinessDepth);

        for (var i = 0; i < ThreadShapes.BusinessDepth.Length; i++)
            Assert.InRange(shares[i], ThreadShapes.BusinessDepth[i].Percent - 4, ThreadShapes.BusinessDepth[i].Percent + 4);
    }

    [Fact]
    public void Daily_SomeThreadsArePrivilegedInsideCounselOrganization()
    {
        var (ctx, _, skeleton) = Build(Mix(daily: 400));
        var daily = Of(skeleton, NonRelevantCategory.DailyBusiness);
        Assert.True(daily.Count >= 50);

        var privileged = daily.Where(t => t.IsPrivileged).ToList();
        Assert.NotEmpty(privileged);
        Assert.True(privileged.Count <= Math.Max(1, (int)Math.Round(daily.Count * 0.05)));

        Assert.All(privileged, t =>
        {
            var people = ThreadPeople(t).ToList();
            var counsel = people.Where(id => ctx.CastById[id].Role == CastRole.InHouseCounsel).ToList();
            Assert.NotEmpty(counsel);
            var org = OrgOf(ctx, counsel[0]);
            Assert.All(people, id => Assert.Equal(org, OrgOf(ctx, id)));
            Assert.True(people.Count >= 2);
        });
    }

    [Fact]
    public void Ancillary_BackgroundSendersWithKeyColleagues()
    {
        var (ctx, _, skeleton) = Build(Mix(ancillary: 400));
        var ancillary = Of(skeleton, NonRelevantCategory.Ancillary);

        Assert.All(ancillary, t => Assert.Equal(CastTier.Background, ctx.CastById[t.Messages[0].FromId].Tier));
        Assert.All(ancillary, t => Assert.True(t.Messages.Count <= ThreadShapes.AncillaryDepth[^1].Max));

        var withKey = ancillary.Count(t =>
        {
            var org = OrgOf(ctx, t.Messages[0].FromId);
            return ThreadPeople(t).Any(id => ctx.CastById[id].Tier == CastTier.Key && OrgOf(ctx, id) == org);
        });
        Assert.True(withKey * 2 >= ancillary.Count, $"{withKey} of {ancillary.Count} ancillary threads include a key colleague.");
    }

    [Fact]
    public void Ancillary_DepthFollowsAncillaryTable()
    {
        var (_, _, skeleton) = Build(Mix(ancillary: 3000));
        var shares = DepthShares(Of(skeleton, NonRelevantCategory.Ancillary), ThreadShapes.AncillaryDepth);

        for (var i = 0; i < ThreadShapes.AncillaryDepth.Length; i++)
            Assert.InRange(shares[i], ThreadShapes.AncillaryDepth[i].Percent - 4, ThreadShapes.AncillaryDepth[i].Percent + 4);
    }

    [Fact]
    public void Decoy_KnowersNearAFactWithItsIssues()
    {
        var (ctx, _, skeleton) = Build(Mix(decoy: 300));
        var decoys = Of(skeleton, NonRelevantCategory.Decoy);
        Assert.NotEmpty(decoys);

        Assert.All(decoys, t =>
        {
            var people = ThreadPeople(t).ToList();
            var root = LocalDay(ctx, t.Messages[0]);
            var match = ctx.DatedFacts.Any(f =>
            {
                var knowers = f.ActorIds.Concat(f.KnownByIds).ToHashSet(StringComparer.Ordinal);
                var date = SkeletonContext.FactDate(f)!.Value;
                var low = date.AddDays(-NonRelevantSkeletonBuilder.DecoyDaysAroundFact);
                var high = date.AddDays(NonRelevantSkeletonBuilder.DecoyDaysAroundFact);
                if (low < ctx.WindowStart) low = ctx.WindowStart;
                if (high > ctx.WindowEnd) high = ctx.WindowEnd;
                return people.All(knowers.Contains) && root >= low && root <= high
                       && f.IssueIds.ToHashSet(StringComparer.Ordinal).SetEquals(t.IssueIds);
            });
            Assert.True(match, $"Decoy {t.Id} on {root:yyyy-MM-dd} with {string.Join(",", people)} matches no fact.");
            Assert.Empty(t.FactIds);
            Assert.NotEmpty(t.IssueIds);
        });
    }

    [Fact]
    public void AllThreads_ParticipantsActiveAndBusinessTime()
    {
        var (ctx, _, skeleton) = Build(Mix(daily: 1000, ancillary: 400, decoy: 200));

        Assert.All(skeleton.Threads, t =>
        {
            Assert.True(ThreadScheduler.CheckInvariants(t, ctx.ZoneFor, out var problem), $"{t.Id}: {problem}");
            Assert.All(t.Messages, m =>
            {
                var day = LocalDay(ctx, m);
                Assert.All(Participants(m), id => Assert.True(ctx.IsActive(id, day), $"{id} inactive on {day:yyyy-MM-dd} in {t.Id}"));
                Assert.InRange(day, ctx.WindowStart, ctx.WindowEnd);
            });
        });
    }

    [Fact]
    public void Attachments_ShareMatchesSettingsPerCategory()
    {
        var settings = new CorpusSettings { AttachmentPercent = 25 };
        var (ctx, _, skeleton) = Build(Mix(daily: 600, ancillary: 300, decoy: 200), settings: settings, media: true);

        foreach (var category in BusinessLike)
        {
            var threads = Of(skeleton, category);
            var messages = threads.SelectMany(t => t.Messages).ToList();
            var share = messages.Count(m => m.Attachments.Count > 0) * 100.0 / messages.Count;
            Assert.InRange(share, 20, 30);

            foreach (var thread in threads)
            {
                foreach (var message in thread.Messages)
                {
                    Assert.All(message.Attachments, a =>
                    {
                        Assert.Contains(a.Kind, ctx.EnabledTypes);
                        Assert.False(string.IsNullOrWhiteSpace(a.FileName));
                        if (a.Kind == AttachmentType.CalendarInvite)
                            Assert.Equal(1, message.Seq);
                        if (a.Kind == AttachmentType.Voicemail)
                            Assert.Single(thread.Messages);
                    });
                }
            }
        }
    }

    [Fact]
    public void Attachments_OnlyEnabledTypes()
    {
        var settings = new CorpusSettings { IncludeCalendar = false, IncludeImages = false, IncludeVoicemail = false };
        var (_, _, skeleton) = Build(Mix(daily: 400, ancillary: 100, decoy: 100), settings: settings, media: true);

        var kinds = skeleton.Threads.SelectMany(t => t.Messages).SelectMany(m => m.Attachments).Select(a => a.Kind).Distinct().ToList();
        Assert.NotEmpty(kinds);
        Assert.All(kinds, k => Assert.Contains(k, new[] { AttachmentType.Word, AttachmentType.Excel, AttachmentType.PowerPoint }));
    }

    [Fact]
    public void Custodians_OwnerIsCastRootSenderAndEveryoneCanBeOne()
    {
        var (ctx, _, skeleton) = Build(Mix(daily: 3000, ancillary: 300, decoy: 200));

        foreach (var thread in skeleton.Threads.Where(t => t.Category != NonRelevantCategory.NewsletterSpamPersonal))
        {
            Assert.Equal(thread.Messages[0].FromId, thread.OwnerCustodianId);
            Assert.True(ctx.CastById.ContainsKey(thread.OwnerCustodianId));
        }

        var owners = skeleton.Threads.Select(t => t.OwnerCustodianId).ToHashSet(StringComparer.Ordinal);
        Assert.All(ctx.CastById.Keys, id => Assert.Contains(id, owners));
    }

    [Fact]
    public void Build_SameSeedSameSkeleton()
    {
        var mix = Mix(daily: 300, ancillary: 100, newsletter: 100, decoy: 60);
        var a = JsonSerializer.Serialize(Build(mix, seed: 7).Skeleton);
        var b = JsonSerializer.Serialize(Build(mix, seed: 7).Skeleton);
        Assert.Equal(a, b);
    }
}
