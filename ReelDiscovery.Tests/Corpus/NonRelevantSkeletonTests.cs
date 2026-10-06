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
    public void Build_NoCastActiveInWindow_PlansWithWholeCastAndNotes()
    {
        // WR-03: every active range misses the window; sender lists used to be empty and indexed at -1 / 0
        var draft = CorpusTestData.RichDraft();
        foreach (var member in draft.Guide!.Cast)
        {
            member.ActiveFrom = "1990-01-01";
            member.ActiveTo = "1990-12-31";
        }
        var ctx = SkeletonContext.Create(draft, new CorpusSettings(), false, new Random(42));
        var mix = Mix(daily: 120, ancillary: 40, newsletter: 30, decoy: 20);

        var skeleton = NonRelevantSkeletonBuilder.Build(ctx, mix, new List<PlannedThread>());

        foreach (var category in BusinessLike)
            Assert.Equal(mix[category], Of(skeleton, category).Sum(t => t.Messages.Count));
        Assert.Contains(ctx.Notes, n => n.Contains("No cast member is active inside the date window"));
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

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void Daily_SendersAreCastWeightedByRelevantVolume(int seed)
    {
        var (ctx, relevant, skeleton) = Build(Mix(daily: 400), seed: seed);
        var daily = Of(skeleton, NonRelevantCategory.DailyBusiness);

        Assert.All(daily.SelectMany(t => t.Messages), m => Assert.True(ctx.CastById.ContainsKey(m.FromId)));

        var topRelevant = relevant.SelectMany(t => t.Messages).GroupBy(m => m.FromId)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
        var topDaily = daily.GroupBy(t => t.Messages[0].FromId)
            .OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key).ToList();
        Assert.Contains(topRelevant, topDaily);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void Daily_AboutAFifthOfThreadsIncludeAnotherOrganization(int seed)
    {
        var (ctx, _, skeleton) = Build(Mix(daily: 400), seed: seed);
        var daily = Of(skeleton, NonRelevantCategory.DailyBusiness).Where(t => !t.IsPrivileged).ToList();

        var cross = daily.Count(t =>
        {
            var org = OrgOf(ctx, t.Messages[0].FromId);
            return ThreadPeople(t).Any(id => OrgOf(ctx, id) != org);
        });
        Assert.InRange(cross * 100.0 / daily.Count, 10, 30);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void Daily_DepthFollowsBusinessTable(int seed)
    {
        var (_, _, skeleton) = Build(Mix(daily: 3000), seed: seed);
        var shares = DepthShares(Of(skeleton, NonRelevantCategory.DailyBusiness), ThreadShapes.BusinessDepth);

        for (var i = 0; i < ThreadShapes.BusinessDepth.Length; i++)
            Assert.InRange(shares[i], ThreadShapes.BusinessDepth[i].Percent - 4, ThreadShapes.BusinessDepth[i].Percent + 4);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void Daily_SomeThreadsArePrivilegedInsideCounselOrganization(int seed)
    {
        var (ctx, _, skeleton) = Build(Mix(daily: 400), seed: seed);
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

    // ----- D-23: privilege incidence parity -----

    private static async Task<(CaseDraft Draft, CorpusPlan Plan)> RealPlanAsync(int total)
    {
        var draft = CorpusPlannerTests.Draft(total);
        var (planner, fake) = CorpusPlannerTests.Planner(2106);
        fake.Respond = CorpusPlannerTests.Responder();
        var result = await planner.PlanAsync(draft, mediaAvailable: true, string.Empty);
        Assert.True(result.Succeeded, result.FailureMessage);
        return (draft, result.Plan!);
    }

    private static int OldDailyTarget(int dailyThreads)
    {
        var target = (int)Math.Round(dailyThreads * NonRelevantSkeletonBuilder.PrivilegedDailyShare, MidpointRounding.AwayFromZero);
        return target == 0 && dailyThreads >= 50 ? 1 : target;
    }

    [Theory]
    [InlineData(50)]
    [InlineData(120)]
    [InlineData(250)]
    public async Task RealPlanner_SmallCorpus_PrivilegedRelevantMeansPrivilegedDaily(int total)
    {
        var (draft, plan) = await RealPlanAsync(total);
        var relevant = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).ToList();
        var daily = plan.Threads.Where(t => t.Category == NonRelevantCategory.DailyBusiness).ToList();
        Assert.Contains(relevant, t => t.IsPrivileged);
        Assert.NotEmpty(daily);

        // D-23: the banner must not appear on relevant mail only
        var privileged = daily.Where(t => t.IsPrivileged).ToList();
        Assert.NotEmpty(privileged);

        var cast = draft.Guide!.Cast.ToDictionary(c => c.Id, StringComparer.Ordinal);
        Assert.All(privileged, t =>
        {
            var people = ThreadPeople(t).ToList();
            var orgs = people.Select(id => cast[id].OrganizationId).Distinct(StringComparer.Ordinal).ToList();
            Assert.Single(orgs);
            Assert.Contains(people, id => cast[id].Role == CastRole.InHouseCounsel);
        });
    }

    [Fact]
    public async Task RealPlanner_LargeCorpus_DailyPrivilegeTracksRelevantShare()
    {
        var (_, plan) = await RealPlanAsync(1_000);
        var relevant = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).ToList();
        var daily = plan.Threads.Where(t => t.Category == NonRelevantCategory.DailyBusiness).ToList();
        var relevantShare = relevant.Count(t => t.IsPrivileged) / (double)relevant.Count;
        var dailyPrivileged = daily.Count(t => t.IsPrivileged);

        Assert.True(relevantShare > 0);
        Assert.True(dailyPrivileged / (double)daily.Count >= relevantShare * 0.5,
            $"daily {dailyPrivileged}/{daily.Count} vs relevant share {relevantShare:P1}");
        Assert.True(dailyPrivileged >= OldDailyTarget(daily.Count));
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void Daily_NoPrivilegedRelevant_KeepsOldTwoPercentRule(int seed)
    {
        var settings = new CorpusSettings { PrivilegedPercent = 0 };
        var (_, relevant, skeleton) = Build(Mix(daily: 300), settings: settings, seed: seed);
        Assert.DoesNotContain(relevant, t => t.IsPrivileged);

        var daily = Of(skeleton, NonRelevantCategory.DailyBusiness);
        Assert.Equal(OldDailyTarget(daily.Count), daily.Count(t => t.IsPrivileged));
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void Ancillary_BackgroundSendersWithKeyColleagues(int seed)
    {
        var (ctx, _, skeleton) = Build(Mix(ancillary: 400), seed: seed);
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

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void Ancillary_DepthFollowsAncillaryTable(int seed)
    {
        var (_, _, skeleton) = Build(Mix(ancillary: 3000), seed: seed);
        var shares = DepthShares(Of(skeleton, NonRelevantCategory.Ancillary), ThreadShapes.AncillaryDepth);

        for (var i = 0; i < ThreadShapes.AncillaryDepth.Length; i++)
            Assert.InRange(shares[i], ThreadShapes.AncillaryDepth[i].Percent - 4, ThreadShapes.AncillaryDepth[i].Percent + 4);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void Decoy_KnowersNearAFactWithItsIssues(int seed)
    {
        var (ctx, _, skeleton) = Build(Mix(decoy: 300), seed: seed);
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

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void AllThreads_ParticipantsActiveAndBusinessTime(int seed)
    {
        var (ctx, _, skeleton) = Build(Mix(daily: 1000, ancillary: 400, decoy: 200), seed: seed);

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

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2024)]
    public void Attachments_ShareMatchesSettingsPerCategory(int seed)
    {
        var settings = new CorpusSettings { AttachmentPercent = 25 };
        var (ctx, _, skeleton) = Build(Mix(daily: 600, ancillary: 300, decoy: 200), settings: settings, media: true, seed: seed);

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

    // ----- Newsletters, spam and personal mail -----

    private static ExternalParty External(NonRelevantSkeleton s, string id) => s.Externals.Single(e => e.Id == id);

    [Theory]
    [InlineData(160)]
    [InlineData(161)]
    [InlineData(1500)]
    public void Bulk_ExternalRootToOneCastMemberAndExactTotal(int newsletter)
    {
        var (ctx, _, skeleton) = Build(Mix(daily: 100, newsletter: newsletter));
        var bulk = Of(skeleton, NonRelevantCategory.NewsletterSpamPersonal);

        Assert.Equal(newsletter, bulk.Sum(t => t.Messages.Count));
        Assert.All(bulk, t =>
        {
            var root = t.Messages[0];
            Assert.StartsWith("EXT-", root.FromId);
            var external = External(skeleton, root.FromId);
            var recipient = Assert.Single(root.ToIds);
            Assert.True(ctx.CastById.ContainsKey(recipient));
            Assert.Empty(root.CcIds);
            Assert.Equal(recipient, t.OwnerCustodianId);
            Assert.Empty(t.IssueIds);
            Assert.Empty(t.FactIds);

            if (external.Kind != ExternalKind.Personal)
            {
                Assert.Single(t.Messages);
            }
            else
            {
                Assert.InRange(t.Messages.Count, 1, 2);
                if (t.Messages.Count == 2)
                {
                    var reply = t.Messages[1];
                    Assert.Equal(MessageKind.Reply, reply.Kind);
                    Assert.Equal(recipient, reply.FromId);
                    Assert.Equal(new[] { root.FromId }, reply.ToIds);
                    Assert.Equal(1, reply.ReplyToSeq);
                }
            }
        });
    }

    [Fact]
    public void Bulk_KindMixAndPersonalReplies()
    {
        var (_, _, skeleton) = Build(Mix(newsletter: 1500));
        var bulk = Of(skeleton, NonRelevantCategory.NewsletterSpamPersonal);
        var messages = bulk.SelectMany(t => t.Messages.Select(m => (Thread: t, Message: m))).ToList();
        double Share(Func<ExternalKind, bool> pick) =>
            messages.Count(x => pick(External(skeleton, x.Thread.Messages[0].FromId).Kind)) * 100.0 / messages.Count;

        Assert.InRange(Share(k => k == ExternalKind.Newsletter), 32, 48);
        Assert.InRange(Share(k => k is ExternalKind.Spam or ExternalKind.Vendor), 22, 38);
        Assert.InRange(Share(k => k == ExternalKind.Personal), 22, 38);

        var personal = bulk.Where(t => External(skeleton, t.Messages[0].FromId).Kind == ExternalKind.Personal).ToList();
        var replied = personal.Count(t => t.Messages.Count == 2) * 100.0 / personal.Count;
        Assert.InRange(replied, 4, 16);
    }

    [Theory]
    [InlineData(160, 10)]
    [InlineData(20, 3)]
    [InlineData(1500, 40)]
    public void Externals_CountIdsAndAddresses(int newsletter, int expected)
    {
        var (ctx, _, skeleton) = Build(Mix(newsletter: newsletter));

        Assert.Equal(expected, skeleton.Externals.Count);
        for (var i = 0; i < skeleton.Externals.Count; i++)
            Assert.Equal($"EXT-{i + 1:000}", skeleton.Externals[i].Id);

        var orgDomains = ctx.Guide.Organizations.Select(o => o.Domain.ToLowerInvariant()).ToHashSet();
        Assert.All(skeleton.Externals, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.DoesNotContain(e.Domain.ToLowerInvariant(), orgDomains);
            Assert.EndsWith("@" + e.Domain, e.Email);
            switch (e.Kind)
            {
                case ExternalKind.Newsletter:
                    Assert.StartsWith("news@", e.Email);
                    Assert.Contains(NonRelevantSkeletonBuilder.DefaultNewsletterNames, n => e.Name.EndsWith(n, StringComparison.Ordinal));
                    break;
                case ExternalKind.Vendor:
                    Assert.StartsWith("offers@", e.Email);
                    Assert.Contains(NonRelevantSkeletonBuilder.DefaultVendorNames, n => e.Name.EndsWith(n, StringComparison.Ordinal));
                    break;
                case ExternalKind.Spam:
                    Assert.StartsWith("offers@", e.Email);
                    break;
                default:
                    Assert.Contains(e.Domain, NonRelevantSkeletonBuilder.PersonalDomains);
                    break;
            }
        });
        Assert.Equal(skeleton.Externals.Count, skeleton.Externals.Select(e => e.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(skeleton.Externals, e => e.Kind == ExternalKind.Newsletter);
        Assert.Contains(skeleton.Externals, e => e.Kind == ExternalKind.Personal);
    }

    [Fact]
    public void Externals_NeverCollideWithCaseDomains()
    {
        var draft = CorpusTestData.RichDraft();
        draft.Guide!.Organizations.Single(o => o.Id == "ORG-01").Domain = "Gmail.com";
        draft.Guide.Organizations.Single(o => o.Id == "ORG-02").Domain = "commercialbuilderweekly.com";

        var (_, _, skeleton) = Build(Mix(newsletter: 1500), draft: draft);

        Assert.All(skeleton.Externals, e =>
        {
            Assert.NotEqual("gmail.com", e.Domain.ToLowerInvariant());
            Assert.NotEqual("commercialbuilderweekly.com", e.Domain.ToLowerInvariant());
        });
        Assert.Contains(skeleton.Externals, e => e.Kind == ExternalKind.Personal);
    }

    [Fact]
    public void Externals_NeverCustodians()
    {
        var (_, _, skeleton) = Build(Mix(daily: 300, ancillary: 100, newsletter: 300, decoy: 60));
        var externalIds = skeleton.Externals.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(externalIds);
        Assert.All(skeleton.Threads, t => Assert.DoesNotContain(t.OwnerCustodianId, externalIds));
        Assert.All(skeleton.Threads.Where(t => t.Category != NonRelevantCategory.NewsletterSpamPersonal),
            t => Assert.DoesNotContain(ThreadPeople(t), externalIds.Contains));
    }

    [Fact]
    public void Bulk_KeyRecipientsWeightedThreeToOne()
    {
        var (ctx, _, skeleton) = Build(Mix(newsletter: 1500));
        var recipients = Of(skeleton, NonRelevantCategory.NewsletterSpamPersonal).Select(t => t.Messages[0].ToIds[0]).ToList();

        var key = ctx.CastById.Values.Where(c => c.Tier == CastTier.Key).Select(c => c.Id).ToList();
        var background = ctx.CastById.Values.Where(c => c.Tier == CastTier.Background).Select(c => c.Id).ToList();
        var perKey = recipients.Count(key.Contains) / (double)key.Count;
        var perBackground = recipients.Count(background.Contains) / (double)background.Count;
        Assert.InRange(perKey / perBackground, 2.0, 4.5);
    }

    [Fact]
    public void Bulk_SpamImagesOnlyAndNoNewsletterAttachments()
    {
        var (_, _, skeleton) = Build(Mix(newsletter: 1500), media: true);
        var bulk = Of(skeleton, NonRelevantCategory.NewsletterSpamPersonal);
        ExternalKind KindOf(PlannedThread t) => External(skeleton, t.Messages[0].FromId).Kind;

        var spam = bulk.Where(t => KindOf(t) == ExternalKind.Spam).SelectMany(t => t.Messages).ToList();
        Assert.NotEmpty(spam);
        var expected = (int)Math.Round(spam.Count * NonRelevantSkeletonBuilder.SpamImageShare, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, spam.Count(m => m.Attachments.Count > 0));
        Assert.True(expected > 0);
        Assert.All(spam.SelectMany(m => m.Attachments), a => Assert.Equal(AttachmentType.Image, a.Kind));

        Assert.All(bulk.Where(t => KindOf(t) != ExternalKind.Spam).SelectMany(t => t.Messages), m => Assert.Empty(m.Attachments));
    }

    [Fact]
    public void Bulk_NoImagesWhenImagesDisabled()
    {
        var (_, _, skeleton) = Build(Mix(newsletter: 1500), settings: new CorpusSettings { IncludeImages = false }, media: true);
        Assert.All(Of(skeleton, NonRelevantCategory.NewsletterSpamPersonal).SelectMany(t => t.Messages), m => Assert.Empty(m.Attachments));
    }

    [Fact]
    public void Bulk_ExternalSendersScheduledInDefaultZone()
    {
        var (ctx, _, skeleton) = Build(Mix(newsletter: 600));
        var zone = BusinessCalendar.ResolveId(ReelDiscovery.Services.Case.UsTimeZones.Default)!;

        Assert.All(Of(skeleton, NonRelevantCategory.NewsletterSpamPersonal), t =>
        {
            Assert.True(ThreadScheduler.CheckInvariants(t, ctx.ZoneFor, out var problem), $"{t.Id}: {problem}");
            var root = t.Messages[0];
            Assert.Equal(zone.Id, ctx.ZoneFor(root.FromId).Id);
            Assert.Equal(zone.Id, root.TimeZoneId);
            Assert.True(BusinessCalendar.IsBusinessTime(root.SentAt, zone));
            Assert.All(t.Messages, m =>
            {
                var day = LocalDay(ctx, m);
                Assert.All(Participants(m), id => Assert.True(ctx.IsActive(id, day)));
                Assert.InRange(day, ctx.WindowStart, ctx.WindowEnd);
            });
        });
    }
}
