using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Turns the relevant budget into fully structured, scheduled relevant threads (PLAN-02, PLAN-03, D-13): facts, issues,
/// participants, kinds, reply links and timestamps are all decided here; the LLM later writes the text fields only.
/// Pure: every draw uses the injected <see cref="SkeletonContext.Random"/>.
/// </summary>
public static class ThreadSkeletonBuilder
{
    // Share of relevant threads that carry two concurrent facts from different issues (RESEARCH Pattern 4, assumed 10-15%)
    public const double MultiIssueShare = 0.12;
    public const int MultiIssueWindowBusinessDays = 10;

    // Anchor offset after the fact date: 60% within 0..2 business days, otherwise 3..15
    private const int NearAnchorPercent = 60;
    private const int NearAnchorMaxDays = 2;
    private const int FarAnchorMaxDays = 15;

    // Later messages carry their own fact evidence 60% of the time, otherwise only quote it
    private const int OwnEvidencePercent = 60;

    // A later sender is picked from the parent CC instead of the parent To 25% of the time (when there is a CC)
    private const int SenderFromCcPercent = 25;

    // Facts further apart than this are never compared for multi-issue pairing (keeps the business-day walk bounded)
    private const int MaxPairCalendarDays = 31;

    private class ThreadSpec
    {
        public List<KeyFact> Facts { get; set; } = new();
        public int Size { get; set; }
        public DateOnly Anchor { get; set; }
    }

    private class BuiltThread
    {
        public PlannedThread Thread { get; set; } = null!;
        public ThreadSpec Spec { get; set; } = null!;
        public List<string> Pool { get; set; } = new();
    }

    /// <summary>
    /// Builds every relevant thread for <paramref name="budget"/>. Message totals equal <c>budget.TotalMessages</c>;
    /// anything the code cannot do (single knowers, nobody active) is recorded in <c>ctx.Notes</c>.
    /// </summary>
    public static List<PlannedThread> BuildRelevant(SkeletonContext ctx, RelevantBudget budget)
    {
        var specs = CreateSpecs(ctx, budget);
        MergeMultiIssue(ctx, specs);

        var built = new List<BuiltThread>();
        var notedSingle = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            var item = BuildThread(ctx, spec, ThreadId(built.Count + 1), notedSingle);
            if (item != null)
                built.Add(item);
        }

        foreach (var item in built)
            ThreadScheduler.Schedule(item.Thread, item.Spec.Anchor, ctx, LatestFactDate(item.Spec));

        return built.Select(b => b.Thread).ToList();
    }

    // ----- Specs: one per budget thread, in fact date order -----

    private static List<ThreadSpec> CreateSpecs(SkeletonContext ctx, RelevantBudget budget)
    {
        var factById = new Dictionary<string, KeyFact>(StringComparer.Ordinal);
        foreach (var fact in ctx.DatedFacts)
            factById.TryAdd(fact.Id ?? string.Empty, fact);

        var specs = new List<ThreadSpec>();
        var allocations = (budget.Facts ?? new())
            .Where(a => a != null && factById.ContainsKey(a.FactId ?? string.Empty))
            .OrderBy(a => factById[a.FactId].Date, StringComparer.Ordinal)
            .ThenBy(a => a.FactId, StringComparer.Ordinal);

        foreach (var allocation in allocations)
        {
            var fact = factById[allocation.FactId];
            foreach (var size in allocation.MessagesPerThread ?? new())
            {
                if (size <= 0)
                    continue;
                specs.Add(new ThreadSpec
                {
                    Facts = new List<KeyFact> { fact },
                    Size = Math.Min(size, ThreadShapes.MaxThreadDepth),
                    Anchor = SampleAnchor(ctx, fact)
                });
            }
        }

        return specs;
    }

    private static DateOnly SampleAnchor(SkeletonContext ctx, KeyFact fact)
    {
        var factDate = SkeletonContext.FactDate(fact) ?? ctx.WindowStart;
        var offset = ThreadShapes.Chance(NearAnchorPercent, ctx.Random)
            ? ctx.Random.Next(0, NearAnchorMaxDays + 1)
            : ctx.Random.Next(NearAnchorMaxDays + 1, FarAnchorMaxDays + 1);
        var anchor = AddBusinessDays(factDate, offset);

        // Never before the fact (knowledge rule wins over the window); otherwise inside the window
        var low = factDate > ctx.WindowStart ? factDate : ctx.WindowStart;
        var high = ctx.WindowEnd > low ? ctx.WindowEnd : low;
        if (anchor < low)
            anchor = low;
        if (anchor > high)
            anchor = high;
        return BusinessCalendar.NextBusinessDay(anchor);
    }

    // ----- Multi-issue threads (D-15) -----

    private static void MergeMultiIssue(SkeletonContext ctx, List<ThreadSpec> specs)
    {
        var target = (int)Math.Round(specs.Count * MultiIssueShare, MidpointRounding.AwayFromZero);
        if (target <= 0)
            return;

        var pairs = new List<(KeyFact A, KeyFact B)>();
        var facts = ctx.DatedFacts;
        for (var i = 0; i < facts.Count; i++)
        {
            for (var j = i + 1; j < facts.Count; j++)
            {
                if (IsMultiIssuePair(ctx, facts[i], facts[j]))
                    pairs.Add((facts[i], facts[j]));
            }
        }

        if (pairs.Count == 0)
            return;
        Shuffle(pairs, ctx.Random);

        var merges = 0;
        var progress = true;
        // Each merge removes one single-fact spec, so this is bounded by the spec count
        while (merges < target && progress)
        {
            progress = false;
            foreach (var (a, b) in pairs)
            {
                if (merges >= target)
                    break;

                var specsA = SingleFactSpecs(specs, a);
                var specsB = SingleFactSpecs(specs, b);

                // Keep at least one single-fact thread for each fact
                if (specsA.Count < 2 || specsB.Count < 2)
                    continue;

                var keep = specsA[ctx.Random.Next(specsA.Count)];
                var drop = specsB[ctx.Random.Next(specsB.Count)];
                Merge(ctx, specs, keep, drop);
                merges++;
                progress = true;
            }
        }
    }

    private static bool IsMultiIssuePair(SkeletonContext ctx, KeyFact a, KeyFact b)
    {
        var issuesA = Clean(a.IssueIds);
        var issuesB = Clean(b.IssueIds);
        if (issuesA.Count == 0 || issuesB.Count == 0 || issuesA.ToHashSet(StringComparer.Ordinal).SetEquals(issuesB))
            return false;

        var dateA = SkeletonContext.FactDate(a);
        var dateB = SkeletonContext.FactDate(b);
        if (dateA == null || dateB == null || BusinessDaysBetween(dateA.Value, dateB.Value) > MultiIssueWindowBusinessDays)
            return false;

        // Two people who knew both facts, so the thread is a real conversation among shared characters
        return KnowerPool(ctx, new List<KeyFact> { a, b }).Count >= 2;
    }

    private static List<ThreadSpec> SingleFactSpecs(List<ThreadSpec> specs, KeyFact fact) =>
        specs.Where(s => s.Facts.Count == 1 && ReferenceEquals(s.Facts[0], fact)).ToList();

    private static void Merge(SkeletonContext ctx, List<ThreadSpec> specs, ThreadSpec keep, ThreadSpec drop)
    {
        var droppedFact = drop.Facts[0];
        keep.Facts = keep.Facts.Concat(drop.Facts)
            .OrderBy(f => f.Date, StringComparer.Ordinal)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();
        if (drop.Anchor > keep.Anchor)
            keep.Anchor = drop.Anchor;

        var total = keep.Size + drop.Size;
        keep.Size = Math.Min(total, ThreadShapes.MaxThreadDepth);
        var remainder = total - keep.Size;
        specs.Remove(drop);

        // Totals stay exact: overflow goes to another thread of the dropped fact, then of the kept fact, then a new thread
        remainder = Absorb(specs, droppedFact, remainder);
        remainder = Absorb(specs, keep.Facts.First(f => !ReferenceEquals(f, droppedFact)), remainder);
        if (remainder > 0)
        {
            specs.Add(new ThreadSpec
            {
                Facts = new List<KeyFact> { droppedFact },
                Size = remainder,
                Anchor = SampleAnchor(ctx, droppedFact)
            });
        }
    }

    private static int Absorb(List<ThreadSpec> specs, KeyFact fact, int remainder)
    {
        foreach (var spec in specs.Where(s => s.Facts.Count == 1 && ReferenceEquals(s.Facts[0], fact)))
        {
            if (remainder <= 0)
                break;
            var room = ThreadShapes.MaxThreadDepth - spec.Size;
            var add = Math.Min(room, remainder);
            spec.Size += add;
            remainder -= add;
        }

        return remainder;
    }

    // ----- Structure -----

    private static BuiltThread? BuildThread(SkeletonContext ctx, ThreadSpec spec, string threadId, HashSet<string> notedSingle)
    {
        var pool = KnowerPool(ctx, spec.Facts);
        var label = string.Join(" and ", spec.Facts.Select(f => f.Id));

        if (pool.Count == 1 && spec.Facts.Count == 1 && notedSingle.Add(spec.Facts[0].Id))
        {
            ctx.Notes.Add($"{spec.Facts[0].Id} has only one person who knew about it, so its emails are notes to self. " +
                          "Add people under Who knew to get real threads.");
        }

        var active = pool.Where(id => ctx.IsActive(id, spec.Anchor)).ToList();
        if (active.Count == 0)
        {
            if (pool.Count == 0)
            {
                // Dangling or missing knowers (T-04-27): use an active key character so the fact is still evidenced
                var stand = ctx.CastById.Values.Where(c => ctx.IsActive(c.Id, spec.Anchor))
                    .OrderBy(c => c.Tier).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
                if (stand == null)
                {
                    ctx.Notes.Add($"{label} has nobody in the cast to write its emails, so {spec.Size} relevant emails were not planned.");
                    return null;
                }

                ctx.Notes.Add($"{label} has nobody in the cast who knew about it, so {stand.FullName} writes notes to self. Add people under Who knew.");
                pool = new List<string> { stand.Id };
                active = new List<string>(pool);
            }
            else
            {
                ctx.Notes.Add($"Nobody who knew about {label} was active on {spec.Anchor:yyyy-MM-dd}; the thread uses the people who did it.");
                var actors = ActorsIn(spec.Facts, pool);
                active = actors.Count > 0 ? actors : new List<string>(pool);
            }
        }

        var thread = new PlannedThread
        {
            Id = threadId,
            Relevance = ThreadRelevance.Relevant,
            Category = NonRelevantCategory.None,
            FactIds = spec.Facts.Select(f => f.Id).ToList(),
            IssueIds = spec.Facts.SelectMany(f => Clean(f.IssueIds)).Distinct(StringComparer.Ordinal).ToList()
        };

        BuildMessages(ctx, thread, spec, active, null);
        return new BuiltThread { Thread = thread, Spec = spec, Pool = pool };
    }

    /// <summary>
    /// Fills <paramref name="thread"/> with <c>spec.Size</c> messages among <paramref name="people"/>. When
    /// <paramref name="mustInclude"/> is set (privileged counsel), that person is on the root message.
    /// </summary>
    private static void BuildMessages(SkeletonContext ctx, PlannedThread thread, ThreadSpec spec, List<string> people, string? mustInclude)
    {
        var random = ctx.Random;
        var factIds = thread.FactIds;
        thread.Messages.Clear();

        var actors = ActorsIn(spec.Facts, people);
        var keyActors = actors.Where(id => ctx.CastById.TryGetValue(id, out var m) && m.Tier == CastTier.Key).ToList();
        var senderPool = keyActors.Count > 0 ? keyActors : actors.Count > 0 ? actors : people;
        var sender = senderPool[random.Next(senderPool.Count)];

        var others = people.Where(id => id != sender).ToList();
        Shuffle(others, random);
        if (mustInclude != null && mustInclude != sender && others.Remove(mustInclude))
            others.Insert(0, mustInclude);

        var toCount = Math.Min(ThreadShapes.SampleRootToCount(random), others.Count);
        var ccCount = Math.Min(ThreadShapes.SampleRootCcCount(random), others.Count - toCount);
        var root = new PlannedMessage
        {
            Seq = 1,
            ReplyToSeq = 0,
            Kind = MessageKind.New,
            FromId = sender,
            ToIds = toCount > 0 ? others.Take(toCount).ToList() : new List<string> { sender },
            CcIds = others.Skip(toCount).Take(ccCount).ToList(),
            Basis = RelevanceBasis.Own,
            FactIds = new List<string>(factIds)
        };
        thread.Messages.Add(root);

        for (var seq = 2; seq <= spec.Size; seq++)
            thread.Messages.Add(BuildReply(ctx, thread, seq, people));

        AssignMessageIds(thread);
    }

    private static PlannedMessage BuildReply(SkeletonContext ctx, PlannedThread thread, int seq, List<string> people)
    {
        var random = ctx.Random;
        var replyTo = ThreadShapes.SampleReplyToSeq(seq, random);
        var parent = thread.Messages[replyTo - 1];

        var sender = parent.CcIds.Count > 0 && ThreadShapes.Chance(SenderFromCcPercent, random)
            ? parent.CcIds[random.Next(parent.CcIds.Count)]
            : parent.ToIds[random.Next(parent.ToIds.Count)];

        var kind = ThreadShapes.SampleKind(parent.ToIds.Count + parent.CcIds.Count, random);
        string? forwardTarget = null;
        if (kind == MessageKind.Forward)
        {
            var onThread = thread.Messages.SelectMany(Participants).ToHashSet(StringComparer.Ordinal);
            var candidates = people.Where(id => id != sender && !onThread.Contains(id)).ToList();
            forwardTarget = candidates.Count > 0 ? candidates[random.Next(candidates.Count)] : null;
        }

        var resolved = ThreadShapes.ResolveKind(kind, parent, sender, forwardTarget);
        var (to, cc) = ThreadShapes.RecipientsFor(resolved, parent, sender, forwardTarget, random);
        var own = ThreadShapes.Chance(OwnEvidencePercent, random);

        return new PlannedMessage
        {
            Seq = seq,
            ReplyToSeq = replyTo,
            Kind = resolved,
            FromId = sender,
            ToIds = to,
            CcIds = cc,
            Basis = own ? RelevanceBasis.Own : RelevanceBasis.Quoted,
            FactIds = own ? new List<string>(thread.FactIds) : new List<string>()
        };
    }

    private static void AssignMessageIds(PlannedThread thread)
    {
        for (var i = 0; i < thread.Messages.Count; i++)
        {
            thread.Messages[i].Seq = i + 1;
            thread.Messages[i].Id = $"{thread.Id}-M{i + 1:00}";
        }
    }

    // ----- Helpers -----

    /// <summary>
    /// People who knew every one of <paramref name="facts"/> (ActorIds ∪ KnownByIds, cast only), in first-fact order.
    /// </summary>
    private static List<string> KnowerPool(SkeletonContext ctx, List<KeyFact> facts)
    {
        List<string>? pool = null;
        foreach (var fact in facts)
        {
            var knowers = Clean(fact.ActorIds).Concat(Clean(fact.KnownByIds))
                .Where(ctx.CastById.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            pool = pool == null ? knowers : pool.Where(knowers.Contains).ToList();
        }

        return pool ?? new List<string>();
    }

    private static List<string> ActorsIn(List<KeyFact> facts, List<string> people)
    {
        var actors = facts.SelectMany(f => Clean(f.ActorIds)).ToHashSet(StringComparer.Ordinal);
        return people.Where(actors.Contains).ToList();
    }

    private static DateOnly LatestFactDate(ThreadSpec spec) =>
        spec.Facts.Select(f => SkeletonContext.FactDate(f) ?? DateOnly.MinValue).Max();

    private static IEnumerable<string> Participants(PlannedMessage m) => new[] { m.FromId }.Concat(m.ToIds).Concat(m.CcIds);

    private static string ThreadId(int n) => $"T-{n:0000}";

    private static List<string> Clean(List<string>? ids) =>
        (ids ?? new()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();

    private static DateOnly AddBusinessDays(DateOnly start, int days)
    {
        var day = BusinessCalendar.NextBusinessDay(start);
        for (var i = 0; i < days; i++)
            day = BusinessCalendar.NextBusinessDay(day.AddDays(1));
        return day;
    }

    private static int BusinessDaysBetween(DateOnly a, DateOnly b)
    {
        if (b < a)
            (a, b) = (b, a);
        if (b.DayNumber - a.DayNumber > MaxPairCalendarDays)
            return int.MaxValue;

        var count = 0;
        for (var d = a.AddDays(1); d <= b; d = d.AddDays(1))
        {
            if (BusinessCalendar.IsBusinessDay(d))
                count++;
        }

        return count;
    }

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
